using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Xunit;

namespace Portfolio.Web.Tests.Routes;

/// <summary>Boots the app with a SQLite-backed profile and checks the pages render exactly as they do from config alone.</summary>
public class DatabaseBackedProfileTests : IClassFixture<DatabaseBackedProfileTests.DatabaseFactory>,
    IClassFixture<PublicRouteSmokeTests.NoDatabaseFactory>
{
    public class DatabaseFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");

        public DatabaseFactory()
        {
            QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
            _connection.Open();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "sqlite-test" }));
            builder.ConfigureServices(services =>
            {
                // Swap the SQL Server context registration for the shared in-memory SQLite database.
                services.RemoveAll(typeof(DbContextOptions<PortfolioDbContext>));
                services.RemoveAll(typeof(DbContextOptions));
                services.RemoveAll(typeof(PortfolioDbContext));
                foreach (var d in services.Where(d => d.ServiceType.IsGenericType
                             && d.ServiceType.GetGenericArguments().Contains(typeof(PortfolioDbContext))).ToList())
                    services.Remove(d);
                services.AddDbContext<PortfolioDbContext>(o => o.UseSqlite(_connection));

                using var db = new PortfolioDbContext(new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite(_connection).Options);
                db.Database.EnsureCreated();
            });
        }

        public int ProfileRows()
        {
            using var db = new PortfolioDbContext(new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite(_connection).Options);
            return db.SiteProfiles.Count();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }
    }

    private readonly DatabaseFactory _database;
    private readonly PublicRouteSmokeTests.NoDatabaseFactory _config;

    public DatabaseBackedProfileTests(DatabaseFactory database, PublicRouteSmokeTests.NoDatabaseFactory config)
    {
        _database = database;
        _config = config;
    }

    // Anything that differs between two renders of the same page (antiforgery tokens, nonces).
    private static string Normalise(string html) =>
        Regex.Replace(html, "(__RequestVerificationToken\" type=\"hidden\" value=\"|nonce=\")[^\"]*", "$1");

    [Theory]
    [InlineData("/")]
    [InlineData("/about")]
    [InlineData("/cv")]
    public async Task Page_RendersTheSameFromTheDatabaseProfileAsFromConfig(string path)
    {
        var fromDatabase = await _database.CreateClient().GetStringAsync(path);
        var fromConfig = await _config.CreateClient().GetStringAsync(path);

        Assert.Equal(1, _database.ProfileRows());
        Assert.Equal(Normalise(fromConfig), Normalise(fromDatabase));
    }
}
