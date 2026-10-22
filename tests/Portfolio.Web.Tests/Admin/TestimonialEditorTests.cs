using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Pages.Admin.Content;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class TestimonialEditorTests
{
    private const string Edit = ContentFlow.Testimonials + "/edit";

    private static async Task<List<Testimonial>> RowsAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return await db.Testimonials.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
    }

    private static Dictionary<string, string> Fields(string quote = "She turned a chaotic rota into something the whole ward trusted.", string author = "Jordan Lee",
        string role = "Ward Manager, Northwind", string source = "https://example.org/recommendation") =>
        ContentFlow.Fields(("Quote", quote), ("Author", author), ("AuthorTitle", role), ("SourceUrl", source));

    private static int At(string html, string text) { var i = html.IndexOf(text, StringComparison.Ordinal); Assert.True(i >= 0, $"{text} is not on the page"); return i; }

    [Fact]
    public async Task CreatingATestimonial_Persists_AndShowsOnTheHomePage_AtTheEndOfTheList()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var first = await ContentFlow.PostAsync(owner, Edit, Fields(author: "First Person", quote: "First quote, long enough to count."));
        await ContentFlow.PostAsync(owner, Edit, Fields(author: "Second Person", quote: "Second quote, long enough to count."));

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        var rows = await RowsAsync(app);
        Assert.Equal(["First Person", "Second Person"], rows.Select(r => r.Author));
        Assert.Equal([1, 2], rows.Select(r => r.SortOrder));
        Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.ConcurrencyStamp)));
        Assert.Equal(("Ward Manager, Northwind", "https://example.org/recommendation"), (rows[0].AuthorTitle, rows[0].SourceUrl));
        var home = await app.NewClient().GetStringAsync("/");
        Assert.True(At(home, "First quote") < At(home, "Second quote"));
        Assert.Contains("Ward Manager, Northwind", home);
        Assert.Contains("Saved Second Person.", await owner.GetStringAsync(ContentFlow.Testimonials));
    }

    [Fact]
    public async Task TheQuote_IsPlainText_MarkupIsRefused_AndAnAmpersandIsEscapedOnThePage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var markup = await ContentFlow.PostAsync(owner, Edit, Fields(quote: "Great <script>alert(1)</script> work, truly."));
        Assert.Contains("is plain text", await markup.Content.ReadAsStringAsync());
        Assert.Empty(await RowsAsync(app));

        await ContentFlow.PostAsync(owner, Edit, Fields(quote: "Pay & conditions were \"fair\" and 5 < 6 is fine."));
        var home = await app.NewClient().GetStringAsync("/");
        Assert.Contains("Pay &amp; conditions were &quot;fair&quot; and 5 &lt; 6 is fine.", home);
        Assert.DoesNotContain("<script>alert", home);
    }

    [Theory]
    [InlineData("SourceUrl", "javascript:alert(1)")]
    [InlineData("SourceUrl", "data:text/html,x")]
    [InlineData("SourceUrl", "ftp://example.org/x")]
    [InlineData("SourceUrl", "//evil.example/x")]
    [InlineData("SourceUrl", "https://user:pw@example.org/")]
    [InlineData("Author", "<b>Jordan</b>")]
    [InlineData("Author", "Jordan\u001fLee")]
    [InlineData("AuthorTitle", "Manager <i>x</i>")]
    [InlineData("Quote", "Quote with a control\u0007 character")]
    [InlineData("Quote", "")]
    [InlineData("Author", "")]
    public async Task InvalidInput_IsRefused_AndNothingIsSaved(string field, string value)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = Fields();
        fields["Form." + field] = value;

        var response = await ContentFlow.PostAsync(owner, Edit, fields);

        Assert.Contains("Some fields need attention", await response.Content.ReadAsStringAsync());
        Assert.Empty(await RowsAsync(app));
    }

    [Theory]
    [InlineData("Quote", 2001)]
    [InlineData("Author", 121)]
    [InlineData("AuthorTitle", 201)]
    [InlineData("SourceUrl", 401)]
    public async Task OverlongInput_IsRefused_AndTheLimitItselfIsAccepted(string field, int tooLong)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        string Make(int n) => field == "SourceUrl" ? "https://example.org/" + new string('a', n - 20) : new string('a', n);
        var fields = Fields();
        fields["Form." + field] = Make(tooLong);

        Assert.Contains("at most", await (await ContentFlow.PostAsync(owner, Edit, fields)).Content.ReadAsStringAsync());
        Assert.Empty(await RowsAsync(app));
        fields["Form." + field] = Make(tooLong - 1);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, Edit, fields)).StatusCode);
        Assert.Single(await RowsAsync(app));
    }

    [Fact]
    public async Task TheSeederKey_AuthorPlusTheStartOfTheQuote_IsUnique_WithAFriendlyError()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var quote = "The same opening forty characters of a quote, then different endings.";
        await ContentFlow.PostAsync(owner, Edit, Fields(quote: quote + " One."));

        var clash = await ContentFlow.PostAsync(owner, Edit, Fields(quote: quote + " Two.", author: "JORDAN LEE"));
        var otherPerson = await ContentFlow.PostAsync(owner, Edit, Fields(quote: quote + " Two.", author: "Someone Else"));

        Assert.Contains("already have a testimonial from", await clash.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, otherPerson.StatusCode);
        Assert.Equal(2, (await RowsAsync(app)).Count);
    }

    [Fact]
    public async Task TestimonialsWithTheSamePosition_AreShownByIdOnTheHomePage()
    {
        using var app = new AdminApp();
        _ = app.Server; // starts the host, which creates the schema
        using (var db = app.CreateDbContext())
        {
            db.Testimonials.AddRange(
                new Testimonial { Quote = "Quote added first.", Author = "Person One", SortOrder = 1 },
                new Testimonial { Quote = "Quote added second.", Author = "Person Two", SortOrder = 1 },
                new Testimonial { Quote = "Quote added third.", Author = "Person Three", SortOrder = 1 });
            await db.SaveChangesAsync();
        }

        var home = await app.NewClient().GetStringAsync("/");

        Assert.True(At(home, "Quote added first.") < At(home, "Quote added second."));
        Assert.True(At(home, "Quote added second.") < At(home, "Quote added third."));
    }

    [Fact]
    public async Task Reordering_PersistsAndGoesPublic_AndUsesTheListStamp()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        foreach (var n in new[] { "Alpha", "Bravo", "Charlie" })
            await ContentFlow.PostAsync(owner, Edit, Fields(author: n + " Person", quote: n + " says something long enough."));
        var ids = (await RowsAsync(app)).Select(r => r.Id).ToList();

        await ContentFlow.MoveAsync(owner, ContentFlow.Testimonials, ids[2], "up");

        Assert.Equal(["Alpha Person", "Charlie Person", "Bravo Person"], (await RowsAsync(app)).Select(r => r.Author));
        var home = await app.NewClient().GetStringAsync("/");
        Assert.True(At(home, "Charlie says") < At(home, "Bravo says"));
        using var db = app.CreateDbContext();
        Assert.Contains(db.ListStamps, s => s.Section == "testimonials");
    }

    [Fact]
    public async Task ATestimonialCreateRacingAMove_IsRefusedWithTheListMessage_AndNoTwoRowsShareAPosition()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        foreach (var n in new[] { "Alpha", "Bravo", "Charlie" })
            await ContentFlow.PostAsync(owner, Edit, Fields(author: n + " Person", quote: n + " says something long enough."));
        var ids = (await RowsAsync(app)).Select(r => r.Id).ToList();
        var seen = await ContentFlow.ListStampAsync(owner, ContentFlow.Testimonials);

        app.BeforeOrderedSave = () => ContentFlow.MoveWithStampAsync(owner, ContentFlow.Testimonials, ids[2], "up", seen);
        var outer = await ContentFlow.PostAsync(owner, Edit, Fields(author: "Late Person", quote: "Late arrival, long enough to count."));
        Assert.Null(app.BeforeOrderedSave);

        var html = await outer.Content.ReadAsStringAsync();
        Assert.Contains(ContentPageModel.OrderRaceMessage, html);
        Assert.Contains("Late Person", html);
        var rows = await RowsAsync(app);
        Assert.Equal(["Alpha Person", "Charlie Person", "Bravo Person"], rows.Select(r => r.Author));
        Assert.Equal(rows.Count, rows.Select(r => r.SortOrder).Distinct().Count());
    }

    [Fact]
    public async Task AMoveRacingAMove_OnTheTestimonials_OnlyTheFirstApplies()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        foreach (var n in new[] { "Alpha", "Bravo", "Charlie" })
            await ContentFlow.PostAsync(owner, Edit, Fields(author: n + " Person", quote: n + " says something long enough."));
        var ids = (await RowsAsync(app)).Select(r => r.Id).ToList();
        var seen = await ContentFlow.ListStampAsync(owner, ContentFlow.Testimonials);

        app.BeforeOrderedSave = () => ContentFlow.MoveWithStampAsync(owner, ContentFlow.Testimonials, ids[2], "up", seen);
        var outer = await ContentFlow.MoveWithStampAsync(owner, ContentFlow.Testimonials, ids[0], "down", seen);

        Assert.DoesNotContain("#row-", outer.Headers.Location!.OriginalString);
        var rows = await RowsAsync(app);
        Assert.Equal(["Alpha Person", "Charlie Person", "Bravo Person"], rows.Select(r => r.Author));
        Assert.Equal([1, 2, 3], rows.Select(r => r.SortOrder));
    }

    [Fact]
    public async Task ARowStampConflict_RefusesTheEdit_AndKeepsTheNewerVersion()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var path = $"{Edit}/{seeded.TestimonialId}";
        var tabOne = await ContentFlow.StampAsync(owner, path);
        var tabTwo = await ContentFlow.StampAsync(owner, path);
        var winner = Fields(author: "Seed Person", quote: "Tab two wins and is long enough."); winner["Form.Stamp"] = tabTwo;
        await ContentFlow.PostAsync(owner, path, winner);
        var before = await ContentFlow.SnapshotAsync(app);

        var loser = Fields(author: "Seed Person", quote: "Tab one loses and is long enough."); loser["Form.Stamp"] = tabOne;
        var response = await ContentFlow.PostAsync(owner, path, loser);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(ContentPageModel.ConflictMessage, html);
        Assert.Contains("Tab one loses", html);
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task EditingKeepsThePosition_AndOnlyTheEditedRowChanges()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var path = $"{Edit}/{seeded.TestimonialId}";
        var others = (await ContentFlow.SnapshotAsync(app)).Split('\n').Where(l => !l.StartsWith($"M{seeded.TestimonialId}|")).ToList();
        var fields = Fields(author: "Seed Person", quote: "An edited quote that is long enough."); fields["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);

        await ContentFlow.PostAsync(owner, path, fields);

        Assert.Equal(others, (await ContentFlow.SnapshotAsync(app)).Split('\n').Where(l => !l.StartsWith($"M{seeded.TestimonialId}|")).ToList());
        var row = (await RowsAsync(app)).Single(r => r.Id == seeded.TestimonialId);
        Assert.Equal(("An edited quote that is long enough.", 1), (row.Quote, row.SortOrder));
    }

    [Fact]
    public async Task RemovingATestimonial_ShowsWhatGoes_AndTouchesNothingElse()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var path = $"{ContentFlow.Testimonials}/delete/{seeded.TestimonialId}";
        var others = (await ContentFlow.SnapshotAsync(app)).Split('\n').Where(l => !l.StartsWith($"M{seeded.TestimonialId}|")).ToList();

        var confirm = await owner.GetStringAsync(path);
        Assert.Contains("Seed Person", confirm);
        Assert.Contains("Seeded praise", confirm);
        Assert.Equal(2, (await RowsAsync(app)).Count);

        var remove = await ContentFlow.PostAsync(owner, path, new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, path) });

        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        Assert.Equal(others, (await ContentFlow.SnapshotAsync(app)).Split('\n').ToList());
        Assert.Contains("Removed Seed Person.", await owner.GetStringAsync(ContentFlow.Testimonials));
    }

    [Fact]
    public async Task EveryTestimonialWrite_MovesTheCacheVersion_AndARefusedOneDoesNot()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        int Version() => app.Services.GetRequiredService<ISiteProfileProvider>().Version;
        var v = Version();

        await ContentFlow.PostAsync(owner, Edit, Fields());
        Assert.True(Version() > v); v = Version();
        await ContentFlow.PostAsync(owner, Edit, Fields(author: ""));
        Assert.Equal(v, Version());
        var id = (await RowsAsync(app)).Single().Id;
        await ContentFlow.PostAsync(owner, Edit, Fields(author: "Another", quote: "Another quote that is long enough."));
        v = Version();
        await ContentFlow.MoveAsync(owner, ContentFlow.Testimonials, id, "down");
        Assert.True(Version() > v); v = Version();
        await ContentFlow.MoveWithStampAsync(owner, ContentFlow.Testimonials, id, "up", "stale");
        Assert.Equal(v, Version());
        var del = $"{ContentFlow.Testimonials}/delete/{id}";
        await ContentFlow.PostAsync(owner, del, new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, del) });
        Assert.True(Version() > v);
        Assert.DoesNotContain("Jordan Lee", await app.NewClient().GetStringAsync("/"));
    }
}
