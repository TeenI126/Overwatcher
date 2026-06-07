using System.Drawing;

namespace OwTracker.Core.Services;

/// <summary>
/// Pixel coordinates for mouse-click targets at 2560×1440 with default OW UI scale.
/// These are starting estimates derived from screenshots; the Calibration tool (future
/// milestone) will let the user correct them and save overrides to calibration.json.
///
/// All points are screen-absolute (not window-relative) and assume OW is maximised/fullscreen
/// on the primary 2560×1440 display.
/// </summary>
public static class UiCoordinates
{
    // ── Escape menu ───────────────────────────────────────────────────────

    /// <summary>Centre of the "CAREER PROFILE" button in the escape menu.</summary>
    public static readonly Point EscMenu_CareerProfile = new(829, 398);

    // ── Career Profile tabs ───────────────────────────────────────────────

    /// <summary>Centre of the "HISTORY" tab at the top of the Career Profile page.</summary>
    public static readonly Point CareerProfile_HistoryTab = new(449, 33);

    // ── History sidebar ───────────────────────────────────────────────────

    /// <summary>Centre of the "GAME REPORTS" item in the History left sidebar.</summary>
    public static readonly Point History_GameReportsSidebar = new(159, 341);

    // ── Game Reports list ─────────────────────────────────────────────────

    /// <summary>
    /// Centre of the "VIEW GAME REPORTS" button at the bottom of the match list.
    /// Click this after selecting a row to enter the match detail.
    /// </summary>
    // ── Game Reports list ─────────────────────────────────────────────────
    // Calibrated via Diagnostics_ScanGameReportsList sweep against 2560×1440.
    //
    // ⚠ SCROLL DEPENDENCY: the list is a scrollable menu.  HistoryScraper MUST
    //   send Ctrl+Home (or scroll to top) before using these fixed Y positions.
    //   All Y values below assume the list is scrolled to its very top.
    //
    // Row structure (from sweep):
    //   First row centre ≈ y=530  (queue label UNRANKED at y=515, map name at y=530)
    //   Row height ≈ 90 px         (queue labels at y=530/620/710/800 → Δ=90)
    //
    // Queue label location (from sweep):
    //   "UNRANKED" / "COMPETITIVE" appear at x≈1440-1640, far-right column.
    //   ROI starts at x=1330 with width 280 to also capture "COMPETITIVE - ROLE QUEUE".

    /// <summary>Y-centre of row 0 when the list is scrolled to its very top.</summary>
    public const int GameReportsList_FirstRowCenterY = 530;

    /// <summary>Pixel height of each match row in the Game Reports list.</summary>
    public const int GameReportsList_RowHeight = 90;

    /// <summary>
    /// Y-centre of a match row (0-based index from the top of the list).
    /// Requires the list to be scrolled to its top first.
    /// </summary>
    public static int GameReportsList_RowY(int rowIndex) =>
        GameReportsList_FirstRowCenterY + rowIndex * GameReportsList_RowHeight;

    /// <summary>X-centre to click for a match row (over the map-name / result area).</summary>
    public static readonly int GameReportsList_RowX = 728;

    /// <summary>
    /// Point to hover the cursor over the TOP (most recent) match row, used to anchor
    /// keyboard navigation. Arrow keys from cold focus don't control the list; hovering the
    /// top row first highlights it (game 0) and gives the list keyboard focus, after which
    /// Down-arrow walks the highlight down the list (auto-scrolling for off-screen rows).
    /// </summary>
    public static readonly Point GameReportsList_TopRowHover = new(728, 545);

    /// <summary>
    /// ROI capturing both queue-tier and queue-type labels for a match row
    /// (e.g. "UNRANKED / QUICK PLAY" or "COMPETITIVE / ROLE QUEUE").
    /// Queue labels are in the far-right column of the list.
    /// ROI is 70 px tall — line 1 ("UNRANKED") starts at ~rowCenter-15,
    /// line 2 ("QUICK PLAY") at ~rowCenter+15; 70 px gives 28 px margin on each side.
    /// Read with ReadRegionBlock (SingleBlock) — not SingleLine.
    /// </summary>
    public static Rectangle GameReportsList_QueueRoi(int rowIndex) =>
        new(1330, GameReportsList_RowY(rowIndex) - 30, 280, 70);

    /// <summary>
    /// Centre of the "VIEW GAME REPORT" button below the match list.
    /// Calibrated: text "VIEW GAME REPORT" appears at x≈1100–1500, y≈1300–1320.
    /// TODO: verify exact Y on first live run (button is fixed footer, not part of
    /// the scrollable area, so it should always be at this position).
    /// </summary>
    public static readonly Point GameReportsList_ViewButton = new(1260, 1310);

    // ── Match detail tabs ─────────────────────────────────────────────────

    public static readonly Point MatchDetail_SummaryTab  = new(117, 33);
    public static readonly Point MatchDetail_TeamsTab    = new(208, 33);
    public static readonly Point MatchDetail_PersonalTab = new(301, 33);

    // ── Summary tab — right info panel ───────────────────────────────────
    // Coordinates verified via the Diagnostics_ScanSummaryRightPanel sweep against
    // 2560×1440 screenshots. The info box sits low-right (x≈1700, y≈890-1035);
    // the map name and VICTORY/DEFEAT banner are large fonts above the box.

    /// <summary>ROI containing the map name (large bold caps, above the map thumbnail).
    /// Left-aligned with the thumbnail card (text starts ≈x1736); width is generous (600,
    /// reaching x≈2195) so long names — "WATCHPOINT: GIBRALTAR", "ANTARCTIC PENINSULA",
    /// "THRONE OF ANUBIS" — aren't clipped. The empty left margin (1595→1736) gives drift
    /// tolerance, and the band to the right at this Y is empty dark card-top (no noise).</summary>
    public static readonly Rectangle Summary_MapName    = new(1595, 238, 600, 52);

    /// <summary>ROI containing "VICTORY" / "DEFEAT" / "DRAW" (large italic banner).</summary>
    public static readonly Rectangle Summary_Outcome    = new(1700, 798, 360, 64);

    /// <summary>ROI containing "· FINAL SCORE: X VS Y".</summary>
    public static readonly Rectangle Summary_Score      = new(1700, 888, 360, 32);

    /// <summary>ROI containing "· DATE: MM/DD/YY - HH:MM".</summary>
    public static readonly Rectangle Summary_Date       = new(1700, 920, 360, 30);

    /// <summary>ROI containing "· GAME MODE: ESCORT" etc.</summary>
    public static readonly Rectangle Summary_GameMode   = new(1700, 962, 360, 30);

    /// <summary>ROI containing "· GAME LENGTH: MM:SS".</summary>
    public static readonly Rectangle Summary_GameLength = new(1700, 1004, 360, 32);

    /// <summary>
    /// The whole bullet-list info box (FINAL SCORE / DATE / GAME MODE / GAME LENGTH).
    /// Read as a single multi-line block — more robust than 4 tight per-line ROIs.
    /// </summary>
    public static readonly Rectangle Summary_InfoBox    = new(1700, 882, 372, 178);

    // ── Summary tab — Heroes Played cards (left panel) ───────────────────

    /// <summary>Number of hero cards visible on the Summary left panel (max 3 in one match).</summary>
    public const int Summary_MaxHeroCards = 3;

    // Card pitch recalibrated 2026-06 from summary.jpg: the three Heroes-Played cards are ~320 px
    // apart (the old 248 left card 1's playtime reading an empty band → 00:00). The hero NAME is
    // a stylised italic that OCRs to garbage ("ECHO"→"rin") — names come from the Personal sub-tab
    // sidebar instead (OcrEngine reads them); only the play-time/percent are taken from here.
    // Stride 322 and the playtime/percent Y/heights below are calibrated from the summary.jpg
    // sweep: percent at y=434/756, playtime at y=518/840 for cards 0/1 → Δ=322.
    private const int Summary_CardStride = 322;

    /// <summary>
    /// ROI of the hero-name text within a hero card. NOTE: unreliable (italic font) — kept for
    /// diagnostics only; real hero names come from the Personal sidebar sub-tabs.
    /// cardIndex 0 = topmost card (typically highest play-time hero).
    /// </summary>
    public static Rectangle Summary_HeroName(int cardIndex) =>
        new(235, 355 + cardIndex * Summary_CardStride, 215, 40);

    /// <summary>ROI of the play-time value ("MM:SS") within a hero card.</summary>
    public static Rectangle Summary_HeroPlayTime(int cardIndex) =>
        new(300, 508 + cardIndex * Summary_CardStride, 150, 40);

    /// <summary>ROI of the percent-played value ("76%") within a hero card.</summary>
    public static Rectangle Summary_HeroPercent(int cardIndex) =>
        new(290, 424 + cardIndex * Summary_CardStride, 140, 36);

    // ── Teams tab ─────────────────────────────────────────────────────────
    // Calibrated via Diagnostics_ScanTeamsTable sweep against 2560×1440 screenshots.
    // Row centres found at y≈353/443/533/623/713 (my team) and 913/1003/1093/1183 (enemy).
    // → RowHeight=90, FirstRowY=308 (=first_centre−45).  VS gap = 110 px.
    // Column positions from y=280 header text ("E A"@x1180, "DMG"@x1420, "MIT"@x1660)
    // and confirmed by data values ("19,641"@x1420, "12,297"@x1580, "5,376"@x1740, etc.).

    /// <summary>Left X of the hero-portrait column in the Teams table.</summary>
    public const int Teams_PortraitX = 755;

    /// <summary>Width/height of each circular hero portrait crop.</summary>
    public const int Teams_PortraitSize = 58;

    /// <summary>Y of the first (top) player row in the Teams table.</summary>
    // Recalibrated 2026-06: row-0 stat-text centre measured at y≈360 (2560×1440).
    // Stat ROI centre = rowY + 32 + 34/2 = rowY + 49, so rowY (row top) = 360 − 49 = 311.
    public const int Teams_FirstRowY = 311;

    /// <summary>Height of each player row.</summary>
    // Pitch ≈88: live frames vary between ~85 (Hybrid/Escort) and ~90 (Control/Flashpoint render a
    // taller table), so 88 is the best compromise. The enemy block is additionally re-anchored to
    // its detected red-background top (ScreenDetector.FindEnemyTeamTopY) to absorb the rest.
    public const int Teams_RowHeight = 88;

    /// <summary>
    /// Pixels below the VS-divider Y to the top of the first enemy-team row.
    /// The gap contains the decorative "VS" banner.
    /// </summary>
    // Recalibrated 2026-06: enemy row-0 stat centre measured at y≈916.
    // EnemyRowY(0) = vsDivider(=311+5×86=741) + VsGap + 49 = 916  →  VsGap = 126.
    public const int Teams_VsGapHeight = 126;

    /// <summary>Portrait ROI for a given row Y (top of row).</summary>
    public static Rectangle Teams_PortraitRoi(int rowY) =>
        new(Teams_PortraitX, rowY + (Teams_RowHeight - Teams_PortraitSize) / 2,
            Teams_PortraitSize, Teams_PortraitSize);

    // Column ROIs.  Recalibrated 2026-06 against teams.jpg (2560×1440); the previous E/A/D
    // x-positions were ~70 px too far left (they read the gap between username and the E
    // column → garbage), which is why E/A/D "never" captured. Measured stat-column centres:
    //   E≈1247  A≈1321  D≈1395  DMG≈1513  H≈1644  MIT≈1774.
    // E/A/D are 1–2 digit numbers (narrow ROI, ~64 px); DMG/H/MIT are up to 6-char numbers
    // ("10,879") so need a wider ROI (~130 px) centred on the column.
    // y_offset=+32 keeps the ROI in the lower-middle of the row, clearing the role-icon band.
    public static Rectangle Teams_ColE(int rowY)   => new(1215, rowY + 32, 64,  34);
    public static Rectangle Teams_ColA(int rowY)   => new(1289, rowY + 32, 64,  34);
    public static Rectangle Teams_ColD(int rowY)   => new(1363, rowY + 32, 64,  34);
    public static Rectangle Teams_ColDmg(int rowY) => new(1448, rowY + 32, 130, 34);
    public static Rectangle Teams_ColH(int rowY)   => new(1584, rowY + 32, 120, 34);
    public static Rectangle Teams_ColMit(int rowY) => new(1709, rowY + 32, 130, 34);

    /// <summary>
    /// Wide ROI spanning all six stat columns (E…MIT) for one row. Read as a single strip
    /// and split by word bounding boxes — Tesseract reads numbers sharing one baseline far
    /// more reliably than six isolated single-cell ROIs (which misread lone digits as garbage
    /// like "Ke)" for "5"). Each detected number is snapped to its nearest column centre.
    /// </summary>
    // Height 52 (was 38), centred on rowY+49: the extra vertical span tolerates the few px of
    // residual per-map row drift without overlapping the neighbouring row's numbers.
    public static Rectangle Teams_StatStrip(int rowY) => new(1205, rowY + 23, 660, 52);

    /// <summary>Screen-X centres of the six stat columns, in order: E, A, D, DMG, H, MIT.</summary>
    public static readonly int[] Teams_StatColumnCentersX = { 1247, 1321, 1395, 1513, 1644, 1774 };

    // Username — x=820 starts just past the portrait edge (PortraitX+PortraitSize≈813).
    // OCR still picks up a few portrait-adjacent artefacts; ExtractTeamBlock strips
    // any leading non-alphanumeric prefix after reading.
    public static Rectangle Teams_Username(int rowY) => new(820, rowY + 28, 240, 34);

    /// <summary>Y-coordinate of the top of a my-team player row (0-based index).</summary>
    public static int Teams_MyTeamRowY(int rowIndex) =>
        Teams_FirstRowY + rowIndex * Teams_RowHeight;

    /// <summary>
    /// Y-coordinate of the top of an enemy-team player row, given the VS-divider Y
    /// returned by <c>ScreenDetector.FindVsDividerY</c>.
    /// </summary>
    public static int Teams_EnemyRowY(int vsDividerY, int rowIndex) =>
        vsDividerY + Teams_VsGapHeight + rowIndex * Teams_RowHeight;

    // ── Team-size-aware row centres (5v5 vs 6v6) ──────────────────────────
    // 6v6 packs 6 tighter rows: pitch ≈80 (vs 88) and the table starts a touch higher. Team size
    // comes from the queue label ("6V6"), the reliable signal — the stat strip is centred on each
    // returned Y. Calibrated from a live 6v6 frame: my centres ≈340,420,500…; enemy red-anchored.

    /// <summary>Row pitch for the given team size.</summary>
    public static int Teams_RowPitch(int teamSize) => teamSize >= 6 ? 80 : Teams_RowHeight; // 88

    /// <summary>Stat-row centres for MY (top) team — fixed grid from the stable table top.</summary>
    public static IReadOnlyList<int> Teams_MyCentres(int teamSize)
    {
        var first  = teamSize >= 6 ? 340 : 360;   // 5v5 row-0 centre 360; 6v6 starts ~20 px higher
        var pitch  = Teams_RowPitch(teamSize);
        var c = new int[teamSize];
        for (var i = 0; i < teamSize; i++) c[i] = first + i * pitch;
        return c;
    }

    /// <summary>Stat-row centres for the ENEMY (bottom) team, anchored to its detected red top
    /// (<see cref="ScreenDetector.FindEnemyTeamTopY"/> returns the row-0 TOP; centre = top+49).</summary>
    public static IReadOnlyList<int> Teams_EnemyCentres(int enemyTopY, int teamSize)
    {
        var pitch = Teams_RowPitch(teamSize);
        var c = new int[teamSize];
        for (var i = 0; i < teamSize; i++) c[i] = enemyTopY + 49 + i * pitch;
        return c;
    }

    /// <summary>Fallback enemy row-0 TOP when red detection fails (rare).</summary>
    public static int Teams_EnemyFallbackTop(int teamSize) => teamSize >= 6 ? 881 : 867;

    // ── Personal tab → ALL HEROES combined-stat cards ─────────────────────
    // A 2×3 card grid showing MY totals. Calibrated from the live All-Heroes word dump
    // (2560×1440): value rows at y≈395/715/1034; right-column values at x≈1480.
    //   Right column (HERO DAMAGE DONE / HEALING DONE / DAMAGE MITIGATED): large multi-digit
    //   numbers, read per-cell with these ROIs (enough digits for the LSTM to read in isolation).
    //   Left column (ELIMINATIONS / ASSISTS / DEATHS): small 1–2 digit numbers that DON'T OCR in
    //   isolation — OcrEngine.ReadCardRow reads the whole value row as a band instead. The ROI is
    //   clear of the label + "AVG PER 10 MIN" line below each value.
    public static readonly Rectangle Personal_HeroDamage = new(1418,  367, 240, 58);
    public static readonly Rectangle Personal_Healing    = new(1418,  687, 240, 58);
    public static readonly Rectangle Personal_Mitigation = new(1418, 1006, 240, 58);

    // Personal-tab sidebar hero sub-tabs — the heroes I played, top→bottom by playtime, then a
    // blank slot or two, then "ALL HEROES". Calibrated from the All-Heroes frame sweep: clean
    // names at y≈310/410/510, stride 100. x starts at 70 to skip the button's left edge/highlight.
    // These give reliable hero NAMES (the Summary card names are unreadable italic), zipped by
    // order with the Summary cards' play-times.
    public const int Personal_MaxHeroTabs = 5;
    public static Rectangle Personal_HeroTab(int index) => new(45, 288 + index * 100, 300, 40);

    /// <summary>Point to click to select hero sub-tab <paramref name="index"/> in the sidebar.</summary>
    public static Point Personal_HeroTabClick(int index) => new(195, 308 + index * 100);

    /// <summary>
    /// ROI of the "PLAY TIME" value ("MM:SS") in a hero-specific Personal view (the top-left
    /// card when a single hero sub-tab is selected). Calibrated from personal.jpg (ECHO = 10:01
    /// at x≈740,y≈468). Clicking each hero tab and reading this captures EVERY hero's play-time,
    /// not just the top-3 the Summary shows.
    /// </summary>
    public static readonly Rectangle Personal_HeroPlayTime = new(740, 466, 190, 36);
}
