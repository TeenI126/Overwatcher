using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OwTracker.App.Navigation;
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
    private MainWindow?       _mainWindow;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;

    protected override void OnStartup(StartupEventArgs e)
    {
        // ── Global exception hooks ────────────────────────────────────────
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
            var captureRank = Array.FindIndex(e.Args, a => a.Equals("--no-rank", StringComparison.OrdinalIgnoreCase)) < 0;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            AttachConsole(ATTACH_PARENT_PROCESS);
            _ = RunHeadlessScrapeAsync(max, stopOnDuplicates: deepIdx < 0, startIndex: start, captureRank: captureRank);
            return;
        }

        // ── Normal startup ────────────────────────────────────────────────
        try
        {
            base.OnStartup(e);

            // Keep the process alive when the window is hidden to tray.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var collection = new ServiceCollection();
            ConfigureServices(collection);
            _services = collection.BuildServiceProvider();

            _services.MigrateDatabase();

            _watcher = _services.GetRequiredService<OverwatchWatcher>();
            _watcher.Start();

            var window = _services.GetRequiredService<MainWindow>();
            _mainWindow = window;

            InitTrayIcon();

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

    // ── Tray icon ─────────────────────────────────────────────────────────

    private void InitTrayIcon()
    {
        var dashboard = _services!.GetRequiredService<DashboardViewModel>();

        var menu = new System.Windows.Forms.ContextMenuStrip();

        var scrapeOne = new System.Windows.Forms.ToolStripMenuItem("Scrape Last Match");
        scrapeOne.Click += (_, _) => TrayScapeAction(dashboard, 1);

        var scrapeThree = new System.Windows.Forms.ToolStripMenuItem("Scrape Last 3 Matches");
        scrapeThree.Click += (_, _) => TrayScapeAction(dashboard, 3);

        var open = new System.Windows.Forms.ToolStripMenuItem("Open Overwatcher");
        open.Click += (_, _) => ShowWindow();
        open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);

        var exit = new System.Windows.Forms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitApp();

        menu.Items.Add(scrapeOne);
        menu.Items.Add(scrapeThree);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(open);
        menu.Items.Add(exit);

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon             = CreateTrayIcon(),
            Text             = "Overwatcher",
            Visible          = true,
            ContextMenuStrip = menu,
        };

        _trayIcon.DoubleClick += (_, _) => ShowWindow();
    }

    private void TrayScapeAction(DashboardViewModel dashboard, int maxGames)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (dashboard.IsScraping)
            {
                _trayIcon?.ShowBalloonTip(3000, "Overwatcher",
                    "A scrape is already in progress.", System.Windows.Forms.ToolTipIcon.Warning);
                return;
            }
            if (!dashboard.Watcher.IsOwRunning)
            {
                _trayIcon?.ShowBalloonTip(3000, "Overwatcher",
                    "Overwatch is not running.", System.Windows.Forms.ToolTipIcon.Warning);
                return;
            }

            await dashboard.ScrapeForTrayAsync(maxGames);

            var result = dashboard.ScrapeLog.LastOrDefault() ?? "Scrape complete.";
            _trayIcon?.ShowBalloonTip(4000, "Overwatcher", result, System.Windows.Forms.ToolTipIcon.Info);
        });
    }

    private void ShowWindow()
    {
        Dispatcher.Invoke(() =>
        {
            if (_mainWindow is null) return;
            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    private void ExitApp()
    {
        Dispatcher.Invoke(() =>
        {
            if (_mainWindow is not null)
                _mainWindow.AllowClose = true;
            _trayIcon?.Dispose();
            _trayIcon = null;
            Shutdown();
        });
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using var g   = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);

        // Orange accent background
        using var bg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xFF, 0x7A, 0x18));
        g.FillRectangle(bg, 0, 0, 32, 32);

        // "OW" label in dark ink
        using var fg   = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x1A, 0x12, 0x05));
        using var font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold);
        var sf = new System.Drawing.StringFormat
        {
            Alignment     = System.Drawing.StringAlignment.Center,
            LineAlignment = System.Drawing.StringAlignment.Center,
        };
        g.DrawString("OW", font, fg, new System.Drawing.RectangleF(0, 1, 32, 31), sf);

        // Icon.FromHandle borrows the GDI HICON; the bitmap stays alive for the app's lifetime.
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    // ── Headless scrape ───────────────────────────────────────────────────

    private async Task RunHeadlessScrapeAsync(int? maxGames, bool stopOnDuplicates = true, int startIndex = 0,
        bool captureRank = true)
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
                 $"{(stopOnDuplicates ? "stop-on-dupes" : "deep")}, from {startIndex}" +
                 $"{(captureRank ? "" : ", no-rank")}) ===");
            var result = await scraper.ScrapeAsync(
                maxGames: maxGames, stopOnDuplicates: stopOnDuplicates, startIndex: startIndex,
                captureRank: captureRank);
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
        await main.HeroMap.RefreshAsync();
        await main.Sessions.RefreshAsync();
        await main.RankHistory.RefreshAsync();
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

        services.AddSingleton<NavigationService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<MatchHistoryViewModel>();
        services.AddSingleton<HeroMapViewModel>();
        services.AddSingleton<SessionViewModel>();
        services.AddSingleton<RankHistoryViewModel>();
        services.AddSingleton<HeroReviewViewModel>();
        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton<MainWindow>();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
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
        catch { }
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
