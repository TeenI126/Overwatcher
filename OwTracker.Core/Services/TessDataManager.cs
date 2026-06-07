using OwTracker.Core;

namespace OwTracker.Core.Services;

/// <summary>
/// Ensures the Tesseract <c>eng.traineddata</c> file is present in
/// <c>%APPDATA%\OwTracker\tessdata\</c>, downloading it from the tessdata_best repository
/// on first run if needed.
///
/// Uses the "best" model (~15 MB), NOT "fast" (~4 MB): scraping OCR runs offline (not in a
/// hot loop), so accuracy matters far more than speed. The fast model misreads isolated
/// single digits in the Teams scoreboard (e.g. "5"→")", "6"→"(e)") which the best model reads
/// correctly. Installs that already have the smaller fast file are auto-upgraded (see IsReady).
/// </summary>
public sealed class TessDataManager
{
    private const string DownloadUrl =
        "https://github.com/tesseract-ocr/tessdata_best/raw/main/eng.traineddata";

    /// <summary>The best model is ~15 MB; anything materially smaller is the old fast model
    /// (or a truncated download) and should be re-fetched.</summary>
    private const long MinBestModelBytes = 8_000_000;

    public static string TessDataDirectory
    {
        get
        {
            var dir = Path.Combine(AppPaths.Root, "tessdata");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string TrainedDataPath =>
        Path.Combine(TessDataDirectory, "eng.traineddata");

    /// <summary>Ready only if the best model is present — a leftover small fast file fails the
    /// size check and triggers a one-time re-download/upgrade.</summary>
    public bool IsReady =>
        File.Exists(TrainedDataPath) &&
        new FileInfo(TrainedDataPath).Length >= MinBestModelBytes;

    /// <summary>
    /// Downloads eng.traineddata if not already present.
    /// Reports download progress (0.0–1.0) via <paramref name="progress"/>.
    /// </summary>
    public async Task EnsureReadyAsync(
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (IsReady) return;

        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(3);

        using var response = await client
            .GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        var tempPath = TrainedDataPath + ".tmp";

        try
        {
            await using var src  = await response.Content.ReadAsStreamAsync(ct);
            await using var dest = File.Create(tempPath);

            var buffer   = new byte[81_920];
            long written = 0;
            int  read;

            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                written += read;
                if (total > 0)
                    progress?.Report((double)written / total);
            }
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }

        File.Move(tempPath, TrainedDataPath, overwrite: true);
        progress?.Report(1.0);
    }
}
