using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OwTracker.Core;
using OwTracker.Core.Repositories.Interfaces;
using OwTracker.Data.Repositories;

namespace OwTracker.Data;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the SQLite-backed data layer: a pooled context factory pointed at
    /// <see cref="AppPaths.DatabaseFile"/> and the three repositories (safe as singletons
    /// because each operation creates its own short-lived context).
    /// </summary>
    public static IServiceCollection AddOwTrackerData(this IServiceCollection services)
    {
        services.AddDbContextFactory<OwTrackerDbContext>(options =>
            options.UseSqlite($"Data Source={AppPaths.DatabaseFile}"));

        services.AddSingleton<IMatchRepository, MatchRepository>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<IHeroLabelRepository, HeroLabelRepository>();
        services.AddSingleton<IRankRepository, RankRepository>();

        return services;
    }

    /// <summary>Applies any pending migrations, creating the database on first run.</summary>
    public static void MigrateDatabase(this IServiceProvider provider)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<OwTrackerDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.Migrate();
    }
}
