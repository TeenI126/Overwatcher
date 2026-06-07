namespace OwTracker.Core.Models;

/// <summary>
/// A period during which Overwatch was running, with active (user not idle) vs. total open time.
/// </summary>
public class SessionRecord
{
    public int Id { get; set; }
    public DateTime SessionStart { get; set; }
    public DateTime SessionEnd { get; set; }
    public TimeSpan ActiveDuration { get; set; }
    public TimeSpan TotalOpenDuration { get; set; }
}
