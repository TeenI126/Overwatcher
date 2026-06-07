using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwTracker.Data;

namespace OwTracker.Tests;

/// <summary>
/// An <see cref="IDbContextFactory{TContext}"/> backed by a single shared in-memory SQLite
/// connection, so every context created sees the same database. Dispose to tear it down.
/// </summary>
public sealed class TestDbContextFactory : IDbContextFactory<OwTrackerDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OwTrackerDbContext> _options;

    public TestDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<OwTrackerDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new OwTrackerDbContext(_options);
        db.Database.EnsureCreated();
    }

    public OwTrackerDbContext CreateDbContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
