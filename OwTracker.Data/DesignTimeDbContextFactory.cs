using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OwTracker.Core;

namespace OwTracker.Data;

/// <summary>
/// Used by the EF Core CLI (<c>dotnet ef migrations</c>) to construct a context at design time.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OwTrackerDbContext>
{
    public OwTrackerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OwTrackerDbContext>()
            .UseSqlite($"Data Source={AppPaths.DatabaseFile}")
            .Options;

        return new OwTrackerDbContext(options);
    }
}
