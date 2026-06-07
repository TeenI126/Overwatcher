using CommunityToolkit.Mvvm.ComponentModel;
using OwTracker.Core.Models;
using OwTracker.Core.Repositories.Interfaces;

namespace OwTracker.Core.Services;

/// <summary>
/// Monitors whether Overwatch is running and/or in the foreground, driving session tracking.
/// Persists a <see cref="SessionRecord"/> when a session ends. Polls at 500 ms (design §6.1).
///
/// Two distinct states are tracked:
///   <see cref="IsOwRunning"/>      — OW window exists anywhere on the desktop (scrape button enabled)
///   <see cref="IsOwInForeground"/> — OW is the active window (used for session active-time accrual)
/// </summary>
public sealed partial class OverwatchWatcher : ObservableObject, IDisposable
{
    public const string OwWindowTitleMarker = "Overwatch";

    public static readonly TimeSpan PollInterval  = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan IdleThreshold = TimeSpan.FromSeconds(30);

    private readonly ISessionRepository _sessionRepository;
    private readonly SessionTracker _tracker = new();
    private readonly Func<DateTime> _clock;
    private readonly Func<(string title, long idleMs)> _sampler;
    private readonly Func<IntPtr> _hwndFinder;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    // ── Observable state ──────────────────────────────────────────────────

    /// <summary>OW window exists on the desktop (may be in background). Enables Start Scrape.</summary>
    [ObservableProperty] private bool _isOwRunning;

    /// <summary>OW is the current foreground window. Used for session active-time.</summary>
    [ObservableProperty] private bool _isOwInForeground;

    [ObservableProperty] private bool _isUserActive;
    [ObservableProperty] private bool _isInSession;
    [ObservableProperty] private TimeSpan _currentActiveDuration;
    [ObservableProperty] private TimeSpan _currentTotalDuration;

    /// <summary>
    /// Window handle of the OW window, or <see cref="IntPtr.Zero"/> when not running.
    /// The <see cref="InputSimulator"/> uses this to bring OW to foreground before scraping.
    /// </summary>
    public IntPtr OwWindowHandle { get; private set; }

    // ── Events ────────────────────────────────────────────────────────────

    public event EventHandler? SessionStarted;
    public event EventHandler<SessionRecord>? SessionEnded;

    // ── Constructors ──────────────────────────────────────────────────────

    public OverwatchWatcher(ISessionRepository sessionRepository)
        : this(sessionRepository,
               () => DateTime.UtcNow,
               () => (NativeMethods.GetForegroundWindowTitle(), NativeMethods.GetIdleMilliseconds()),
               () => NativeMethods.FindWindowByTitleMarker(OwWindowTitleMarker))
    {
    }

    /// <summary>Test seam: inject clock, foreground sampler, and window finder.</summary>
    public OverwatchWatcher(
        ISessionRepository sessionRepository,
        Func<DateTime> clock,
        Func<(string title, long idleMs)> sampler,
        Func<IntPtr> hwndFinder)
    {
        _sessionRepository = sessionRepository;
        _clock      = clock;
        _sampler    = sampler;
        _hwndFinder = hwndFinder;
    }

    // ── Pure helper methods (testable) ────────────────────────────────────

    public static bool IsOwTitle(string? title) =>
        !string.IsNullOrEmpty(title) &&
        title.Contains(OwWindowTitleMarker, StringComparison.OrdinalIgnoreCase);

    public static bool IsActive(long idleMilliseconds) =>
        idleMilliseconds < IdleThreshold.TotalMilliseconds;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public void Start()
    {
        if (_loop is not null) return;
        _cts  = new CancellationTokenSource();
        _loop = RunAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try   { if (_loop is not null) await _loop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }

        var record = _tracker.Finalize(_clock());
        if (record is not null)
            await PersistAndNotifyEndAsync(record).ConfigureAwait(false);

        _cts.Dispose();
        _cts  = null;
        _loop = null;
    }

    // ── Poll loop ─────────────────────────────────────────────────────────

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do { await TickAsync().ConfigureAwait(false); }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    private async Task TickAsync()
    {
        // Update window handle (cheap EnumWindows call each 500 ms).
        var hwnd = _hwndFinder();
        OwWindowHandle = hwnd;
        IsOwRunning    = hwnd != IntPtr.Zero;

        var (title, idleMs) = _sampler();
        var now = _clock();

        var owForeground = IsOwTitle(title);
        var userActive   = IsActive(idleMs);

        IsOwInForeground = owForeground;
        IsUserActive     = userActive;

        var wasInSession = _tracker.IsInSession;
        var ended = _tracker.Observe(now, owForeground, userActive);

        IsInSession            = _tracker.IsInSession;
        CurrentActiveDuration  = _tracker.CurrentActiveDuration;
        CurrentTotalDuration   = _tracker.CurrentTotalDuration;

        if (!wasInSession && _tracker.IsInSession)
            SessionStarted?.Invoke(this, EventArgs.Empty);

        if (ended is not null)
            await PersistAndNotifyEndAsync(ended).ConfigureAwait(false);
    }

    private async Task PersistAndNotifyEndAsync(SessionRecord record)
    {
        await _sessionRepository.AddAsync(record).ConfigureAwait(false);
        SessionEnded?.Invoke(this, record);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
