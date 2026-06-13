using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.RegularExpressions;
using Tesseract;

namespace OwTracker.Core.Services;

// ── Parsed data records ───────────────────────────────────────────────────────

public sealed record SummaryData(
    string   MapName,
    string   Outcome,        // "VICTORY" | "DEFEAT" | "DRAW"
    int      MyTeamScore,
    int      EnemyTeamScore,
    DateTime MatchDatetime,
    string   GameMode,       // "ESCORT", "CONTROL", etc.
    TimeSpan GameLength,
    IReadOnlyList<HeroCardData> HeroCards);

public sealed record HeroCardData(
    string   HeroName,
    TimeSpan PlayTime,
    int      PercentPlayed); // 0-100

public sealed record TeamPlayerData(
    string Username,
    int    Eliminations,
    int    Assists,
    int    Deaths,
    int    DamageDealt,
    int    HealingDone,
    int    DamageMitigated);

public sealed record QueueRowData(
    string RankingMode,   // "COMPETITIVE" | "UNRANKED" | "ARCADE" | "STADIUM"
    string QueueType,     // "ROLE QUEUE" | "OPEN QUEUE" | "QUICK PLAY"
    int    TeamSize = 5); // 6 when the queue label says "6V6" (e.g. UNRANKED 6V6 QUICK PLAY)

/// <summary>The local player's combined match totals, read from Personal → ALL HEROES.
/// Used both to identify which Teams row is "me" (DMG is a near-unique key) and as an
/// authoritative stat line for that player (the Personal tab reads more reliably than the
/// dense Teams scoreboard for my own row).</summary>
public sealed record PersonalStats(
    int Eliminations,
    int Assists,
    int Deaths,
    int DamageDealt,
    int HealingDone,
    int DamageMitigated);

/// <summary>A hero the local player played in a match and how long, read from that hero's
/// Personal sub-tab.</summary>
public sealed record HeroPlayData(string HeroName, TimeSpan PlayTime);

// ── OcrEngine ─────────────────────────────────────────────────────────────────

/// <summary>
/// Extracts structured data from OW screenshots using Tesseract OCR.
/// Pre-processes each crop (grayscale → adaptive threshold → 2× upscale) before
/// passing to Tesseract in single-line mode for best accuracy on game UI text.
/// </summary>
public sealed class OcrEngine : IDisposable
{
    private readonly string _tessDataPath;
    private TesseractEngine? _engine;

    // Template-based digit reader for the fixed-font stat cells; overrides Tesseract per cell when
    // confident (it has no 7→4-type glyph ambiguity). Loaded once from the embedded template set.
    private readonly DigitOcr _digits = new();

    // Lazily initialised on first OCR call so app startup never fails if
    // tessdata hasn't been downloaded yet.
    private TesseractEngine Engine => _engine ??= CreateEngine();

    public OcrEngine()
        : this(TessDataManager.TessDataDirectory)
    {
    }

    public OcrEngine(string tessDataPath)
    {
        _tessDataPath = tessDataPath;
    }

    private TesseractEngine CreateEngine()
    {
        if (!File.Exists(Path.Combine(_tessDataPath, "eng.traineddata")))
            throw new InvalidOperationException(
                "Tesseract eng.traineddata not found. " +
                "Wait for the download to complete before scraping.");

        // No character whitelist: LSTM binarises internally and a whitelist
        // can cause the model to hallucinate constrained-character garbage.
        return new TesseractEngine(_tessDataPath, "eng", EngineMode.LstmOnly);
    }

    // ── Public extraction methods ─────────────────────────────────────────

    /// <summary>Extracts all Summary-tab fields from a full-screen capture.</summary>
    public SummaryData ExtractSummary(Bitmap screen)
    {
        // The map title is solid white caps on an animated (often bright) menu gradient. Read it
        // with the white-text mask (background-independent); fall back to the plain LSTM read only
        // if the masked read doesn't resolve to a known map. Snap canonicalises either read.
        var mapWhite = ReadRegionWhiteText(screen, UiCoordinates.Summary_MapName).Trim();
        var mapName  = MapRoster.Snap(mapWhite);
        if (mapName is null)
        {
            var mapPlain = ReadRegion(screen, UiCoordinates.Summary_MapName).Trim();
            mapName = MapRoster.Snap(mapPlain) ?? (mapWhite.Length >= 3 ? mapWhite : mapPlain);
        }
        var outcomeRaw = ReadRegion(screen, UiCoordinates.Summary_Outcome).Trim();

        // Read the whole info box as one multi-line block — robust to small drift.
        var infoBlock = ReadRegionBlock(screen, UiCoordinates.Summary_InfoBox);

        var scoreKnown = ParseScore(infoBlock, out var myScore, out var enemyScore);
        var dt      = ParseMatchDate(infoBlock);
        var length  = ParseGameLength(infoBlock);
        var mode    = ParseGameMode(infoBlock);
        var outcome = ParseOutcome(outcomeRaw, myScore, enemyScore, scoreKnown);

        var heroCards = new List<HeroCardData>();
        for (var i = 0; i < UiCoordinates.Summary_MaxHeroCards; i++)
        {
            var name    = ReadRegion(screen, UiCoordinates.Summary_HeroName(i)).Trim();
            var timeStr = ReadRegion(screen, UiCoordinates.Summary_HeroPlayTime(i)).Trim();
            var pctStr  = ReadRegion(screen, UiCoordinates.Summary_HeroPercent(i)).Trim();

            if (string.IsNullOrWhiteSpace(name)) break;

            heroCards.Add(new HeroCardData(name, ParseTimeSpan(timeStr), ParsePercent(pctStr)));
        }

        return new SummaryData(mapName, outcome, myScore, enemyScore, dt, mode, length, heroCards);
    }

    /// <summary>
    /// Extracts queue tier and type text from a single match row in the Game Reports list.
    /// Uses ReadRegionBlock (SingleBlock) and keyword search — robust to icon-prefix noise
    /// and the second line sometimes being clipped on lower rows.
    /// </summary>
    public QueueRowData ExtractQueueRow(Bitmap screen, int rowIndex)
    {
        var roi = UiCoordinates.GameReportsList_QueueRoi(rowIndex);
        return ParseQueue(ReadRegionBlock(screen, roi));
    }

    /// <summary>
    /// Extracts the queue tier/type for the row highlighted by the cyan selection box.
    /// The queue labels are at a fixed horizontal band on the right of the row; the vertical
    /// position comes from <paramref name="selectionBox"/> (which the cyan-glow detector found),
    /// making this robust to the list being scrolled to an arbitrary (non-row-snapped) offset.
    /// </summary>
    public QueueRowData ExtractQueueRowAt(Bitmap screen, Rectangle selectionBox)
        => ParseQueue(ReadRegionBlock(screen, QueueRoiForBox(selectionBox)));

    /// <summary>
    /// The ROI the queue tier/type labels occupy for a row whose cyan selection box is
    /// <paramref name="selectionBox"/>. Exposed so the scraper can dump the raw OCR text / a crop
    /// for calibration when the parse comes back UNKNOWN.
    /// Queue labels sit at a fixed x (~x1330–1610); the Y span comes from the box, padded a little.
    /// </summary>
    public static Rectangle QueueRoiForBox(Rectangle selectionBox)
    {
        // Use a FIXED full-row height centred on the box's vertical midpoint rather than the
        // box's own height: on scrolled rows the cyan-glow detector often collapses to a thin
        // sliver (height 4–19 px) which, used directly, gave a queue ROI too small to contain the
        // label. The midpoint is a reliable Y anchor. Height is generous (130) and starts a little
        // wider on the left (x=1305) so a 3-line "UNRANKED / 6V6 / QUICK PLAY" label is fully
        // captured even when the box detection is off by a row — the "6V6" line was being clipped.
        const int rowH = 108;
        var midY = selectionBox.Top + selectionBox.Height / 2;
        return new Rectangle(1305, midY - rowH / 2, 330, rowH);
    }

    /// <summary>Keyword-matches the queue tier/type from an OCR block (tolerant of OCR drift).
    /// Public for testing against real captured raw strings.</summary>
    public static QueueRowData ParseQueue(string rawBlock)
    {
        var raw = rawBlock.Trim().ToUpperInvariant();

        // Match on fragments that survive OCR even when leading characters are dropped:
        // "COMPETITIVE" frequently reads as "MPETTIVE" / "OMPETITIVE" (leading "CO" eaten), so
        // anchor on the stable middle "MPET"/"PETIT"; likewise "RANK" for UNRANKED, etc.
        var ranking =
            raw.Contains("MPET") || raw.Contains("PETIT") ? "COMPETITIVE" :
            raw.Contains("RANK") || raw.Contains("NRANK") ? "UNRANKED"    :
            raw.Contains("ARCAD") || raw.Contains("RCADE") ? "ARCADE"     :
            raw.Contains("STADI") || raw.Contains("TADIU") ? "STADIUM"    : "UNKNOWN";

        var queue =
            raw.Contains("ROLE")  ? "ROLE QUEUE" :   // ROLE QUEUE / ROLE PLAY
            raw.Contains("QUICK") ? "QUICK PLAY" :   // QUICK PLAY
            raw.Contains("OLICK") ? "QUICK PLAY" :   // OCR drift: O→Q, I→U
            raw.Contains("OPEN")  ? "OPEN QUEUE" :
            string.Empty;

        // 6v6 modes carry a literal "6V6" in the label (e.g. "UNRANKED 6V6 QUICK PLAY"). This is
        // the reliable team-size signal — the Teams scoreboard renders 6 tighter rows per side.
        // Match the contiguous token tolerantly for OCR drift (6→G/0): [6G0]V[6G0].
        var teamSize = System.Text.RegularExpressions.Regex.IsMatch(raw, @"[6G0]V[6G0]") ? 6 : 5;

        return new QueueRowData(ranking, queue, teamSize);
    }

    /// <summary>
    /// Extracts all player rows from the Teams tab. Automatically detects whether
    /// the match is 5v5 or 6v6 by finding the VS-divider Y position.
    /// </summary>
    public (IReadOnlyList<TeamPlayerData> MyTeam, IReadOnlyList<TeamPlayerData> EnemyTeam)
        ExtractTeams(Bitmap screen, int vsDividerY, int? enemyTopY = null)
    {
        // Determine team size from divider position.
        var teamSize = EstimateTeamSize(vsDividerY);

        // My team is anchored at the (stable) table top; the enemy block is anchored at its actual
        // top — detected from the red row background by the caller — because the scoreboard's
        // vertical pitch varies per map (Control maps render taller), shifting the enemy block out
        // of fixed ROIs. Fall back to the divider-derived position when no detected top is given.
        var myTop  = UiCoordinates.Teams_FirstRowY;
        var enmTop = enemyTopY ?? UiCoordinates.Teams_EnemyRowY(vsDividerY, 0);

        var myCentres  = RowCentresFromGrid(myTop,  teamSize);
        var enmCentres = RowCentresFromGrid(enmTop, teamSize);
        return ExtractTeams(screen, myCentres, enmCentres);
    }

    /// <summary>
    /// Extracts the team rows from explicit per-row Y-centres (e.g. from
    /// <see cref="ScreenDetector.FindTeamRowCentres"/>, which reads the separator lines). This
    /// handles non-uniform nameplate/title row heights that a fixed pitch can't.
    /// </summary>
    public (IReadOnlyList<TeamPlayerData> MyTeam, IReadOnlyList<TeamPlayerData> EnemyTeam)
        ExtractTeams(Bitmap screen, IReadOnlyList<int> myCentres, IReadOnlyList<int> enemyCentres)
        => (ExtractTeamBlock(screen, myCentres), ExtractTeamBlock(screen, enemyCentres));

    /// <summary>Row centres for a fixed-pitch block: centre i = top + i·pitch + 49 (the strip is
    /// centred on rowY+49).</summary>
    private static IReadOnlyList<int> RowCentresFromGrid(int blockTopY, int teamSize)
    {
        var c = new int[teamSize];
        for (var i = 0; i < teamSize; i++) c[i] = blockTopY + i * UiCoordinates.Teams_RowHeight + 49;
        return c;
    }

    /// <summary>
    /// Reads the six combined-stat values from the Personal → ALL HEROES card grid.
    ///
    /// Isolated per-cell ROIs don't work here — Tesseract returns empty/garbage on a lone big
    /// number like "18" without surrounding layout context. Instead each VALUE ROW is OCR'd as a
    /// band spanning both columns (excluding the label and "AVG PER 10 MIN" line below it), and
    /// the two numbers found are snapped to the left/right column. Rows (value-text Y bands):
    ///   row1 ≈ y367–427  ELIMINATIONS | HERO DAMAGE DONE
    ///   row2 ≈ y687–747  ASSISTS      | HEALING DONE
    ///   row3 ≈ y1007–1067 DEATHS      | DAMAGE MITIGATED
    /// </summary>
    public PersonalStats ExtractPersonalAllHeroes(Bitmap screen)
    {
        // Right column (Hero Damage / Healing / Mitigation): big multi-digit numbers. The cards are
        // semi-transparent over the animated menu gradient, so on a bright frame the plain read
        // mis-binarises ("18,033" → "[RKO KK"); the white-text mask isolates the white digits and
        // reads them cleanly regardless of background.
        var dmg  = ParseInt(ReadRegionWhiteText(screen, UiCoordinates.Personal_HeroDamage));
        var heal = ParseInt(ReadRegionWhiteText(screen, UiCoordinates.Personal_Healing));
        var mit  = ParseInt(ReadRegionWhiteText(screen, UiCoordinates.Personal_Mitigation));

        // Left column (Eliminations / Assists / Deaths): small 1–2 digit numbers that won't OCR
        // in isolation — read the value ROW as a band (with layout context) and take the left
        // column number. (Right column from the band is discarded; per-cell is more accurate.)
        var (e, _) = ReadCardRow(screen, 367,  427);
        var (a, _) = ReadCardRow(screen, 687,  747);
        var (d, _) = ReadCardRow(screen, 1007, 1067);

        return new PersonalStats(e, a, d, dmg, heal, mit);
    }

    /// <summary>
    /// Reads the hero names from the Personal-tab sidebar sub-tabs (the heroes I played, ordered
    /// by play-time, highest first). Stops at the first blank slot or the "ALL HEROES" button.
    /// These are clean, machine-printed names — far more reliable than the stylised italic hero
    /// names on the Summary cards — and need no portrait classifier.
    /// </summary>
    public IReadOnlyList<string> ExtractPersonalHeroNames(Bitmap screen)
    {
        var names = new List<string>();
        for (var i = 0; i < UiCoordinates.Personal_MaxHeroTabs; i++)
        {
            var hero = ReadHeroTabName(screen, i);
            if (hero is null)                  // blank/unreadable slot
            {
                if (names.Count > 0) break;    // trailing blank after the heroes → end of list
                continue;                      // a leading miss → keep scanning
            }
            names.Add(hero);
        }
        return names;
    }

    /// <summary>
    /// Reads one Personal sidebar hero sub-tab and snaps it to a roster hero, or null for a blank
    /// slot / the "ALL HEROES" button / an unreadable name. Read this from a frame where the slot
    /// is NOT the selected (blue-highlighted) tab — a highlighted sidebar item OCRs to garbage.
    /// </summary>
    public string? ReadHeroTabName(Bitmap screen, int slot)
    {
        var roi = UiCoordinates.Personal_HeroTab(slot);

        // The sidebar hero name is solid WHITE text on the animated (often bright purple) menu
        // gradient — the plain LSTM mis-binarises it to garbage on bright frames (same failure as
        // the map title: "KIRIKO" → "| '4|*d]| Co"). Read with the white-text mask first, falling
        // back to the plain read; snap either to the roster.
        static string? Resolve(string raw)
        {
            var up = raw.ToUpperInvariant();
            if (up.Contains("ALL HER") || up.Contains("HEROES")) return null;   // the ALL HEROES button
            return HeroRoster.Snap(up);
        }

        // Try the white-text mask at several upscales: a name the LSTM mangles at one scale often
        // reads (or reads close enough for HeroRoster's fuzzy snap) at another — e.g. accented
        // "LÚCIO" reads "Ico" at 3× but "Lclo" at 4×, which snaps to Lúcio. Return the first that
        // resolves; the default 3× is tried first so clean names cost only one OCR pass.
        foreach (var scale in new[] { 3, 4, 2 })
        {
            try { var hit = Resolve(ReadRegionWhiteText(screen, roi, scale: scale)); if (hit is not null) return hit; }
            catch { /* try next scale / the plain read */ }
        }

        try   { return Resolve(ReadRegion(screen, roi)); }
        catch { return null; }
    }

    /// <summary>Reads the "PLAY TIME" (MM:SS) value from a hero-specific Personal view frame.</summary>
    public TimeSpan ExtractHeroPlayTime(Bitmap screen) =>
        ParseTimeSpan(ReadRegion(screen, UiCoordinates.Personal_HeroPlayTime));

    /// <summary>Left-/right-column X centres of the All-Heroes value cards (2560×1440).</summary>
    private const int Personal_LeftColX = 805, Personal_RightColX = 1480;

    /// <summary>OCRs one value-row band and returns the (left-column, right-column) numbers.</summary>
    private (int Left, int Right) ReadCardRow(Bitmap screen, int yTop, int yBot)
    {
        var roi = Rectangle.FromLTRB(740, yTop, 1700, yBot);
        List<(string Word, Rectangle Box)> words;
        try   { words = ReadWords(screen, roi, whiteText: true); }   // white-isolate (semi-transparent cards)
        catch { return (0, 0); }

        // Merge comma-split fragments of a single number (small horizontal gap), left→right.
        var merged = new List<(int Left, int Right, string Digits)>();
        foreach (var (w, box) in words.OrderBy(x => x.Box.X))
        {
            var digits = Regex.Replace(
                NormaliseOcrNoise(w).Replace("l", "1").Replace("I", "1"), @"[^\d]", "");
            if (digits.Length == 0) continue;
            if (merged.Count > 0 && box.Left - merged[^1].Right <= 22)
                merged[^1] = (merged[^1].Left, box.Right, merged[^1].Digits + digits);
            else
                merged.Add((box.Left, box.Right, digits));
        }

        // Per column, choose the BEST candidate: most digits wins (the real stat value beats a
        // stray "1" from an icon/separator), tie-broken by proximity to the column centre.
        (int Len, int Dist, int Val) bestL = (0, int.MaxValue, 0), bestR = (0, int.MaxValue, 0);
        foreach (var (l, r, digits) in merged)
        {
            if (!int.TryParse(digits, out var v)) continue;
            var cx   = (l + r) / 2;
            var dL   = Math.Abs(cx - Personal_LeftColX);
            var dR   = Math.Abs(cx - Personal_RightColX);
            var len  = digits.Length;
            if (dL <= dR)
            {
                if (len > bestL.Len || (len == bestL.Len && dL < bestL.Dist)) bestL = (len, dL, v);
            }
            else
            {
                if (len > bestR.Len || (len == bestR.Len && dR < bestR.Dist)) bestR = (len, dR, v);
            }
        }
        return (bestL.Val, bestR.Val);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private IReadOnlyList<TeamPlayerData> ExtractTeamBlock(
        Bitmap screen, IReadOnlyList<int> rowCentres)
    {
        var players = new List<TeamPlayerData>(rowCentres.Count);
        foreach (var centreY in rowCentres)
        {
            var rowY = centreY - 49;   // back to the "row top" convention (strip centred at rowY+49)

            // Strip any leading portrait-edge artefacts (©, ®, dashes, etc.) — real
            // BattleTags start with an alphanumeric character.
            var usernameRaw = ReadRegion(screen, UiCoordinates.Teams_Username(rowY)).Trim();
            var username    = System.Text.RegularExpressions.Regex.Replace(
                                  usernameRaw, @"^[^A-Za-z0-9]+", "");

            // Read the six stat numbers in one strip pass, snapping each to its column.
            var stats = ReadStatRow(screen, rowY);
            players.Add(new TeamPlayerData(
                username, stats[0], stats[1], stats[2], stats[3], stats[4], stats[5]));
        }
        return players;
    }

    /// <summary>
    /// Reads the six stat numbers (E, A, D, DMG, H, MIT) for one player row by OCR-ing the
    /// whole stat strip and snapping each detected number to its nearest column centre.
    /// Far more robust than six tiny per-cell ROIs: Tesseract reads digits on a shared baseline
    /// reliably, garbage non-numeric tokens are dropped, and comma-split fragments of a wide
    /// number (e.g. "14" + "693") are re-merged by horizontal proximity. Returns 6 values
    /// (0 where a column produced no readable number).
    /// </summary>
    private int[] ReadStatRow(Bitmap screen, int rowY)
    {
        // Multi-scale majority vote. No single upscale is best on the tiny E/A/D digits: 2× can
        // drop a digit (21→1) or miss a lone digit (6→0), while 4× recovers those but sometimes
        // ADDS a digit (9→90) or merges. Reading at several scales and taking the most common
        // value per column cancels these uncorrelated errors. Ties favour the lower scale (2×),
        // the most reliable single pass for the big DMG/H/MIT numbers.
        var roi      = UiCoordinates.Teams_StatStrip(rowY);
        var nCols    = UiCoordinates.Teams_StatColumnCentersX.Length;
        var perScale = new List<int[]>();
        foreach (var scale in new[] { 2, 3, 4 })
        {
            try   { perScale.Add(SnapStatRow(ReadWords(roi: roi, source: screen,
                                  mode: PageSegMode.SingleLine, scale: scale))); }
            catch { /* skip this scale */ }
        }
        if (perScale.Count == 0) return new int[nCols];

        // The E/A/D and DMG/H/MIT columns fail in OPPOSITE ways, so they're voted differently.
        const int FirstWideCol = 3;   // 0–2 = E/A/D (narrow); 3–5 = DMG/H/MIT (wide)
        var result = new int[nCols];
        for (var c = 0; c < nCols; c++)
        {
            if (c >= FirstWideCol)
            {
                // Wide numeric column (DMG/HEAL/MIT): the OCR error here is DROPPING digits — a
                // comma-split big number ("13,868") fragments at some scales into a short value
                // ("11") that TWO scales can coincidentally agree on, while another scale reads it
                // whole. So prefer the LONGEST (most complete) read. A lone read is trusted ONLY if
                // it's a substantial number (≥3 digits): a real big number that just one scale got
                // (e.g. "9,989" read at 2× but garbled at 3×/4×) must NOT be discarded as 0, whereas
                // a lone 1–2 digit read is noise on an empty cell.
                var nz = new List<int>();
                foreach (var row in perScale)
                    if (row[c] > 0 && row[c].ToString().Length <= 6) nz.Add(row[c]);

                if (nz.Count == 0)
                {
                    result[c] = 0;
                }
                else
                {
                    var maxLen = nz.Max(v => v.ToString().Length);
                    result[c] = (nz.Count == 1 && maxLen <= 2)
                        ? 0   // lone tiny read → noise on an empty cell
                        : nz.Where(v => v.ToString().Length == maxLen)
                            .GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;
                }
                continue;
            }

            // Narrow column (E/A/D): tiny 1–2 digit numbers. Two opposite errors: 2× DROPS a digit
            // (14→4), while 3×/4× upscaling adds glyph artifacts that SUBSTITUTE same-length digits
            // ("7"→"V4"→4) — and both higher scales often make the SAME substitution, so a plain
            // majority picks their wrong value over a correct 2×. Resolution: 2× has the cleanest
            // glyph shape, so trust it when it read something — UNLESS ≥2 scales agree on a LONGER
            // value (then 2× dropped a digit → take the longer). If 2× read 0 (missed), rescue from
            // ≥2 agreeing scales.
            var counts = new Dictionary<int, int>();
            foreach (var row in perScale)
                if (row[c] > 0) counts[row[c]] = counts.GetValueOrDefault(row[c]) + 1;

            var bestVal = 0;
            var bestCnt = 0;
            foreach (var (v, n) in counts)
                if (n > bestCnt || (n == bestCnt && v.ToString().Length > bestVal.ToString().Length))
                    { bestVal = v; bestCnt = n; }

            var baseline = perScale[0][c];   // 2× — cleanest glyph
            if (baseline > 0)
                result[c] = (bestCnt >= 2 && bestVal.ToString().Length > baseline.ToString().Length)
                    ? bestVal       // 2× dropped a digit; the agreeing scales read it whole
                    : baseline;     // trust 2×'s glyph over a same-length higher-scale substitution
            else
                result[c] = bestCnt >= 2 ? bestVal : 0;   // 2× missed → rescue from agreement
        }

        // Template-based override: the fixed stat font reads more reliably glyph-by-glyph than the
        // general LSTM. Replace a column's value with DigitOcr's when it confidently reads a number;
        // empty cells (no glyphs → value 0) and low-confidence reads keep the Tesseract result.
        if (_digits.IsReady)
        {
            var cy = rowY + 49;                              // strip is centred at rowY+49
            for (var c = 0; c < nCols; c++)
            {
                var half = c < FirstWideCol ? 36 : 60;       // narrow E/A/D vs wide DMG/H/MIT
                var rect = new Rectangle(UiCoordinates.Teams_StatColumnCentersX[c] - half, cy - 26, half * 2, 52);
                var (val, conf) = _digits.ReadCell(screen, rect);
                if (val > 0 && conf >= 0.78) result[c] = val;
            }
        }
        return result;
    }

    /// <summary>Diagnostic: the per-scale (2×/3×/4×) snapped 6 stat values [E,A,D,DMG,H,MIT] for the
    /// row whose top is <paramref name="rowY"/>. Exposes what each upscale read at each column so the
    /// multi-scale vote can be audited against a real frame.</summary>
    public int[][] DebugStatRowScales(Bitmap screen, int rowY)
    {
        var res = new List<int[]>();
        foreach (var scale in new[] { 2, 3, 4 })
        {
            try   { res.Add(SnapStatRow(ReadWords(screen, UiCoordinates.Teams_StatStrip(rowY),
                            PageSegMode.SingleLine, null, scale))); }
            catch { res.Add(new int[UiCoordinates.Teams_StatColumnCentersX.Length]); }
        }
        return res.ToArray();
    }

    /// <summary>Merges comma-split fragments and snaps each detected number to its nearest stat
    /// column centre, returning one value per column (0 where none read).</summary>
    private static int[] SnapStatRow(List<(string Word, Rectangle Box)> words)
    {
        var centers = UiCoordinates.Teams_StatColumnCentersX;
        var result  = new int[centers.Length];

        // Keep only digit-bearing tokens (normalised), left→right.
        var nums = new List<(int Left, int Right, string Digits)>();
        foreach (var (w, box) in words.OrderBy(x => x.Box.X))
        {
            var digits = Regex.Replace(
                NormaliseOcrNoise(w).Replace("l", "1").Replace("I", "1"), @"[^\d]", "");
            if (digits.Length == 0) continue;
            nums.Add((box.Left, box.Right, digits));
        }

        // Merge fragments separated by a tiny gap (comma-split within one number). Real column
        // separation is ≥74 px, so a ≤22 px gap can only be an intra-number split.
        var merged = new List<(int Left, int Right, string Digits)>();
        foreach (var n in nums)
        {
            if (merged.Count > 0 && n.Left - merged[^1].Right <= 22)
            {
                var last = merged[^1];
                merged[^1] = (last.Left, n.Right, last.Digits + n.Digits);
            }
            else merged.Add(n);
        }

        // Snap each number to the nearest column centre.
        foreach (var (left, right, digits) in merged)
        {
            if (!int.TryParse(digits, out var val)) continue;
            var cx    = (left + right) / 2;
            var col   = -1;
            var bestD = int.MaxValue;
            for (var i = 0; i < centers.Length; i++)
            {
                var dist = Math.Abs(cx - centers[i]);
                if (dist < bestD) { bestD = dist; col = i; }
            }
            if (col >= 0 && bestD <= 70) result[col] = val;
        }
        return result;
    }

    /// <summary>
    /// OCRs a region of the screenshot and returns the raw text. Applies pre-processing
    /// (grayscale → threshold → 2× upscale) before Tesseract.
    /// </summary>
    public string ReadRegion(Bitmap source, Rectangle roi)
    {
        using var crop       = ClampAndCrop(source, roi);
        using var processed  = PreProcess(crop);
        using var pixData    = BitmapToPix(processed);
        using var page       = Engine.Process(pixData, PageSegMode.SingleLine);
        return page.GetText() ?? string.Empty;
    }

    /// <summary>
    /// OCRs a region containing solid <b>white</b> text on an arbitrary (often bright, gradient)
    /// background by first isolating near-white pixels into clean black-on-white before Tesseract.
    /// The Summary map title is always solid white caps, but its menu background animates through
    /// bright purple gradients; on a bright frame the LSTM's adaptive binarisation over the wide
    /// strip mangles even crisp text ("HAVANA" → "g FAT.", "DORADO" → "(pe 7.10 0)"). Masking to
    /// white text removes the gradient entirely, so the read is background-independent.
    /// </summary>
    public string ReadRegionWhiteText(Bitmap source, Rectangle roi, byte threshold = 190, int scale = 3)
    {
        using var crop      = ClampAndCrop(source, roi);
        using var processed = PreProcessWhiteText(crop, scale: scale, threshold: threshold);
        using var pixData   = BitmapToPix(processed);
        using var page      = Engine.Process(pixData, PageSegMode.SingleLine);
        return page.GetText() ?? string.Empty;
    }

    /// <summary>
    /// OCRs a multi-line region as a single block (newlines preserved). Use for areas
    /// that contain several lines, e.g. the Summary info box.
    /// </summary>
    public string ReadRegionBlock(Bitmap source, Rectangle roi)
    {
        using var crop       = ClampAndCrop(source, roi);
        using var processed  = PreProcess(crop);
        using var pixData    = BitmapToPix(processed);
        using var page       = Engine.Process(pixData, PageSegMode.SingleBlock);
        return page.GetText() ?? string.Empty;
    }

    /// <summary>
    /// OCRs a region as a block and returns each recognised word with its bounding box
    /// mapped back to <b>screen-space</b> coordinates (accounting for the 2× preprocessing
    /// upscale and the ROI offset).
    /// </summary>
    public List<(string Word, Rectangle Box)> ReadWords(
        Bitmap source, Rectangle roi, PageSegMode mode = PageSegMode.SingleBlock,
        string? charWhitelist = null, int scale = 2, bool whiteText = false)
    {
        var clamped = Rectangle.Intersect(new Rectangle(0, 0, source.Width, source.Height), roi);
        if (clamped.IsEmpty) return new();

        using var crop      = source.Clone(clamped, source.PixelFormat);
        // whiteText: isolate solid-white text from a (possibly bright/gradient) background first —
        // the Personal stat cards are semi-transparent so numbers over the bright menu gradient
        // otherwise mis-binarise to garbage (the same failure as the map/hero names).
        using var processed = whiteText ? PreProcessWhiteText(crop, scale, 190) : PreProcess(crop, scale);
        using var pixData   = BitmapToPix(processed);

        // Optional char whitelist (e.g. digits-only for numeric regions). NOTE: the LSTM engine
        // (EngineMode.LstmOnly) ignores tessedit_char_whitelist — it only takes effect with the
        // legacy/Tesseract engine. Kept for callers that may use a legacy-capable engine.
        if (charWhitelist is not null) Engine.SetVariable("tessedit_char_whitelist", charWhitelist);
        using var page = Engine.Process(pixData, mode);
        if (charWhitelist is not null) Engine.SetVariable("tessedit_char_whitelist", string.Empty);

        var results = new List<(string, Rectangle)>();
        using var iter = page.GetIterator();
        iter.Begin();
        do
        {
            var word = iter.GetText(PageIteratorLevel.Word);
            if (string.IsNullOrWhiteSpace(word)) continue;
            if (!iter.TryGetBoundingBox(PageIteratorLevel.Word, out var r)) continue;

            // r is in the scale×-upscaled crop space → divide by scale, then offset by ROI origin.
            var box = new Rectangle(
                clamped.X + r.X1 / scale,
                clamped.Y + r.Y1 / scale,
                Math.Max(1, (r.X2 - r.X1) / scale),
                Math.Max(1, (r.Y2 - r.Y1) / scale));
            results.Add((word, box));
        }
        while (iter.Next(PageIteratorLevel.Word));

        return results;
    }

    /// <summary>
    /// Locates label text within a search region and returns the screen-space centre of the
    /// matched word (single-token keyword) — the basis of resolution-independent navigation:
    /// find a button by its label and click exactly where that label actually is.
    ///
    /// For a single word (e.g. "HISTORY") the returned point is the centre of that word's
    /// bounding box. For a multi-word phrase (e.g. "VIEW GAME") it confirms presence by
    /// matching the concatenated word stream and returns the centre of the matched span.
    /// Returns null if not found.
    /// </summary>
    public Point? FindTextCenter(Bitmap screen, string keyword, Rectangle searchArea,
                                 int bandHeight = 46, int step = 22)
    {
        var needle = keyword.ToUpperInvariant().Trim();
        var tokens = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bottom = Math.Min(searchArea.Bottom, screen.Height);

        // Sweep narrow horizontal bands (isolating one text line at a time gives far more
        // reliable OCR than a single block pass over the whole region), and within each band
        // use word bounding boxes to return the matched label's *actual* screen position.
        for (var y = searchArea.Top; y + bandHeight <= bottom; y += step)
        {
            var band = new Rectangle(searchArea.X, y, searchArea.Width, bandHeight);
            List<(string Word, Rectangle Box)> words;
            try { words = ReadWords(screen, band); }
            catch { continue; }
            if (words.Count == 0) continue;

            if (tokens.Length == 1)
            {
                foreach (var (w, box) in words)
                    if (w.ToUpperInvariant().Contains(needle, StringComparison.Ordinal))
                        return new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
            }
            else
            {
                for (var i = 0; i + tokens.Length <= words.Count; i++)
                {
                    var match = true;
                    for (var t = 0; t < tokens.Length; t++)
                        if (!words[i + t].Word.ToUpperInvariant().Contains(tokens[t], StringComparison.Ordinal))
                        { match = false; break; }
                    if (!match) continue;

                    var first = words[i].Box;
                    var last  = words[i + tokens.Length - 1].Box;
                    return new Point((first.Left + last.Right) / 2, (first.Top + last.Bottom) / 2);
                }
            }
        }
        return null;
    }

    // ── Image preprocessing ───────────────────────────────────────────────

    private static Bitmap ClampAndCrop(Bitmap src, Rectangle roi)
    {
        var bounds  = new Rectangle(0, 0, src.Width, src.Height);
        var clamped = Rectangle.Intersect(bounds, roi);
        if (clamped.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(roi), "ROI outside source bounds.");
        return src.Clone(clamped, src.PixelFormat);
    }

    /// <summary>
    /// Prepares a crop for Tesseract.
    ///
    /// Previous approach (grayscale → fixed-threshold binarisation → inversion) was
    /// destroying JPEG-compressed OW screenshots: compression artefacts at text edges
    /// caused the threshold to produce uniform noise rather than clean text.
    ///
    /// Current approach: greyscale-only + <paramref name="scale"/>× upscale with high-quality
    /// bicubic interpolation. Tesseract's LSTM engine performs its own adaptive binarisation
    /// internally, which is far more robust on real-world screenshots. A larger scale (3–4×) gives
    /// the LSTM more pixels on tiny isolated digits (the Teams E/A/D columns), improving them.
    /// </summary>
    private static Bitmap PreProcess(Bitmap src, int scale = 2)
    {
        // 1. Greyscale (reduces colour noise, keeps luminance information intact).
        var grey = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(grey))
        {
            var cm = new System.Drawing.Imaging.ColorMatrix(new float[][]
            {
                new[] { 0.299f, 0.299f, 0.299f, 0, 0 },
                new[] { 0.587f, 0.587f, 0.587f, 0, 0 },
                new[] { 0.114f, 0.114f, 0.114f, 0, 0 },
                new[] { 0f,     0f,     0f,     1, 0 },
                new[] { 0f,     0f,     0f,     0, 1 },
            });
            var attrs = new System.Drawing.Imaging.ImageAttributes();
            attrs.SetColorMatrix(cm);
            g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height),
                0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        }

        // 2. Upscale with high-quality bicubic — smooths JPEG artefacts and gives
        //    Tesseract more pixels per character to work with.
        var scaled = new Bitmap(grey.Width * scale, grey.Height * scale, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode   = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(grey, 0, 0, scaled.Width, scaled.Height);
        }
        grey.Dispose();
        return scaled;
    }

    /// <summary>
    /// Prepares a crop of solid white text for Tesseract by thresholding luminance: near-white
    /// pixels (the text) become black, everything else white → clean black-on-white, then upscale.
    /// Unlike <see cref="PreProcess"/> this DOES binarise — safe here because the target is pure
    /// white text (≈255) sitting well above any background luminance, so a single fixed threshold
    /// separates them cleanly regardless of the (bright, gradient) background colour.
    /// </summary>
    private static Bitmap PreProcessWhiteText(Bitmap src, int scale, byte threshold)
    {
        var mask = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < src.Height; y++)
            for (var x = 0; x < src.Width; x++)
            {
                var px  = src.GetPixel(x, y);
                var lum = 0.299 * px.R + 0.587 * px.G + 0.114 * px.B;
                var c   = lum >= threshold ? Color.Black : Color.White;   // white text → black on white
                mask.SetPixel(x, y, c);
            }

        var scaled = new Bitmap(mask.Width * scale, mask.Height * scale, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode   = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(mask, 0, 0, scaled.Width, scaled.Height);
        }
        mask.Dispose();
        return scaled;
    }

    private static Pix BitmapToPix(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return Pix.LoadFromMemory(ms.ToArray());
    }

    // ── Parsing helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Normalises common Tesseract confusions in the OW UI font before parsing numbers.
    /// <list type="bullet">
    ///   <item><c>|</c>, <c>l</c>, <c>I</c> → <c>1</c> (vertical strokes misread)</item>
    ///   <item><c>O</c> → <c>0</c> in purely numeric contexts (handled per call-site)</item>
    ///   <item><c>V5</c> → <c>VS</c> in score strings</item>
    /// </list>
    /// </summary>
    private static string NormaliseOcrNoise(string raw)
        => raw.Replace("|", "1");

    /// <summary>
    /// Parses "· FINAL SCORE: 2 VS 1" into the two scores. Returns false when no "X VS Y"
    /// could be read at all (so the caller can avoid fabricating a 0–0 draw — see ParseOutcome).
    /// </summary>
    private static bool ParseScore(string raw, out int my, out int enemy)
    {
        my = enemy = 0;
        var n = NormaliseOcrNoise(raw);

        // Isolate the score line (everything after "SCORE" up to the next newline) before applying
        // digit look-alike fixes, so we don't corrupt letters in the other info-box lines.
        var idx = n.IndexOf("SCORE", StringComparison.OrdinalIgnoreCase);
        var seg = idx >= 0 ? n[(idx + "SCORE".Length)..] : n;
        var nl  = seg.IndexOf('\n');
        if (nl >= 0) seg = seg[..nl];

        // The score font's "0" reliably OCRs as the letter O/o/Q (a leading "0 VS 1" reads
        // "O VS 1"); "1" can read as I/l. Map those look-alikes to digits within the score line
        // ONLY, THEN match. Without this a shutout score (my team scored 0) yields no match →
        // 0 vs 0 → a false DRAW.
        seg = Regex.Replace(seg, "[OoQ]", "0");
        seg = Regex.Replace(seg, "[Il]", "1");
        seg = Regex.Replace(seg, @"V[5S]", "VS", RegexOptions.IgnoreCase);

        var m = Regex.Match(seg, @"(\d+)\s*VS\s*(\d+)", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        my    = int.Parse(m.Groups[1].Value);
        enemy = int.Parse(m.Groups[2].Value);
        return true;
    }

    private static DateTime ParseMatchDate(string raw)
    {
        // Searches anywhere in the block for "MM/DD/YY [sep] HH:MM".
        // Separator may OCR as -, –, _, or be dropped entirely.
        var n = NormaliseOcrNoise(raw);
        var m = Regex.Match(n, @"(\d{2}/\d{2}/\d{2})\s*[-–_]?\s*(\d{1,2}:\d{2})");
        if (!m.Success) return DateTime.MinValue;
        var dateStr = $"{m.Groups[1].Value} {m.Groups[2].Value}";
        return DateTime.TryParseExact(dateStr, "MM/dd/yy H:mm",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt : DateTime.MinValue;
    }

    /// <summary>
    /// Extracts the game length from the info block. There are two MM:SS-like tokens
    /// (the date's time and the game length); the game length is the last one.
    /// </summary>
    private static TimeSpan ParseGameLength(string block)
    {
        var n = NormaliseOcrNoise(block);
        var matches = Regex.Matches(n, @"(\d{1,2}):(\d{2})");
        if (matches.Count == 0) return TimeSpan.Zero;
        var last = matches[^1];
        return new TimeSpan(0, int.Parse(last.Groups[1].Value), int.Parse(last.Groups[2].Value));
    }

    /// <summary>
    /// Extracts the game mode word from the info block (tolerant of label OCR noise:
    /// matches on "MODE" then the following all-caps word).
    /// </summary>
    private static string ParseGameMode(string block)
    {
        var up = block.ToUpperInvariant();
        var m  = Regex.Match(up, @"MODE\s*[:.\-]?\s*([A-Z]{3,})");
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    /// <summary>
    /// Determines the outcome. Tries the (large-italic, often noisy) banner OCR first,
    /// falling back to the score. Returns "VICTORY" / "DEFEAT" / "DRAW".
    /// </summary>
    private static string ParseOutcome(string rawBanner, int myScore, int enemyScore, bool scoreKnown)
    {
        var up = rawBanner.ToUpperInvariant();
        // Banner is one word; match on the leading letters that survive OCR.
        if (up.StartsWith("VI") || up.Contains("CTOR")) return "VICTORY";
        if (up.StartsWith("DE") || up.Contains("FEAT")) return "DEFEAT";
        if (up.StartsWith("DR") || up.Contains("RAW"))  return "DRAW";

        // Fallback: derive from score (player's team is listed first). If the score itself
        // couldn't be read, report UNKNOWN rather than fabricating a DRAW — a 0–0 default would
        // silently mislabel shutout losses (and a true 0–0 draw is rare but real, so we only
        // call DRAW when the score was actually read as equal).
        if (!scoreKnown) return "UNKNOWN";
        if (myScore > enemyScore) return "VICTORY";
        if (myScore < enemyScore) return "DEFEAT";
        return "DRAW";
    }

    private static TimeSpan ParseTimeSpan(string raw)
    {
        // "14:39" or "GAME LENGTH: 14:39"
        var n = NormaliseOcrNoise(raw);
        var m = Regex.Match(n, @"(\d+):(\d{2})");
        return m.Success
            ? new TimeSpan(0, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value))
            : TimeSpan.Zero;
    }

    private static int ParsePercent(string raw)
    {
        var n = NormaliseOcrNoise(raw);
        var m = Regex.Match(n, @"(\d+)%?");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    private static int ParseInt(string raw)
    {
        // Replace vertical-stroke look-alikes (|, l, I) with 1 before stripping non-digits.
        // These are the most common Tesseract confusions on OW's stat font.
        var n = NormaliseOcrNoise(raw)
            .Replace("l", "1")   // lowercase L
            .Replace("I", "1");  // uppercase i  (safe: we're in a pure-number region)
        var cleaned = Regex.Replace(n, @"[^\d]", "");
        return cleaned.Length > 0 && int.TryParse(cleaned, out var v) ? v : 0;
    }

    private static int EstimateTeamSize(int vsDividerY)
    {
        // vsDividerY = Teams_FirstRowY + teamSize * Teams_RowHeight
        //   5v5: 308 + 5×90 = 758  → approx = 5  → teamSize 5
        //   6v6: 308 + 6×90 = 848  → approx = 6  → teamSize 6
        var approx = (vsDividerY - UiCoordinates.Teams_FirstRowY) / UiCoordinates.Teams_RowHeight;
        return approx >= 6 ? 6 : 5;
    }

    public void Dispose() => _engine?.Dispose();
}
