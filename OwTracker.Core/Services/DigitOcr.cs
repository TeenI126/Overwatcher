using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace OwTracker.Core.Services;

/// <summary>
/// Reads the Teams scoreboard stat NUMBERS by template-matching each digit glyph against a trained
/// set (0–9) rather than with general OCR. The stat font is fixed at 2560×1440, so every digit is a
/// known shape — this removes the glyph ambiguities (notably 7→4) that Tesseract's general LSTM hits
/// on isolated digits. Text regions (map / hero names) still use Tesseract + the white-text mask.
///
/// The template set is an embedded asset (<c>Assets/DigitTemplates.json</c>) built offline by
/// <c>OwTracker.OcrLab</c> from real frames (see <see cref="Segment"/>/<see cref="Match"/>, which the
/// builder reuses so training and inference share identical pre-processing).
/// </summary>
public sealed class DigitOcr
{
    public const int GW = 16, GH = 24;                 // normalised glyph size
    private const int WhiteThreshold = 180;            // near-white text isolation
    private const string ResourceName = "OwTracker.Core.Assets.DigitTemplates.json";

    private readonly Dictionary<char, float[]> _templates;

    /// <summary>True once a full 0–9 template set is loaded (else ReadCell always low-confidence).</summary>
    public bool IsReady => _templates.Count >= 10;

    public DigitOcr() => _templates = LoadTemplates();

    /// <summary>Reads the integer in <paramref name="cell"/>, with a 0..1 confidence (the weakest
    /// per-glyph match score). Returns (0, 0) if no glyphs are found or templates are missing.</summary>
    public (int Value, double Confidence) ReadCell(Bitmap screen, Rectangle cell)
    {
        if (_templates.Count == 0) return (0, 0);
        var glyphs = Segment(screen, cell);
        if (glyphs.Count == 0) return (0, 0);

        var sb = new StringBuilder();
        double minScore = 1.0;
        foreach (var g in glyphs)
        {
            var (d, score) = Match(g, _templates);
            sb.Append(d);
            minScore = Math.Min(minScore, score);
        }
        return int.TryParse(sb.ToString(), out var v) ? (v, minScore) : (0, 0);
    }

    // ── Segmentation (shared with the offline template builder) ───────────────────────────────
    /// <summary>White-masks the cell, splits it into digit glyphs (touching digits divided by width,
    /// commas/specks dropped) and returns each as a normalised GW×GH occupancy vector, left→right.</summary>
    public static List<float[]> Segment(Bitmap screen, Rectangle cell)
    {
        var rect = Rectangle.Intersect(new Rectangle(0, 0, screen.Width, screen.Height), cell);
        var glyphs = new List<float[]>();
        if (rect.Width < 2 || rect.Height < 2) return glyphs;

        int w = rect.Width, h = rect.Height;
        var data   = screen.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = data.Stride;
        var buf    = new byte[Math.Abs(stride) * h];
        Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        screen.UnlockBits(data);

        var mask = new bool[h, w];
        for (var y = 0; y < h; y++)
        {
            var rb = y * stride;
            for (var x = 0; x < w; x++)
            {
                var i = rb + x * 4;                     // BGRA
                mask[y, x] = buf[i] > WhiteThreshold && buf[i + 1] > WhiteThreshold && buf[i + 2] > WhiteThreshold;
            }
        }

        // columns containing ink → contiguous runs
        var runs = new List<(int X0, int X1)>();
        int start = -1;
        for (var x = 0; x < w; x++)
        {
            var ink = false;
            for (var y = 0; y < h; y++) if (mask[y, x]) { ink = true; break; }
            if (ink) { if (start < 0) start = x; }
            else if (start >= 0) { runs.Add((start, x - 1)); start = -1; }
        }
        if (start >= 0) runs.Add((start, w - 1));

        // vertical extent per run; drop commas (short) and specks
        var boxes = new List<(int X0, int X1, int Y0, int Y1)>();
        foreach (var (x0, x1) in runs)
        {
            int y0 = h, y1 = -1;
            for (var y = 0; y < h; y++)
                for (var x = x0; x <= x1; x++) if (mask[y, x]) { y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); break; }
            if (y1 >= 0) boxes.Add((x0, x1, y0, y1));
        }
        if (boxes.Count == 0) return glyphs;
        var maxH = boxes.Max(b => b.Y1 - b.Y0 + 1);
        boxes = boxes.Where(b => b.Y1 - b.Y0 + 1 >= maxH * 0.55 && b.X1 - b.X0 + 1 >= 2).ToList();
        if (boxes.Count == 0) return glyphs;

        // touching digits: split a run into round(width / typical-digit-width) equal slices
        var medW = boxes.Select(b => b.X1 - b.X0 + 1).OrderBy(x => x).ElementAt(boxes.Count / 2);
        foreach (var (x0, x1, _, _) in boxes)
        {
            var wRun = x1 - x0 + 1;
            var n = Math.Max(1, (int)Math.Round(wRun / (double)medW));
            for (var i = 0; i < n; i++)
                glyphs.Add(Normalise(mask, h, x0 + i * wRun / n, x0 + (i + 1) * wRun / n - 1));
        }
        return glyphs;
    }

    /// <summary>Crops the glyph's bounding box within column span [x0,x1] and resizes to GW×GH (0/1).</summary>
    private static float[] Normalise(bool[,] mask, int h, int x0, int x1)
    {
        int y0 = h, y1 = -1;
        for (var y = 0; y < h; y++)
            for (var x = x0; x <= x1; x++) if (mask[y, x]) { y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); break; }
        if (y1 < 0) { y0 = 0; y1 = h - 1; }
        int gh = y1 - y0 + 1, gw = Math.Max(1, x1 - x0 + 1);
        var v = new float[GW * GH];
        for (var y = 0; y < GH; y++)
            for (var x = 0; x < GW; x++)
            {
                var sy = y0 + y * gh / GH;
                var sx = x0 + x * gw / GW;
                v[y * GW + x] = (sy >= 0 && sy < mask.GetLength(0) && sx >= 0 && sx < mask.GetLength(1) && mask[sy, sx]) ? 1f : 0f;
            }
        return v;
    }

    /// <summary>Best-matching digit by fraction of agreeing pixels (0..1).</summary>
    public static (char Digit, double Score) Match(float[] v, IReadOnlyDictionary<char, float[]> templates)
    {
        char best = '?'; double bestScore = -1;
        foreach (var (d, t) in templates)
        {
            double agree = 0;
            for (var k = 0; k < v.Length; k++) agree += 1 - Math.Abs(v[k] - t[k]);
            agree /= v.Length;
            if (agree > bestScore) { bestScore = agree; best = d; }
        }
        return (best, bestScore < 0 ? 0 : bestScore);
    }

    // ── Template asset (de)serialization ──────────────────────────────────────────────────────
    public static string SerializeTemplates(IReadOnlyDictionary<char, float[]> templates)
        => JsonSerializer.Serialize(templates.ToDictionary(k => k.Key.ToString(), v => v.Value));

    private static Dictionary<char, float[]> LoadTemplates()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (s is null) return new();
            var raw = JsonSerializer.Deserialize<Dictionary<string, float[]>>(s);
            return raw is null ? new() : raw.Where(kv => kv.Key.Length == 1)
                                            .ToDictionary(kv => kv.Key[0], kv => kv.Value);
        }
        catch { return new(); }
    }
}
