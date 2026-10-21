using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Data.Seeding;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Seeding;

public class PortfolioSeederTests
{
    private const string Skills = """
        [
          { "name": "C#", "group": "Backend", "sortOrder": 1 },
          { "name": "Azure", "group": "Cloud/DevOps", "sortOrder": 2 }
        ]
        """;

    private const string Projects = """
        [
          { "title": "Alpha", "slug": "alpha", "summary": "Seeded alpha", "sortOrder": 1 },
          { "title": "Beta", "slug": "beta", "summary": "Seeded beta", "sortOrder": 2 }
        ]
        """;

    private static SeedFiles Files(string skills = Skills, string projects = Projects) =>
        new SeedFiles().Write("skills.json", skills).Write("projects.json", projects);

    [Fact]
    public async Task IfEmpty_SeedsEmptyTables()
    {
        using var database = new TestDatabase();
        using var files = Files();
        using var db = database.CreateContext();

        await PortfolioSeeder.SeedAsync(db, files.Path);

        Assert.Equal(2, await db.Skills.CountAsync());
        Assert.Equal(new[] { "alpha", "beta" }, await db.Projects.OrderBy(p => p.SortOrder).Select(p => p.Slug).ToArrayAsync());
    }

    [Fact]
    public async Task IfEmpty_IsTheDefaultMode()
    {
        using var database = new TestDatabase();
        using var files = Files();
        using var db = database.CreateContext();
        db.Projects.Add(new Project { Title = "Mine", Slug = "mine", Summary = "Edited" });
        await db.SaveChangesAsync();

        await PortfolioSeeder.SeedAsync(db, files.Path);

        Assert.Equal(new[] { "mine" }, await db.Projects.Select(p => p.Slug).ToArrayAsync());
    }

    [Fact]
    public async Task IfEmpty_DoesNotOverwriteEditedRows()
    {
        using var database = new TestDatabase();
        using var files = Files();
        using (var db = database.CreateContext())
            await PortfolioSeeder.SeedAsync(db, files.Path, SeedMode.IfEmpty);

        using (var db = database.CreateContext())
        {
            var alpha = await db.Projects.SingleAsync(p => p.Slug == "alpha");
            alpha.Summary = "Edited in the UI";
            (await db.Skills.SingleAsync(s => s.Name == "C#")).Group = "Languages";
            await db.SaveChangesAsync();
        }

        using (var db = database.CreateContext())
            await PortfolioSeeder.SeedAsync(db, files.Path, SeedMode.IfEmpty);

        using (var db = database.CreateContext())
        {
            Assert.Equal("Edited in the UI", (await db.Projects.SingleAsync(p => p.Slug == "alpha")).Summary);
            Assert.Equal("Languages", (await db.Skills.SingleAsync(s => s.Name == "C#")).Group);
        }
    }

    [Fact]
    public async Task IfEmpty_DoesNotDeleteRowsMissingFromSeedFiles()
    {
        using var database = new TestDatabase();
        using var files = Files();
        using (var db = database.CreateContext())
        {
            db.Projects.Add(new Project { Title = "Mine", Slug = "mine", Summary = "Created in the UI" });
            db.Skills.Add(new Skill { Name = "Welding", Group = "Trade" });
            await db.SaveChangesAsync();
        }

        using (var db = database.CreateContext())
            await PortfolioSeeder.SeedAsync(db, files.Path, SeedMode.IfEmpty);

        using (var db = database.CreateContext())
        {
            Assert.Equal(new[] { "mine" }, await db.Projects.Select(p => p.Slug).ToArrayAsync());
            Assert.Equal(new[] { "Welding" }, await db.Skills.Select(s => s.Name).ToArrayAsync());
        }
    }

    [Fact]
    public async Task IfEmpty_SeedsOnlyTheTablesThatAreEmpty()
    {
        using var database = new TestDatabase();
        using var files = Files();
        using var db = database.CreateContext();
        db.Skills.Add(new Skill { Name = "Welding", Group = "Trade" });
        await db.SaveChangesAsync();

        await PortfolioSeeder.SeedAsync(db, files.Path, SeedMode.IfEmpty);

        Assert.Equal(new[] { "Welding" }, await db.Skills.Select(s => s.Name).ToArrayAsync());
        Assert.Equal(2, await db.Projects.CountAsync());
    }

    [Fact]
    public async Task IfEmpty_SeededContentSurvivesRepeatedRuns()
    {
        using var database = new TestDatabase();
        using var files = Files();
        for (var i = 0; i < 3; i++)
        {
            using var db = database.CreateContext();
            await PortfolioSeeder.SeedAsync(db, files.Path, SeedMode.IfEmpty);
        }

        using var check = database.CreateContext();
        Assert.Equal(2, await check.Skills.CountAsync());
        Assert.Equal(2, await check.Projects.CountAsync());
    }

    [Fact]
    public async Task Authoritative_AddsUpdatesAndRemovesRows()
    {
        using var database = new TestDatabase();
        using var files = Files();
        using (var db = database.CreateContext())
        {
            db.Projects.Add(new Project { Title = "Alpha", Slug = "alpha", Summary = "Stale summary" });
            db.Projects.Add(new Project { Title = "Gone", Slug = "gone", Summary = "Not in the seed file" });
            await db.SaveChangesAsync();
        }

        using (var db = database.CreateContext())
            await PortfolioSeeder.SeedAsync(db, files.Path, SeedMode.Authoritative);

        using (var db = database.CreateContext())
        {
            var projects = await db.Projects.OrderBy(p => p.SortOrder).ToListAsync();
            Assert.Equal(new[] { "alpha", "beta" }, projects.Select(p => p.Slug));
            Assert.Equal("Seeded alpha", projects[0].Summary);
            Assert.Equal(2, await db.Skills.CountAsync());
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(-1)]
    public async Task UndefinedMode_IsNonDestructive(int value)
    {
        using var database = new TestDatabase();
        using var files = Files();
        using var db = database.CreateContext();
        db.Projects.Add(new Project { Title = "Mine", Slug = "mine", Summary = "Kept" });
        await db.SaveChangesAsync();

        await PortfolioSeeder.SeedAsync(db, files.Path, (SeedMode)value);

        Assert.Equal(new[] { "mine" }, await db.Projects.Select(p => p.Slug).ToArrayAsync());
    }

    [Fact]
    public async Task Authoritative_EmptySeedFileNeverWipesATable()
    {
        using var database = new TestDatabase();
        using var files = Files(projects: "[]");
        using var db = database.CreateContext();
        db.Projects.Add(new Project { Title = "Mine", Slug = "mine", Summary = "Kept" });
        await db.SaveChangesAsync();

        await PortfolioSeeder.SeedAsync(db, files.Path, SeedMode.Authoritative);

        Assert.Equal(1, await db.Projects.CountAsync());
    }

    [Fact]
    public async Task MissingSeedDirectory_IsANoOp()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();

        await PortfolioSeeder.SeedAsync(db, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.Equal(0, await db.Projects.CountAsync());
    }

    [Fact]
    public async Task ShippedSeedFiles_LoadIntoEmptyTables()
    {
        var seedDir = Path.Combine(AppContext.BaseDirectory, "Seed");
        Assert.True(Directory.Exists(seedDir), $"Seed folder not copied to {seedDir}");
        using var database = new TestDatabase();
        using var db = database.CreateContext();

        await PortfolioSeeder.SeedAsync(db, seedDir);

        Assert.True(await db.Projects.AnyAsync());
        Assert.True(await db.Experiences.AnyAsync());
        Assert.True(await db.Skills.AnyAsync());
    }
}
