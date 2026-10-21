using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;

namespace Portfolio.Web.Tests.Support;

/// <summary>An in-memory SQLite database with the full model created, shared by every context it hands out.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public TestDatabase(bool createSchema = true)
    {
        _connection.Open();
        if (createSchema)
        {
            using var db = CreateContext();
            db.Database.EnsureCreated();
        }
    }

    public SqliteConnection Connection => _connection;

    public DbContextOptions<PortfolioDbContext> Options =>
        new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite(_connection).Options;

    public PortfolioDbContext CreateContext() => new(Options);

    public void Dispose() => _connection.Dispose();
}
