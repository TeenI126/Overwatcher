using System.Drawing;
using OwTracker.Core.Services;

// ── Template generator + evaluator for DigitOcr ────────────────────────────────────────────────
// Auto-labels training data: for each stat cell, if all THREE Tesseract upscales (2×/3×/4×) agree on
// a non-zero value, that's a high-confidence label — segment the cell and accumulate its glyphs into
// the per-digit template average. Cells where the scales DISAGREE (the ambiguous ones, e.g. 7→4) are
// excluded from training and used as the eval set. Writes Core/Assets/DigitTemplates.json.
//
// Run: dotnet run --project OwTracker.OcrLab

const string ShotDir = @"F:\User\Documents\Projects\Overwatcher\test-screenshots";
const string AssetOut = @"F:\User\Documents\Projects\Overwatcher\OwTracker.Core\Assets\DigitTemplates.json";

int[] colX = UiCoordinates.Teams_StatColumnCentersX;
string[] teamsFrames =
{
    "recent1", "recent2", "recent3", "teams-zerorow", "teams-samoa-dmgbug",
    "teams-live-good", "teams-live", "teams-6v6-live",
};

Rectangle CellRect(int col, int centre)
{
    var half = col < 3 ? 36 : 60;
    return new Rectangle(colX[col] - half, centre - 26, half * 2, 52);
}

using var ocr = MakeOcr();
var detector = new ScreenDetector(ocr);

// ── 1. Build templates from agree-cells across all teams frames ──
var acc = new Dictionary<char, (double[] Sum, int N)>();
var perDigit = new Dictionary<char, int>();
foreach (var file in teamsFrames)
{
    var path = Path.Combine(ShotDir, file + ".png");
    if (!File.Exists(path)) { Console.WriteLine($"(skip missing {file})"); continue; }
    using var bmp = new Bitmap(path);
    var (my, enm) = detector.FindTeamRowCentresByIcon(bmp);
    foreach (var centre in my.Concat(enm))
    {
        var ps = ocr.DebugStatRowScales(bmp, centre - 49);     // [scale][col]
        for (var c = 0; c < colX.Length; c++)
        {
            var v = ps[0][c];
            if (v <= 0 || ps[1][c] != v || ps[2][c] != v) continue;   // need all 3 scales to agree
            var label = v.ToString();
            var glyphs = DigitOcr.Segment(bmp, CellRect(c, centre));
            if (glyphs.Count != label.Length) continue;               // segmentation must match label
            for (var i = 0; i < label.Length; i++)
            {
                var d = label[i];
                if (!acc.TryGetValue(d, out var a)) a = (new double[DigitOcr.GW * DigitOcr.GH], 0);
                for (var k = 0; k < glyphs[i].Length; k++) a.Sum[k] += glyphs[i][k];
                acc[d] = (a.Sum, a.N + 1);
                perDigit[d] = perDigit.GetValueOrDefault(d) + 1;
            }
        }
    }
}

var templates = acc.ToDictionary(kv => kv.Key, kv => kv.Value.Sum.Select(s => (float)(s / kv.Value.N)).ToArray());
Console.WriteLine($"Trained digits: {string.Join("", templates.Keys.OrderBy(c => c))}");
Console.WriteLine("Samples/digit: " + string.Join("  ", perDigit.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
File.WriteAllText(AssetOut, DigitOcr.SerializeTemplates(templates));
Console.WriteLine($"Wrote {AssetOut}\n");

// ── 2. Evaluate on the ambiguous cells (where scales disagreed) + controls ──
var tests = new (string F, int C, int Col, string Truth)[]
{
    ("recent3", 1096, 0, "7"), ("recent3", 1096, 2, "8"),
    ("recent3",  357, 0, "25"), ("recent3", 357, 1, "6"),
    ("teams-zerorow", 937, 0, "7"), ("teams-zerorow", 937, 2, "7"),
    ("teams-samoa-dmgbug", 362, 0, "17"), ("teams-samoa-dmgbug", 362, 2, "9"),  // glyph is a 9 (Tesseract vote had 4)
    ("recent3", 923, 3, "14539"), ("recent3", 1096, 3, "7961"),   // wide controls
};
int ok = 0;
foreach (var t in tests)
{
    using var bmp = new Bitmap(Path.Combine(ShotDir, t.F + ".png"));
    var glyphs = DigitOcr.Segment(bmp, CellRect(t.Col, t.C));
    var read = string.Concat(glyphs.Select(g => DigitOcr.Match(g, templates).Digit));
    var minScore = glyphs.Count == 0 ? 0 : glyphs.Min(g => DigitOcr.Match(g, templates).Score);
    var pass = read == t.Truth;
    if (pass) ok++;
    Console.WriteLine($"{(pass ? "OK " : "XX ")} {t.F}@{t.C} col{t.Col}: read=[{read}] conf={minScore:F3}  truth=[{t.Truth}]");
}
Console.WriteLine($"\n{ok}/{tests.Length} correct");

static OcrEngine MakeOcr() => new(TessDataManager.TessDataDirectory);
