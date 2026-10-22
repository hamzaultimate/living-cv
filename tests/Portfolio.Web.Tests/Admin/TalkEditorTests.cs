using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class TalkEditorTests
{
    private const string Edit = ContentFlow.Talks + "/edit";

    private static async Task<List<Talk>> RowsAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return await db.Talks.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
    }

    private static Dictionary<string, string> TalkFields(string title = "Scaling the ward rota", string date = "2025-05-14", string ev = "DemoConf",
        string video = "aqz-KE-bpKQ", string url = "https://example.dev/talks/rota", string type = "Talk") =>
        ContentFlow.Fields(("Title", title), ("Type", type), ("Event", ev), ("Date", date), ("YoutubeId", video), ("Url", url));

    private static int At(string html, string text) { var i = html.IndexOf(text, StringComparison.Ordinal); Assert.True(i >= 0, $"{text} is not on the page"); return i; }

    [Fact]
    public async Task CreatingATalk_Persists_AndShowsOnThePublicPage_WithTheEmbeddedVideo()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var create = await ContentFlow.PostAsync(owner, Edit, TalkFields());

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.Equal(ContentFlow.Talks, create.Headers.Location!.OriginalString);
        var row = Assert.Single(await RowsAsync(app));
        Assert.Equal(("Scaling the ward rota", TalkType.Talk, "DemoConf", new DateOnly(2025, 5, 14), "aqz-KE-bpKQ", "https://example.dev/talks/rota"),
            (row.Title, row.Type, row.Event, row.Date, row.YoutubeId, row.Url));
        Assert.False(string.IsNullOrEmpty(row.ConcurrencyStamp));
        var page = await app.NewClient().GetStringAsync("/talks");
        Assert.Contains("Scaling the ward rota", page);
        Assert.Contains("https://www.youtube-nocookie.com/embed/aqz-KE-bpKQ", page);
        Assert.Contains("May 2025", page);
        Assert.Contains("Saved Scaling the ward rota.", await owner.GetStringAsync(ContentFlow.Talks));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ&t=3", "aqz-KE-bpKQ")]
    [InlineData("https://youtu.be/aqz-KE-bpKQ?si=x", "aqz-KE-bpKQ")]
    [InlineData("https://www.youtube.com/embed/aqz-KE-bpKQ", "aqz-KE-bpKQ")]
    [InlineData("https://www.youtube.com/shorts/aqz-KE-bpKQ", "aqz-KE-bpKQ")]
    [InlineData("aqz-KE-bpKQ", "aqz-KE-bpKQ")]
    public async Task AYouTubeLinkOrId_IsStoredAsTheBareId(string input, string stored)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        await ContentFlow.PostAsync(owner, Edit, TalkFields(video: input));

        Assert.Equal(stored, Assert.Single(await RowsAsync(app)).YoutubeId);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("\"><script>alert(1)</script>")]
    [InlineData("aqz-KE-bpK")]                                  // ten characters
    [InlineData("aqz-KE-bpKQQ")]                                // twelve characters
    [InlineData("aqz KE-bpKQ")]
    [InlineData("https://evil.example/watch?v=aqz-KE-bpKQ")]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ%22onload")]
    [InlineData("https://user:pw@www.youtube.com/watch?v=aqz-KE-bpKQ")]
    [InlineData("data:text/html,aqz-KE-bpKQ")]
    public async Task ABadVideo_IsRefused_AndNothingIsSaved(string input)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var response = await ContentFlow.PostAsync(owner, Edit, TalkFields(video: input));

        Assert.Contains("must be a YouTube link or the 11 character video id", await response.Content.ReadAsStringAsync());
        Assert.Empty(await RowsAsync(app));
    }

    [Theory]
    [InlineData("Url", "javascript:alert(1)")]
    [InlineData("Url", "data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("Url", "ftp://example.dev/x")]
    [InlineData("Url", "/relative/path")]
    [InlineData("Url", "https://user:pw@example.dev/")]
    [InlineData("Title", "<b>bold</b>")]
    [InlineData("Title", "Control\u0007char")]
    [InlineData("Event", "<script>x</script>")]
    [InlineData("Date", "someday")]
    [InlineData("Date", "2025-13-45")]
    [InlineData("Date", "1800")]
    [InlineData("Type", "Podcast")]
    public async Task InvalidInput_IsRefused_WithAMessage_AndNothingIsSaved(string field, string value)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = TalkFields();
        fields["Form." + field] = value;

        var response = await ContentFlow.PostAsync(owner, Edit, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Some fields need attention", await response.Content.ReadAsStringAsync());
        Assert.Empty(await RowsAsync(app));
    }

    [Theory]
    [InlineData("Title", 201)]
    [InlineData("Event", 201)]
    [InlineData("Url", 401)]
    public async Task OverlongInput_IsRefused_AndTheLimitItselfIsAccepted(string field, int tooLong)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = TalkFields();
        string Make(int n) => field == "Url" ? "https://example.dev/" + new string('a', n - 20) : new string('a', n);
        fields["Form." + field] = Make(tooLong);

        var refused = await ContentFlow.PostAsync(owner, Edit, fields);
        Assert.Contains("at most", await refused.Content.ReadAsStringAsync());
        Assert.Empty(await RowsAsync(app));

        fields["Form." + field] = Make(tooLong - 1);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, Edit, fields)).StatusCode);
        Assert.Single(await RowsAsync(app));
    }

    [Fact]
    public async Task ADateMayBeAYearOrAMonth_AndTheTitleIsTheOnlyRequiredField()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        await ContentFlow.PostAsync(owner, Edit, ContentFlow.Fields(("Title", "Only a title")));
        await ContentFlow.PostAsync(owner, Edit, ContentFlow.Fields(("Title", "Month"), ("Date", "2024-03")));
        await ContentFlow.PostAsync(owner, Edit, ContentFlow.Fields(("Title", "Year"), ("Date", "2023")));
        var empty = await ContentFlow.PostAsync(owner, Edit, ContentFlow.Fields(("Title", "")));

        Assert.Contains("is required", await empty.Content.ReadAsStringAsync());
        var rows = await RowsAsync(app);
        Assert.Equal([null, new DateOnly(2024, 3, 1), new DateOnly(2023, 1, 1)], rows.Select(r => r.Date));
    }

    [Fact]
    public async Task TheListAndThePublicPage_ShowTalksNewestFirst_WithUndatedOnesLast_AndOfferNoManualOrder()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, TalkFields("Oldest talk", "2020-01-01", "Old Conf"));
        await ContentFlow.PostAsync(owner, Edit, ContentFlow.Fields(("Title", "Undated talk")));
        await ContentFlow.PostAsync(owner, Edit, TalkFields("Newest talk", "2025-09-01", "New Conf"));
        await ContentFlow.PostAsync(owner, Edit, TalkFields("Middle talk", "2023-06-01", "Mid Conf"));

        var list = await owner.GetStringAsync(ContentFlow.Talks);
        var pub = await app.NewClient().GetStringAsync("/talks");

        foreach (var page in new[] { list, pub })
        {
            Assert.True(At(page, ">Newest talk<") < At(page, ">Middle talk<"));
            Assert.True(At(page, ">Middle talk<") < At(page, ">Oldest talk<"));
            Assert.True(At(page, ">Oldest talk<") < At(page, ">Undated talk<"));
        }
        Assert.DoesNotContain("Move up", list);
        Assert.DoesNotContain("Move down", list);
        Assert.DoesNotContain("listStamp", list);
        Assert.DoesNotContain(">Order<", list);
    }

    [Fact]
    public async Task APostedMove_OnTheTalksList_ChangesNothing()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var before = await ContentFlow.SnapshotAsync(app);

        var move = await ContentFlow.MoveWithStampAsync(owner, ContentFlow.Talks, seeded.TalkId, "down", ContentEditor.InitialListStamp);

        Assert.Equal(HttpStatusCode.Redirect, move.StatusCode);
        Assert.Contains("ordered automatically", await owner.GetStringAsync(ContentFlow.Talks));
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task EditingATalk_ChangesOnlyThatTalk()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var others = (await ContentFlow.SnapshotAsync(app)).Split('\n').Where(l => !l.StartsWith($"T{seeded.TalkId}|")).ToList();
        var path = $"{Edit}/{seeded.TalkId}";
        var fields = TalkFields("Seed Talk, renamed", "2024-06-01");
        fields["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);

        var save = await ContentFlow.PostAsync(owner, path, fields);

        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var after = (await ContentFlow.SnapshotAsync(app)).Split('\n').ToList();
        Assert.Equal(others, after.Where(l => !l.StartsWith($"T{seeded.TalkId}|")).ToList());
        var row = (await RowsAsync(app)).Single(r => r.Id == seeded.TalkId);
        Assert.Equal(("Seed Talk, renamed", new DateOnly(2024, 6, 1), 1), (row.Title, row.Date, row.SortOrder));
    }

    [Fact]
    public async Task ARoleSavedFromAStaleForm_IsRefused_AndTheNewerVersionIsKept()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var path = $"{Edit}/{seeded.TalkId}";
        var openInTabOne = await ContentFlow.StampAsync(owner, path);
        var openInTabTwo = await ContentFlow.StampAsync(owner, path);
        var winner = TalkFields("Tab two wins"); winner["Form.Stamp"] = openInTabTwo;
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, winner)).StatusCode);
        var before = await ContentFlow.SnapshotAsync(app);

        var loser = TalkFields("Tab one loses"); loser["Form.Stamp"] = openInTabOne;
        var response = await ContentFlow.PostAsync(owner, path, loser);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(Portfolio.Web.Pages.Admin.Content.ContentPageModel.ConflictMessage, html);
        Assert.Contains("Tab one loses", html); // what was typed is shown again
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task ATalkSavedWithAMissingStamp_IsRefused()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var before = await ContentFlow.SnapshotAsync(app);

        var response = await ContentFlow.PostAsync(owner, $"{Edit}/{seeded.TalkId}", TalkFields("No stamp"));

        Assert.Contains(Portfolio.Web.Pages.Admin.Content.ContentPageModel.ConflictMessage, await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task TwoTalksCannotShareATitle_AndAnEditMayKeepItsOwn()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, TalkFields("Same title"));

        var again = await ContentFlow.PostAsync(owner, Edit, TalkFields("same TITLE"));

        Assert.Contains("already have a talk called", await again.Content.ReadAsStringAsync());
        Assert.Single(await RowsAsync(app));
        var path = $"{Edit}/{(await RowsAsync(app)).Single().Id}";
        var keep = TalkFields("Same title", "2024-01-01"); keep["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, keep)).StatusCode);
    }

    [Fact]
    public async Task RemovingATalk_NeedsTheConfirmationPost_ShowsWhatGoes_AndTouchesNothingElse()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var path = $"{ContentFlow.Talks}/delete/{seeded.TalkId}";
        var others = (await ContentFlow.SnapshotAsync(app)).Split('\n').Where(l => !l.StartsWith($"T{seeded.TalkId}|")).ToList();

        var confirm = await owner.GetStringAsync(path);
        Assert.Contains("Seed Talk", confirm);
        Assert.Contains("Seed Conf", confirm);
        Assert.Contains("This cannot be undone", confirm);
        Assert.Equal(2, (await RowsAsync(app)).Count);

        var stamp = await ContentFlow.StampAsync(owner, path);
        // A stale stamp does not remove it.
        Assert.Contains("changed or removed", await (await owner.GetAsync(ContentFlow.Talks)).Content.ReadAsStringAsync() + await FlashAfter(owner, path, "stale-stamp"));
        Assert.Equal(2, (await RowsAsync(app)).Count);

        var remove = await ContentFlow.PostAsync(owner, path, new Dictionary<string, string> { ["Stamp"] = stamp });

        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        Assert.DoesNotContain(await RowsAsync(app), r => r.Id == seeded.TalkId);
        Assert.Equal(others, (await ContentFlow.SnapshotAsync(app)).Split('\n').ToList());
        Assert.Contains("Removed Seed Talk.", await owner.GetStringAsync(ContentFlow.Talks));
    }

    private static async Task<string> FlashAfter(HttpClient owner, string path, string stamp)
    {
        await ContentFlow.PostAsync(owner, path, new Dictionary<string, string> { ["Stamp"] = stamp });
        return await owner.GetStringAsync(ContentFlow.Talks);
    }

    [Fact]
    public async Task EveryTalkWrite_MovesTheCacheVersion_AndARefusedOneDoesNot()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        int Version() => app.Services.GetRequiredService<ISiteProfileProvider>().Version;
        var v = Version();

        await ContentFlow.PostAsync(owner, Edit, TalkFields());
        Assert.True(Version() > v); v = Version();
        await ContentFlow.PostAsync(owner, Edit, TalkFields(title: "")); // invalid
        Assert.Equal(v, Version());
        var id = (await RowsAsync(app)).Single().Id;
        var fields = TalkFields("Renamed"); fields["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{Edit}/{id}");
        await ContentFlow.PostAsync(owner, $"{Edit}/{id}", fields);
        Assert.True(Version() > v); v = Version();
        await ContentFlow.PostAsync(owner, $"{Edit}/{id}", TalkFields("Stale"));
        Assert.Equal(v, Version());
        var del = $"{ContentFlow.Talks}/delete/{id}";
        await ContentFlow.PostAsync(owner, del, new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, del) });
        Assert.True(Version() > v);
        Assert.Contains("class=\"mt-10 card max-w-xl\"", await app.NewClient().GetStringAsync("/talks")); // the empty state
    }
}
