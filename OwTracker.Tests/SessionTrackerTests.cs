using OwTracker.Core.Services;

namespace OwTracker.Tests;

public class SessionTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NoSession_WhenOwNeverForeground()
    {
        var tracker = new SessionTracker();

        Assert.Null(tracker.Observe(T0, owForeground: false, userActive: true));
        Assert.False(tracker.IsInSession);
    }

    [Fact]
    public void StartsSession_WhenOwBecomesForeground()
    {
        var tracker = new SessionTracker();

        var ended = tracker.Observe(T0, owForeground: true, userActive: true);

        Assert.Null(ended);
        Assert.True(tracker.IsInSession);
    }

    [Fact]
    public void AccruesTotalButNotActive_WhileIdle()
    {
        var tracker = new SessionTracker();
        tracker.Observe(T0, owForeground: true, userActive: true);                 // start
        tracker.Observe(T0.AddSeconds(10), owForeground: true, userActive: false); // 10s idle

        Assert.Equal(TimeSpan.FromSeconds(10), tracker.CurrentTotalDuration);
        Assert.Equal(TimeSpan.Zero, tracker.CurrentActiveDuration);
    }

    [Fact]
    public void AccruesActiveAndTotal_WhileActive()
    {
        var tracker = new SessionTracker();
        tracker.Observe(T0, owForeground: true, userActive: true);                // start
        tracker.Observe(T0.AddSeconds(5), owForeground: true, userActive: true);  // +5s active

        Assert.Equal(TimeSpan.FromSeconds(5), tracker.CurrentActiveDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), tracker.CurrentTotalDuration);
    }

    [Fact]
    public void EndsSession_AndProducesRecord_WhenOwLeavesForeground()
    {
        var tracker = new SessionTracker();
        tracker.Observe(T0, owForeground: true, userActive: true);                 // start
        tracker.Observe(T0.AddSeconds(20), owForeground: true, userActive: true);  // +20s active
        tracker.Observe(T0.AddSeconds(30), owForeground: true, userActive: false); // +10s idle

        var record = tracker.Observe(T0.AddSeconds(31), owForeground: false, userActive: false);

        Assert.NotNull(record);
        Assert.Equal(T0, record!.SessionStart);
        Assert.Equal(TimeSpan.FromSeconds(20), record.ActiveDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), record.TotalOpenDuration);
        Assert.False(tracker.IsInSession);
    }

    [Fact]
    public void Finalize_ClosesOpenSession()
    {
        var tracker = new SessionTracker();
        tracker.Observe(T0, owForeground: true, userActive: true);
        tracker.Observe(T0.AddSeconds(8), owForeground: true, userActive: true);

        var record = tracker.Finalize(T0.AddSeconds(8));

        Assert.NotNull(record);
        Assert.Equal(TimeSpan.FromSeconds(8), record!.ActiveDuration);
        Assert.Null(tracker.Finalize(T0.AddSeconds(8))); // already closed
    }
}
