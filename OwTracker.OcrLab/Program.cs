using System.Drawing;
using System.Drawing.Imaging;
using OwTracker.Core.Services;

// ── Template-matching prototype for the Teams stat digits ──────────────────────────────────────
// The stat numbers are a fixed font at a fixed size, so each digit 0–9 is a known glyph. We build a
// template per digit from a few cells whose values we know, then read any cell by white-masking it,
// splitting it into glyphs, and matching each glyph to the closest template. This sidesteps the
// general-OCR ambiguities (7→4) that Tesseract hits on isolated digits.
//
// Run: dotnet run --project OwTracker.OcrLab

const string Dir = @"F:\User\Documents\Projects\Overwatcher\test-screenshots";
const int GW = 16, GH = 24;        // normalised glyph size for matching

// Column centres: E,A,D (narrow) and DMG,H,MIT (wide).
int[] colX = UiCoordinates.Teams_StatColumnCentersX;

// (file, rowCentre, colIndex, knownValue) — used to BUILD templates (multi-digit cells covering 0–9).
var refs = new (string F, int C, int Col, string Val)[]
{
    ("recent3", 357,  3, "16193"), ("recent3", 357, 5, "18020"), ("recent3", 357, 4, "3010"),
    ("recent3", 923,  3, "14539"), ("recent3", 1096, 3, "7961"),
    ("recent3", 449,  3, "7519"),  ("recent3", 713, 4, "15380"), ("recent3", 1265, 4, "10194"),
    ("recent1", 1002, 3, "8087"),  ("recent3", 1011, 3, "9217"),
};

// (file, rowCentre, colIndex, truth) — cells to TEST (incl. the ones Tesseract/Windows OCR missed).
var tests = new (string F, int C, int Col, string Truth)[]
{
    ("recent3", 1096, 0, "7"),  ("recent3", 1096, 2, "8"),   // ENM2 E/D — Tesseract 7→4
    ("recent3", 357,  0, "25"), ("recent3", 357,  1, "6"),   // MY0 E/A
    ("teams-zerorow", 937, 0, "7"), ("teams-zerorow", 937, 2, "7"),
    ("recent3", 923,  3, "14539"),                            // wide control
    ("teams-samoa-dmgbug", 362, 0, "17"), ("teams-samoa-dmgbug", 362, 2, "4"),
};

// Build one averaged template per digit.
var acc = new Dictionary<char, (float[] Sum, int N)>();
foreach (var r in refs)
{
    var glyphs = Glyphs(Cell(r.F, r.C, r.Col));
    if (glyphs.Count != r.Val.Length)
    {
        Console.WriteLine($"[build] {r.F}@{r.C} col{r.Col} '{r.Val}': segmented {glyphs.Count} glyphs (expected {r.Val.Length}) — skipped");
        continue;
    }
    for (var i = 0; i < r.Val.Length; i++)
    {
        var v = Norm(glyphs[i]);
        if (!acc.TryGetValue(r.Val[i], out var a)) a = (new float[GW * GH], 0);
        for (var k = 0; k < v.Length; k++) a.Sum[k] += v[k];
        acc[r.Val[i]] = (a.Sum, a.N + 1);
    }
}
var templates = acc.ToDictionary(kv => kv.Key, kv => kv.Value.Sum.Select(s => s / kv.Value.N).ToArray());
Console.WriteLine($"Built templates for digits: {string.Join("", templates.Keys.OrderBy(c => c))}\n");

// Evaluate.
int ok = 0;
foreach (var t in tests)
{
    var read = string.Concat(Glyphs(Cell(t.F, t.C, t.Col)).Select(g => Match(Norm(g), templates)));
    var pass = read == t.Truth;
    if (pass) ok++;
    Console.WriteLine($"{(pass ? "OK " : "XX ")} {t.F}@{t.C} col{t.Col}: read=[{read}]  truth=[{t.Truth}]");
}
Console.WriteLine($"\n{ok}/{tests.Length} correct");

// ── helpers ────────────────────────────────────────────────────────────────────────────────────
Bitmap Cell(string file, int centre, int col)
{
    using var full = new Bitmap(Path.Combine(Dir, file + ".png"));
    var half = col < 3 ? 38 : 78;                       // narrow vs wide cell width
    var roi  = new Rectangle(colX[col] - half, centre - 26, half * 2, 52);
    roi.Intersect(new Rectangle(0, 0, full.Width, full.Height));
    return full.Clone(roi, full.PixelFormat);
}

// White-mask → list of glyph bitmaps (bool grids), comma/noise filtered, left→right.
static List<bool[,]> Glyphs(Bitmap cell)
{
    using var _ = cell;
    int w = cell.Width, h = cell.Height;
    var mask = new bool[h, w];
    for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var p = cell.GetPixel(x, y);
            mask[y, x] = p.R > 180 && p.G > 180 && p.B > 180;
        }

    // column has ink?
    var colInk = new bool[w];
    for (var x = 0; x < w; x++)
        for (var y = 0; y < h; y++) if (mask[y, x]) { colInk[x] = true; break; }

    // contiguous runs of ink columns → candidate glyphs
    var runs = new List<(int X0, int X1)>();
    int start = -1;
    for (var x = 0; x < w; x++)
    {
        if (colInk[x]) { if (start < 0) start = x; }
        else if (start >= 0) { runs.Add((start, x - 1)); start = -1; }
    }
    if (start >= 0) runs.Add((start, w - 1));

    // for each run, find vertical extent; drop commas (short, low) and specks
    var boxes = new List<(int X0, int X1, int Y0, int Y1)>();
    foreach (var (x0, x1) in runs)
    {
        int y0 = h, y1 = -1;
        for (var y = 0; y < h; y++)
            for (var x = x0; x <= x1; x++) if (mask[y, x]) { y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); break; }
        if (y1 >= 0) boxes.Add((x0, x1, y0, y1));
    }
    var glyphs = new List<bool[,]>();
    if (boxes.Count == 0) return glyphs;
    var maxH = boxes.Max(b => b.Y1 - b.Y0 + 1);
    boxes = boxes.Where(b => b.Y1 - b.Y0 + 1 >= maxH * 0.55 && b.X1 - b.X0 + 1 >= 2).ToList();  // drop commas/specks
    if (boxes.Count == 0) return glyphs;

    // Touching digits merge into one wide run; split a run into round(width / typical-digit-width)
    // equal slices. Typical width = median of the (mostly single-digit) run widths.
    var widths = boxes.Select(b => b.X1 - b.X0 + 1).OrderBy(x => x).ToList();
    var medW = widths[widths.Count / 2];
    bool[,] Crop(int x0, int x1)
    {
        int y0 = h, y1 = -1;
        for (var y = 0; y < h; y++) for (var x = x0; x <= x1; x++) if (mask[y, x]) { y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); break; }
        if (y1 < 0) return new bool[1, 1];
        var g = new bool[y1 - y0 + 1, x1 - x0 + 1];
        for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++) g[y - y0, x - x0] = mask[y, x];
        return g;
    }
    foreach (var (x0, x1, _, _) in boxes)
    {
        var wRun = x1 - x0 + 1;
        var n = Math.Max(1, (int)Math.Round(wRun / (double)medW));
        if (n == 1) { glyphs.Add(Crop(x0, x1)); continue; }
        for (var i = 0; i < n; i++)                      // split touching digits into equal slices
            glyphs.Add(Crop(x0 + i * wRun / n, x0 + (i + 1) * wRun / n - 1));
    }
    return glyphs;
}

// Resize a glyph to GWxGH normalised float vector (0/1).
static float[] Norm(bool[,] g)
{
    int gh = g.GetLength(0), gw = g.GetLength(1);
    var v = new float[GW * GH];
    for (var y = 0; y < GH; y++)
        for (var x = 0; x < GW; x++)
        {
            var sy = y * gh / GH; var sx = x * gw / GW;
            v[y * GW + x] = g[sy, sx] ? 1f : 0f;
        }
    return v;
}

// Best-matching digit by fraction of agreeing pixels.
static char Match(float[] v, Dictionary<char, float[]> templates)
{
    char best = '?'; float bestScore = -1;
    foreach (var (d, t) in templates)
    {
        float agree = 0;
        for (var k = 0; k < v.Length; k++) agree += 1 - Math.Abs(v[k] - t[k]);
        if (agree > bestScore) { bestScore = agree; best = d; }
    }
    return best;
}
