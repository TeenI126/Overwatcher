using System.Drawing;
using OwTracker.Core.Services;
using Xunit.Abstractions;

namespace OwTracker.Tests;

/// <summary>
/// OCR smoke tests against real OW screenshots.
/// Screenshots live in  &lt;solution-root&gt;/test-screenshots/  (.jpg or .png accepted).
/// Run with:  dotnet test --filter "OcrSmokeTests" -v n
/// The primary purpose is to print what every OCR region reads so coordinates
/// in UiCoordinates.cs can be tuned before a live scrape.
/// </summary>
public class OcrSmokeTests
{
    private readonly ITestOutputHelper _out;

    private static readonly string ScreenshotDir = Path.GetFullPath(
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..",   // bin/Debug/net9.0-windows → solution root
            "test-screenshots"));

    public OcrSmokeTests(ITestOutputHelper output) => _out = output;

    // ── Preprocessing check ───────────────────────────────────────────────

    /// <summary>
    /// Saves the preprocessed version of every summary ROI to  test-screenshots/preprocessed/
    /// so you can open them and confirm Tesseract is seeing legible text.
    /// </summary>
    [Fact]
    public void Diagnostics_SavePreprocessedCrops()
    {
        RequireScreenshot("summary", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var outDir = Path.Combine(ScreenshotDir, "preprocessed");
            Directory.CreateDirectory(outDir);

            var regions = new (string name, System.Drawing.Rectangle roi)[]
            {
                ("MapName",    UiCoordinates.Summary_MapName),
                ("Outcome",    UiCoordinates.Summary_Outcome),
                ("Score",      UiCoordinates.Summary_Score),
                ("Date",       UiCoordinates.Summary_Date),
                ("GameMode",   UiCoordinates.Summary_GameMode),
                ("GameLength", UiCoordinates.Summary_GameLength),
                ("HeroName0",  UiCoordinates.Summary_HeroName(0)),
                ("PlayTime0",  UiCoordinates.Summary_HeroPlayTime(0)),
            };

            foreach (var (name, roi) in regions)
            {
                // Save raw crop
                var bounds  = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
                var clamped = System.Drawing.Rectangle.Intersect(bounds, roi);
                if (clamped.IsEmpty) { _out.WriteLine($"{name}: ROI out of bounds!"); continue; }

                using var crop = bmp.Clone(clamped, bmp.PixelFormat);
                crop.Save(Path.Combine(outDir, $"{name}_raw.png"),
                    System.Drawing.Imaging.ImageFormat.Png);

                // Also save what OCR reads
                var text = ocr.ReadRegion(bmp, roi).Trim();
                _out.WriteLine($"{name}: [{text}]");
            }

            _out.WriteLine($"\nRaw crops saved to: {outDir}");
            _out.WriteLine("Open them to confirm Tesseract is seeing readable text.");
        }
    }

    /// <summary>
    /// Full 2D sweep of the right half of the screen to find where the Summary info
    /// panel (VICTORY / DATE / GAME MODE / GAME LENGTH) actually lives.
    ///
    /// Results saved to  x-scan-output.txt  AND a full right-half crop saved to
    /// test-screenshots/right-half.png  so you can open it and see the layout.
    /// </summary>
    [Fact]
    public void Diagnostics_ScanSummaryRightPanel()
    {
        RequireScreenshot("summary", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var logPath = Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "x-scan-output.txt"));
            var lines   = new List<string>();
            void Log(string s) { _out.WriteLine(s); lines.Add(s); }

            // Save the right half of the screenshot as a reference image.
            var rightHalfRoi = new System.Drawing.Rectangle(bmp.Width / 2, 0, bmp.Width / 2, bmp.Height);
            using (var rightHalf = bmp.Clone(rightHalfRoi, bmp.PixelFormat))
                rightHalf.Save(Path.Combine(ScreenshotDir, "right-half.png"),
                    System.Drawing.Imaging.ImageFormat.Png);
            Log($"Right-half crop saved to test-screenshots/right-half.png — open it to see the layout.");

            // Sweep: for every y in 100-1200 step 15, scan x=1600-2300 step 100.
            // Now that we know the right panel is at x≈1700+, y≈900+, scan that area densely.
            Log($"\n=== 2D sweep: x=1600-2300 (step 100), y=100-1200 (step 15), probe width=200 height=30 ===");
            Log($"Format: y | x=... [text] x=... [text] ...");

            for (var y = 100; y <= 1200; y += 15)
            {
                var rowHits = new List<string>();
                for (var x = 1600; x <= 2300; x += 100)
                {
                    var roi = new System.Drawing.Rectangle(x, y,
                        Math.Min(200, bmp.Width - x),
                        Math.Min(30, bmp.Height - y));
                    if (roi.Width <= 0 || roi.Height <= 0) continue;
                    try
                    {
                        var text = ocr.ReadRegion(bmp, roi).Trim().Replace("\n", " ");
                        if (!string.IsNullOrWhiteSpace(text))
                            rowHits.Add($"x={x}: [{text}]");
                    }
                    catch { }
                }
                if (rowHits.Count > 0)
                    Log($"  y={y,4} | {string.Join("  ", rowHits)}");
            }

            File.WriteAllLines(logPath, lines);
            Log($"\nSaved to: {logPath}");
        }
    }

    /// <summary>
    /// Sweeps the LEFT panel (Heroes Played cards) to locate hero name / percent / play-time.
    /// Output saved to  left-scan-output.txt.
    /// </summary>
    [Fact]
    public void Diagnostics_ScanSummaryLeftPanel()
    {
        RequireScreenshot("summary", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var logPath = Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "left-scan-output.txt"));
            var lines   = new List<string>();
            void Log(string s) { _out.WriteLine(s); lines.Add(s); }

            Log("=== LEFT panel sweep: x=80-560 (step 80), y=240-780 (step 15), probe w=220 h=30 ===");
            for (var y = 240; y <= 780; y += 15)
            {
                var rowHits = new List<string>();
                for (var x = 80; x <= 560; x += 80)
                {
                    var roi = new System.Drawing.Rectangle(x, y,
                        Math.Min(220, bmp.Width - x), Math.Min(30, bmp.Height - y));
                    if (roi.Width <= 0 || roi.Height <= 0) continue;
                    try
                    {
                        var text = ocr.ReadRegion(bmp, roi).Trim().Replace("\n", " ");
                        if (!string.IsNullOrWhiteSpace(text))
                            rowHits.Add($"x={x}: [{text}]");
                    }
                    catch { }
                }
                if (rowHits.Count > 0)
                    Log($"  y={y,4} | {string.Join("  ", rowHits)}");
            }

            File.WriteAllLines(logPath, lines);
            Log($"\nSaved to: {logPath}");
        }
    }

    /// <summary>
    /// Final verification: reads + parses all Summary fields with the calibrated coordinates.
    /// Output saved to  summary-verify-output.txt.
    /// </summary>
    [Fact]
    public void Summary_VerifyParsedFields()
    {
        RequireScreenshot("summary", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var logPath = Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "summary-verify-output.txt"));
            var lines   = new List<string>();
            void Log(string s) { _out.WriteLine(s); lines.Add(s); }

            Log("=== Raw reads (calibrated coords) ===");
            Log($"MapName    : [{ocr.ReadRegion(bmp, UiCoordinates.Summary_MapName).Trim()}]");
            Log($"Outcome    : [{ocr.ReadRegion(bmp, UiCoordinates.Summary_Outcome).Trim()}]");
            Log($"Score      : [{ocr.ReadRegion(bmp, UiCoordinates.Summary_Score).Trim()}]");
            Log($"Date       : [{ocr.ReadRegion(bmp, UiCoordinates.Summary_Date).Trim()}]");
            Log($"GameMode   : [{ocr.ReadRegion(bmp, UiCoordinates.Summary_GameMode).Trim()}]");
            Log($"GameLength : [{ocr.ReadRegion(bmp, UiCoordinates.Summary_GameLength).Trim()}]");

            Log("\n=== Parsed SummaryData ===");
            var d = ocr.ExtractSummary(bmp);
            Log($"Map     : {d.MapName}");
            Log($"Outcome : {d.Outcome}");
            Log($"Score   : {d.MyTeamScore} vs {d.EnemyTeamScore}");
            Log($"Date    : {d.MatchDatetime:MM/dd/yy HH:mm}");
            Log($"Mode    : {d.GameMode}");
            Log($"Length  : {d.GameLength}");

            File.WriteAllLines(logPath, lines);
            Log($"\nSaved to: {logPath}");
        }
    }

    /// <summary>
    /// 2D sweep of the Teams table to locate row Y positions and stat-column X positions.
    /// Output saved to  teams-scan-output.txt  and a right/centre crop to teams-crop.png.
    /// The table is horizontally centred (~x750-1810), not left-aligned.
    /// </summary>
    [Theory]
    [InlineData("teams")]
    [InlineData("6v6-teams")]
    public void Diagnostics_ScanTeamsTable(string file)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var logPath = Path.GetFullPath(Path.Combine(ScreenshotDir, "..", $"teams-scan-{file}.txt"));
            var lines   = new List<string>();
            void Log(string s) { _out.WriteLine(s); lines.Add(s); }

            Log($"=== Teams sweep — {file} ({bmp.Width}x{bmp.Height}) ===");
            Log("x=700-1900 (step 80), y=240-1180 (step 20), probe w=150 h=26");
            for (var y = 240; y <= 1180; y += 20)
            {
                var rowHits = new List<string>();
                for (var x = 700; x <= 1900; x += 80)
                {
                    var roi = new System.Drawing.Rectangle(x, y,
                        Math.Min(150, bmp.Width - x), Math.Min(26, bmp.Height - y));
                    if (roi.Width <= 0 || roi.Height <= 0) continue;
                    try
                    {
                        var text = ocr.ReadRegion(bmp, roi).Trim().Replace("\n", " ");
                        if (!string.IsNullOrWhiteSpace(text) && text.Length > 1)
                            rowHits.Add($"x{x}:[{text}]");
                    }
                    catch { }
                }
                if (rowHits.Count > 0)
                    Log($"  y={y,4} | {string.Join(" ", rowHits)}");
            }

            File.WriteAllLines(logPath, lines);
            Log($"\nSaved to: {logPath}");
        }
    }

    /// <summary>
    /// Dumps what the sidebar region of the gamereports screen OCRs to, band by band,
    /// using the same geometry ScreenDetector.Sidebar + FindTextCenter use.
    /// Output → sidebar-scan.txt.
    /// </summary>
    [Fact]
    public void Diagnostics_ScanGameReportsSidebar()
    {
        RequireScreenshot("gamereports", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var logPath = Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "sidebar-scan.txt"));
            var lines   = new List<string>();
            void Log(string s) { _out.WriteLine(s); lines.Add(s); }

            var detector = new ScreenDetector(ocr);
            Log($"Detect() returned: {detector.Detect(bmp)}");
            Log($"FindGameReportsSidebar: {detector.FindGameReportsSidebar(bmp)?.ToString() ?? "null"}");
            Log("");

            // Variant A: narrower band that skips the icon column (x=115..435).
            var sy = (int)(bmp.Height * 0.26f);
            var sh = (int)(bmp.Height * 0.26f);
            Log($"-- Variant A: x=115 w=320 (skip icon), band h=46 step=22 --");
            for (var y = sy; y + 46 <= sy + sh; y += 22)
            {
                var roi = new System.Drawing.Rectangle(115, y, 320, 46);
                var text = ocr.ReadRegion(bmp, roi).Trim().Replace("\n", " ");
                Log($"  y={y,4}: [{text}]");
            }

            // Variant B: bottom "VIEW GAME REPORT" button area.
            Log($"\n-- Variant B: bottom button x=1000 w=600, y=1270..1350 band h=40 step=15 --");
            for (var y = 1270; y + 40 <= 1360 && y + 40 <= bmp.Height; y += 15)
            {
                var roi = new System.Drawing.Rectangle(1000, y, 600, 40);
                var text = ocr.ReadRegion(bmp, roi).Trim().Replace("\n", " ");
                Log($"  y={y,4}: [{text}]");
            }

            File.WriteAllLines(logPath, lines);
            Log($"\nSaved to: {logPath}");
        }
    }

    /// <summary>
    /// Verifies the dynamic click-target locators return non-null points on the screens
    /// where the scraper needs to click during navigation.
    /// </summary>
    [Fact]
    public void Navigation_LocatorsFindButtons()
    {
        using var ocr = MakeOcr();
        var det = new ScreenDetector(ocr);
        var results = new List<string>();

        void Check(string file, string label, Func<Bitmap, System.Drawing.Point?> locate)
        {
            RequireScreenshot(file, out var bmp);
            using (bmp)
            {
                var pt = locate(bmp);
                results.Add($"{file} → {label}: {(pt?.ToString() ?? "NULL")}");
                Assert.True(pt is not null, $"{label} not located on {file}");
            }
        }

        Check("escape_menu",            "CAREER PROFILE", det.FindCareerProfileButton);
        Check("career-profile-history", "HISTORY tab",    det.FindHistoryTab);
        Check("career-profile-history", "GAME REPORTS",   det.FindGameReportsSidebar);

        // Match Detail tabs — must resolve to distinct X positions (not overlapping).
        Check("summary", "SUMMARY tab",  b => det.FindMatchDetailTab(b, "SUMMARY"));
        Check("summary", "TEAMS tab",    b => det.FindMatchDetailTab(b, "TEAMS"));
        Check("teams",   "TEAMS tab",    b => det.FindMatchDetailTab(b, "TEAMS"));
        Check("teams",   "PERSONAL tab", b => det.FindMatchDetailTab(b, "PERSONAL"));

        foreach (var r in results) _out.WriteLine(r);
    }

    /// <summary>
    /// Validates the cyan selection-box detector against a real highlighted-row capture
    /// (test-screenshots/highlight.png — a live debug frame). Output → cyan-scan.txt.
    /// </summary>
    [Fact]
    public void Diagnostics_FindSelectionBox_Highlight()
    {
        RequireScreenshot("highlight", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var det = new ScreenDetector(ocr);
            var box = det.FindSelectionBox(bmp);
            _out.WriteLine($"FindSelectionBox: {(box?.ToString() ?? "null")}");
            Assert.True(box is not null, "No cyan box found on highlight.png");
            var q = ocr.ExtractQueueRowAt(bmp, box!.Value);
            _out.WriteLine($"Queue: [{q.RankingMode}] / [{q.QueueType}]");

            // Dump the per-row cyan histogram so we can see the box shape.
            int x0 = (int)(bmp.Width * 0.18f), x1 = (int)(bmp.Width * 0.70f);
            int y0 = (int)(bmp.Height * 0.32f), y1 = (int)(bmp.Height * 0.86f);
            for (var y = y0; y < y1; y += 4)
            {
                var count = 0;
                for (var x = x0; x < x1; x += 2)
                {
                    var p = bmp.GetPixel(x, y);
                    if (p.G > 150 && p.B > 150 && p.R < p.G - 40 && p.R < p.B - 40) count++;
                }
                if (count > 5) _out.WriteLine($"  y={y,4}: {count}");
            }
        }
    }

    /// <summary>
    /// Probes the cyan selection-box detector against the gamereports screenshot and dumps
    /// a per-row cyan-pixel histogram so the threshold can be calibrated. Output → cyan-scan.txt.
    /// </summary>
    [Fact]
    public void Diagnostics_FindSelectionBox()
    {
        RequireScreenshot("gamereports", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var logPath = Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "cyan-scan.txt"));
            var lines   = new List<string>();
            void Log(string s) { _out.WriteLine(s); lines.Add(s); }

            var det = new ScreenDetector(ocr);
            var box = det.FindSelectionBox(bmp);
            Log($"FindSelectionBox: {(box?.ToString() ?? "null")}");
            if (box is not null)
            {
                var q = ocr.ExtractQueueRowAt(bmp, box.Value);
                Log($"ExtractQueueRowAt: [{q.RankingMode}] / [{q.QueueType}]");
            }

            // Per-row cyan histogram over the list region, to calibrate the threshold.
            int x0 = (int)(bmp.Width * 0.03f), x1 = (int)(bmp.Width * 0.68f);
            int y0 = (int)(bmp.Height * 0.32f), y1 = (int)(bmp.Height * 0.86f);
            Log($"\nCyan histogram (x {x0}-{x1}), rows with count>5:");
            for (var y = y0; y < y1; y += 4)
            {
                var count = 0;
                for (var x = x0; x < x1; x += 2)
                {
                    var p = bmp.GetPixel(x, y);
                    if (p.G > 150 && p.B > 150 && p.R < p.G - 40 && p.R < p.B - 40) count++;
                }
                if (count > 5) Log($"  y={y,4}: {count}");
            }

            File.WriteAllLines(logPath, lines);
            Log($"\nSaved to: {logPath}");
        }
    }

    // ── Screen detection ──────────────────────────────────────────────────

    [Theory]
    [InlineData("escape_menu",            GameScreen.EscapeMenu)]
    [InlineData("career-profile-overview",GameScreen.CareerProfile)]
    [InlineData("career-profile-history", GameScreen.CareerProfile)]
    [InlineData("gamereports",            GameScreen.GameReportsList)]
    [InlineData("summary",                GameScreen.MatchDetail)]
    [InlineData("teams",                  GameScreen.MatchDetail)]
    [InlineData("6v6-teams",              GameScreen.MatchDetail)]
    [InlineData("personal",               GameScreen.MatchDetail)]
    public void ScreenDetector_IdentifiesCorrectScreen(string file, GameScreen expected)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var detector = new ScreenDetector(ocr);
            var actual   = detector.Detect(bmp);
            _out.WriteLine($"{file} → detected: {actual}  (expected: {expected})");
            Assert.Equal(expected, actual);
        }
    }

    // ── Summary tab ───────────────────────────────────────────────────────

    [Fact]
    public void Summary_PrintsRawRegionReads()
    {
        RequireScreenshot("summary", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            _out.WriteLine("=== Raw region reads (summary) ===");
            PrintRegion(ocr, bmp, "MapName    ", UiCoordinates.Summary_MapName);
            PrintRegion(ocr, bmp, "Outcome    ", UiCoordinates.Summary_Outcome);
            PrintRegion(ocr, bmp, "Score      ", UiCoordinates.Summary_Score);
            PrintRegion(ocr, bmp, "Date       ", UiCoordinates.Summary_Date);
            PrintRegion(ocr, bmp, "GameMode   ", UiCoordinates.Summary_GameMode);
            PrintRegion(ocr, bmp, "GameLength ", UiCoordinates.Summary_GameLength);
            for (var i = 0; i < UiCoordinates.Summary_MaxHeroCards; i++)
            {
                PrintRegion(ocr, bmp, $"HeroName[{i}] ", UiCoordinates.Summary_HeroName(i));
                PrintRegion(ocr, bmp, $"PlayTime[{i}] ", UiCoordinates.Summary_HeroPlayTime(i));
                PrintRegion(ocr, bmp, $"Percent[{i}]  ", UiCoordinates.Summary_HeroPercent(i));
            }
        }
    }

    /// <summary>Sweeps the Summary Heroes-Played left panel to locate card 1/2 playtime rows.
    /// Output → summary-hero-sweep.txt.</summary>
    [Fact]
    public void Diagnostics_SweepSummaryHeroCards()
    {
        RequireScreenshot("summary", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var lines = new List<string>();
            void Log(string s) { _out.WriteLine(s); lines.Add(s); }
            for (var y = 350; y <= 1100; y += 14)
            {
                var roi = new System.Drawing.Rectangle(270, y, 200, 30);
                var t = ocr.ReadRegion(bmp, roi).Trim().Replace("\n", " ");
                if (!string.IsNullOrWhiteSpace(t) && t.Length > 1) Log($"  y={y}: [{t}]");
            }
            File.WriteAllLines(
                Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "summary-hero-sweep.txt")), lines);
        }
    }

    /// <summary>Sweeps the Personal-tab sidebar (in the captured All-Heroes frame) to locate the
    /// hero sub-tab names. Output → personal-sidebar-sweep.txt.</summary>
    [Fact]
    public void Diagnostics_SweepPersonalSidebar()
    {
        RequireScreenshot("personal-allheroes", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var lines = new List<string>();
            void Log(string s) { _out.WriteLine(s); lines.Add(s); }
            for (var y = 220; y <= 760; y += 12)
            {
                var roi = new System.Drawing.Rectangle(45, y, 320, 34);
                var t = ocr.ReadRegion(bmp, roi).Trim().Replace("\n", " ");
                if (!string.IsNullOrWhiteSpace(t) && t.Length > 1) Log($"  y={y}: [{t}]");
            }
            File.WriteAllLines(
                Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "personal-sidebar-sweep.txt")), lines);
        }
    }

    /// <summary>Sweeps the hero-specific Personal view (personal.jpg = ECHO, 10:01) to locate the
    /// PLAY TIME value. Output → personal-playtime-sweep.txt.</summary>
    [Fact]
    public void Diagnostics_SweepPersonalHeroPlayTime()
    {
        RequireScreenshot("personal", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var lines = new List<string>();
            void Log(string s) { _out.WriteLine(s); lines.Add(s); }
            for (var y = 420; y <= 560; y += 12)
                for (var x = 620; x <= 1000; x += 60)
                {
                    var roi = new System.Drawing.Rectangle(x, y, 180, 34);
                    var t = ocr.ReadRegion(bmp, roi).Trim().Replace("\n", " ");
                    if (!string.IsNullOrWhiteSpace(t) && t.Length > 1) Log($"  x={x} y={y}: [{t}]");
                }
            File.WriteAllLines(
                Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "personal-playtime-sweep.txt")), lines);
        }
    }

    /// <summary>Sweeps the sidebar + stat-card columns of a LIVE All-Heroes frame to compare with
    /// the calibration screenshot. Output → personal-live-sweep.txt.</summary>
    [Fact]
    public void Diagnostics_SweepPersonalLiveFrame()
    {
        RequireScreenshot("personal-live", out var bmp);   // ASHE/CASSIDY/REAPER; E=28 DMG=21388
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var lines = new List<string>();
            void Log(string s) { _out.WriteLine(s); lines.Add(s); }
            Log("=== sidebar (x=45,w=300) ===");
            for (var y = 250; y <= 900; y += 12)
            {
                var t = ocr.ReadRegion(bmp, new System.Drawing.Rectangle(45, y, 300, 34)).Trim().Replace("\n", " ");
                if (!string.IsNullOrWhiteSpace(t) && t.Length > 1) Log($"  y={y}: [{t}]");
            }
            Log("=== stat cards (left x=760, right x=1418) ===");
            for (var y = 300; y <= 1120; y += 12)
            {
                var l = ocr.ReadRegion(bmp, new System.Drawing.Rectangle(740, y, 200, 40)).Trim().Replace("\n", " ");
                var r = ocr.ReadRegion(bmp, new System.Drawing.Rectangle(1418, y, 250, 40)).Trim().Replace("\n", " ");
                if (l.Length > 1 || r.Length > 1) Log($"  y={y}: L[{l}]  R[{r}]");
            }
            File.WriteAllLines(
                Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "personal-live-sweep.txt")), lines);
        }
    }

    [Fact]
    public void Personal_ReadsHeroNamesFromLiveFrame()
    {
        RequireScreenshot("personal-live", out var bmp);   // sidebar: ASHE, CASSIDY, REAPER
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var names = new[] { ocr.ReadHeroTabName(bmp, 0), ocr.ReadHeroTabName(bmp, 1),
                                ocr.ReadHeroTabName(bmp, 2) };
            _out.WriteLine("slots: " + string.Join(", ", names.Select(n => n ?? "null")));
            Assert.Equal("Ashe",    names[0]);
            Assert.Equal("Cassidy", names[1]);
            Assert.Equal("Reaper",  names[2]);
        }
    }

    /// <summary>Sweeps a LIVE Teams frame's DMG column to find the actual row Y positions vs the
    /// calibrated ones. Output → teams-live-sweep.txt.</summary>
    [Fact]
    public void Diagnostics_SweepTeamsLiveFrame()
    {
        RequireScreenshot("teams-live", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var lines = new List<string>();
            void Log(string s) { _out.WriteLine(s); lines.Add(s); }
            Log($"calibrated myRowY = {string.Join(",", Enumerable.Range(0,5).Select(UiCoordinates.Teams_MyTeamRowY))}");
            Log($"calibrated enmRowY(741) = {string.Join(",", Enumerable.Range(0,5).Select(i => UiCoordinates.Teams_EnemyRowY(741, i)))}");
            // Sweep the DMG column band (x~1420-1560) down the whole table to locate each row.
            for (var y = 300; y <= 1300; y += 10)
            {
                var t = ocr.ReadRegion(bmp, new System.Drawing.Rectangle(1410, y, 170, 30)).Trim().Replace("\n", " ");
                if (t.Length > 1 && System.Text.RegularExpressions.Regex.IsMatch(t, @"\d")) Log($"  y={y}: [{t}]");
            }
            File.WriteAllLines(
                Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "teams-live-sweep.txt")), lines);
        }
    }

    /// <summary>Renders every Teams ROI and the divider-scan columns onto real frames using the
    /// ACTUAL production geometry (queue team size + red-anchor enemy top), so the alignment can be
    /// eyeballed. Writes teams-rois-&lt;file&gt;.png to the repo root. Legend:
    ///   lime   = MY stat strip   orange = ENEMY stat strip   yellow = username ROI
    ///   cyan   = stat column centres   white dot = row centre
    ///   red    = FindEnemyTeamTopY red-scan band   blue line = detected enemy top
    ///   magenta= FindTeamRowCentres role-icon column.</summary>
    // The white-role-icon per-row detector must measure the correct per-team counts and tightly-
    // spaced centres on real frames — full 5v5/6v6 (incl. the horizontally-offset 6v6 frame) AND
    // leaver cases: a short team, and a whole enemy team gone (no red section → empty enemy list).
    [Theory]
    [InlineData("teams-live-good",  5, 5)]
    [InlineData("teams-6v6-live",   6, 6)]
    [InlineData("one teamate left", 4, 5)]   // a teammate left → my team collapses to 4
    [InlineData("enemy team left",  5, 0)]   // whole enemy team gone → no enemy section
    public void Teams_IconDetector_FindsAllRowCentres(string file, int expectedMy, int expectedEnemy)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var (my, enm) = new ScreenDetector(ocr).FindTeamRowCentresByIcon(bmp);
            Assert.Equal(expectedMy,    my.Count);
            Assert.Equal(expectedEnemy, enm.Count);

            // Centres strictly increasing with a plausible per-row pitch (≈75–95 px).
            foreach (var team in new[] { my, enm })
                for (var i = 1; i < team.Count; i++)
                    Assert.InRange(team[i] - team[i - 1], 70, 100);
        }
    }

    /// <summary>Renders the NEW white-role-icon per-row detector (FindTeamRowCentresByIcon): the
    /// scan column, the detected row centres, and the stat strips placed at those measured centres,
    /// so alignment can be eyeballed at full res. Writes teams-iconrows-&lt;file&gt;.png.</summary>
    [Theory]
    [InlineData("teams-live-good")]
    [InlineData("teams-live")]
    [InlineData("teams-6v6-live")]
    [InlineData("enemy team left")]
    [InlineData("one teamate left")]
    [InlineData("teams-zerorow")]
    public void Diagnostics_DrawIconRowCentres(string file)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var detector = new ScreenDetector(ocr);
            var (myC, enmC) = detector.FindTeamRowCentresByIcon(bmp);

            using var canvas = new Bitmap(bmp);
            using var g = Graphics.FromImage(canvas);
            using var yellow = new Pen(Color.Yellow, 2);
            using var lime   = new Pen(Color.Lime, 2);
            using var orange = new Pen(Color.Orange, 2);
            using var red    = new Pen(Color.Red, 1);
            using var cyan   = new Pen(Color.Cyan, 1);
            using var dot    = new SolidBrush(Color.Red);

            g.DrawRectangle(yellow, 708, 300, 750 - 708, 1320 - 300);   // scan column
            foreach (var cx in UiCoordinates.Teams_StatColumnCentersX)   // stat column centres
                g.DrawLine(cyan, cx, 300, cx, 1320);
            void Mark(int centre, Pen p)
            {
                var rowY  = centre - 49;
                var strip = UiCoordinates.Teams_StatStrip(rowY);
                g.DrawRectangle(p, strip.X, strip.Y, strip.Width, strip.Height);
                g.DrawLine(red, 708, centre, 1865, centre);
                g.FillEllipse(dot, 726, centre - 5, 10, 10);
            }
            foreach (var c in myC)  Mark(c, lime);
            foreach (var c in enmC) Mark(c, orange);

            var outPath = Path.GetFullPath(Path.Combine(ScreenshotDir, "..", $"teams-iconrows-{file}.png"));
            canvas.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            _out.WriteLine($"{file}: my=[{string.Join(",", myC)}]  enemy=[{string.Join(",", enmC)}]");
            File.AppendAllText(Path.Combine(ScreenshotDir, "..", "iconrows-diag.txt"),
                $"{file}: my=[{string.Join(",", myC)}]  enemy=[{string.Join(",", enmC)}]\n");
        }
    }

    [Theory]
    [InlineData("teams-live-good", 5)]   // KING'S ROW 5v5
    [InlineData("teams-live",      5)]   // LIJIANG 5v5 (stretched)
    [InlineData("teams-6v6-live",  6)]   // SURAVASA 6v6
    public void Diagnostics_DrawTeamsRois(string file, int teamSize)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var detector = new ScreenDetector(ocr);
            var enemyTop = detector.FindEnemyTeamTopY(bmp, UiCoordinates.Teams_EnemyFallbackTop(teamSize));
            var myC  = UiCoordinates.Teams_MyCentres(teamSize);
            var enmC = UiCoordinates.Teams_EnemyCentres(enemyTop, teamSize);

            using var canvas = new Bitmap(bmp);
            using var g = Graphics.FromImage(canvas);
            using var lime    = new Pen(Color.Lime, 2);
            using var orange  = new Pen(Color.Orange, 2);
            using var yellow  = new Pen(Color.Yellow, 2);
            using var cyan    = new Pen(Color.Cyan, 1);
            using var red     = new Pen(Color.Red, 3);
            using var blue    = new Pen(Color.DeepSkyBlue, 3);
            using var magenta = new Pen(Color.Magenta, 2);
            using var white   = new SolidBrush(Color.White);

            void Row(int centre, Pen p)
            {
                var rowY  = centre - 49;
                var strip = UiCoordinates.Teams_StatStrip(rowY);
                g.DrawRectangle(p, strip.X, strip.Y, strip.Width, strip.Height);
                var user = UiCoordinates.Teams_Username(rowY);
                g.DrawRectangle(yellow, user.X, user.Y, user.Width, user.Height);
                g.FillEllipse(white, 1190, centre - 4, 8, 8);
            }
            foreach (var c in myC)  Row(c, lime);
            foreach (var c in enmC) Row(c, orange);

            // Stat column centres (cyan verticals across the table's stat band).
            foreach (var cx in UiCoordinates.Teams_StatColumnCentersX)
                g.DrawLine(cyan, cx, 300, cx, 1320);

            // FindEnemyTeamTopY red-scan band + detected top.
            int x0 = (int)(bmp.Width * 0.295f), x1 = (int)(bmp.Width * 0.46f);
            int y0 = (int)(bmp.Height * 0.55f), y1 = (int)(bmp.Height * 0.70f);
            g.DrawRectangle(red, x0, y0, x1 - x0, y1 - y0);
            g.DrawLine(blue, 700, enemyTop, 1860, enemyTop);

            // FindTeamRowCentres role-icon scan column (x 710..752).
            g.DrawRectangle(magenta, 710, 300, 42, 1020);

            var outPath = Path.GetFullPath(Path.Combine(ScreenshotDir, "..", $"teams-rois-{file}.png"));
            canvas.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            _out.WriteLine($"enemyTop={enemyTop}  myC=[{string.Join(",", myC)}]  enmC=[{string.Join(",", enmC)}]");
            _out.WriteLine($"saved {outPath}");
        }
    }

    [Theory]
    [InlineData("teams-live",      886)]   // LIJIANG (stretched): enemy row-0 top ≈ 886
    [InlineData("teams-live-good", 871)]   // KING'S ROW: enemy row-0 top ≈ 871
    public void Teams_DetectsEnemyTopByRedBackground(string file, int expectedTop)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var detector = new ScreenDetector(ocr);
            var top = detector.FindEnemyTeamTopY(bmp, fallbackTopY: -1);
            _out.WriteLine($"{file}: detected enemy top = {top} (expected ~{expectedTop})");
            Assert.InRange(top, expectedTop - 25, expectedTop + 25);
        }
    }

    /// <summary>Dumps the full ExtractTeams (with red-anchored enemy block) for both live frames
    /// so the enemy reads can be checked vs ground truth. Output → teams-live-extract.txt.</summary>
    [Theory]
    [InlineData("teams-live")]       // LIJIANG: enemy DMG 7760/5226/7326/3657/3015
    [InlineData("teams-live-good")]  // KING'S ROW: enemy DMG 13225/13449/10053/6235/2769
    public void Diagnostics_ExtractTeamsLive(string file)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var detector  = new ScreenDetector(ocr);
            var vsY       = detector.FindVsDividerY(bmp);

            // Replicate the scraper's hybrid: separator centres when valid, else red-anchor.
            var (myC, enmC) = detector.FindTeamRowCentres(bmp);
            bool Valid(IReadOnlyList<int> a, IReadOnlyList<int> b)
            {
                if (a.Count != b.Count || a.Count is < 5 or > 6) return false;
                foreach (var t in new[] { a, b })
                    for (var i = 1; i < t.Count; i++) if (t[i] - t[i - 1] is < 68 or > 108) return false;
                return true;
            }
            string path;
            IReadOnlyList<TeamPlayerData> my, enm;
            if (Valid(myC, enmC)) { path = $"separators {myC.Count}v{enmC.Count}"; (my, enm) = ocr.ExtractTeams(bmp, myC, enmC); }
            else { var et = detector.FindEnemyTeamTopY(bmp, UiCoordinates.Teams_EnemyRowY(vsY, 0));
                   path = $"red-anchor (enemyTop={et})"; (my, enm) = ocr.ExtractTeams(bmp, vsY, et); }

            var lines = new List<string> { $"=== {file}: {path} ===", "ENEMY:" };
            foreach (var p in enm)
                lines.Add($"  E={p.Eliminations,-3} A={p.Assists,-3} D={p.Deaths,-3} " +
                          $"DMG={p.DamageDealt,-7} HEAL={p.HealingDone,-7} MIT={p.DamageMitigated}");
            foreach (var l in lines) _out.WriteLine(l);
            File.AppendAllLines(
                Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "teams-live-extract.txt")), lines);
        }
    }

    /// <summary>Dumps a per-row brightness profile of the Teams table to find the dark separator
    /// lines between player rows. Output → teams-rows-profile.txt.</summary>
    [Theory]
    [InlineData("teams-live")]
    [InlineData("teams-live-good")]
    [InlineData("6v6-teams")]
    public void Diagnostics_TeamsRowProfile(string file)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        {
            // Role-icon column (x≈710–752): brighter background → separators show with high contrast.
            int x0 = 710, x1 = 752;
            var rect = new System.Drawing.Rectangle(x0, 250, x1 - x0, 1050);
            var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var buf = new byte[Math.Abs(data.Stride) * rect.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(data);

            var lines = new List<string>();
            for (var y = 0; y < rect.Height; y++)
            {
                long sum = 0; var rb = y * data.Stride;
                for (var x = 0; x < rect.Width; x++)
                { var i = rb + x * 4; sum += (buf[i] + buf[i + 1] + buf[i + 2]) / 3; }
                var lum = (int)(sum / rect.Width);
                // Mark notably dark rows (likely separators) with a bar for easy scanning.
                lines.Add($"{250 + y,4}: {lum,3} {(lum < 40 ? new string('#', Math.Max(0, 40 - lum)) : "")}");
            }
            File.WriteAllLines(
                Path.GetFullPath(Path.Combine(ScreenshotDir, "..", $"teams-rows-profile-{file}.txt")), lines);
        }
    }

    [Theory]
    [InlineData("teams-live")]
    [InlineData("teams-live-good")]
    [InlineData("teams-6v6-live")]
    [InlineData("6v6-teams")]
    public void Diagnostics_FindTeamRowCentres(string file)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var detector = new ScreenDetector(ocr);
            var (my, enm) = detector.FindTeamRowCentres(bmp);
            var msg = $"{file}: MY[{my.Count}]={string.Join(",", my)}  ENM[{enm.Count}]={string.Join(",", enm)}";
            _out.WriteLine(msg);
            File.AppendAllText(
                Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "teams-rowcentres.txt")), msg + "\n");
        }
    }

    /// <summary>Scans every debug_teams_*.png frame and reports the role-icon peak counts per team
    /// to find the 6v6 frames (6 peaks). Output → teams-6v6-scan.txt.</summary>
    [Fact]
    public void Diagnostics_FindSixVsSixFrames()
    {
        var debugDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OwTracker", "debug");
        if (!Directory.Exists(debugDir)) { _out.WriteLine("no debug dir"); return; }
        using var ocr = MakeOcr();
        var detector = new ScreenDetector(ocr);
        var lines = new List<string>();
        foreach (var f in Directory.GetFiles(debugDir, "debug_teams_*.png"))
        {
            try
            {
                using var bmp = new Bitmap(f);
                var (my, enm) = detector.FindTeamRowCentres(bmp);
                if (my.Count >= 6 || enm.Count >= 6)
                    lines.Add($"{Path.GetFileName(f)}: MY={my.Count} ENM={enm.Count}  my={string.Join(",", my)} enm={string.Join(",", enm)}");
            }
            catch { }
        }
        foreach (var l in lines) _out.WriteLine(l);
        File.WriteAllLines(
            Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "teams-6v6-scan.txt")), lines);
    }

    [Fact]
    public void Teams_Extracts6v6FromQueueSize()
    {
        RequireScreenshot("teams-6v6-live", out var bmp);  // confirmed 6v6
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var detector   = new ScreenDetector(ocr);
            var enemyTop    = detector.FindEnemyTeamTopY(bmp, UiCoordinates.Teams_EnemyFallbackTop(6));
            var myCentres   = UiCoordinates.Teams_MyCentres(6);
            var enmCentres  = UiCoordinates.Teams_EnemyCentres(enemyTop, 6);
            var (my, enm)   = ocr.ExtractTeams(bmp, myCentres, enmCentres);

            var lines = new List<string> { $"6v6 (enemyTop={enemyTop}):", "MY:" };
            foreach (var p in my)  lines.Add($"  E={p.Eliminations,-3} A={p.Assists,-3} D={p.Deaths,-3} DMG={p.DamageDealt,-7} H={p.HealingDone,-7} MIT={p.DamageMitigated}");
            lines.Add("ENEMY:");
            foreach (var p in enm) lines.Add($"  E={p.Eliminations,-3} A={p.Assists,-3} D={p.Deaths,-3} DMG={p.DamageDealt,-7} H={p.HealingDone,-7} MIT={p.DamageMitigated}");
            foreach (var l in lines) _out.WriteLine(l);
            File.WriteAllLines(Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "teams-6v6-extract.txt")), lines);

            Assert.Equal(6, my.Count);
            Assert.Equal(6, enm.Count);
        }
    }

    /// <summary>Compares the current map-name read vs the white-text mask across real frames whose
    /// bright menu background garbled the plain LSTM read. Dumps to map-name-diag.txt.</summary>
    [Fact]
    public void Diagnostics_MapNameWhiteMask()
    {
        var cases = new (string file, string truth)[]
        {
            ("summary-havana",    "Havana"),
            ("summary-esperanca", "Esperança"),
            ("summary-kingsrow",  "King's Row"),
            ("summary-dorado",    "Dorado"),
            ("summary",           "Dorado"),   // older, darker frame — must not regress
        };
        var lines = new List<string>();
        using var ocr = MakeOcr();
        foreach (var (file, truth) in cases)
        {
            RequireScreenshot(file, out var bmp);
            using (bmp)
            {
                var plain = ocr.ReadRegion(bmp, UiCoordinates.Summary_MapName).Trim().Replace("\n", " ");
                lines.Add($"{file}  (truth={truth})");
                lines.Add($"    plain      : [{plain}]  → snap=[{MapRoster.Snap(plain)}]");
                foreach (var th in new byte[] { 170, 190, 210 })
                {
                    var w = ocr.ReadRegionWhiteText(bmp, UiCoordinates.Summary_MapName, th).Trim().Replace("\n", " ");
                    lines.Add($"    white@{th} : [{w}]  → snap=[{MapRoster.Snap(w)}]");
                }
            }
        }
        foreach (var l in lines) _out.WriteLine(l);
        File.WriteAllLines(Path.Combine(ScreenshotDir, "..", "map-name-diag.txt"), lines);
    }

    // Real captured Summary frames whose bright menu gradient garbled the plain LSTM read; the
    // white-text mask must recover the true map name on every one (and not regress the dark frame).
    [Theory]
    [InlineData("summary-havana",    "Havana")]
    [InlineData("summary-esperanca", "Esperança")]
    [InlineData("summary-kingsrow",  "King's Row")]
    [InlineData("summary-dorado",    "Dorado")]
    [InlineData("summary",           "Dorado")]
    public void Summary_MapName_WhiteMaskRecoversBrightFrames(string file, string expected)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var read = ocr.ReadRegionWhiteText(bmp, UiCoordinates.Summary_MapName).Trim();
            Assert.Equal(expected, MapRoster.Snap(read));
        }
    }

    [Theory]
    [InlineData("DORADO",          "Dorado")]
    [InlineData("KING'S ROW",      "King's Row")]
    [InlineData("SURAVASA",        "Suravasa")]
    [InlineData("SHAMBALI MONAS",  "Shambali Monastery")]
    [InlineData("RUNASAPI",        "Runasapi")]
    [InlineData("COLOSSEO",        "Colosseo")]
    [InlineData("LIJIANG TOWER",   "Lijiang Tower")]
    [InlineData("WATCHPOINT GIBRALTAR", "Watchpoint: Gibraltar")]
    public void MapRoster_Snaps(string raw, string expected)
        => Assert.Equal(expected, MapRoster.Snap(raw));

    // Every canonical pool map must round-trip: its own uppercase name snaps back to itself and
    // is recognised as known. Guards against typos/omissions when the pool is updated.
    [Fact]
    public void MapRoster_AllCanonicalNamesRoundTrip()
    {
        foreach (var map in MapRoster.Maps)
        {
            Assert.True(MapRoster.IsKnown(map),         $"IsKnown failed for [{map}]");
            Assert.Equal(map, MapRoster.Snap(map.ToUpperInvariant()));
        }
    }

    // Seasonal/event variants render with a suffix in the Settings menu but show the BASE name in
    // match history — and even if a variant string were read, it must snap to the base map so the
    // dedup key stays stable. Names taken from the live SETTINGS / MAPS screenshots.
    [Theory]
    [InlineData("LIJIANG TOWER (LUNAR NEW YEAR)", "Lijiang Tower")]
    [InlineData("BLIZZARD WORLD (WINTER)",        "Blizzard World")]
    [InlineData("EICHENWALDE (HALLOWEEN)",        "Eichenwalde")]
    [InlineData("HOLLYWOOD (HALLOWEEN)",          "Hollywood")]
    [InlineData("KING'S ROW (WINTER)",            "King's Row")]
    [InlineData("THRONE OF ANUBIS",               "Throne of Anubis")]
    [InlineData("HANAOKA",                        "Hanaoka")]
    [InlineData("NEW QUEEN STREET",               "New Queen Street")]
    [InlineData("ANTARCTIC PENINSULA",            "Antarctic Peninsula")]
    public void MapRoster_SnapsVariantsToBase(string raw, string expected)
        => Assert.Equal(expected, MapRoster.Snap(raw));

    // A severe garble must NOT mis-snap to a real map, and must read as unknown so the scraper
    // re-captures. (The DMG-soup title from a still-loading Summary header.)
    [Theory]
    [InlineData("{e]V}]¥.1.]")]
    [InlineData("Ly Z-[e ge]")]
    public void MapRoster_GarbleIsUnknown(string garble)
    {
        Assert.Null(MapRoster.Snap(garble));
        Assert.False(MapRoster.IsKnown(garble));
    }

    // Real captured queue-ROI raw strings from the deep-scrape log — verifies the 6V6 team-size
    // signal and the ranking/queue-type parse against actual OCR output.
    [Theory]
    [InlineData("JG 4 UNRANKED 6V6 QUICK PLA",        "UNRANKED",    "QUICK PLAY", 6)]
    [InlineData("1 J 6V6 QUICK PLA 4 UNRANKED",       "UNRANKED",    "QUICK PLAY", 6)]
    [InlineData("4 J 6V6 QUICK PLA W \\ 4 UNRANKED",  "UNRANKED",    "QUICK PLAY", 6)]
    [InlineData("ee / QUICK PLAY & 4 UNRANKED",       "UNRANKED",    "QUICK PLAY", 5)]
    [InlineData("=| 7N\" ROLE QUEUE = 4 UNRANKED",    "UNRANKED",    "ROLE QUEUE", 5)]
    [InlineData("\\¢ MPETITIVE 4 WW ROLE QUEUE",      "COMPETITIVE", "ROLE QUEUE", 5)]
    public void ParseQueue_DetectsModeAndTeamSize(string raw, string ranking, string type, int size)
    {
        var q = OcrEngine.ParseQueue(raw);
        Assert.Equal(ranking, q.RankingMode);
        Assert.Equal(type,    q.QueueType);
        Assert.Equal(size,    q.TeamSize);
    }

    /// <summary>Compares plain vs white-mask reads of the Personal sidebar hero tabs on a frame with
    /// a bright gradient background (KIRIKO / MIZUKI / BRIGITTE), which garbles the plain LSTM read.</summary>
    [Fact]
    public void Diagnostics_HeroTabWhiteMask()
    {
        RequireScreenshot("personal-heroes-bright", out var bmp);
        var lines = new List<string>();
        using (bmp)
        using (var ocr = MakeOcr())
            for (var i = 0; i < 3; i++)
            {
                var roi   = UiCoordinates.Personal_HeroTab(i);
                var plain = ocr.ReadRegion(bmp, roi).Trim().Replace("\n", " ");
                var white = ocr.ReadRegionWhiteText(bmp, roi).Trim().Replace("\n", " ");
                lines.Add($"slot {i}: plain=[{plain}] → {HeroRoster.Snap(plain)}   " +
                          $"white=[{white}] → {HeroRoster.Snap(white)}");
            }
        foreach (var l in lines) _out.WriteLine(l);
        File.WriteAllLines(Path.Combine(ScreenshotDir, "..", "herotab-diag.txt"), lines);
    }

    // PLAY TIME on a bright-gradient frame: the plain LSTM garbles the white value ("00:55"→
    // "(003.7)"); ExtractHeroPlayTime must recover it via the white-text mask.
    [Fact]
    public void Personal_ReadsHeroPlayTime_BrightFrame()
    {
        RequireScreenshot("personal-playtime-bright", out var bmp);   // ASHE, PLAY TIME 00:55
        using (bmp)
        using (var ocr = MakeOcr())
            Assert.Equal(new TimeSpan(0, 0, 55), ocr.ExtractHeroPlayTime(bmp));
    }

    // HeroRoster.Snap fuzzy (Levenshtein) fallback recovers glyph-corrupted reads the substring/LCS
    // pass misses: a highlighted/selected sidebar tab ("SO .IOUIRN"→Sojourn) and accented names the
    // LSTM mangles ("Lclo"→Lúcio). Short/ambiguous reads must stay Unknown rather than mis-snap.
    [Theory]
    [InlineData("SO .IOUIRN", "Sojourn")]   // selected-tab garble (dist 0.25)
    [InlineData("Lclo",       "Lúcio")]     // LÚCIO at 4× (dist 0.40, margin over Echo)
    [InlineData("Ico",        null)]        // too short — closer to Echo than Lúcio → reject
    [InlineData("LHiclo",     null)]        // dist 0.50 > 0.45 → reject (no confident match)
    [InlineData("Echo",       "Echo")]      // clean name still resolves (LCS pass, no regression)
    [InlineData("zzzzzz",     null)]        // junk → no match
    public void HeroRoster_Snap_FuzzyFallback(string ocr, string? expected)
        => Assert.Equal(expected, HeroRoster.Snap(ocr));

    // ReadHeroTabName must recover white sidebar names on a bright-gradient frame (white-mask) AND
    // not regress the clean darker fixture.
    // Newer heroes added from the live in-game roster (and via heroes.txt) must be recognised.
    [Theory]
    [InlineData("Mizuki")]
    [InlineData("Domina")]
    [InlineData("Anran")]
    [InlineData("Emre")]
    [InlineData("Sierra")]
    [InlineData("Freja")]
    public void HeroRoster_KnowsNewerHeroes(string hero)
        => Assert.Equal(hero, HeroRoster.Snap(hero.ToUpperInvariant()));

    [Theory]
    [InlineData("personal-heroes-bright", 0, "Kiriko")]
    [InlineData("personal-heroes-bright", 1, "Mizuki")]
    [InlineData("personal-heroes-bright", 2, "Brigitte")]
    [InlineData("personal-allheroes",     0, "Cassidy")]
    [InlineData("personal-allheroes",     1, "Sojourn")]
    [InlineData("personal-allheroes",     2, "Echo")]
    [InlineData("personal-lucio",         3, "Lúcio")]    // accented name — recovered via multi-scale + fuzzy snap
    [InlineData("personal-lucio",         5, "Kiriko")]   // 6th hero on the same frame still reads
    public void Personal_ReadHeroTabName_RecoversWhiteNames(string file, int slot, string expected)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
            Assert.Equal(expected, ocr.ReadHeroTabName(bmp, slot));
    }

    /// <summary>Dumps the per-scale stat-strip word boxes + gaps for each MY row of a frame whose
    /// tank DMG dropped digits (DMG=11 with MIT=12822), to see whether comma fragments fail to merge.
    /// Writes statstrip-diag.txt.</summary>
    [Theory]
    [InlineData("teams-samoa-dmgbug", false)]
    [InlineData("teams-zerorow",      true)]
    public void Diagnostics_StatStripWordBoxesFor(string file, bool enemy)
    {
        RequireScreenshot(file, out var bmp);
        var lines = new List<string>();
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var (myC, enmC) = new ScreenDetector(ocr).FindTeamRowCentresByIcon(bmp);
            var myCC = enemy ? enmC : myC;
            for (var r = 0; r < myCC.Count; r++)
            {
                var rowYY = myCC[r] - 49;
                var roiR  = UiCoordinates.Teams_StatStrip(rowYY);
                lines.Add($"{(enemy ? "ENM" : "MY")} row {r} (centre={myCC[r]}, strip={roiR}):");
                foreach (var scale in new[] { 2, 3, 4 })
                {
                    var words = ocr.ReadWords(bmp, roiR, OcrEngine_SingleLine(), null, scale)
                        .OrderBy(w => w.Box.X).ToList();
                    var sb = new System.Text.StringBuilder($"  {scale}x: ");
                    foreach (var (w, box) in words) sb.Append($"[{w.Trim()}]@{box.Left}-{box.Right} ");
                    lines.Add(sb.ToString());
                }
            }
            foreach (var l in lines) _out.WriteLine(l);
            File.WriteAllLines(Path.Combine(ScreenshotDir, "..", $"statstrip-{file}.txt"), lines);
        }
    }

    /// <summary>For each recent game frame: draws the icon-row overlay (teams-overlay-&lt;file&gt;.png)
    /// AND dumps, per row, what each scale (2×/3×/4×) read at every stat column + the final voted
    /// value — so misses can be confirmed as scale issues. Writes recent-stats.txt.</summary>
    [Theory]
    [InlineData("recent1")]
    [InlineData("recent2")]
    [InlineData("recent3")]
    public void Diagnostics_RecentGameStats(string file)
    {
        RequireScreenshot(file, out var bmp);
        var lines = new List<string> { $"===== {file} =====" };
        string[] col = { "E", "A", "D", "DMG", "H", "MIT" };
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var det = new ScreenDetector(ocr);
            var (myC, enmC) = det.FindTeamRowCentresByIcon(bmp);
            var usedIcon = myC.Count >= 1;
            // Fall back to the grid (mirrors the scraper) so the overlay matches what was scraped.
            if (!usedIcon || myC.Count == 0)
            {
                myC = UiCoordinates.Teams_MyCentres(5);
                enmC = UiCoordinates.Teams_EnemyCentres(
                    det.FindEnemyTeamTopY(bmp, UiCoordinates.Teams_EnemyFallbackTop(5)), 5);
            }
            lines.Add($"rows={(usedIcon ? "icon" : "grid")}  my=[{string.Join(",", myC)}]  enemy=[{string.Join(",", enmC)}]");

            var (my, enm) = ocr.ExtractTeams(bmp, myC, enmC);

            void Dump(string tag, IReadOnlyList<int> centres, IReadOnlyList<TeamPlayerData> team)
            {
                for (var r = 0; r < centres.Count; r++)
                {
                    var ps = ocr.DebugStatRowScales(bmp, centres[r] - 49);
                    var p  = team[r];
                    int[] fin = { p.Eliminations, p.Assists, p.Deaths, p.DamageDealt, p.HealingDone, p.DamageMitigated };
                    lines.Add($"  {tag}{r} (c={centres[r]}):");
                    for (var ci = 0; ci < col.Length; ci++)
                        lines.Add($"      {col[ci],-3}: 2x={ps[0][ci],-7} 3x={ps[1][ci],-7} 4x={ps[2][ci],-7} → {fin[ci]}");
                }
            }
            Dump("MY", myC, my);
            Dump("ENM", enmC, enm);

            // Overlay
            using var canvas = new Bitmap(bmp);
            using var g = Graphics.FromImage(canvas);
            using var lime = new Pen(Color.Lime, 2); using var orange = new Pen(Color.Orange, 2);
            using var cyan = new Pen(Color.Cyan, 1); using var red = new Pen(Color.Red, 1);
            using var dot = new SolidBrush(Color.Red);
            foreach (var cx in UiCoordinates.Teams_StatColumnCentersX) g.DrawLine(cyan, cx, 300, cx, 1340);
            void Mark(int c, Pen pen) { var s = UiCoordinates.Teams_StatStrip(c - 49);
                g.DrawRectangle(pen, s.X, s.Y, s.Width, s.Height); g.DrawLine(red, 1180, c, 1865, c); g.FillEllipse(dot, 726, c - 5, 10, 10); }
            foreach (var c in myC) Mark(c, lime);
            foreach (var c in enmC) Mark(c, orange);
            canvas.Save(Path.GetFullPath(Path.Combine(ScreenshotDir, "..", $"teams-overlay-{file}.png")),
                System.Drawing.Imaging.ImageFormat.Png);
        }
        foreach (var l in lines) _out.WriteLine(l);
        File.AppendAllLines(Path.Combine(ScreenshotDir, "..", "recent-stats.txt"), lines);
    }

    [Fact]
    public void Diagnostics_StatStripWordBoxes()
    {
        RequireScreenshot("teams-samoa-dmgbug", out var bmp);
        var lines = new List<string>();
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var (myC, _) = new ScreenDetector(ocr).FindTeamRowCentresByIcon(bmp);
            for (var r = 0; r < myC.Count; r++)
            {
                var rowY = myC[r] - 49;
                var roi  = UiCoordinates.Teams_StatStrip(rowY);
                lines.Add($"MY row {r} (centre={myC[r]}, strip={roi}):");
                foreach (var scale in new[] { 2, 3, 4 })
                {
                    var words = ocr.ReadWords(bmp, roi, OcrEngine_SingleLine(), null, scale)
                        .OrderBy(w => w.Box.X).ToList();
                    var sb = new System.Text.StringBuilder($"  {scale}x: ");
                    Rectangle? prev = null;
                    foreach (var (w, box) in words)
                    {
                        var gap = prev is null ? 0 : box.Left - prev.Value.Right;
                        sb.Append($"[{w.Trim()}]@{box.Left}-{box.Right}(gap{gap}) ");
                        prev = box;
                    }
                    lines.Add(sb.ToString());
                }
            }
        }
        foreach (var l in lines) _out.WriteLine(l);
        File.WriteAllLines(Path.Combine(ScreenshotDir, "..", "statstrip-diag.txt"), lines);
    }

    private static Tesseract.PageSegMode OcrEngine_SingleLine() => Tesseract.PageSegMode.SingleLine;

    /// <summary>The wide-column vote must recover a comma-fragmented tank DMG (13,868) that two
    /// scales dropped to "11", without regressing the narrow E/A/D columns.</summary>
    [Fact]
    public void Teams_WideColumnVote_RecoversDroppedDmgDigits()
    {
        RequireScreenshot("teams-samoa-dmgbug", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var (myC, enmC) = new ScreenDetector(ocr).FindTeamRowCentresByIcon(bmp);
            var (my, _) = ocr.ExtractTeams(bmp, myC, enmC);

            // Row 0: tank — DMG was dropping to 11; should read the full 13,868 (MIT 12,822).
            Assert.Equal(13868, my[0].DamageDealt);
            Assert.Equal(12822, my[0].DamageMitigated);
            Assert.Equal(17, my[0].Eliminations);   // narrow columns unaffected
            Assert.Equal(9,  my[0].Deaths);         // glyph is a clear 9; the old Tesseract vote
                                                    // mis-read it as 4 (2×/4×), DigitOcr corrects it
            // Other rows' big numbers still correct.
            Assert.Equal(13105, my[1].DamageDealt);
            Assert.Equal(11464, my[3].DamageDealt);
        }
    }

    // The embedded digit templates must load, and template-OCR must read the stat cells that the
    // general LSTM systematically mis-read (7→4) correctly through the full ExtractTeams pipeline.
    [Fact]
    public void DigitOcr_LoadsTemplates() => Assert.True(new DigitOcr().IsReady);

    [Theory]
    [InlineData("recent3",      1096, 0, 7)]   // ENM2 E — Tesseract voted 4
    [InlineData("recent3",      1096, 2, 8)]   // ENM2 D
    [InlineData("teams-zerorow", 937, 0, 7)]   // ENM0 E
    [InlineData("teams-zerorow", 937, 2, 7)]   // ENM0 D
    public void DigitOcr_CorrectsStatMisreads(string file, int centre, int col, int expected)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var det = new ScreenDetector(ocr);
            var (my, enm) = det.FindTeamRowCentresByIcon(bmp);
            var (_, enemyTeam) = ocr.ExtractTeams(bmp, my, enm);
            var idx = enm.ToList().IndexOf(centre);
            Assert.True(idx >= 0, $"enemy centre {centre} not detected");
            var p = enemyTeam[idx];
            int[] vals = { p.Eliminations, p.Assists, p.Deaths, p.DamageDealt, p.HealingDone, p.DamageMitigated };
            Assert.Equal(expected, vals[col]);
        }
    }

    /// <summary>A big DMG read correctly by only ONE scale (2×) while the others garble must NOT be
    /// discarded to 0 (the enemy rows on this frame read 9,989 / 2,572 at 2× only).</summary>
    [Fact]
    public void Teams_WideColumn_KeepsLoneBigRead()
    {
        RequireScreenshot("teams-zerorow", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var (myC, enmC) = new ScreenDetector(ocr).FindTeamRowCentresByIcon(bmp);
            var (_, enm) = ocr.ExtractTeams(bmp, myC, enmC);
            Assert.Equal(9989, enm[0].DamageDealt);   // was 0 (only 2× read it)
            Assert.Equal(2572, enm[4].DamageDealt);   // was 0
        }
    }

    /// <summary>Plain vs white-mask read of the Personal All-Heroes stat cards on a frame where the
    /// top cards (brighter gradient) read 0 — elims 31, damage 18,033 should both recover.</summary>
    [Fact]
    public void Diagnostics_PersonalCardsWhiteMask()
    {
        RequireScreenshot("personal-sierra-bug", out var bmp);
        var lines = new List<string>();
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var cells = new (string name, Rectangle roi)[]
            {
                ("Elims(31)",   new Rectangle(740, 367, 200, 58)),   // left-col top value
                ("Damage(18033)", UiCoordinates.Personal_HeroDamage),
                ("Healing(0)",  UiCoordinates.Personal_Healing),
                ("Mitig(0)",    UiCoordinates.Personal_Mitigation),
            };
            foreach (var (name, roi) in cells)
            {
                var plain = ocr.ReadRegion(bmp, roi).Trim().Replace("\n", " ");
                var white = ocr.ReadRegionWhiteText(bmp, roi).Trim().Replace("\n", " ");
                lines.Add($"{name,-14} plain=[{plain}]   white=[{white}]");
            }
        }
        foreach (var l in lines) _out.WriteLine(l);
        File.WriteAllLines(Path.Combine(ScreenshotDir, "..", "personal-cards-diag.txt"), lines);
    }

    /// <summary>Reports the ALL HEROES button Y + each sidebar slot's hero-name read for frames with
    /// different hero counts, to derive the correct hero-count formula (spacer or not).</summary>
    [Theory]
    [InlineData("personal-sierra-bug")]   // 1 hero (SIERRA)
    [InlineData("personal-allheroes")]    // 3 heroes (Cassidy/Sojourn/Echo)
    public void Diagnostics_PersonalSidebarLayout(string file)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var allHeroes = new ScreenDetector(ocr).FindPersonalAllHeroes(bmp);
            _out.WriteLine($"{file}: ALL HEROES button = {allHeroes}");
            if (allHeroes is { } p)
            {
                var slot = (p.Y - 308 + 50) / 100;
                _out.WriteLine($"  computed allHeroesSlot={slot}  (current heroCount = {Math.Clamp(slot - 1, 0, 5)})");
            }
            for (var i = 0; i < 6; i++)
            {
                var nm = ocr.ReadHeroTabName(bmp, i);
                _out.WriteLine($"  slot {i} (y={308 + i * 100}): hero=[{nm ?? "—"}]");
            }
        }
    }

    // White mask recovers the Personal All-Heroes stats on a bright frame (top cards read 0 before).
    // The DMG value is the IsMe fingerprint, so this also fixes "couldn't identify me".
    [Fact]
    public void Personal_AllHeroes_WhiteMaskRecoversStats()
    {
        RequireScreenshot("personal-sierra-bug", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var s = ocr.ExtractPersonalAllHeroes(bmp);
            Assert.Equal(18033, s.DamageDealt);    // was 0 (plain read "[RKO KK")
            Assert.Equal(31,    s.Eliminations);   // was 0
            Assert.Equal(9,     s.Deaths);
            Assert.Equal(0,     s.HealingDone);
            Assert.Equal(0,     s.DamageMitigated);
        }
    }

    // Filled-slot hero count: 1 for the single-hero (Sierra) frame, 3 for the 3-hero fixture —
    // robust to the inconsistent blank spacer pill that the old allHeroesSlot−1 mishandled.
    [Theory]
    [InlineData("personal-sierra-bug", 1, 1)]   // ALL HEROES at slot 1, no spacer → 1 hero
    [InlineData("personal-allheroes",  4, 3)]   // ALL HEROES at slot 4, spacer slot 3 → 3 heroes
    public void Personal_HeroCount_FromFilledSlots(string file, int allHeroesSlot, int expected)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var det = new ScreenDetector(ocr);
            var count = 0;
            for (var i = 0; i < allHeroesSlot && i < UiCoordinates.Personal_MaxHeroTabs; i++)
                if (det.PersonalSlotHasHero(bmp, i)) count++;
            Assert.Equal(expected, count);
        }
    }

    [Fact]
    public void Personal_ReadsHeroPlayTime()
    {
        RequireScreenshot("personal", out var bmp);   // ECHO view, PLAY TIME = 10:01
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var t = ocr.ExtractHeroPlayTime(bmp);
            _out.WriteLine($"PlayTime = {t}");
            Assert.Equal(new TimeSpan(0, 10, 1), t);
        }
    }

    [Fact]
    public void Summary_ParsesAllFields()
    {
        RequireScreenshot("summary", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var data = ocr.ExtractSummary(bmp);
            var lines = new List<string>
            {
                "=== Parsed summary ===",
                $"Map        : {data.MapName}",
                $"Outcome    : {data.Outcome}",
                $"Score      : {data.MyTeamScore} vs {data.EnemyTeamScore}",
                $"Date       : {data.MatchDatetime:MM/dd/yy HH:mm}",
                $"Mode       : {data.GameMode}",
                $"Length     : {data.GameLength}",
            };
            foreach (var h in data.HeroCards)
                lines.Add($"  Hero: [{h.HeroName}]  {h.PlayTime}  {h.PercentPlayed}%");
            foreach (var l in lines) _out.WriteLine(l);
            File.WriteAllLines(
                Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "summary-verify.txt")), lines);

            Assert.False(string.IsNullOrWhiteSpace(data.MapName),
                "Map name empty — check Summary_MapName ROI");
            Assert.True(data.MatchDatetime != DateTime.MinValue,
                "Date parse failed — check Summary_Date ROI");
        }
    }

    // ── Personal tab → ALL HEROES ─────────────────────────────────────────

    /// <summary>
    /// Regression guard for the Personal→All-Heroes parse against a real captured frame.
    /// Ground truth for personal-allheroes.png: E=18 A=2 D=5 DMG=8581 HEAL=235 MIT=129.
    /// The fingerprint key (DMG) plus the values Teams reads least reliably (E/D) must be exact;
    /// Assists is allowed to miss (a lone "2" doesn't OCR; it falls back to the Teams value).
    /// </summary>
    [Fact]
    public void PersonalAllHeroes_ParsesCapturedFrame()
    {
        RequireScreenshot("personal-allheroes", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var s = ocr.ExtractPersonalAllHeroes(bmp);
            _out.WriteLine($"E={s.Eliminations} A={s.Assists} D={s.Deaths} " +
                           $"DMG={s.DamageDealt} HEAL={s.HealingDone} MIT={s.DamageMitigated}");

            Assert.Equal(8581, s.DamageDealt);     // fingerprint key — must be exact
            Assert.Equal(235,  s.HealingDone);
            Assert.Equal(129,  s.DamageMitigated);
            Assert.Equal(18,   s.Eliminations);
            Assert.Equal(5,    s.Deaths);

            var heroes = ocr.ExtractPersonalHeroNames(bmp);
            _out.WriteLine("Heroes: " + string.Join(", ", heroes));
            File.WriteAllText(
                Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "personal-heroes.txt")),
                string.Join(", ", heroes));
            Assert.Contains("Cassidy", heroes);
            Assert.Contains("Sojourn", heroes);
            Assert.Contains("Echo",    heroes);
        }
    }

    // ── Teams tab (5v5 and 6v6) ───────────────────────────────────────────

    [Theory]
    [InlineData("teams",     5)]
    [InlineData("6v6-teams", 6)]
    public void Teams_PrintsRawStatColumns(string file, int expectedTeamSize)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var detector   = new ScreenDetector(ocr);
            var vsDividerY = detector.FindVsDividerY(bmp);
            _out.WriteLine($"VS divider Y = {vsDividerY}  (expected ~{183 + expectedTeamSize * 50 + 30})");
            Assert.True(vsDividerY > 0, "VS divider not found");

            _out.WriteLine($"\n=== Raw columns — my team ({file}) ===");
            for (var i = 0; i < expectedTeamSize; i++)
            {
                var rowY = UiCoordinates.Teams_MyTeamRowY(i);
                _out.WriteLine($"Row {i} (y={rowY}):");
                PrintRegion(ocr, bmp, "  Username", UiCoordinates.Teams_Username(rowY));
                PrintRegion(ocr, bmp, "  E       ", UiCoordinates.Teams_ColE(rowY));
                PrintRegion(ocr, bmp, "  A       ", UiCoordinates.Teams_ColA(rowY));
                PrintRegion(ocr, bmp, "  D       ", UiCoordinates.Teams_ColD(rowY));
                PrintRegion(ocr, bmp, "  DMG     ", UiCoordinates.Teams_ColDmg(rowY));
                PrintRegion(ocr, bmp, "  H       ", UiCoordinates.Teams_ColH(rowY));
                PrintRegion(ocr, bmp, "  MIT     ", UiCoordinates.Teams_ColMit(rowY));
            }
        }
    }

    // 5v5 only: 6v6 sizing now comes from the queue label, not the divider — see
    // Teams_Extracts6v6FromQueueSize for the 6v6 path.
    [Theory]
    [InlineData("teams",     5)]
    public void Teams_ParsesAllRows(string file, int expectedTeamSize)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var detector   = new ScreenDetector(ocr);
            var vsDividerY = detector.FindVsDividerY(bmp);
            Assert.True(vsDividerY > 0, "VS divider not found");

            var (myTeam, enemyTeam) = ocr.ExtractTeams(bmp, vsDividerY);

            _out.WriteLine($"=== {file}: {myTeam.Count} vs {enemyTeam.Count} ===");
            _out.WriteLine("My team:");
            PrintTeam(myTeam);
            _out.WriteLine("Enemy team:");
            PrintTeam(enemyTeam);

            Assert.Equal(expectedTeamSize, myTeam.Count);
            Assert.Equal(expectedTeamSize, enemyTeam.Count);
        }
    }

    // ── Diagnostics ───────────────────────────────────────────────────────

    /// <summary>
    /// Prints screenshot dimensions and pixel-brightness VS scan.
    /// Output is written to  test-screenshots/../diagnostic-output.txt  (solution root)
    /// as well as xUnit output, so it's always readable regardless of how tests are run.
    /// </summary>
    [Fact]
    public void Diagnostics_PrintDimensionsAndVsScan()
    {
        var logPath = Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "diagnostic-output.txt"));
        var lines   = new List<string>();
        void Log(string s) { _out.WriteLine(s); lines.Add(s); }

        Log($"=== Screenshot dimensions ===");
        foreach (var name in new[] { "summary", "teams", "6v6-teams", "gamereports",
                                     "escape_menu", "career-profile-overview",
                                     "career-profile-history", "personal" })
        {
            var path = new[] { ".jpg", ".jpeg", ".png" }
                .Select(ext => Path.Combine(ScreenshotDir, name + ext))
                .FirstOrDefault(File.Exists);
            if (path is null) { Log($"  {name}: NOT FOUND"); continue; }
            using var bmp = new Bitmap(path);
            Log($"  {name}: {bmp.Width} x {bmp.Height}");
        }

        // Pixel brightness scan — shows what FindVsDividerY sees.
        foreach (var name in new[] { "teams", "6v6-teams" })
        {
            var path = new[] { ".jpg", ".jpeg", ".png" }
                .Select(ext => Path.Combine(ScreenshotDir, name + ext))
                .FirstOrDefault(File.Exists);
            if (path is null) { Log($"\n{name}: NOT FOUND — skipping brightness scan"); continue; }

            using var bmp = new Bitmap(path);
            Log($"\n=== Brightness scan — {name} ({bmp.Width}x{bmp.Height}) ===");
            Log("  avg brightness across x=500-950 sampled every 25px, printed every 5px");

            for (var y = 300; y <= Math.Min(700, bmp.Height - 1); y += 5)
            {
                var total = 0; var count = 0;
                for (var x = 500; x <= Math.Min(950, bmp.Width - 1); x += 25)
                {
                    var px = bmp.GetPixel(x, y);
                    total += (px.R + px.G + px.B) / 3;
                    count++;
                }
                var avg = count > 0 ? total / count : 0;
                var marker = avg < 55 ? "  ←" : "";
                Log($"  y={y,4}: avg={avg,3}{marker}");
            }

            using var ocr2 = MakeOcr();
            var det = new ScreenDetector(ocr2);
            Log($"\n  FindVsDividerY returned: {det.FindVsDividerY(bmp)}");
        }

        // Always write to file.
        File.WriteAllLines(logPath, lines);
        Log($"\nOutput also saved to: {logPath}");
    }

    /// <summary>
    /// Verification test: reads every player row from the Teams tab using the calibrated
    /// coordinates and writes the results to  teams-verify-output.txt  at the solution root.
    /// Run this after updating UiCoordinates Teams constants to confirm they are correct.
    /// </summary>
    [Theory]
    [InlineData("teams",     5)]
    [InlineData("6v6-teams", 6)]
    public void Diagnostics_VerifyTeamsRows(string file, int expectedTeamSize)
    {
        RequireScreenshot(file, out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var logPath = Path.GetFullPath(
                Path.Combine(ScreenshotDir, "..", $"teams-verify-{file}.txt"));
            var lines   = new List<string>();
            void Log(string s) { _out.WriteLine(s); lines.Add(s); }

            var detector   = new ScreenDetector(ocr);
            var vsDividerY = detector.FindVsDividerY(bmp);
            Log($"=== Teams verification — {file} ===");
            Log($"VS divider Y = {vsDividerY}  (expected {UiCoordinates.Teams_FirstRowY + expectedTeamSize * UiCoordinates.Teams_RowHeight})");
            Log($"Teams_FirstRowY={UiCoordinates.Teams_FirstRowY}  Teams_RowHeight={UiCoordinates.Teams_RowHeight}  Teams_VsGapHeight={UiCoordinates.Teams_VsGapHeight}");
            Log("");

            void PrintRows(string header, bool myTeam)
            {
                Log(header);
                for (var i = 0; i < expectedTeamSize; i++)
                {
                    var rowY = myTeam
                        ? UiCoordinates.Teams_MyTeamRowY(i)
                        : UiCoordinates.Teams_EnemyRowY(vsDividerY, i);
                    string Read(System.Drawing.Rectangle roi)
                    {
                        try { return ocr.ReadRegion(bmp, roi).Trim().Replace("\n", " "); }
                        catch { return "ERR"; }
                    }
                    var user = Read(UiCoordinates.Teams_Username(rowY));
                    var e    = Read(UiCoordinates.Teams_ColE(rowY));
                    var a    = Read(UiCoordinates.Teams_ColA(rowY));
                    var d    = Read(UiCoordinates.Teams_ColD(rowY));
                    var dmg  = Read(UiCoordinates.Teams_ColDmg(rowY));
                    var h    = Read(UiCoordinates.Teams_ColH(rowY));
                    var mit  = Read(UiCoordinates.Teams_ColMit(rowY));
                    Log($"  Row {i} (y={rowY}): user=[{user}]  E=[{e}] A=[{a}] D=[{d}]  DMG=[{dmg}]  H=[{h}]  MIT=[{mit}]");
                }
            }

            PrintRows("My team:", myTeam: true);
            Log("");
            PrintRows("Enemy team:", myTeam: false);

            // Also dump the REAL extraction path (strip-based ReadStatRow via ExtractTeams).
            Log("");
            Log("── ExtractTeams (strip reader — the production path) ──");
            var (myTeam, enemyTeam) = ocr.ExtractTeams(bmp, vsDividerY);
            void Dump(string h, IReadOnlyList<TeamPlayerData> t)
            {
                Log(h);
                foreach (var p in t)
                    Log($"  {p.Username,-18} E={p.Eliminations,-3} A={p.Assists,-3} D={p.Deaths,-3} " +
                        $"DMG={p.DamageDealt,-6} H={p.HealingDone,-6} MIT={p.DamageMitigated}");
            }
            Dump("My team:", myTeam);
            Dump("Enemy team:", enemyTeam);

            File.WriteAllLines(logPath, lines);
            Log($"\nSaved to: {logPath}");
        }
    }

    // ── Game Reports list ─────────────────────────────────────────────────

    /// <summary>
    /// 2D sweep of the Game Reports list screen.
    /// Left sweep  (x=60-900,   step 60):  finds row Y positions — map names, dates, outcomes.
    /// Right sweep (x=900-1400, step 80):  finds queue-label Y and X positions.
    /// Output saved to  gamereports-scan.txt.
    /// </summary>
    [Fact]
    public void Diagnostics_ScanGameReportsList()
    {
        RequireScreenshot("gamereports", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var logPath = Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "gamereports-scan.txt"));
            var lines   = new List<string>();
            void Log(string s) { _out.WriteLine(s); lines.Add(s); }

            Log($"=== Game Reports sweep — gamereports ({bmp.Width}x{bmp.Height}) ===");

            // ── Left panel (row structure, map names, outcomes) ────────────────
            Log("\n-- Left panel: x=60-900 (step 60), y=200-800 (step 15), probe w=200 h=28 --");
            for (var y = 200; y <= 800; y += 15)
            {
                var rowHits = new List<string>();
                for (var x = 60; x <= 900; x += 60)
                {
                    var roi = new System.Drawing.Rectangle(x, y,
                        Math.Min(200, bmp.Width - x), Math.Min(28, bmp.Height - y));
                    if (roi.Width <= 0 || roi.Height <= 0) continue;
                    try
                    {
                        var text = ocr.ReadRegion(bmp, roi).Trim().Replace("\n", " ");
                        if (!string.IsNullOrWhiteSpace(text) && text.Length > 1)
                            rowHits.Add($"x{x}:[{text}]");
                    }
                    catch { }
                }
                if (rowHits.Count > 0)
                    Log($"  y={y,4} | {string.Join(" ", rowHits)}");
            }

            // ── Right panel (queue labels) ─────────────────────────────────────
            Log("\n-- Right panel: x=800-1500 (step 80), y=200-800 (step 15), probe w=200 h=28 --");
            for (var y = 200; y <= 800; y += 15)
            {
                var rowHits = new List<string>();
                for (var x = 800; x <= 1500; x += 80)
                {
                    var roi = new System.Drawing.Rectangle(x, y,
                        Math.Min(200, bmp.Width - x), Math.Min(28, bmp.Height - y));
                    if (roi.Width <= 0 || roi.Height <= 0) continue;
                    try
                    {
                        var text = ocr.ReadRegion(bmp, roi).Trim().Replace("\n", " ");
                        if (!string.IsNullOrWhiteSpace(text) && text.Length > 2)
                            rowHits.Add($"x{x}:[{text}]");
                    }
                    catch { }
                }
                if (rowHits.Count > 0)
                    Log($"  y={y,4} | {string.Join(" ", rowHits)}");
            }

            File.WriteAllLines(logPath, lines);
            Log($"\nSaved to: {logPath}");
        }
    }

    /// <summary>
    /// Scans the bottom half of the Game Reports screen (y=800-1440) to locate the
    /// "VIEW GAME REPORT" button and verify the queue-ROI calibration against the
    /// first visible rows.  Output saved to  gamereports-bottom-scan.txt.
    /// </summary>
    [Fact]
    public void Diagnostics_ScanGameReportsBottom()
    {
        RequireScreenshot("gamereports", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            var logPath = Path.GetFullPath(Path.Combine(ScreenshotDir, "..", "gamereports-bottom-scan.txt"));
            var lines   = new List<string>();
            void Log(string s) { _out.WriteLine(s); lines.Add(s); }

            Log($"=== Game Reports bottom scan — gamereports ({bmp.Width}x{bmp.Height}) ===");

            // Full-width sweep of the bottom portion to find the VIEW GAME REPORT button.
            Log("\n-- Bottom sweep: x=400-1200 (step 100), y=800-1440 (step 20), probe w=300 h=32 --");
            for (var y = 800; y <= Math.Min(1440, bmp.Height) - 32; y += 20)
            {
                var rowHits = new List<string>();
                for (var x = 400; x <= 1200; x += 100)
                {
                    var roi = new System.Drawing.Rectangle(x, y,
                        Math.Min(300, bmp.Width - x), 32);
                    if (roi.Width <= 0) continue;
                    try
                    {
                        var text = ocr.ReadRegion(bmp, roi).Trim().Replace("\n", " ");
                        if (!string.IsNullOrWhiteSpace(text) && text.Length > 3)
                            rowHits.Add($"x{x}:[{text}]");
                    }
                    catch { }
                }
                if (rowHits.Count > 0)
                    Log($"  y={y,4} | {string.Join(" ", rowHits)}");
            }

            // Also verify the calibrated queue ROI against each visible row.
            Log("\n-- Calibrated QueueRoi reads (rows 0-5) --");
            for (var i = 0; i < 6; i++)
            {
                var roi = UiCoordinates.GameReportsList_QueueRoi(i);
                try
                {
                    var text = ocr.ReadRegionBlock(bmp, roi).Trim().Replace("\n", " / ");
                    Log($"  Row {i} (y={UiCoordinates.GameReportsList_RowY(i)}): [{text}]");
                }
                catch (Exception ex) { Log($"  Row {i}: ERR {ex.Message}"); }
            }

            File.WriteAllLines(logPath, lines);
            Log($"\nSaved to: {logPath}");
        }
    }

    [Fact]
    public void GameReports_PrintsQueueInfoForVisibleRows()
    {
        RequireScreenshot("gamereports", out var bmp);
        using (bmp)
        using (var ocr = MakeOcr())
        {
            _out.WriteLine("=== Queue info per row ===");
            for (var i = 0; i < 8; i++)
            {
                var q = ocr.ExtractQueueRow(bmp, i);
                _out.WriteLine($"Row {i}: [{q.RankingMode}] / [{q.QueueType}]");
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void PrintRegion(OcrEngine ocr, Bitmap bmp, string label, System.Drawing.Rectangle roi)
    {
        try
        {
            var text = ocr.ReadRegion(bmp, roi).Trim().Replace("\n", "\\n").Replace("\r", "");
            _out.WriteLine($"  {label}: [{text}]");
        }
        catch (Exception ex) { _out.WriteLine($"  {label}: ERROR — {ex.Message}"); }
    }

    private void PrintTeam(IReadOnlyList<TeamPlayerData> team)
    {
        foreach (var p in team)
            _out.WriteLine($"  [{p.Username,-20}] E={p.Eliminations,3} A={p.Assists,3} D={p.Deaths,3} " +
                           $"DMG={p.DamageDealt,6} H={p.HealingDone,6} MIT={p.DamageMitigated,6}");
    }

    private static OcrEngine MakeOcr()
    {
        var tessDir = TessDataManager.TessDataDirectory;
        Assert.True(File.Exists(Path.Combine(tessDir, "eng.traineddata")),
            $"eng.traineddata not found at {tessDir}\nRun the app once to download it.");
        return new OcrEngine(tessDir);
    }

    private void RequireScreenshot(string baseName, out Bitmap bmp)
    {
        var candidates = new[] { ".jpg", ".jpeg", ".png" }
            .Select(ext => Path.Combine(ScreenshotDir, baseName + ext))
            .ToArray();

        var path = candidates.FirstOrDefault(File.Exists);
        _out.WriteLine($"Loading: {path ?? candidates[0]}");
        Assert.True(path is not null,
            $"Screenshot not found. Place '{baseName}.jpg' in:\n  {ScreenshotDir}");
        bmp = new Bitmap(path!);
    }
}
