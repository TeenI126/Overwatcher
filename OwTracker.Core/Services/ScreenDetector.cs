using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OwTracker.Core.Services;

/// <summary>The screens the scraper can be at during navigation.</summary>
public enum GameScreen
{
    Unknown,
    EscapeMenu,
    CareerProfile,
    GameReportsList,
    MatchDetail,
}

/// <summary>
/// Identifies which OW screen is currently visible by OCR-ing small text anchors.
/// Each screen has unique, stable text that makes a reliable fingerprint.
/// </summary>
public sealed class ScreenDetector
{
    private readonly OcrEngine _ocr;

    public ScreenDetector(OcrEngine ocr) => _ocr = ocr;

    // ── Search regions (fractions of the captured bitmap) ──────────────────
    // Detection and click-targeting both sweep these regions for label text, so
    // exact pixel positions never need hard-coding. Verified against 2560×1440.

    private static Rectangle Frac(Bitmap b, float x, float y, float w, float h) =>
        new((int)(b.Width * x), (int)(b.Height * y),
            Math.Max(1, (int)(b.Width * w)), Math.Max(1, (int)(b.Height * h)));

    /// <summary>Top navigation tab bar (SUMMARY/TEAMS/PERSONAL, OVERVIEW/HISTORY).</summary>
    private static Rectangle TopTabBar(Bitmap b)      => Frac(b, 0.00f, 0.00f, 0.34f, 0.07f);
    /// <summary>Left sidebar where "GAME REPORTS" lives — x starts at 4.5% to skip the icon
    /// column (the icon + selection highlight corrupt OCR of the label otherwise).</summary>
    private static Rectangle Sidebar(Bitmap b)        => Frac(b, 0.045f, 0.26f, 0.13f, 0.26f);
    /// <summary>Centre-right button panel of the escape/lobby MENU.</summary>
    private static Rectangle EscMenuPanel(Bitmap b)   => Frac(b, 0.42f, 0.18f, 0.30f, 0.70f);
    /// <summary>Bottom "VIEW GAME REPORT" button bar — unique to the Game Reports list.</summary>
    private static Rectangle ViewReportBar(Bitmap b)  => Frac(b, 0.39f, 0.87f, 0.24f, 0.07f);
    /// <summary>Left sidebar of the match-detail PERSONAL tab (hero list + "ALL HEROES" button).</summary>
    private static Rectangle PersonalSidebar(Bitmap b) => Frac(b, 0.015f, 0.14f, 0.22f, 0.66f);

    /// <summary>Returns the screen currently visible in <paramref name="screenshot"/>.</summary>
    public GameScreen Detect(Bitmap screenshot)
    {
        // ── Match Detail (Summary/Teams/Personal tabs) ─────────────────────
        if (Find(screenshot, "SUMMARY", TopTabBar(screenshot)) is not null &&
            Find(screenshot, "TEAMS",   TopTabBar(screenshot)) is not null)
            return GameScreen.MatchDetail;

        // ── Game Reports list ──────────────────────────────────────────────
        // Anchor on the "VIEW GAME REPORT(S)" button at the bottom — it OCRs cleanly and
        // is unique to this screen. NOT the sidebar "REPORTS" label: that also appears
        // (unselected) on the History landing, which would misdetect as this screen.
        if (Find(screenshot, "VIEW GAME", ViewReportBar(screenshot)) is not null)
            return GameScreen.GameReportsList;

        // ── Career Profile (Overview/History tab bar) ──────────────────────
        if (Find(screenshot, "HISTORY",  TopTabBar(screenshot)) is not null &&
            Find(screenshot, "OVERVIEW", TopTabBar(screenshot)) is not null)
            return GameScreen.CareerProfile;

        // ── Escape / lobby menu (CAREER PROFILE button) ────────────────────
        if (Find(screenshot, "CAREER", EscMenuPanel(screenshot)) is not null)
            return GameScreen.EscapeMenu;

        return GameScreen.Unknown;
    }

    // ── Click-target locators (return screen-space centre to click) ─────────

    /// <summary>Locates the "CAREER PROFILE" button on the escape menu.</summary>
    public Point? FindCareerProfileButton(Bitmap b) =>
        _ocr.FindTextCenter(b, "CAREER", EscMenuPanel(b));

    /// <summary>Locates the "HISTORY" tab on the Career Profile screen.</summary>
    public Point? FindHistoryTab(Bitmap b) =>
        _ocr.FindTextCenter(b, "HISTORY", TopTabBar(b));

    /// <summary>Locates the "GAME REPORTS" item in the History sidebar (matches "REPORTS").</summary>
    public Point? FindGameReportsSidebar(Bitmap b) =>
        _ocr.FindTextCenter(b, "REPORTS", Sidebar(b));

    /// <summary>
    /// Locates a Match Detail tab ("SUMMARY" / "TEAMS" / "PERSONAL") by its label text and
    /// returns the centre of that word — robust to the tabs' actual pixel positions, which the
    /// old hard-coded coordinates got wrong (clicking the edge of the neighbouring tab).
    /// </summary>
    public Point? FindMatchDetailTab(Bitmap b, string label) =>
        _ocr.FindTextCenter(b, label, TopTabBar(b));

    /// <summary>Locates the "ALL HEROES" button in the PERSONAL-tab sidebar (matches "HEROES",
    /// the distinctive word — "ALL" is short and collides with other UI text).</summary>
    public Point? FindPersonalAllHeroes(Bitmap b) =>
        _ocr.FindTextCenter(b, "HEROES", PersonalSidebar(b));

    /// <summary>
    /// Determines the Y-coordinate of the VS gap between the two team blocks.
    ///
    /// Approach: check whether a 6th row has readable content via OCR.
    ///   • In a 5v5 game the row-6 position sits inside the VS gap → username is empty/symbols.
    ///   • In a 6v6 game the row-6 position is a real player row   → username has real letters.
    ///
    /// Returns the calculated gap start Y based on detected team size.
    /// </summary>
    public int FindVsDividerY(Bitmap screenshot)
    {
        // Row index 5 (the 6th row): exists in 6v6, is inside the VS gap in 5v5.
        var row5Y    = UiCoordinates.Teams_MyTeamRowY(5);
        var teamSize = 5; // default: 5v5

        try
        {
            // A real 6th player row has multi-digit STAT numbers (DMG/Healing/Mitigation are
            // 3–6 digits); the 5v5 VS gap — even with the decorative "VS" / divider line — never
            // produces a 3+ digit number. Keying on digits is far more reliable than the old
            // username-letters test, which flip-flopped (the gap's noise sometimes formed a 4+
            // letter run → false 6v6, shifting every enemy row down out of alignment).
            var strip = _ocr.ReadRegionBlock(screenshot, UiCoordinates.Teams_StatStrip(row5Y));
            if (System.Text.RegularExpressions.Regex.IsMatch(strip, @"\d{3,}"))
                teamSize = 6;
        }
        catch { /* OCR failure → keep 5v5 default */ }

        // Gap starts just after the last team row.
        return UiCoordinates.Teams_FirstRowY + teamSize * UiCoordinates.Teams_RowHeight;
    }

    /// <summary>
    /// Finds the Y of the TOP of the first enemy-team row by detecting the red row background
    /// (enemy rows are red-tinted; my team is blue; the VS gap is dark/neutral). This is robust to
    /// per-map vertical stretch of the scoreboard, which shifts the enemy block 15–30 px out of
    /// fixed ROIs (e.g. Control maps render a taller table). Returns <paramref name="fallbackTopY"/>
    /// if no red block is found.
    /// </summary>
    public int FindEnemyTeamTopY(Bitmap b, int fallbackTopY)
    {
        // Search the left portion of the table (portrait/name area — avoids the white stat digits)
        // over the Y band where the first enemy row can sit across maps.
        int x0 = (int)(b.Width * 0.295f), x1 = (int)(b.Width * 0.46f);
        int y0 = (int)(b.Height * 0.55f),  y1 = (int)(b.Height * 0.70f);
        var rect = Rectangle.FromLTRB(x0, y0, x1, y1);

        var data   = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = data.Stride;
        var buf    = new byte[Math.Abs(stride) * rect.Height];
        Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        b.UnlockBits(data);

        // Count red-dominant pixels per row.
        var redCount = new int[rect.Height];
        for (var y = 0; y < rect.Height; y++)
        {
            var rowBase = y * stride;
            var c = 0;
            for (var x = 0; x < rect.Width; x++)
            {
                var i  = rowBase + x * 4;                 // BGRA
                int bl = buf[i], gr = buf[i + 1], rd = buf[i + 2];
                if (rd > 90 && rd > gr + 25 && rd > bl + 25) c++;
            }
            redCount[y] = c;
        }

        // A real enemy row is densely red; the gap above it is not. Find the first y where red
        // density crosses a threshold AND stays high for a few rows (a real row, not noise).
        var threshold = rect.Width / 4;                   // ≥25% of the scanned width is red
        for (var y = 0; y < rect.Height - 8; y++)
        {
            if (redCount[y] < threshold) continue;
            var sustained = 0;
            for (var k = 0; k < 8 && y + k < rect.Height; k++)
                if (redCount[y + k] >= threshold) sustained++;
            if (sustained >= 6)
                return y0 + y;                            // top of the red enemy block
        }
        return fallbackTopY;
    }

    /// <summary>
    /// Detects each player row's Y-centre on the Teams tab from the thin dark separator lines
    /// between rows — robust to per-player nameplate/title height (rows are NOT evenly spaced) and
    /// to 5v5 vs 6v6 (the row count falls out of the separators found). Returns the my-team and
    /// enemy-team row centres (top→bottom), or empty lists if the structure can't be read.
    ///
    /// Method: a clean background band between the username and the E column (x≈1065–1200, no
    /// portraits/text) is profiled for mean luminance per y. Row bodies are bright (blue/red bg);
    /// separators are local dark dips; the VS gap between the teams is a wide deep dip. Prominent
    /// minima are the row boundaries; row centres are the midpoints between consecutive boundaries.
    /// </summary>
    public (IReadOnlyList<int> MyCentres, IReadOnlyList<int> EnemyCentres) FindTeamRowCentres(Bitmap b)
    {
        // Profile the ROLE-ICON column at the row's far left (shield / ammo / health-cross). Each
        // row's white role icon is a bright PEAK sitting right at the row's vertical centre, well
        // above the row's (already brighter-than-mid-row) left-edge background — so the icon peaks
        // ARE the row centres, detected directly. This is far more reliable than the dim divider
        // lines, which blend into a flat profile on some maps.
        const int X0 = 710, X1 = 752, Y0 = 300, Y1 = 1320;
        var lum = ProfileLuminance(b, X0, X1, Y0, Y1);   // lum[y - Y0]

        // Local maxima with vertical prominence ≥ minProm = the role icons. Compare to the minima
        // within ±look (< half a row) so an icon stands out from its own row's background.
        const int look = 22, minProm = 22;
        var peaks = new List<int>();
        for (var i = look; i < lum.Length - look; i++)
        {
            var v = lum[i];
            if (v < lum[i - 1] || v < lum[i + 1]) continue;          // must be a local max
            int leftMin = int.MaxValue, rightMin = int.MaxValue;
            for (var k = 1; k <= look; k++) { leftMin = Math.Min(leftMin, lum[i - k]); rightMin = Math.Min(rightMin, lum[i + k]); }
            if (v - Math.Max(leftMin, rightMin) >= minProm) peaks.Add(Y0 + i);
        }

        // Collapse adjacent peaks (an icon spans several px) into one centre each.
        var centres = new List<int>();
        foreach (var y in peaks)
            if (centres.Count == 0 || y - centres[^1] > 45) centres.Add(y);
            else centres[^1] = (centres[^1] + y) / 2;

        if (centres.Count < 8) return (Array.Empty<int>(), Array.Empty<int>());   // need both teams

        // Split at the widest spacing between consecutive centres — the VS gap between the teams.
        var splitIdx = -1; var widest = 0;
        for (var i = 1; i < centres.Count; i++)
            if (centres[i] - centres[i - 1] > widest) { widest = centres[i] - centres[i - 1]; splitIdx = i; }
        if (splitIdx < 1) return (Array.Empty<int>(), Array.Empty<int>());

        return (centres.GetRange(0, splitIdx), centres.GetRange(splitIdx, centres.Count - splitIdx));
    }

    /// <summary>
    /// True if the Personal-tab sidebar slot holds a hero (white name text on a dark pill), vs an
    /// empty spacer pill or the gradient below the list. Counting filled slots is robust to the
    /// inconsistent blank-spacer-before-ALL-HEROES (present with several heroes, absent with one),
    /// which the old <c>allHeroesSlot − 1</c> assumed — that dropped the only hero of a 1-hero match.
    /// </summary>
    public bool PersonalSlotHasHero(Bitmap b, int slot)
    {
        var roi  = UiCoordinates.Personal_HeroTab(slot);
        var rect = Rectangle.Intersect(new Rectangle(0, 0, b.Width, b.Height), roi);
        if (rect.IsEmpty) return false;

        var data   = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = data.Stride;                              // full-image stride for a sub-rect lock
        var buf    = new byte[Math.Abs(stride) * rect.Height];
        Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        b.UnlockBits(data);

        // Index per row/col using the stride — a flat scan would read across the whole image width
        // (the white stat-card numbers to the right) and false-positive on empty pills.
        var white = 0;
        for (var y = 0; y < rect.Height; y++)
        {
            var rb = y * stride;
            for (var x = 0; x < rect.Width; x++)
            {
                var i = rb + x * 4;
                if (buf[i] > 180 && buf[i + 1] > 180 && buf[i + 2] > 180) white++;
            }
        }
        return white >= 60;   // a hero NAME is 700+ white px; an empty pill / purple gradient → ~0
    }

    /// <summary>
    /// Finds every player-row centre on the Teams tab by detecting the bright-WHITE role icons
    /// (tank shield / damage ammo / support cross) in the far-left role-icon column. Each icon sits
    /// at its row's vertical centre on a uniform coloured row background, so a column of "white"
    /// pixels clusters into one blob per row; the cluster's white-weighted centre-of-mass is the
    /// row centre. Every centre is MEASURED, not assumed off a fixed pitch — absorbing the per-map
    /// vertical stretch (the Teams layout is otherwise identical across maps).
    ///
    /// Each detected row is assigned to a team by its background COLOUR (my team rows are cyan/blue,
    /// enemy rows are red), NOT by the gap between teams. This handles leavers correctly: a team can
    /// have fewer than 5/6 players, and if the whole enemy team left there is simply no red-backed
    /// row → an empty enemy list. (A gap-based split would mis-cut a lone team.)
    ///
    /// Returns (myCentres, enemyCentres) top→bottom; both empty if no rows can be read (caller falls
    /// back to the fixed grid + red-anchor).
    /// </summary>
    public (IReadOnlyList<int> MyCentres, IReadOnlyList<int> EnemyCentres) FindTeamRowCentresByIcon(Bitmap b)
    {
        // Lock a generous left-of-table band; we find the table's left edge inside it, then scan the
        // role-icon column relative to that edge (the scoreboard can sit a few dozen px left/right if
        // the capture window is offset, so a fixed x would be fragile).
        const int LX = 640, RX = 815, TY = 300, BY = 1375;   // BY low enough for a 6th 6v6 enemy row
        var rect   = Rectangle.FromLTRB(LX, TY, Math.Min(RX, b.Width), Math.Min(BY, b.Height));
        var data   = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = data.Stride, W = rect.Width, H = rect.Height;
        var buf    = new byte[Math.Abs(stride) * H];
        Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        b.UnlockBits(data);

        bool Bright(int i) => buf[i] > 110 && buf[i + 1] > 110;          // cyan/white/red-ish table bg vs dark navy margin
        bool White (int i) => buf[i] > 180 && buf[i + 1] > 180 && buf[i + 2] > 180;

        // 1. Find the table's left edge from full-height bright coverage: table columns are bright over
        //    many rows; the dark navy margin is not. Relative to the densest column so it still works
        //    when a team is short a few players (fewer bright rows overall).
        var coverage = new int[W];
        for (var col = 0; col < W; col++)
        {
            int cnt = 0;
            for (var y = 0; y < H; y += 2) if (Bright(y * stride + col * 4)) cnt++;
            coverage[col] = cnt;
        }
        var maxCov = coverage.Length == 0 ? 0 : coverage.Max();
        if (maxCov < 40) return (Array.Empty<int>(), Array.Empty<int>());
        int edgeCol = -1;
        for (var col = 0; col < W - 20; col++)
        {
            if (coverage[col] < maxCov * 0.5) continue;
            var ok = true;
            for (var k = 0; k < 20; k++) if (coverage[col + k] < maxCov * 0.4) { ok = false; break; }
            if (ok) { edgeCol = col; break; }
        }
        if (edgeCol < 0) return (Array.Empty<int>(), Array.Empty<int>());

        int icon0 = edgeCol + 2, icon1 = Math.Min(edgeCol + 44, W);
        int colW  = icon1 - icon0, capHigh = (int)(colW * 0.78);

        // 2. Per scan-line: count near-white px (the icon), and sum background R/B (the coloured row
        //    field, i.e. non-white px) so each row can be classified my(cyan)/enemy(red) by colour.
        //    The white cap excludes the near-full-width solid header underline / divider highlights.
        var white = new int[H];
        var bgR   = new long[H];
        var bgB   = new long[H];
        var bgN   = new int[H];
        for (var y = 0; y < H; y++)
        {
            var rb = y * stride; int wc = 0, nc = 0; long sr = 0, sb = 0;
            for (var x = icon0; x < icon1; x++)
            {
                var i = rb + x * 4;
                if (White(i)) wc++;
                else { sr += buf[i + 2]; sb += buf[i]; nc++; }           // R, B of a background px
            }
            white[y] = wc <= capHigh ? wc : 0;
            bgR[y] = sr; bgB[y] = sb; bgN[y] = nc;
        }

        // 3. Group contiguous white scan-lines into clusters (one per role icon); the white-weighted
        //    centre-of-mass is the row centre. Classify each by summed background colour over its span.
        // A real player row's background is SATURATED — cyan (B≫R) for my team, red (R≫B) for the
        // enemy. The gray column-header bar ("E A D DMG…") is desaturated (B−R only ≈38 vs ≈83/127
        // for red/cyan rows) and would otherwise add a spurious top row; clusters whose colour bias
        // is below SatBias are dropped. This is also the robust team-split signal — no gap needed,
        // so leavers (a short team, or no enemy section at all) are handled correctly.
        const int MinWhitePerLine = 2, MaxGap = 12, MinClusterWeight = 120, SatBias = 55;
        var my = new List<int>();
        var enm = new List<int>();
        int top = -1, gap = 0; long wsum = 0; double wy = 0; long rSum = 0, bSum = 0, nSum = 0;
        void Flush()
        {
            if (top >= 0 && wsum >= MinClusterWeight && nSum > 0)
            {
                var centre = (int)Math.Round(wy / wsum);
                var bias   = (bSum - rSum) / (double)nSum;               // +cyan(my) / −red(enemy)
                if      (bias >  SatBias) my.Add(centre);
                else if (bias < -SatBias) enm.Add(centre);
                // else desaturated (header / non-row) → drop
            }
            top = -1; wsum = 0; wy = 0; rSum = 0; bSum = 0; nSum = 0; gap = 0;
        }
        for (var y = 0; y < H; y++)
        {
            if (white[y] >= MinWhitePerLine)
            {
                if (top < 0) top = y;
                wsum += white[y];
                wy   += (double)white[y] * (TY + y);
                rSum += bgR[y]; bSum += bgB[y]; nSum += bgN[y];
                gap   = 0;
            }
            else if (top >= 0 && ++gap > MaxGap) Flush();
        }
        Flush();

        my.Sort(); enm.Sort();
        return (my, enm);
    }

    /// <summary>Mean luminance per scan-line over [x0,x1) × [y0,y1).</summary>
    private static int[] ProfileLuminance(Bitmap b, int x0, int x1, int y0, int y1)
    {
        var rect = Rectangle.FromLTRB(x0, y0, Math.Min(x1, b.Width), Math.Min(y1, b.Height));
        var data = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var buf  = new byte[Math.Abs(data.Stride) * rect.Height];
        Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        b.UnlockBits(data);

        var prof = new int[rect.Height];
        for (var y = 0; y < rect.Height; y++)
        {
            long sum = 0; var rb = y * data.Stride;
            for (var x = 0; x < rect.Width; x++) { var i = rb + x * 4; sum += (buf[i] + buf[i + 1] + buf[i + 2]) / 3; }
            prof[y] = (int)(sum / rect.Width);
        }
        return prof;
    }

    // ── Cyan selection-box detection ────────────────────────────────────────

    /// <summary>
    /// Finds the cyan highlight/selection box around the currently-highlighted match row
    /// in the Game Reports list, returning its bounding rectangle in screen coordinates,
    /// or null if no highlighted row is present (e.g. the highlight has moved onto the
    /// VIEW GAME REPORT button below the list — i.e. we've run past the last game).
    ///
    /// The search is restricted to the list content region to avoid other cyan UI elements
    /// (the PLAY button, tab underlines). A selected row pulses, so the box's vertical span
    /// is taken from the full set of detected cyan border rows.
    /// </summary>
    public Rectangle? FindSelectionBox(Bitmap b)
    {
        // List content region (fractions of the capture). x starts at 18% to EXCLUDE the
        // left sidebar — the selected "GAME REPORTS" sidebar item has a blue highlight that
        // also passes the cyan test and would otherwise inflate the box.
        int x0 = (int)(b.Width  * 0.18f), x1 = (int)(b.Width  * 0.70f);
        int y0 = (int)(b.Height * 0.32f), y1 = (int)(b.Height * 0.86f);
        var rect = Rectangle.FromLTRB(x0, y0, x1, y1);

        var data  = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = data.Stride;
        var buf = new byte[Math.Abs(stride) * rect.Height];
        Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        b.UnlockBits(data);

        // Per-row cyan stats.
        var rows = new (int Count, int MinX, int MaxX)[rect.Height];
        for (var y = 0; y < rect.Height; y++)
        {
            var rowBase  = y * stride;
            var rowCount = 0;
            int rMinX = int.MaxValue, rMaxX = int.MinValue;
            for (var x = 0; x < rect.Width; x++)
            {
                var i  = rowBase + x * 4;       // 32bpp ARGB little-endian → B,G,R,A
                int bl = buf[i + 0], gr = buf[i + 1], rd = buf[i + 2];
                // Cyan: green & blue bright, red distinctly lower (excludes white/grey).
                if (gr > 150 && bl > 150 && rd < gr - 40 && rd < bl - 40)
                {
                    rowCount++;
                    if (x < rMinX) rMinX = x;
                    if (x > rMaxX) rMaxX = x;
                }
            }
            rows[y] = (rowCount, rMinX, rMaxX);
        }

        // Find the densest contiguous cluster of cyan rows (tolerating small gaps).
        // This isolates the selected row's box and ignores faint stray cyan elsewhere.
        const int RowThreshold = 20;   // a real box-border row has a long cyan run
        const int MaxGap       = 16;   // px gap tolerated within one cluster (pre-scale)
        int bestTotal = 0, bestTop = -1, bestBot = -1, bestMinX = 0, bestMaxX = 0;
        int curTotal = 0, curTop = -1, curBot = -1, curMinX = int.MaxValue, curMaxX = int.MinValue, gap = 0;

        void Flush()
        {
            if (curTop >= 0 && curTotal > bestTotal)
            {
                bestTotal = curTotal; bestTop = curTop; bestBot = curBot;
                bestMinX = curMinX;   bestMaxX = curMaxX;
            }
            curTotal = 0; curTop = -1; curBot = -1; curMinX = int.MaxValue; curMaxX = int.MinValue; gap = 0;
        }

        for (var y = 0; y < rect.Height; y++)
        {
            if (rows[y].Count >= RowThreshold)
            {
                if (curTop < 0) curTop = y;
                curBot   = y;
                curTotal += rows[y].Count;
                if (rows[y].MinX < curMinX) curMinX = rows[y].MinX;
                if (rows[y].MaxX > curMaxX) curMaxX = rows[y].MaxX;
                gap = 0;
            }
            else if (curTop >= 0 && ++gap > MaxGap)
            {
                Flush();
            }
        }
        Flush();

        if (bestTop < 0 || bestTotal < 40) return null;
        return new Rectangle(
            x0 + bestMinX, y0 + bestTop,
            Math.Max(1, bestMaxX - bestMinX),
            Math.Max(1, bestBot - bestTop));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Sweeps <paramref name="region"/> for <paramref name="needle"/>; null if absent.</summary>
    private Point? Find(Bitmap bmp, string needle, Rectangle region) =>
        _ocr.FindTextCenter(bmp, needle, region);
}
