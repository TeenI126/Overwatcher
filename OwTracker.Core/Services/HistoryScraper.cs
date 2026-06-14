using System.Drawing;
using System.Linq;
using OwTracker.Core.Models;
using OwTracker.Core.Repositories.Interfaces;
using OwTracker.Core.Services.Interfaces;

namespace OwTracker.Core.Services;

/// <summary>Result of a completed scrape run.</summary>
public sealed record ScrapeResult(int NewRecords, int SkippedDuplicates, string? ErrorMessage)
{
    public bool Success => ErrorMessage is null;
}

/// <summary>
/// Orchestrates a full match-history scrape:
///   ESC → Career Profile → History → Game Reports → iterate rows →
///   per match: Summary + Teams → persist → back → next row.
///
/// Navigation is click-based (mouse + ESC key) via <see cref="IInputSimulator"/>.
/// The scraper detects the current screen before acting and aborts cleanly on
/// unexpected states.
/// </summary>
public sealed class HistoryScraper
{
    private readonly IInputSimulator    _input;
    private readonly ScreenCapturer     _capturer;
    private readonly OcrEngine          _ocr;
    private readonly ScreenDetector     _detector;
    private readonly IMatchRepository   _matchRepo;
    private readonly IHeroClassifier    _classifier;
    private readonly IHeroLabelRepository _labelRepo;

    /// <summary>Raised after each log line so the UI can display progress.</summary>
    public event Action<string>? LogLine;

    /// <summary>When set (deep scrape), every Teams frame is saved to the debug folder.</summary>
    private bool _saveEveryTeamsFrame;

    /// <summary>When set (deep scrape), an already-stored match is overwritten with the fresh
    /// re-read instead of being left untouched — a back-fill corrects records with improved OCR.</summary>
    private bool _overwriteExisting;

    public HistoryScraper(
        IInputSimulator       input,
        ScreenCapturer        capturer,
        OcrEngine             ocr,
        ScreenDetector        detector,
        IMatchRepository      matchRepo,
        IHeroClassifier       classifier,
        IHeroLabelRepository  labelRepo)
    {
        _input      = input;
        _capturer   = capturer;
        _ocr        = ocr;
        _detector   = detector;
        _matchRepo  = matchRepo;
        _classifier = classifier;
        _labelRepo  = labelRepo;
    }

    // ── Entry point ───────────────────────────────────────────────────────

    /// <param name="maxGames">
    /// Optional cap on how many games to scrape (newest first). Used by the "Scrape last N
    /// (test)" button. Null = scrape until a stop condition (duplicates / end of list).
    /// </param>
    /// <param name="stopOnDuplicates">
    /// When true (default), the scrape stops after 3 consecutive already-in-DB matches (it has
    /// reached previously-scraped history). When false ("deep" scrape), it keeps going to the end
    /// of the list regardless — a COMPLETE back-fill of the whole history (full Summary + Teams +
    /// Personal per game, same as a normal scrape). It also saves each Teams frame to the debug
    /// folder for calibration. Slower (the per-game re-navigation grows down the list).
    /// </param>
    /// <param name="overwrite">
    /// When an already-stored match is re-read, replace the stored row with the fresh data instead
    /// of leaving it untouched. <c>null</c> (default) ties it to the scrape kind — a deep scrape
    /// overwrites, a normal scrape does not — preserving the CLI behaviour; the dashboard passes an
    /// explicit value via its "overwrite on duplicate" toggle.
    /// </param>
    public async Task<ScrapeResult> ScrapeAsync(
        CancellationToken ct = default, int? maxGames = null, bool stopOnDuplicates = true,
        int startIndex = 0, bool? overwrite = null)
    {
        startIndex   = Math.Clamp(startIndex, 0, MaxGames - 1);
        var endIndex = Math.Min(startIndex + (maxGames ?? MaxGames), MaxGames);
        _saveEveryTeamsFrame = !stopOnDuplicates;   // deep scrape: capture all Teams frames for calibration
        _overwriteExisting   = overwrite ?? !stopOnDuplicates;   // explicit, else tied to deep scrape

        // Fresh log file for each scrape run.
        try { File.WriteAllText(_logFile,
                  $"=== Scrape started {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                  + (maxGames is null ? "" : $" (limit {maxGames})")
                  + (startIndex > 0 ? $" (from {startIndex})" : "") + $" ==={Environment.NewLine}"); }
        catch { }

        Log("Bringing OW to foreground…");
        if (!_input.BringOwToForeground())
            return Fail("Could not bring Overwatch to foreground. Is the game running?");

        await Task.Delay(400, ct); // let OW settle after focus change

        Log("Navigating to Game Reports list…");
        // Retry navigation a few times: when launched while a screen is still loading (e.g. the
        // Career Profile overview just opened) a click can land before the target is interactive —
        // a transient race that clears on a second attempt.
        var navOk = false;
        for (var navTry = 0; navTry < 3 && !navOk && !ct.IsCancellationRequested; navTry++)
        {
            if (navTry > 0) { Log($"  Navigation attempt {navTry + 1} (previous attempt failed)…"); await Task.Delay(1200, ct); }
            navOk = await NavigateToGameReportsListAsync(ct);
        }
        if (!navOk) return Fail("Could not navigate to the Game Reports list.");

        Log("Starting match loop…");
        var newRecords  = 0;
        var duplicates  = 0;
        var consecutiveDuplicates = 0;
        var consecutiveSeen = 0;
        var consecutiveViewFails = 0;
        var seenThisRun = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // gameIndex 0 = most recent game. After viewing a report the list returns to the top
        // but keyboard focus is lost, so each iteration we re-anchor by HOVERING the top row
        // (highlights game 0 + gives the list keyboard focus), then press Down ×gameIndex to
        // walk the highlight to game N (the list auto-scrolls for off-screen rows). startIndex>0
        // jumps straight to game N (Down×N), skipping earlier entries — used to reach old games
        // (e.g. the 6v6 cluster) without re-walking the whole list.
        for (var gameIndex = startIndex; gameIndex < endIndex && !ct.IsCancellationRequested; gameIndex++)
        {
            // ── Anchor + navigate + select ────────────────────────────────────
            // It's mouse *motion* (not its resting position) that grabs highlight control.
            // So: hover the top row ONCE to highlight game 0, then leave the cursor STILL —
            // the arrow keys now drive the highlight. Pressing Space then both highlights and
            // selects game N, leaving a single cyan box. (We never move the mouse again until
            // the VIEW click, so nothing fights the keyboard highlight.)
            Log($"  Game {gameIndex}: hover top (still) → Down ×{gameIndex} → Space…");
            await _input.MoveMouseAsync(UiCoordinates.GameReportsList_TopRowHover, ct);
            await Task.Delay(300, ct);                 // settle; cursor now still on game 0
            for (var d = 0; d < gameIndex; d++)
                await _input.SendKeyAsync(NativeMethods.VK_DOWN, ct);
            await Task.Delay(250, ct);
            await _input.SendKeyAsync(NativeMethods.VK_SPACE, ct);
            await Task.Delay(300, ct);                 // let the selection settle

            // ── Locate the cyan selection box (game N is now the sole selection) ─
            Rectangle? box;
            using (var s = Capture())
            {
                box = s is null ? null : _detector.FindSelectionBox(s);

                // DEBUG: save the first navigation frame (and any no-box frame) for inspection.
                if (s is not null && (gameIndex == 0 || box is null))
                {
                    var p = Path.Combine(AppPaths.DebugDirectory,
                        $"debug_highlight_g{gameIndex}_{DateTime.Now:HHmmss}.png");
                    try { s.Save(p, System.Drawing.Imaging.ImageFormat.Png);
                          Log($"  DEBUG: selection frame saved → {p}  (box={(box?.ToString() ?? "null")})"); }
                    catch { }
                }
            }

            if (box is null)
            {
                Log("  No selected game row (Down landed on VIEW button / end of list). Stopping.");
                break;
            }

            // ── Read queue type for THIS row, anchored to the cyan box ─────────
            QueueRowData? queue = null;
            using (var s = Capture())
            {
                if (s is not null)
                {
                    var queueRoi = OcrEngine.QueueRoiForBox(box.Value);
                    try
                    {
                        // Dump the RAW text so we can see/calibrate when the parse is UNKNOWN.
                        // Show both the plain 3× read and the saturated-text read (which resolves the
                        // coloured ranking word) so the log reflects what ParseQueue actually sees.
                        var rawQueue = (_ocr.ReadRegionBlock(s, queueRoi, 3) + "  sat=" +
                                        _ocr.ReadRegionSaturatedText(s, queueRoi))
                                           .Replace("\r", " ").Replace("\n", " ").Trim();
                        queue = _ocr.ExtractQueueRowAt(s, box.Value);
                        Log($"  Game {gameIndex}: queue = [{queue.RankingMode}] / [{queue.QueueType}]  " +
                            $"(box y={box.Value.Top}–{box.Value.Bottom}, roi={queueRoi})  raw=\"{rawQueue}\"");

                        // If we couldn't read it, save the queue-ROI crop for recalibration.
                        if (queue.RankingMode == "UNKNOWN" && string.IsNullOrEmpty(queue.QueueType))
                        {
                            var qp = Path.Combine(AppPaths.DebugDirectory,
                                $"debug_queue_g{gameIndex}_{DateTime.Now:HHmmss}.png");
                            try { using var qc = _capturer.CropRegion(s, queueRoi);
                                  qc.Save(qp, System.Drawing.Imaging.ImageFormat.Png);
                                  Log($"    DEBUG: queue crop saved → {qp}"); }
                            catch { }
                        }
                    }
                    catch (Exception ex) { Log($"  Game {gameIndex}: queue read failed: {ex.Message}"); }
                }
            }

            // ── Open the selected match (VIEW GAME REPORT) ────────────────────
            await _input.ClickAsync(UiCoordinates.GameReportsList_ViewButton, ct);
            await Task.Delay(700, ct);

            // Confirm we entered the match detail, re-checking once after a longer wait (the report
            // can still be loading). If it never opens, recover to the list and SKIP this game
            // rather than aborting the whole run — a single transient miss shouldn't stop a deep
            // back-fill 40 games in.
            var entered = false;
            for (var chk = 0; chk < 2 && !entered; chk++)
            {
                if (chk > 0) await Task.Delay(900, ct);
                using var enter = Capture();
                entered = enter is not null && _detector.Detect(enter) == GameScreen.MatchDetail;
            }
            if (!entered)
            {
                if (++consecutiveViewFails >= 5)
                {
                    Log("  5 consecutive VIEW failures — navigation broken, stopping.");
                    break;
                }
                Log($"  Game {gameIndex}: did not enter match detail after VIEW — recovering, skipping ({consecutiveViewFails}/5).");
                // Only ESC if we drifted OFF the list; if the VIEW click just didn't register we're
                // still on the list and the next iteration can proceed.
                using (var s = Capture())
                    if (s is null || _detector.Detect(s) != GameScreen.GameReportsList)
                        await BackToListAsync(ct);
                continue;
            }
            consecutiveViewFails = 0;

            // ── Scrape the open match (summary + teams + personal + persist) ───
            var status = await ScrapeOpenMatchAsync(queue, seenThisRun, ct);

            // ── Back to the list (resets to top) ──────────────────────────────
            if (!await BackToListAsync(ct))
            {
                Log("  Lost the Game Reports list after ESC — stopping.");
                break;
            }

            // ── Tally + stop conditions ───────────────────────────────────────
            if (status == RowStatus.AlreadySeenThisRun) consecutiveSeen++;
            else                                         consecutiveSeen = 0;

            switch (status)
            {
                case RowStatus.NewRecord:
                    newRecords++;
                    consecutiveDuplicates = 0;
                    break;

                case RowStatus.DbDuplicate:
                    duplicates++;
                    consecutiveDuplicates++;
                    Log($"    duplicate of existing record ({consecutiveDuplicates}/{MaxConsecutiveDuplicates}).");
                    if (stopOnDuplicates && consecutiveDuplicates >= MaxConsecutiveDuplicates)
                    {
                        Log($"  {MaxConsecutiveDuplicates} consecutive duplicates — reached already-scraped history. Stopping.");
                        gameIndex = endIndex; // break the for-loop
                    }
                    break;

                case RowStatus.AlreadySeenThisRun:
                    // Re-reading the same match means navigation isn't advancing (focus stuck
                    // or highlight clamped on the last game). Bail out rather than loop forever.
                    Log($"    (already seen this run — navigation not advancing {consecutiveSeen}/{MaxConsecutiveSeen})");
                    consecutiveDuplicates = 0;
                    if (consecutiveSeen >= MaxConsecutiveSeen)
                    {
                        Log("  Navigation not advancing — stopping.");
                        gameIndex = endIndex;
                    }
                    break;

                case RowStatus.ScrapeFailed:
                    Log("    scrape failed, skipping.");
                    consecutiveDuplicates = 0;
                    break;
            }
        }

        Log($"Done. New: {newRecords}, duplicates {(_overwriteExisting ? "overwritten" : "skipped")}: {duplicates}.");
        return new ScrapeResult(newRecords, duplicates, null);
    }

    // ── Per-match scrape ──────────────────────────────────────────────────────

    private const int MaxGames                 = 500; // safety cap
    private const int MaxConsecutiveDuplicates = 3;   // stop once we re-hit scraped history
    private const int MaxConsecutiveSeen       = 3;   // stop if navigation re-reads same match

    private enum RowStatus
    {
        NewRecord, DbDuplicate, AlreadySeenThisRun, ScrapeFailed
    }

    /// <summary>
    /// Scrapes the currently-open match detail (Summary + Teams) and persists it.
    /// Assumes we are already on the MatchDetail screen; does NOT navigate back.
    /// </summary>
    private async Task<RowStatus> ScrapeOpenMatchAsync(
        QueueRowData? queue, HashSet<string> seenThisRun, CancellationToken ct)
    {
        // --- Summary ---
        await ClickMatchDetailTabAsync("SUMMARY", ct);

        // Capture + extract the Summary, retrying once if the map title reads as garbage. On the
        // first match opened the Summary header is sometimes still loading/animating in when
        // captured, so the map name OCRs to symbol soup ("{e]V}]¥.1.]") that Snap can't resolve.
        // A short settle + re-capture recovers it; a genuinely off map keeps its raw read.
        SummaryData? summary = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var screen = Capture();
            if (screen is null) break;
            try
            {
                summary = _ocr.ExtractSummary(screen);
                if (!MapRoster.IsKnown(summary.MapName))
                {
                    // Save the frame so the Summary map-name ROI can be inspected/recalibrated.
                    var gpath = Path.Combine(AppPaths.DebugDirectory,
                        $"debug_summary_garbled_a{attempt}_{DateTime.Now:HHmmss}.png");
                    try { screen.Save(gpath, System.Drawing.Imaging.ImageFormat.Png); } catch { }
                    if (attempt == 0)
                    {
                        Log($"    Garbled map title [{summary.MapName}] — re-capturing after settle. frame → {gpath}");
                        await Task.Delay(700, ct);
                        continue;
                    }
                    Log($"    Garbled map title [{summary.MapName}] persists. frame → {gpath}");
                }
                Log($"    → {summary.MapName}  {summary.Outcome}  {summary.MatchDatetime:MM/dd/yy HH:mm}");
                break;
            }
            catch (Exception ex) { Log($"    Summary OCR failed: {ex.Message}"); break; }
        }

        // Within-run de-dup safety (e.g. mis-navigation): skip the expensive Teams scrape.
        if (summary is not null && !seenThisRun.Add(MatchKey(summary)))
            return RowStatus.AlreadySeenThisRun;

        // --- Teams ---
        await ClickMatchDetailTabAsync("TEAMS", ct);

        IReadOnlyList<TeamPlayerData>? myTeam    = null;
        IReadOnlyList<TeamPlayerData>? enemyTeam = null;
        var heroPortraits = new List<(Bitmap crop, int idx)>();

        // Capture + extract the Teams table, retrying once if a whole team reads poorly. On the
        // first match opened, the Teams tab is sometimes still loading/animating in when captured
        // (the table sits a few px lower with a larger row pitch), which pushes the lower (enemy)
        // rows out of their ROIs → zeros. A short re-capture lets it settle.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var screen = Capture();
            if (screen is null) break;

            try
            {
                // Row centres are MEASURED from the white role icons (FindTeamRowCentresByIcon):
                // exact per-row positions for both teams, absorbing per-map vertical stretch AND
                // handling leavers (a short team, or no enemy section at all). Fall back to the fixed
                // grid + red-anchor only if the icon detector can't read a sane structure.
                var (myCentres, enmCentres) = _detector.FindTeamRowCentresByIcon(screen);
                string rowSrc;
                if (myCentres.Count >= 1 && PitchSane(myCentres) && PitchSane(enmCentres))
                {
                    rowSrc = "icon";
                }
                else
                {
                    var teamSize  = queue?.TeamSize ?? 5;
                    var enemyTopY = _detector.FindEnemyTeamTopY(screen,
                        fallbackTopY: UiCoordinates.Teams_EnemyFallbackTop(teamSize));
                    myCentres  = UiCoordinates.Teams_MyCentres(teamSize);
                    enmCentres = UiCoordinates.Teams_EnemyCentres(enemyTopY, teamSize);
                    rowSrc     = "grid";
                }

                var (my, enm) = _ocr.ExtractTeams(screen, myCentres, enmCentres);

                // A misaligned row reads tiny garbage (0/1/2/117…), not a clean 0; real match DMG
                // is always 100s+. So "weak" = ≥3 rows under 100 DMG (implausible for real players).
                var badMy  = my.Count(p => p.DamageDealt < 100);
                var badEnm = enm.Count(p => p.DamageDealt < 100);

                if ((badMy >= 3 || badEnm >= 3) && attempt == 0)
                {
                    var path = Path.Combine(AppPaths.DebugDirectory,
                        $"debug_teams_{DateTime.Now:HHmmss}.png");
                    try { screen.Save(path, System.Drawing.Imaging.ImageFormat.Png); } catch { }
                    Log($"    Weak Teams read (my0={badMy} enm0={badEnm}, rows={rowSrc}) — " +
                        $"re-capturing after settle. frame → {path}");
                    await Task.Delay(900, ct);
                    continue;
                }

                myTeam = my; enemyTeam = enm;
                // Portrait crops for the (stubbed) hero classifier — centre on each detected row.
                // Enemy crop indices follow my-team count (matches BuildMatchRecord's heroNames keys).
                for (var i = 0; i < myCentres.Count; i++)
                    heroPortraits.Add((_capturer.CropRegion(screen, UiCoordinates.Teams_PortraitRoi(myCentres[i] - 49)), i));
                for (var i = 0; i < enmCentres.Count; i++)
                    heroPortraits.Add((_capturer.CropRegion(screen, UiCoordinates.Teams_PortraitRoi(enmCentres[i] - 49)), myCentres.Count + i));

                Log($"    → {my.Count} vs {enm.Count} players extracted (rows={rowSrc}):");
                LogTeam("MY ", my);
                LogTeam("ENM", enm);

                // Red flag: a row with a 0 in E or DMG — a real player rarely has exactly 0 of
                // either, so a 0 most likely means a missed/misaligned cell. Save the frame for
                // debugging (independent of the ≥3-weak retry, which one bad cell won't trigger).
                // Deaths is EXCLUDED: 0 deaths is common in one-sided games, so D==0 over-triggered.
                // Assists, Healing and Mitigation are likewise legitimately 0 for many heroes.
                static bool ZeroRow(TeamPlayerData p) =>
                    p.Eliminations == 0 || p.DamageDealt == 0;
                var zeroRows = my.Count(ZeroRow) + enm.Count(ZeroRow);
                if (zeroRows > 0)
                {
                    var zpath = Path.Combine(AppPaths.DebugDirectory,
                        $"debug_teams_zerorow_{DateTime.Now:HHmmss}.png");
                    try { screen.Save(zpath, System.Drawing.Imaging.ImageFormat.Png); } catch { }
                    Log($"    ⚠ {zeroRows} row(s) have a 0 in E/DMG (possible missed cell) — frame → {zpath}");
                }

                if (_saveEveryTeamsFrame)
                {
                    var tag = System.Text.RegularExpressions.Regex.Replace(summary?.MapName ?? "x", @"[^A-Za-z]", "");
                    try { screen.Save(Path.Combine(AppPaths.DebugDirectory,
                            $"debug_teams_{my.Count}v{enm.Count}_{tag}_{DateTime.Now:HHmmss}.png"),
                            System.Drawing.Imaging.ImageFormat.Png); } catch { }
                }
                break;
            }
            catch (Exception ex) { Log($"    Teams OCR failed: {ex.Message}"); break; }
        }

        // --- Personal → All Heroes (my authoritative totals + "is-me" fingerprint) ---
        var (personal, myHeroes) = await ScrapePersonalAllHeroesAsync(ct);
        var myIndex = IdentifyMe(personal, myTeam);
        if (myIndex is not null)
            Log($"    Identified me as MY {myIndex} (DMG {myTeam![myIndex.Value].DamageDealt} ≈ personal {personal!.DamageDealt}).");
        else if (personal is not null)
            Log("    Could not confidently match my row by DMG — IsMe left unset for this match.");

        // --- Classify hero portraits ---
        var heroNames = new Dictionary<int, string>();
        foreach (var (crop, idx) in heroPortraits)
        {
            var pred = _classifier.Predict(crop);
            heroNames[idx] = pred.HeroName;
            if (pred.Confidence < 0.70f)
            {
                await _labelRepo.AddAsync(new PendingHeroLabel
                {
                    CropPath      = SaveCrop(crop, idx),
                    PredictedHero = pred.HeroName,
                    Confidence    = pred.Confidence,
                    CapturedAt    = DateTime.UtcNow
                }, ct);
            }
            crop.Dispose();
        }

        // --- Persist + classify duplicate vs new ---
        if (summary is not null && myTeam is not null && enemyTeam is not null)
        {
            var record = BuildMatchRecord(summary, queue ?? new QueueRowData("UNKNOWN", ""),
                                          myTeam, enemyTeam, heroNames, personal, myIndex, myHeroes);
            var saved = await _matchRepo.UpsertAsync(record, _overwriteExisting, ct);
            // UpsertAsync returns the SAME instance on insert, a DIFFERENT (existing) one on dup
            // (the existing row is overwritten in deep mode, but still reported as a duplicate).
            return ReferenceEquals(saved, record) ? RowStatus.NewRecord : RowStatus.DbDuplicate;
        }

        return RowStatus.ScrapeFailed;
    }

    /// <summary>
    /// Clicks a Match Detail tab located by its label text ("SUMMARY"/"TEAMS"/"PERSONAL").
    /// Falls back to the hard-coded coordinate only if OCR can't find the label.
    /// </summary>
    private async Task ClickMatchDetailTabAsync(string label, CancellationToken ct)
    {
        Point? pt = null;
        using (var s = Capture())
            if (s is not null) pt = _detector.FindMatchDetailTab(s, label);

        if (pt is not null)
        {
            Log($"    Clicking {label} tab at {pt}…");
            await _input.ClickAsync(pt.Value, ct);
        }
        else
        {
            // Fallback (OCR miss): approximate hard-coded position.
            var fallback = label == "TEAMS"    ? UiCoordinates.MatchDetail_TeamsTab
                         : label == "PERSONAL" ? UiCoordinates.MatchDetail_PersonalTab
                         :                       UiCoordinates.MatchDetail_SummaryTab;
            Log($"    {label} tab not located by OCR — using fallback {fallback}.");
            await _input.ClickAsync(fallback, ct);
        }
        await Task.Delay(400, ct);
    }

    /// <summary>
    /// Navigates the open match's PERSONAL tab → ALL HEROES and reads my combined match totals.
    ///
    /// This identifies the local player without relying on the username (nameplate badges shift
    /// the name unpredictably and rotate every ~2 months): the All-Heroes grid shows MY totals,
    /// and exactly one Teams row — always on the TOP team — matches them (DMG is a near-unique
    /// key, see <see cref="IdentifyMe"/>). The Personal tab also reads my numbers more reliably
    /// than the dense Teams scoreboard, so these values become authoritative for my row.
    ///
    /// The same All-Heroes frame's sidebar also yields my hero NAMES (the sub-tab buttons), which
    /// are zipped by order with the Summary cards' play-times to record what I played and for how
    /// long — no portrait classifier needed.
    ///
    /// Fully non-fatal: any failure logs and returns (null, empty), leaving us on a match-detail
    /// tab from which the normal ESC-back-to-list still works. A debug frame is saved only when
    /// the key fingerprint (DMG) fails to read, for recalibration.
    /// </summary>
    private async Task<(PersonalStats? Stats, IReadOnlyList<HeroPlayData> Heroes)> ScrapePersonalAllHeroesAsync(
        CancellationToken ct)
    {
        var none      = (PersonalStats?)null;
        var noHeroes  = (IReadOnlyList<HeroPlayData>)Array.Empty<HeroPlayData>();
        try
        {
            await ClickMatchDetailTabAsync("PERSONAL", ct);

            // Click ALL HEROES FIRST. In that view the hero sub-tabs are NOT the selected (blue)
            // tab, so their names OCR cleanly — reading them from the initial frame failed because
            // the default-selected hero (slot 0) is highlighted and reads as garbage. This frame
            // also yields the combined stats (is-me) and, via the ALL HEROES button's Y, the
            // number of sidebar slots above it.
            Point? allHeroes;
            using (var s = Capture())
                allHeroes = s is null ? null : _detector.FindPersonalAllHeroes(s);

            if (allHeroes is null)
            {
                Log("    Personal: ALL HEROES button not located — can't identify me this match.");
                return (none, noHeroes);
            }

            // The sidebar is heroes (contiguous from slot 0), SOMETIMES a blank spacer pill, then
            // the ALL HEROES button. Its slot index bounds the hero region below.
            var allHeroesSlot = (allHeroes.Value.Y - 308 + 50) / 100;   // +50 = round, not trunc

            // Click ALL HEROES and CONFIRM the combined view actually landed — its tab must become
            // the blue-selected one. The click sometimes doesn't register; then slot 0 stays selected
            // and we'd read a single hero's view, where its name OCRs to garbage (highlighted tab) and
            // its stats aren't the combined totals (breaking the IsMe DMG fingerprint). Re-click until
            // ALL HEROES is selected.
            Bitmap? allFrame = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await _input.ClickAsync(allHeroes.Value, ct);
                await Task.Delay(750, ct);   // let the stat cards finish animating in before reading
                allFrame?.Dispose();
                allFrame = Capture();
                if (allFrame is null) return (none, noHeroes);
                if (_detector.IsSidebarSlotSelected(allFrame, allHeroesSlot)) break;
                Log($"    Personal: ALL HEROES view didn't land (attempt {attempt + 1}) — re-clicking.");
            }
            if (allFrame is null) return (none, noHeroes);   // never captured a frame

            PersonalStats? stats = null;
            var heroNames = new List<string>();
            using (allFrame)
            {
                try   { stats = _ocr.ExtractPersonalAllHeroes(allFrame); }
                catch (Exception ex) { Log($"    Personal parse failed: {ex.Message}"); }

                // How many heroes did I play? Count the FILLED slots (white name text) above the ALL
                // HEROES button — robust to the inconsistent spacer, not gated on fragile name OCR.
                var heroCount     = 0;
                for (var i = 0; i < allHeroesSlot && i < UiCoordinates.Personal_MaxHeroTabs; i++)
                    if (_detector.PersonalSlotHasHero(allFrame, i)) heroCount++;

                // Read each hero's NAME here (in the ALL HEROES view the sub-tabs are not the
                // selected/highlighted tab, so they OCR best). Unreadable → "Unknown", but the tab
                // is still visited below so its play-time is captured. When a name doesn't snap to a
                // roster hero, log the RAW OCR string (and save the frame) so the failing names can
                // be diagnosed — the roster is complete, so misses are an OCR/ROI issue.
                var anyUnknownHero = false;
                for (var i = 0; i < heroCount; i++)
                {
                    var name = _ocr.ReadHeroTabName(allFrame, i);
                    if (name is null)
                    {
                        anyUnknownHero = true;
                        var raw = _ocr.ReadRegion(allFrame, UiCoordinates.Personal_HeroTab(i))
                            .Trim().Replace("\n", " ").Replace("\r", "");
                        Log($"    Hero slot {i}: unrecognised name — raw OCR=[{raw}]");
                        name = "Unknown";
                    }
                    heroNames.Add(name);
                }
                if (anyUnknownHero)
                {
                    var hp = Path.Combine(AppPaths.DebugDirectory,
                        $"debug_personal_heroes_{DateTime.Now:HHmmss}.png");
                    try { allFrame.Save(hp, System.Drawing.Imaging.ImageFormat.Png);
                          Log($"    Hero-name frame saved → {hp}"); } catch { }
                }

                if (stats is not null)
                    Log($"    Personal (me): E={stats.Eliminations} A={stats.Assists} D={stats.Deaths} " +
                        $"DMG={stats.DamageDealt} HEAL={stats.HealingDone} MIT={stats.DamageMitigated}" +
                        $"  ({heroCount} hero tab(s))");

                // Save the frame only if the fingerprint key (DMG) didn't read — for recalibration.
                if (stats is null || stats.DamageDealt == 0)
                {
                    var path = Path.Combine(AppPaths.DebugDirectory,
                        $"debug_personal_allheroes_{DateTime.Now:HHmmss}.png");
                    try { allFrame.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                          Log($"    Personal: DMG unread — frame saved → {path}"); }
                    catch { }
                }
            }

            // Visit EVERY hero sub-tab (count from the ALL HEROES row, not gated on name OCR) to
            // read its PLAY TIME — capturing all heroes played, not just the top 3 the Summary shows.
            var heroes = new List<HeroPlayData>();
            for (var i = 0; i < heroNames.Count; i++)
            {
                await _input.ClickAsync(UiCoordinates.Personal_HeroTabClick(i), ct);
                await Task.Delay(550, ct);   // let the hero card animate in before reading PLAY TIME

                // A played hero ALWAYS has a non-zero PLAY TIME, so a 00:00 read is a miss (usually
                // the card hadn't finished animating in). Settle + re-capture once; if still unread,
                // save the frame + log the raw OCR so the ROI/timing can be diagnosed.
                var time   = TimeSpan.Zero;
                var rawPt  = "";
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    using var hs = Capture();
                    if (hs is null) break;
                    try { time  = _ocr.ExtractHeroPlayTime(hs); } catch { }
                    if (time > TimeSpan.Zero) break;
                    try { rawPt = _ocr.ReadRegion(hs, UiCoordinates.Personal_HeroPlayTime).Trim().Replace("\n", " "); } catch { }
                    if (attempt == 0) { await Task.Delay(450, ct); continue; }
                    var pp = Path.Combine(AppPaths.DebugDirectory,
                        $"debug_playtime_zero_s{i}_{DateTime.Now:HHmmss}.png");
                    try { hs.Save(pp, System.Drawing.Imaging.ImageFormat.Png);
                          Log($"    Hero {heroNames[i]} (slot {i}): PLAY TIME unread (raw=[{rawPt}]) — frame → {pp}"); }
                    catch { }
                }
                heroes.Add(new HeroPlayData(heroNames[i], time));
            }

            if (heroes.Count > 0)
                Log("    Heroes played: " +
                    string.Join(", ", heroes.Select(h => $"{h.HeroName} {h.PlayTime:mm\\:ss}")));

            return (stats, heroes);
        }
        catch (Exception ex)
        {
            Log($"    Personal capture failed (non-fatal): {ex.Message}");
            return (none, noHeroes);
        }
    }

    /// <summary>
    /// Minimum plausible All-Heroes hero-damage total. Real match damage is in the hundreds–
    /// thousands; a single/double-digit read (e.g. DMG=4) is an OCR miss, not a real value, and
    /// must not be used as a fingerprint (it would loosely "match" any low-damage row).
    /// </summary>
    private const int MinFingerprintDamage = 100;

    /// <summary>
    /// Finds which TOP-team (my-team) row is the local player by matching the Personal All-Heroes
    /// DamageDealt against each row's DMG. DMG is large and well-separated across players, so the
    /// nearest match is unambiguous. We accept only a confident match — within 5% (a single-digit
    /// OCR slop on a large number) with a small absolute floor — otherwise return null and leave
    /// IsMe unset rather than guess. A suspiciously-low Personal DMG is treated as unread.
    /// </summary>
    private static int? IdentifyMe(PersonalStats? personal, IReadOnlyList<TeamPlayerData>? myTeam)
    {
        if (personal is null || myTeam is null || myTeam.Count == 0 ||
            personal.DamageDealt < MinFingerprintDamage)
            return null;

        var best = -1;
        var bestDiff = int.MaxValue;
        for (var i = 0; i < myTeam.Count; i++)
        {
            var diff = Math.Abs(myTeam[i].DamageDealt - personal.DamageDealt);
            if (diff < bestDiff) { bestDiff = diff; best = i; }
        }

        // 5% relative tolerance with a small floor: keeps a legit single-digit slop on a large
        // number (e.g. 17589 vs 17889) while rejecting loose small-number "matches" (4 vs 74).
        var tolerance = Math.Max(50, personal.DamageDealt / 20);
        return bestDiff <= tolerance ? best : null;
    }

    /// <summary>Presses ESC and confirms we're back on the Game Reports list.</summary>
    private async Task<bool> BackToListAsync(CancellationToken ct)
    {
        await _input.PressEscapeAsync(ct);
        await Task.Delay(500, ct);
        using var s = Capture();
        return s is not null && _detector.Detect(s) == GameScreen.GameReportsList;
    }

    /// <summary>Logs each captured player row so the Teams-tab scrape can be eyeballed in the log.</summary>
    private void LogTeam(string tag, IReadOnlyList<TeamPlayerData> team)
    {
        for (var i = 0; i < team.Count; i++)
        {
            var p = team[i];
            Log($"      [{tag} {i}] {p.Username,-16} " +
                $"E={p.Eliminations,-3} A={p.Assists,-3} D={p.Deaths,-3} " +
                $"DMG={p.DamageDealt,-7} HEAL={p.HealingDone,-7} MIT={p.DamageMitigated}");
        }
    }

    /// <summary>Stable dedup key for a match: map + exact datetime.</summary>
    private static string MatchKey(SummaryData s) =>
        $"{s.MapName}|{s.MatchDatetime:O}";

    // ── Navigation ────────────────────────────────────────────────────────

    private async Task<bool> NavigateToGameReportsListAsync(CancellationToken ct)
    {
        var current = DetectCurrentVerbose("initial");

        // From wherever we are, get to the escape MENU first.
        // Pressing ESC opens the menu from the home/PLAY screen or closes a match detail. A single
        // press doesn't always register / land on the menu, so try up to TWICE before giving up.
        for (var attempt = 1;
             attempt <= 2 &&
             current != GameScreen.EscapeMenu &&
             current != GameScreen.CareerProfile &&
             current != GameScreen.GameReportsList;
             attempt++)
        {
            Log($"  Pressing ESC to open the menu (attempt {attempt})…");
            await _input.PressEscapeAsync(ct);
            await Task.Delay(600, ct);
            current = DetectCurrentVerbose($"after ESC {attempt}");
        }

        // ── Escape menu → click CAREER PROFILE (located by text) ───────────
        if (current == GameScreen.EscapeMenu)
        {
            using (var s = Capture())
            {
                var pt = s is null ? null : _detector.FindCareerProfileButton(s);
                if (pt is null) { Log("  Could not locate CAREER PROFILE button."); return false; }
                Log($"  Clicking CAREER PROFILE at {pt}…");
                await _input.ClickAsync(pt.Value, ct);
            }
            await Task.Delay(900, ct);
            current = DetectCurrentVerbose("after Career Profile");
        }

        // ── Career Profile → HISTORY tab, then GAME REPORTS sidebar ────────
        if (current == GameScreen.CareerProfile)
        {
            using (var s = Capture())
            {
                var pt = s is null ? null : _detector.FindHistoryTab(s);
                if (pt is null) { Log("  Could not locate HISTORY tab."); return false; }
                Log($"  Clicking HISTORY tab at {pt}…");
                await _input.ClickAsync(pt.Value, ct);
            }
            await Task.Delay(700, ct);

            using (var s = Capture())
            {
                var pt = s is null ? null : _detector.FindGameReportsSidebar(s);
                if (pt is null) { Log("  Could not locate GAME REPORTS sidebar item."); return false; }
                Log($"  Clicking GAME REPORTS at {pt}…");
                await _input.ClickAsync(pt.Value, ct);
            }
            await Task.Delay(700, ct);
            current = DetectCurrentVerbose("after Game Reports");
        }

        if (current == GameScreen.GameReportsList)
        {
            // The list opens scrolled to the top by default, so no scroll-to-top is needed
            // on first entry. (An earlier VK_HOME press here jumped keyboard focus — still on
            // the sidebar after the GAME REPORTS click — to the first sidebar item, HIGHLIGHTS,
            // bouncing us off the list. Scrolling for pagination will be handled in the loop,
            // by clicking into the list first to take focus, when we add multi-page support.)
            Log("  Game Reports list reached.");
            return true;
        }

        Log($"  Navigation ended on unexpected screen: {current}");
        return false;
    }

    /// <summary>Detects the current screen and logs it; saves a debug PNG when Unknown.</summary>
    private GameScreen DetectCurrentVerbose(string stage)
    {
        using var s = Capture();
        if (s is null) { Log($"  [{stage}] capture returned null."); return GameScreen.Unknown; }
        var screen = _detector.Detect(s);
        Log($"  [{stage}] screen = {screen}");
        if (screen == GameScreen.Unknown)
        {
            var p = Path.Combine(AppPaths.DebugDirectory,
                $"debug_{stage.Replace(' ', '_')}_{DateTime.Now:HHmmss}.png");
            try { s.Save(p, System.Drawing.Imaging.ImageFormat.Png); Log($"    saved → {p}"); }
            catch { }
        }
        return screen;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private GameScreen DetectCurrent()
    {
        using var s = Capture();
        return s is null ? GameScreen.Unknown : _detector.Detect(s);
    }

    private Bitmap? Capture() => _capturer.CaptureOwWindow();

    private static readonly string _logFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "OwTracker", "scrape.log");

    private void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        LogLine?.Invoke(line);
        try { File.AppendAllText(_logFile, line + Environment.NewLine); }
        catch { /* never let logging kill the scrape */ }
    }

    private ScrapeResult Fail(string msg)
    {
        Log($"ERROR: {msg}");
        return new ScrapeResult(0, 0, msg);
    }

    /// <summary>True if consecutive row centres have a plausible per-row pitch (≈80–88 px for
    /// 6v6/5v5). Guards the icon-measured centres before they're trusted over the fixed grid;
    /// an empty or single-row team is trivially sane (e.g. the enemy team having left).</summary>
    private static bool PitchSane(IReadOnlyList<int> centres)
    {
        for (var i = 1; i < centres.Count; i++)
        {
            var pitch = centres[i] - centres[i - 1];
            if (pitch < 68 || pitch > 102) return false;
        }
        return true;
    }

    private static MatchRecord BuildMatchRecord(
        SummaryData summary,
        QueueRowData queue,
        IReadOnlyList<TeamPlayerData> myTeam,
        IReadOnlyList<TeamPlayerData> enemyTeam,
        Dictionary<int, string> heroNames,
        PersonalStats? personal,
        int? myIndex,
        IReadOnlyList<HeroPlayData> myHeroes)
    {
        var outcome = summary.Outcome switch
        {
            var s when s.Contains("VICTORY") => MatchOutcome.Win,
            var s when s.Contains("DEFEAT")  => MatchOutcome.Loss,
            var s when s.Contains("DRAW")    => MatchOutcome.Draw,
            _                                => MatchOutcome.Unknown
        };

        var allPlayers = new List<PlayerRecord>();
        var teamSize   = myTeam.Count;

        // Merge my two reads of each stat (Personal All-Heroes vs Teams scoreboard) for my row.
        // Neither source is reliably better: Teams read my elims as 0 where Personal read 18, but
        // Personal dropped a digit on another match (25→5) where Teams read 25. OCR errors here
        // almost always DROP digits (or fail → "0"), yielding a SHORTER value, so prefer the read
        // with MORE digits — the more complete one. Ties favour Personal (authoritative for my row).
        static int Digits(int v) => v <= 0 ? 0 : v.ToString().Length;
        static int Pick(int personalVal, int teamVal) =>
            Digits(personalVal) >= Digits(teamVal) ? personalVal : teamVal;

        for (var i = 0; i < teamSize; i++)
        {
            var pd   = myTeam[i];
            var isMe = myIndex == i;
            var src  = isMe && personal is not null;

            var player = new PlayerRecord
            {
                IsMe            = isMe,
                Team            = "My Team",
                EndingHero      = heroNames.GetValueOrDefault(i, "Unknown"),
                Eliminations    = src ? Pick(personal!.Eliminations,    pd.Eliminations)    : pd.Eliminations,
                Assists         = src ? Pick(personal!.Assists,         pd.Assists)         : pd.Assists,
                Deaths          = src ? Pick(personal!.Deaths,          pd.Deaths)          : pd.Deaths,
                DamageDealt     = src ? Pick(personal!.DamageDealt,     pd.DamageDealt)     : pd.DamageDealt,
                HealingDone     = src ? Pick(personal!.HealingDone,     pd.HealingDone)     : pd.HealingDone,
                DamageMitigated = src ? Pick(personal!.DamageMitigated, pd.DamageMitigated) : pd.DamageMitigated,
            };

            // For me: record EVERY hero I played + how long — both name and play-time come from
            // that hero's Personal sub-tab (read by clicking through them), so all heroes are
            // captured, not just the top 3 the Summary shows. Slot 0 = highest play-time = ending hero.
            if (isMe && myHeroes.Count > 0)
            {
                foreach (var h in myHeroes)
                    player.HeroPlaytimes.Add(new HeroPlaytime { HeroName = h.HeroName, TimePlayed = h.PlayTime });
                player.EndingHero = myHeroes[0].HeroName;
            }

            allPlayers.Add(player);
        }

        for (var i = 0; i < enemyTeam.Count; i++)
        {
            var pd = enemyTeam[i];
            allPlayers.Add(new PlayerRecord
            {
                IsMe            = false,
                Team            = "Enemy Team",
                EndingHero      = heroNames.GetValueOrDefault(teamSize + i, "Unknown"),
                Eliminations    = pd.Eliminations,
                Assists         = pd.Assists,
                Deaths          = pd.Deaths,
                DamageDealt     = pd.DamageDealt,
                HealingDone     = pd.HealingDone,
                DamageMitigated = pd.DamageMitigated,
            });
        }

        var record = new MatchRecord
        {
            MapName        = summary.MapName,
            MatchDatetime  = summary.MatchDatetime,
            GameLength     = summary.GameLength,
            MyTeamScore    = summary.MyTeamScore,
            EnemyTeamScore = summary.EnemyTeamScore,
            Outcome        = outcome,
            GameMode       = summary.GameMode,
            RankingMode    = queue.RankingMode,
            QueueType      = queue.QueueType,
            ScrapedAt      = DateTime.UtcNow,
            AllPlayers     = allPlayers,
        };

        // IsMe is set via Personal→All-Heroes DMG fingerprint (see IdentifyMe), not username.
        // TODO: attach my Summary hero-card playtimes to the IsMe player's HeroPlaytimes.

        return record;
    }

    private static string SaveCrop(Bitmap crop, int index)
    {
        var dir  = AppPaths.CropsDirectory;
        var path = Path.Combine(dir, $"crop_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{index}.png");
        crop.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }
}
