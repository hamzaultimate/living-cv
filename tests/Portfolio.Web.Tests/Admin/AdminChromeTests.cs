using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Data;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class AdminChromeTests
{
    /// <summary>A database with no tables: every count query throws, as it would while the database is struggling.</summary>
    private static PortfolioDbContext BrokenDb() =>
        new(new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite(new SqliteConnection("DataSource=:memory:")).Options);

    private sealed class UnreadableTemplate : RequestTemplate
    {
        public UnreadableTemplate() : base(null!, null!, null!) { }
        public override Task<TemplateView> GetAsync() => throw new InvalidOperationException("the profile could not be read");
    }

    [Fact]
    public async Task Counts_AreCountedPerSection_WithBlogDraftsApart()
    {
        using var app = new AdminApp();
        await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.SeedRowsAsync(app, pairs: true); // two rows in every table, one of the blog rows a draft
        using var db = app.CreateDbContext();

        var counts = await new AdminContentCounts(db, NullLogger<AdminContentCounts>.Instance).GetAsync();

        Assert.NotNull(counts);
        Assert.All(EditorFields.Sections, s => Assert.Equal(2, counts.Entries[s]));
        Assert.Equal(1, counts.BlogDrafts);
    }

    [Fact]
    public async Task Counts_AreNull_WhenTheyCannotBeRead_AndAskedOnlyOnce()
    {
        using var db = BrokenDb();
        var counts = new AdminContentCounts(db, NullLogger<AdminContentCounts>.Instance);

        var first = counts.GetAsync();
        var second = counts.GetAsync();

        Assert.Null(await first);
        Assert.Same(first, second); // one attempt per request, so a struggling database is not asked again for every block
    }

    [Fact]
    public async Task TheMenu_StillBuilds_WhenTheTemplateAndTheCountsCannotBeRead()
    {
        using var db = BrokenDb();
        var chrome = new AdminChrome(new AdminMenu(), new UnreadableTemplate(), new AdminContentCounts(db, NullLogger<AdminContentCounts>.Instance), NullLogger<AdminChrome>.Instance);

        var menu = await chrome.MenuAsync("/admin/photo", CancellationToken.None);

        Assert.Equal("profile.photo", menu.Current?.Key);
        Assert.Contains(menu.Leaves, l => l.Label == "Experience"); // neutral wording, not a failure
        Assert.All(menu.Leaves, l => Assert.Null(l.Count));
    }
}
