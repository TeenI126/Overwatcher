using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OwTracker.App.ViewModels;
using OwTracker.Core;
using OwTracker.Core.Services;
using OwTracker.Core.Services.Interfaces;
using OwTracker.Data;
using OwTracker.ML;

namespace OwTracker.App;

public partial class App : Application
{
    private ServiceProvider?  _services;
    private OverwatchWatcher? _watcher;

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;

    protected override void OnStartup(StartupEventArgs e)
    {
        // ── Global exception hooks ────────────────────────────────────────
        // Surface any exception that would otherwise silently terminate the process.
        DispatcherUnhandledException += (_, ex) =>
        {
            ShowAndLogError("Dispatcher exception", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            ShowAndLogError("Unhandled exception", ex.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            LogError("Unobserved task exception", ex.Exception);
            ex.SetObserved();
        };

        // ── Headless CLI scrape: "OwTracker.App.exe --scrape [N]" ──────────
        // Runs a scrape (optionally limited to the last N games) with no window, then exits.
        // Output goes to %APPDATA%\OwTracker\scrape.log as usual (and the parent console if any).
        // Lets the scraper be driven non-interactively for iteration/automation.
        // "--scrape [N]"      : scrape newest N (or until 3 consecutive duplicates / end of list).
        // "--scrape-deep [N]" : same but DON'T stop on duplicates — walk the whole list (back-fill
        //                       or reach old games e.g. 6v6) and save every Teams frame.
        // "--from K"          : start the row counter at game K (jump past the first K games) —
        //                       e.g. "--scrape-deep --from 44" reaches the 6v6 cluster fast.
        var deepIdx   = Array.FindIndex(e.Args, a => a.Equals("--scrape-deep", StringComparison.OrdinalIgnoreCase));
        var scrapeIdx = deepIdx >= 0 ? deepIdx
                      : Array.FindIndex(e.Args, a => a.Equals("--scrape", StringComparison.OrdinalIgnoreCase));
        if (scrapeIdx >= 0)
        {
            int? max = scrapeIdx + 1 < e.Args.Length && int.TryParse(e.Args[scrapeIdx + 1], out var n)
                ? n : null;
            var fromIdx = Array.FindIndex(e.Args, a => a.Equals("--from", StringComparison.OrdinalIgnoreCase));
            var start   = fromIdx >= 0 && fromIdx + 1 < e.Args.Length && int.TryParse(e.Args[fromIdx + 1], out var s)
                ? s : 0;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;  // stay alive until the scrape finishes
            AttachConsole(ATTACH_PARENT_PROCESS);            // best-effort: echo to the caller's console
            _ = RunHeadlessScrapeAsync(max, stopOnDuplicates: deepIdx < 0, startIndex: start);
            return;
        }

        // ── Normal startup ────────────────────────────────────────────────
        try
        {
            base.OnStartup(e);

            var collection = new ServiceCollection();
            ConfigureServices(collection);
            _services = collection.BuildServiceProvider();

            _services.MigrateDatabase();

            _watcher = _services.GetRequiredService<OverwatchWatcher>();
            _watcher.Start();

            var window = _services.GetRequiredService<MainWindow>();
            window.Show();

            var main = _services.GetRequiredService<MainViewModel>();
            _ = BootstrapAsync(main);
        }
        catch (Exception ex)
        {
            ShowAndLogError("Startup failed", ex);
            Shutdown(1);
        }
    }

    /// <summary>Runs a scrape with no UI and exits the process (exit code 0 = success).</summary>
    private async Task RunHeadlessScrapeAsync(int? maxGames, bool stopOnDuplicates = true, int startIndex = 0)
    {
        var exitCode = 0;
        try
        {
            var collection = new ServiceCollection();
            ConfigureServices(collection);
            _services = collection.BuildServiceProvider();
            _services.MigrateDatabase();

            void Echo(string s) { try { Console.WriteLine(s); } catch { } }

            var tess = _services.GetRequiredService<TessDataManager>();
            if (!tess.IsReady)
            {
                Echo("Downloading Tesseract data…");
                await tess.EnsureReadyAsync();
            }

            // Start the watcher — it polls window titles to locate OW's handle, which
            // BringOwToForeground needs. Wait briefly for it to detect the game.
            _watcher = _services.GetRequiredService<OverwatchWatcher>();
            _watcher.Start();
            for (var i = 0; i < 25 && !_watcher.IsOwRunning; i++)
                await Task.Delay(200);
            if (!_watcher.IsOwRunning)
            {
                Echo("Overwatch window not found — is the game running?");
                exitCode = 3;
                return;
            }

            var scraper = _services.GetRequiredService<HistoryScraper>();
            scraper.LogLine += Echo;
            Echo($"=== CLI scrape (limit {(maxGames?.ToString() ?? "none")}, " +
                 $"{(stopOnDuplicates ? "stop-on-dupes" : "deep")}, from {startIndex}) ===");
            var result = await scraper.ScrapeAsync(
                maxGames: maxGames, stopOnDuplicates: stopOnDuplicates, startIndex: startIndex);
            Echo(result.Success
                ? $"Done. New={result.NewRecords}, duplicates={result.SkippedDuplicates}"
                : $"Failed: {result.ErrorMessage}");
            if (!result.Success) exitCode = 2;
        }
        catch (Exception ex)
        {
            LogError("CLI scrape failed", ex);
            try { Console.WriteLine("ERROR: " + ex); } catch { }
            exitCode = 1;
        }
        finally
        {
            if (_watcher is not null) { try { await _watcher.StopAsync(); } catch { } }
            _services?.Dispose();
            Shutdown(exitCode);
        }
    }

    private async Task BootstrapAsync(MainViewModel main)
    {
        try
        {
            var tessManager = _services!.GetRequiredService<TessDataManager>();
            if (!tessManager.IsReady)
            {
                main.Dashboard.AddLog("Downloading Tesseract language data (~4 MB, first run only)…");
                var progress = new Progress<double>(p =>
                    main.Dashboard.AddLog($"  Downloading… {p:P0}"));
                await tessManager.EnsureReadyAsync(progress);
                main.Dashboard.AddLog("Tesseract data ready.");
            }
        }
        catch (Exception ex)
        {
            main.Dashboard.AddLog($"WARNING: Tesseract download failed — {ex.Message}");
            main.Dashboard.AddLog("Scraping will be unavailable until data is present.");
        }

        await main.Dashboard.RefreshAsync();
        await main.MatchHistory.RefreshAsync();
        await main.Sessions.RefreshAsync();
        await main.HeroReview.RefreshAsync();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddOwTrackerData();
        services.AddOwTrackerMl();

        services.AddSingleton<OverwatchWatcher>();
        services.AddSingleton<TessDataManager>();
        services.AddSingleton<OcrEngine>();
        services.AddSingleton<ScreenCapturer>();
        services.AddSingleton<ScreenDetector>();
        services.AddSingleton<IInputSimulator, InputSimulator>();
        services.AddSingleton<HistoryScraper>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<MatchHistoryViewModel>();
        services.AddSingleton<SessionViewModel>();
        services.AddSingleton<HeroReviewViewModel>();
        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton<MainWindow>();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_watcher is not null)
            await _watcher.StopAsync();
        _services?.Dispose();
        base.OnExit(e);
    }

    // ── Error helpers ─────────────────────────────────────────────────────

    private static void ShowAndLogError(string context, Exception? ex)
    {
        var msg = $"{context}:\n\n{ex}";
        LogError(context, ex);
        try { MessageBox.Show(msg, "OW Tracker Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        catch { /* MessageBox itself failed — nothing we can do */ }
    }

    private static void LogError(string context, Exception? ex)
    {
        try
        {
            var logPath = Path.Combine(AppPaths.Root, "error.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:u}] {context}: {ex}\n\n");
        }
        catch { }
    }
}
