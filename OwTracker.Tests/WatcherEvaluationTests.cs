using OwTracker.Core.Services;

namespace OwTracker.Tests;

public class WatcherEvaluationTests
{
    [Theory]
    [InlineData("Overwatch", true)]
    [InlineData("OVERWATCH", true)]
    [InlineData("Overwatch 2", true)]
    [InlineData("Blizzard Overwatch", true)]
    [InlineData("Notepad", false)]
    // Whole-word match: must NOT match substrings — notably the tracker's own "Overwatcher"
    // window, which a plain Contains() falsely reported as the running game.
    [InlineData("Overwatcher", false)]
    [InlineData("Overwatcher - main", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsOwTitle_MatchesOverwatchAsWholeWord(string? title, bool expected)
        => Assert.Equal(expected, OverwatchWatcher.IsOwTitle(title));

    [Theory]
    [InlineData(0, true)]
    [InlineData(29_999, true)]
    [InlineData(30_000, false)]
    [InlineData(60_000, false)]
    public void IsActive_UsesThirtySecondThreshold(long idleMs, bool expected)
        => Assert.Equal(expected, OverwatchWatcher.IsActive(idleMs));
}
