using OwTracker.Core.Models;
using OwTracker.Data.Repositories;

namespace OwTracker.Tests;

public class RepositoryTests
{
    private static MatchRecord NewMatch(string map = "Kings Row", DateTime? when = null) => new()
    {
        MapName = map,
        MatchDatetime = when ?? new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc),
        GameLength = TimeSpan.FromMinutes(12),
        MyTeamScore = 2,
        EnemyTeamScore = 1,
        Outcome = MatchOutcome.Win,
        ScrapedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Match_Upsert_InsertsNew()
    {
        using var factory = new TestDbContextFactory();
        var repo = new MatchRepository(factory);

        var saved = await repo.UpsertAsync(NewMatch());

        Assert.True(saved.Id > 0);
        Assert.Equal(1, await repo.CountAsync());
    }

    [Fact]
    public async Task Match_Upsert_DeduplicatesOnMapAndDatetime()
    {
        using var factory = new TestDbContextFactory();
        var repo = new MatchRepository(factory);
        var when = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc);

        var first = await repo.UpsertAsync(NewMatch(when: when));
        var second = await repo.UpsertAsync(NewMatch(when: when)); // same (map, datetime)

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await repo.CountAsync());
    }

    [Fact]
    public async Task Match_Upsert_NoOverwrite_LeavesExistingUntouched()
    {
        using var factory = new TestDbContextFactory();
        var repo = new MatchRepository(factory);
        var when = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc);

        await repo.UpsertAsync(NewMatch(when: when));   // Win, 2-1
        var changed = NewMatch(when: when);
        changed.Outcome = MatchOutcome.Loss; changed.MyTeamScore = 0; changed.EnemyTeamScore = 2;
        await repo.UpsertAsync(changed);                // default overwrite:false → ignored

        Assert.Equal(1, await repo.CountAsync());
        var stored = (await repo.GetAllAsync()).Single();
        Assert.Equal(MatchOutcome.Win, stored.Outcome);   // original kept
        Assert.Equal(2, stored.MyTeamScore);
    }

    [Fact]
    public async Task Match_Upsert_Overwrite_ReplacesFieldsAndPlayers()
    {
        using var factory = new TestDbContextFactory();
        var repo = new MatchRepository(factory);
        var when = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc);

        var original = NewMatch(when: when);            // Win, 2-1
        original.AllPlayers.Add(new PlayerRecord { IsMe = true, Team = "My Team", EndingHero = "Unknown" });
        await repo.UpsertAsync(original);

        var corrected = NewMatch(when: when);
        corrected.Outcome = MatchOutcome.Loss; corrected.MyTeamScore = 0; corrected.EnemyTeamScore = 2;
        corrected.AllPlayers.Add(new PlayerRecord { IsMe = true, Team = "My Team", EndingHero = "Sojourn" });
        var saved = await repo.UpsertAsync(corrected, overwrite: true);

        Assert.False(ReferenceEquals(saved, corrected));  // reported as already-present, not new
        Assert.Equal(1, await repo.CountAsync());          // still one row (replaced, not added)
        var stored = (await repo.GetByIdAsync(corrected.Id))!;
        Assert.Equal(MatchOutcome.Loss, stored.Outcome);   // overwritten
        Assert.Equal(0, stored.MyTeamScore);
        Assert.Single(stored.AllPlayers);                  // old player replaced, not duplicated
        Assert.Equal("Sojourn", stored.AllPlayers[0].EndingHero);
    }

    [Fact]
    public async Task Match_Upsert_Overwrite_LeavesNoOrphanedChildren()
    {
        using var factory = new TestDbContextFactory();
        var repo = new MatchRepository(factory);
        var when = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc);

        // Original: IsMe player with two hero playtimes + an enemy player.
        var original = NewMatch(when: when);
        var me = new PlayerRecord { IsMe = true, Team = "My Team", EndingHero = "Echo" };
        me.HeroPlaytimes.Add(new HeroPlaytime { HeroName = "Echo",    TimePlayed = TimeSpan.FromMinutes(6) });
        me.HeroPlaytimes.Add(new HeroPlaytime { HeroName = "Sojourn", TimePlayed = TimeSpan.FromMinutes(4) });
        original.AllPlayers.Add(me);
        original.AllPlayers.Add(new PlayerRecord { IsMe = false, Team = "Enemy Team", EndingHero = "Genji" });
        await repo.UpsertAsync(original);

        // Overwrite with a fresh read: one IsMe player with a single playtime.
        var corrected = NewMatch(when: when);
        var me2 = new PlayerRecord { IsMe = true, Team = "My Team", EndingHero = "Ana" };
        me2.HeroPlaytimes.Add(new HeroPlaytime { HeroName = "Ana", TimePlayed = TimeSpan.FromMinutes(10) });
        corrected.AllPlayers.Add(me2);
        await repo.UpsertAsync(corrected, overwrite: true);

        // The old players + playtimes must be GONE — not left dangling. Count the tables directly.
        await using var db = factory.CreateDbContext();
        Assert.Equal(1, db.MatchRecords.Count());
        Assert.Equal(1, db.PlayerRecords.Count());     // 2 old players removed, 1 new
        Assert.Equal(1, db.HeroPlaytimes.Count());     // 2 old playtimes removed, 1 new (no orphans)
        Assert.Equal("Ana", db.HeroPlaytimes.Single().HeroName);
    }

    [Fact]
    public async Task Match_GetById_IncludesPlayers()
    {
        using var factory = new TestDbContextFactory();
        var repo = new MatchRepository(factory);
        var match = NewMatch();
        match.AllPlayers.Add(new PlayerRecord { IsMe = true, Team = "My Team", EndingHero = "Ana" });
        var saved = await repo.UpsertAsync(match);

        var loaded = await repo.GetByIdAsync(saved.Id);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.AllPlayers);
        Assert.Equal("Ana", loaded.AllPlayers[0].EndingHero);
    }

    [Fact]
    public async Task Session_Add_And_TotalActiveTime()
    {
        using var factory = new TestDbContextFactory();
        var repo = new SessionRepository(factory);

        await repo.AddAsync(new SessionRecord
        {
            SessionStart = DateTime.UtcNow,
            SessionEnd = DateTime.UtcNow.AddMinutes(30),
            ActiveDuration = TimeSpan.FromMinutes(25),
            TotalOpenDuration = TimeSpan.FromMinutes(30)
        });
        await repo.AddAsync(new SessionRecord
        {
            SessionStart = DateTime.UtcNow,
            SessionEnd = DateTime.UtcNow.AddMinutes(10),
            ActiveDuration = TimeSpan.FromMinutes(5),
            TotalOpenDuration = TimeSpan.FromMinutes(10)
        });

        Assert.Equal(TimeSpan.FromMinutes(30), await repo.GetTotalActiveTimeAsync());
        Assert.Equal(2, (await repo.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Rank_Add_PersistsRolesAndNullableFields()
    {
        using var factory = new TestDbContextFactory();
        var repo = new RankRepository(factory);

        var snapshot = new RankSnapshot
        {
            CapturedAt = new DateTime(2026, 6, 13, 23, 14, 0, DateTimeKind.Utc),
            Roles =
            {
                new RoleRank { Role = "Tank", Division = "Diamond", Tier = 5,
                               RankProgress = -2, IsRanked = true, RawText = "DIAMOND 5" },
                new RoleRank { Role = "Support", Division = "Master", Tier = 2,
                               ChallengerScore = 2284, RankProgress = 4, IsRanked = true },
                new RoleRank { Role = "Open Queue", Division = "Gold", Tier = 1,
                               IsRanked = false, PlacementGames = 1, PlacementRequired = 10 },
            },
        };

        var saved = await repo.AddAsync(snapshot);
        Assert.True(saved.Id > 0);

        var loaded = await repo.GetLatestAsync();
        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.Roles.Count);

        var tank = loaded.Roles.Single(r => r.Role == "Tank");
        Assert.Equal("Diamond", tank.Division);
        Assert.Equal(-2, tank.RankProgress);
        Assert.Null(tank.ChallengerScore);

        var openQ = loaded.Roles.Single(r => r.Role == "Open Queue");
        Assert.False(openQ.IsRanked);
        Assert.Equal(1, openQ.PlacementGames);
        Assert.Equal(10, openQ.PlacementRequired);
        Assert.Null(openQ.RankProgress);
    }

    [Fact]
    public async Task Rank_GetLatest_ReturnsMostRecentSnapshot()
    {
        using var factory = new TestDbContextFactory();
        var repo = new RankRepository(factory);

        await repo.AddAsync(new RankSnapshot { CapturedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) });
        await repo.AddAsync(new RankSnapshot { CapturedAt = new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc) });

        var latest = await repo.GetLatestAsync();
        Assert.Equal(new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc), latest!.CapturedAt);
        Assert.Equal(2, (await repo.GetAllAsync()).Count);
    }

    [Fact]
    public async Task HeroLabel_Confirm_MarksReviewed()
    {
        using var factory = new TestDbContextFactory();
        var repo = new HeroLabelRepository(factory);
        var label = await repo.AddAsync(new PendingHeroLabel
        {
            CropPath = @"C:\crops\1.png",
            PredictedHero = "Unknown",
            Confidence = 0.0f,
            CapturedAt = DateTime.UtcNow
        });

        Assert.Equal(1, await repo.GetUnreviewedCountAsync());

        await repo.ConfirmAsync(label.Id, "Reinhardt");

        Assert.Equal(0, await repo.GetUnreviewedCountAsync());
        Assert.Empty(await repo.GetUnreviewedAsync());
    }
}
