using Microsoft.EntityFrameworkCore;
using OwTracker.Core.Models;

namespace OwTracker.Data;

public sealed class OwTrackerDbContext : DbContext
{
    public OwTrackerDbContext(DbContextOptions<OwTrackerDbContext> options) : base(options)
    {
    }

    public DbSet<MatchRecord> MatchRecords => Set<MatchRecord>();
    public DbSet<PlayerRecord> PlayerRecords => Set<PlayerRecord>();
    public DbSet<HeroPlaytime> HeroPlaytimes => Set<HeroPlaytime>();
    public DbSet<SessionRecord> SessionRecords => Set<SessionRecord>();
    public DbSet<PendingHeroLabel> PendingHeroLabels => Set<PendingHeroLabel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MatchRecord>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.MapName).IsRequired();
            // Deduplication key (design §7).
            e.HasIndex(m => new { m.MapName, m.MatchDatetime }).IsUnique();

            e.HasMany(m => m.AllPlayers)
                .WithOne(p => p.Match!)
                .HasForeignKey(p => p.MatchRecordId)
                .OnDelete(DeleteBehavior.Cascade);

            // MyStats is derived from AllPlayers (the IsMe player); not a separate FK.
            e.Ignore(m => m.MyStats);
        });

        modelBuilder.Entity<PlayerRecord>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Team).IsRequired();
            e.Property(p => p.EndingHero).IsRequired();

            e.HasMany(p => p.HeroPlaytimes)
                .WithOne(h => h.Player!)
                .HasForeignKey(h => h.PlayerRecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HeroPlaytime>(e =>
        {
            e.HasKey(h => h.Id);
            e.Property(h => h.HeroName).IsRequired();
        });

        modelBuilder.Entity<SessionRecord>(e =>
        {
            e.HasKey(s => s.Id);
        });

        modelBuilder.Entity<PendingHeroLabel>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.CropPath).IsRequired();
            e.Property(l => l.PredictedHero).IsRequired();
            e.Property(l => l.Reviewed).HasDefaultValue(false);
        });
    }
}
