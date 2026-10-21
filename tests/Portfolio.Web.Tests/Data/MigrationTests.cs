using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Portfolio.Data;
using Xunit;

namespace Portfolio.Web.Tests.Data;

/// <summary>The migrations target SQL Server, which the test run has no instance of, so these checks work offline
/// against the SQL Server provider (no connection is opened). SQLite cannot run these migrations.</summary>
public class MigrationTests
{
    private static PortfolioDbContext SqlServerContext() => new PortfolioDbContextFactory().CreateDbContext([]);

    [Fact]
    public void ModelMatchesTheLastMigrationSnapshot()
    {
        using var db = SqlServerContext();

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void MigrationChain_GeneratesTheFullScript()
    {
        using var db = SqlServerContext();

        var script = db.GetService<IMigrator>().GenerateScript();

        Assert.NotEmpty(db.Database.GetMigrations());
        Assert.Contains("CREATE TABLE [SiteProfiles]", script);
        Assert.Contains("CK_SiteProfiles_Singleton", script);
        Assert.Contains("[OwnerName] nvarchar(200)", script);
    }
}
