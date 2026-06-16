using OwTracker.Core.Models;
using OwTracker.Core.Stats;

namespace OwTracker.Tests;

public class StatsServiceTests
{
    private static MatchRecord Match(string ranking, string queue) => new()
    {
        MapName = "Ilios", MatchDatetime = DateTime.UtcNow,
        RankingMode = ranking, QueueType = queue, Outcome = MatchOutcome.Win,
    };

    // The Dashboard queue filter groups matches by GameModeLabel — these are the buckets it offers.
    [Theory]
    [InlineData("COMPETITIVE", "ROLE QUEUE", "Comp · Role Queue")]
    [InlineData("COMPETITIVE", "OPEN QUEUE", "Comp · Open Queue")]
    [InlineData("UNRANKED",    "QUICK PLAY", "Unranked")]
    [InlineData("UNRANKED",    "ROLE QUEUE", "Unranked")]
    [InlineData("ARCADE",      "",           "Quick Play")]
    [InlineData("",            "",           "Quick Play")]
    public void GameModeLabel_BucketsByRankingAndQueue(string ranking, string queue, string expected)
        => Assert.Equal(expected, StatsService.GameModeLabel(Match(ranking, queue)));

    [Fact]
    public void GameModes_FirstIsCompetitiveRoleQueue_TheDashboardDefault()
        => Assert.Equal("Comp · Role Queue", StatsService.GameModes[0]);

    [Fact]
    public void Overall_OverFilteredSet_ReflectsOnlyThoseMatches()
    {
        // Two comp-role + one quick-play; the filtered overall should see only the comp-role pair.
        var all = new[]
        {
            Match("COMPETITIVE", "ROLE QUEUE"),
            Match("COMPETITIVE", "ROLE QUEUE"),
            Match("UNRANKED",    "QUICK PLAY"),
        };
        var compRole = all.Where(m => StatsService.GameModeLabel(m) == "Comp · Role Queue").ToList();
        var overall  = StatsService.Overall(compRole);
        Assert.Equal(2, overall.Games);
        Assert.Equal(1.0, overall.WinRate);   // both wins
    }
}
