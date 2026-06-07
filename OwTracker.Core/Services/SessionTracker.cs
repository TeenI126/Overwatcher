using OwTracker.Core.Models;

namespace OwTracker.Core.Services;

/// <summary>
/// Pure session state machine, independent of Win32 and timers so it can be unit tested.
/// Fed a stream of observations (timestamp + whether OW is foreground + whether the user
/// is active); produces a <see cref="SessionRecord"/> when a session ends.
///
/// Active time accrues only while OW is foreground AND the user is active; total open time
/// accrues whenever OW is foreground. Intervals are attributed to the state seen at the
/// tick that closes them.
/// </summary>
public sealed class SessionTracker
{
    private bool _inSession;
    private DateTime _sessionStart;
    private DateTime _lastTick;
    private TimeSpan _active;
    private TimeSpan _total;

    public bool IsInSession => _inSession;
    public TimeSpan CurrentActiveDuration => _active;
    public TimeSpan CurrentTotalDuration => _total;
    public DateTime CurrentSessionStart => _sessionStart;

    /// <summary>
    /// Records one observation. Returns a finalized <see cref="SessionRecord"/> on the tick
    /// where OW stops being foreground (the session just ended); otherwise null.
    /// </summary>
    public SessionRecord? Observe(DateTime now, bool owForeground, bool userActive)
    {
        if (owForeground)
        {
            if (!_inSession)
            {
                StartSession(now);
                return null;
            }

            var delta = now - _lastTick;
            if (delta > TimeSpan.Zero)
            {
                _total += delta;
                if (userActive)
                    _active += delta;
            }
            _lastTick = now;
            return null;
        }

        // OW not foreground.
        if (_inSession)
            return EndSession(now);

        return null;
    }

    /// <summary>
    /// Closes any open session, e.g. on application shutdown. Returns the record, or null
    /// if no session was active.
    /// </summary>
    public SessionRecord? Finalize(DateTime now)
        => _inSession ? EndSession(now) : null;

    private void StartSession(DateTime now)
    {
        _inSession = true;
        _sessionStart = now;
        _lastTick = now;
        _active = TimeSpan.Zero;
        _total = TimeSpan.Zero;
    }

    private SessionRecord EndSession(DateTime now)
    {
        var record = new SessionRecord
        {
            SessionStart = _sessionStart,
            SessionEnd = now,
            ActiveDuration = _active,
            TotalOpenDuration = _total
        };
        _inSession = false;
        return record;
    }
}
