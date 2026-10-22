using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Data.Seeding;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>
/// A site that never went through the wizard seeds on its first start-up pass exactly as it always did, records that the pass finished, and from then
/// on never refills a table the owner emptied. Seed:Mode=Authoritative stays the explicit opt-in that seeds as configured. Each test replays
/// <see cref="FirstRun.SeedAtStartupAsync"/> against a real database, deleting every content table in between.
/// </summary>
public class LegacySeedPinTests
{
    private static readonly string SeedDir = Path.Combine(AppContext.BaseDirectory, "Seed");

    /// <summary>Every content table that exists today. Project images live inside the project rows. Later content tables are added here.</summary>
    private static readonly (string Name, Func<PortfolioDbContext, Task<int>> Count, Func<PortfolioDbContext, Task<int>> Clear)[] Tables =
    [
        ("experiences", d => d.Experiences.CountAsync(), d => d.Experiences.ExecuteDeleteAsync()),
        ("skills", d => d.Skills.CountAsync(), d => d.Skills.ExecuteDeleteAsync()),
        ("projects", d => d.Projects.CountAsync(), d => d.Projects.ExecuteDeleteAsync()),
        ("certifications", d => d.Certifications.CountAsync(), d => d.Certifications.ExecuteDeleteAsync()),
        ("posts", d => d.BlogPosts.CountAsync(), d => d.BlogPosts.ExecuteDeleteAsync()),
        ("talks", d => d.Talks.CountAsync(), d => d.Talks.ExecuteDeleteAsync()),
        ("testimonials", d => d.Testimonials.CountAsync(), d => d.Testimonials.ExecuteDeleteAsync()),
    ];

    private static async Task<Dictionary<string, int>> CountsAsync(TestDatabase t)
    {
        using var db = t.CreateContext();
        var result = new Dictionary<string, int>();
        foreach (var table in Tables) result[table.Name] = await table.Count(db);
        return result;
    }

    private static async Task ClearAllAsync(TestDatabase t)
    {
        using var db = t.CreateContext();
        foreach (var table in Tables) await table.Clear(db);
    }

    public enum Site { Legacy, Published, Skipped }

    private static async Task AddProfileAsync(TestDatabase t, Site kind)
    {
        using var db = t.CreateContext();
        db.SiteProfiles.Add(new SiteProfile
        {
            // The migration marks an existing site completed and leaves the outcome (and the seeding marker) empty.
            OnboardingCompletedAt = DateTimeOffset.UtcNow,
            OnboardingOutcome = kind switch { Site.Published => "published", Site.Skipped => "skipped", _ => null },
        });
        await db.SaveChangesAsync();
    }

    private static async Task<bool> RestartAsync(TestDatabase t, bool keyConfigured = false, SeedMode mode = SeedMode.IfEmpty, string? seedDir = null)
    {
        using var db = t.CreateContext();
        return await FirstRun.SeedAtStartupAsync(db, seedDir ?? SeedDir, mode, keyConfigured);
    }

    private static async Task<DateTimeOffset?> MarkerAsync(TestDatabase t)
    {
        using var db = t.CreateContext();
        return await db.SiteProfiles.AsNoTracking().Select(p => p.ContentSeededAt).SingleAsync();
    }

    [Theory]
    [InlineData(false)] // a legacy install nobody can claim: no Setup:Key, no owner
    [InlineData(true)]
    public async Task ALegacySite_SeedsItsFirstPassAsBefore_ThenNeverRefillsAnyTableTheOwnerEmptied(bool keyConfigured)
    {
        using var t = new TestDatabase();
        await AddProfileAsync(t, Site.Legacy);

        Assert.True(await RestartAsync(t, keyConfigured));
        var seeded = await CountsAsync(t);
        Assert.All(seeded, c => Assert.True(c.Value > 0, $"{c.Key} was not seeded on the first pass"));
        Assert.NotNull(await MarkerAsync(t));

        await ClearAllAsync(t);
        Assert.All(await CountsAsync(t), c => Assert.Equal(0, c.Value));
        Assert.False(await RestartAsync(t, keyConfigured));
        Assert.False(await RestartAsync(t, keyConfigured: !keyConfigured));
        Assert.All(await CountsAsync(t), c => Assert.Equal(0, c.Value));
    }

    [Fact]
    public async Task ALegacySite_WithOneTableEmptiedByTheOwner_StaysPinnedForThatTableWhileTheOthersKeepTheirRows()
    {
        using var t = new TestDatabase();
        await AddProfileAsync(t, Site.Legacy);
        await RestartAsync(t);
        using (var db = t.CreateContext()) await db.Talks.ExecuteDeleteAsync();
        var before = await CountsAsync(t);

        await RestartAsync(t);

        Assert.Equal(0, before["talks"]);
        Assert.Equal(before, await CountsAsync(t));
    }

    [Fact]
    public async Task ALegacySite_WhoseTablesAreOnlyPartlyFilledAtTheFirstStart_GetsTheEmptyOnesSeededAsBefore_AndKeepsItsOwnRows()
    {
        using var t = new TestDatabase();
        await AddProfileAsync(t, Site.Legacy);
        using (var db = t.CreateContext())
        {
            db.Experiences.Add(new Experience { Company = "Own Co", Title = "Own Role", Start = "2020", Summary = "Mine.", SortOrder = 1 });
            await db.SaveChangesAsync();
        }

        Assert.True(await RestartAsync(t));

        var counts = await CountsAsync(t);
        Assert.Equal(1, counts["experiences"]); // IfEmpty never touches a table that has rows
        Assert.All(counts.Where(c => c.Key != "experiences"), c => Assert.True(c.Value > 0, $"{c.Key} was not seeded"));
        using (var db = t.CreateContext()) Assert.Equal("Own Co", (await db.Experiences.SingleAsync()).Company);
        Assert.NotNull(await MarkerAsync(t));
    }

    [Fact]
    public async Task ACrashPartWayThroughTheFirstPass_DoesNotPinTheSite_TheNextStartFinishesTheJob()
    {
        using var t = new TestDatabase();
        await AddProfileAsync(t, Site.Legacy);
        // Two projects with one address: the projects table refuses the batch, after skills and experiences were already written.
        var broken = Directory.CreateTempSubdirectory("lcv-seed-").FullName;
        foreach (var file in Directory.GetFiles(SeedDir)) File.Copy(file, Path.Combine(broken, Path.GetFileName(file)));
        File.WriteAllText(Path.Combine(broken, "projects.json"),
            """[{"title":"A","slug":"same","summary":"a","sortOrder":1},{"title":"B","slug":"same","summary":"b","sortOrder":2}]""");

        await Assert.ThrowsAnyAsync<Exception>(() => RestartAsync(t, seedDir: broken));

        Assert.Null(await MarkerAsync(t));
        var half = await CountsAsync(t);
        Assert.True(half["skills"] > 0);
        Assert.Equal(0, half["projects"]);

        Assert.True(await RestartAsync(t)); // the intact seed files: the remaining tables are filled as before
        Assert.All(await CountsAsync(t), c => Assert.True(c.Value > 0, $"{c.Key} was not completed"));
        Assert.NotNull(await MarkerAsync(t));
    }

    [Theory]
    [InlineData(Site.Published)]
    [InlineData(Site.Skipped)]
    public async Task AWizardFinishedSite_NeverRefillsAnyTable_WhateverTheMarkerSays(Site kind)
    {
        using var t = new TestDatabase();
        await AddProfileAsync(t, kind);
        await ClearAllAsync(t);

        Assert.False(await RestartAsync(t));
        Assert.False(await RestartAsync(t, keyConfigured: true));

        Assert.All(await CountsAsync(t), c => Assert.Equal(0, c.Value));
    }

    [Theory]
    [InlineData(Site.Legacy)]
    [InlineData(Site.Published)]
    public async Task AuthoritativeMode_StillSeedsAsConfigured_EvenOnAPinnedSite_ForEveryTable(Site kind)
    {
        using var t = new TestDatabase();
        await AddProfileAsync(t, kind);
        if (kind == Site.Legacy) await RestartAsync(t); // pins it
        await ClearAllAsync(t);
        Assert.False(await RestartAsync(t)); // IfEmpty: pinned, stays empty

        Assert.True(await RestartAsync(t, mode: SeedMode.Authoritative));

        Assert.All(await CountsAsync(t), c => Assert.True(c.Value > 0, $"{c.Key} was not seeded by Authoritative mode"));
    }

    [Fact]
    public async Task ANewInstallWaitingForTheWizard_IsNotPinned_WhenStartUpDefersToIt()
    {
        using var t = new TestDatabase();
        using (var db = t.CreateContext())
        {
            db.SiteProfiles.Add(new SiteProfile()); // a new install: no completion yet
            await db.SaveChangesAsync();
        }

        Assert.False(await RestartAsync(t, keyConfigured: true));

        Assert.Null(await MarkerAsync(t));
        Assert.All(await CountsAsync(t), c => Assert.Equal(0, c.Value));
    }

    [Fact]
    public async Task AFirstStartWithoutAProfileRow_RecordsThePassOnceTheRowExists_AndOnlyOnce()
    {
        using var t = new TestDatabase();
        Assert.True(await RestartAsync(t)); // a development checkout: no Setup:Key, and the profile row is created right after seeding
        using (var db = t.CreateContext()) await FirstRun.MarkContentSeededAsync(db); // no row: nothing to record, nothing thrown
        using (var db = t.CreateContext()) { db.SiteProfiles.Add(new SiteProfile()); await db.SaveChangesAsync(); }
        Assert.Null(await MarkerAsync(t));

        using (var db = t.CreateContext()) await FirstRun.MarkContentSeededAsync(db);
        var first = await MarkerAsync(t);
        Assert.NotNull(first);
        await Task.Delay(20);
        using (var db = t.CreateContext()) await FirstRun.MarkContentSeededAsync(db);

        Assert.Equal(first, await MarkerAsync(t));
        await ClearAllAsync(t);
        Assert.False(await RestartAsync(t));
        Assert.All(await CountsAsync(t), c => Assert.Equal(0, c.Value));
    }

    [Fact]
    public async Task AMissingSeedFolder_SeedsNothing_AndDoesNotPinTheSite()
    {
        using var t = new TestDatabase();
        await AddProfileAsync(t, Site.Legacy);

        await RestartAsync(t, seedDir: Path.Combine(Path.GetTempPath(), "lcv-no-such-seed-folder"));

        Assert.Null(await MarkerAsync(t));
    }

    [Fact]
    public void TheMigration_AddsTheMarkerAsANullableColumn_AndDoesNotBackfillIt()
    {
        using var db = new PortfolioDbContextFactory().CreateDbContext([]);

        var script = db.GetService<IMigrator>().GenerateScript();

        Assert.Contains("ADD [ContentSeededAt] datetimeoffset NULL", script);
        // A backfill would pin a site that has never completed a pass; its first start after the upgrade must still seed as before.
        Assert.DoesNotContain("SET [ContentSeededAt]", script);
        Assert.DoesNotContain("DROP", script.Substring(script.IndexOf("ADD [ContentSeededAt]", StringComparison.Ordinal)).Split("GO")[0]);
    }
}
