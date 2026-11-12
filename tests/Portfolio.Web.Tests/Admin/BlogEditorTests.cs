using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Pages.Admin.Content;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class BlogEditorTests
{
    private const string Edit = ContentFlow.Blog + "/edit";

    private static async Task<List<BlogPost>> RowsAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return await db.BlogPosts.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
    }

    private static Dictionary<string, string> PostFields(string title = "Zebra quartz notes", string date = "2025-09-15", string body = "A first paragraph with **bold** text.",
        bool draft = false, string? slug = null, string? description = null, string? tags = null)
    {
        var fields = ContentFlow.Fields(("Title", title), ("Date", date), ("Body", body), ("Draft", draft ? "true" : "false"));
        if (slug is not null) fields["Form.Slug"] = slug;
        if (description is not null) fields["Form.Description"] = description;
        if (tags is not null) fields["Form.Tags"] = tags;
        return fields;
    }

    private static async Task<string> TextAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync();

    private static byte[] Jpeg() => TestImages.Split(SKEncodedImageFormat.Jpeg, 160, 90);

    private static string[] UploadedFiles(AdminApp app) => Directory.GetFiles(app.UploadsPath).Select(Path.GetFileName).Select(n => n!).OrderBy(n => n).ToArray();

    /// <summary>Everything on the public site that reads posts: the list, the post page, the sitemap and the home page.</summary>
    private static async Task<(string List, HttpStatusCode PostStatus, string Post, string Sitemap, string Home)> PublicAsync(AdminApp app, string slug)
    {
        var client = app.NewClient();
        var post = await client.GetAsync($"/blog/{slug}");
        return (await client.GetStringAsync("/blog"), post.StatusCode, await post.Content.ReadAsStringAsync(), await client.GetStringAsync("/sitemap.xml"), await client.GetStringAsync("/"));
    }

    // ---------------------------------------------------------------- create, edit, remove

    [Fact]
    public async Task CreatingAPublishedPost_Persists_AndShowsOnTheListThePostPageTheSitemapAndTheJsonLd()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var sitemapBefore = await app.NewClient().GetStringAsync("/sitemap.xml"); // primes the cached sitemap
        Assert.DoesNotContain("zebra-quartz-notes", sitemapBefore);

        var create = await ContentFlow.PostAsync(owner, Edit, PostFields(description: "A short description.", tags: "Alpha, Beta, alpha"));

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.Equal(ContentFlow.Blog, create.Headers.Location!.OriginalString);
        var row = Assert.Single(await RowsAsync(app));
        Assert.Equal(("Zebra quartz notes", "zebra-quartz-notes", new DateOnly(2025, 9, 15), false, "A short description."), (row.Title, row.Slug, row.Date, row.Draft, row.Description));
        Assert.Equal(["Alpha", "Beta"], row.Tags);
        Assert.False(string.IsNullOrEmpty(row.ConcurrencyStamp));
        var pub = await PublicAsync(app, row.Slug);
        Assert.Contains("Zebra quartz notes", pub.List);
        Assert.Equal(HttpStatusCode.OK, pub.PostStatus);
        Assert.Contains("<strong>bold</strong>", pub.Post);
        Assert.Contains("\"@type\":\"BlogPosting\"", pub.Post);
        Assert.Contains("\"headline\":\"Zebra quartz notes\"", pub.Post);
        Assert.Contains("/blog/zebra-quartz-notes", pub.Sitemap);
        Assert.Contains("<lastmod>2025-09-15</lastmod>", pub.Sitemap);
        Assert.Contains("Saved Zebra quartz notes. It is shown on your site.", await owner.GetStringAsync(ContentFlow.Blog));
    }

    [Fact]
    public async Task APostWithAFutureDate_IsShownLikeAnyOther_BecauseThePublicPageShowsItAlready()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        await ContentFlow.PostAsync(owner, Edit, PostFields(date: "2099-01-01"));

        var pub = await PublicAsync(app, "zebra-quartz-notes");
        Assert.Contains("Zebra quartz notes", pub.List);
        Assert.Equal(HttpStatusCode.OK, pub.PostStatus);
    }

    [Fact]
    public async Task EditingAPost_ChangesOnlyThatPost_KeepsItsAddress_AndTheOldTextIsGoneFromThePublicPage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var others = (await ContentFlow.SnapshotAsync(app)).Split('\n').Where(l => !l.StartsWith($"B{seeded.BlogId}|")).ToList();
        var path = $"{Edit}/{seeded.BlogId}";
        var fields = PostFields("Seed Post, renamed", "2024-04-02", "A new body.");
        fields["Form.Slug"] = "seed-post";
        fields["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);

        var save = await ContentFlow.PostAsync(owner, path, fields);

        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        Assert.Equal(others, (await ContentFlow.SnapshotAsync(app)).Split('\n').Where(l => !l.StartsWith($"B{seeded.BlogId}|") && !l.StartsWith("L")).ToList());
        var pub = await PublicAsync(app, "seed-post");
        Assert.Contains("A new body.", pub.Post);
        Assert.DoesNotContain("Seed body.", pub.Post);
    }

    [Fact]
    public async Task RemovingAPost_NeedsTheConfirmationPost_ShowsWhatGoes_TouchesNothingElse_AndItsPageAnswersNotFound()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var path = $"{ContentFlow.Blog}/delete/{seeded.BlogId}";
        var others = (await ContentFlow.SnapshotAsync(app)).Split('\n').Where(l => !l.StartsWith($"B{seeded.BlogId}|")).ToList();
        Assert.Equal(HttpStatusCode.OK, (await app.NewClient().GetAsync("/blog/seed-post")).StatusCode);

        var confirm = await owner.GetStringAsync(path);
        Assert.Contains("Seed Post", confirm);
        Assert.Contains("/blog/seed-post", confirm);
        Assert.Contains("This cannot be undone", confirm);
        Assert.Equal(2, (await RowsAsync(app)).Count);

        var remove = await ContentFlow.PostAsync(owner, path, new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, path) });

        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        Assert.Equal(others, (await ContentFlow.SnapshotAsync(app)).Split('\n').ToList());
        Assert.Contains("Removed Seed Post.", await owner.GetStringAsync(ContentFlow.Blog));
        var pub = await PublicAsync(app, "seed-post");
        Assert.Equal(HttpStatusCode.NotFound, pub.PostStatus);
        Assert.DoesNotContain("seed-post", pub.Sitemap);
        Assert.DoesNotContain("Seed Post", pub.List);
    }

    [Fact]
    public async Task ARemovalWithAStaleStamp_ChangesNothing()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var before = await ContentFlow.SnapshotAsync(app);

        await ContentFlow.PostAsync(owner, $"{ContentFlow.Blog}/delete/{seeded.BlogId}", new Dictionary<string, string> { ["Stamp"] = "stale" });

        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
        Assert.Contains("changed or removed", await owner.GetStringAsync(ContentFlow.Blog));
    }

    // ---------------------------------------------------------------- drafts

    [Fact]
    public async Task ADraft_IsOnNoPublicSurface_AndIsInTheAdminListAsText_WithTheCounts()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, PostFields("Published quartz", draft: false, slug: "published-quartz"));
        await ContentFlow.PostAsync(owner, Edit, PostFields("Hidden jasper", draft: true, slug: "hidden-jasper", description: "Hidden description jasper.", body: "Hidden body jasper."));

        var pub = await PublicAsync(app, "hidden-jasper");

        Assert.Equal(HttpStatusCode.NotFound, pub.PostStatus);
        foreach (var surface in new[] { pub.List, pub.Sitemap, pub.Home })
        {
            Assert.DoesNotContain("hidden-jasper", surface);
            Assert.DoesNotContain("jasper", surface);
        }
        // The not-found page for the address may repeat the address that was asked for, but nothing of the draft itself.
        Assert.DoesNotContain("Hidden jasper", pub.Post);
        Assert.DoesNotContain("Hidden description", pub.Post);
        Assert.DoesNotContain("Hidden body", pub.Post);
        Assert.DoesNotContain("BlogPosting", pub.Post);
        Assert.Contains("Published quartz", pub.List);
        var list = WebUtility.HtmlDecode(await owner.GetStringAsync(ContentFlow.Blog));
        Assert.Matches(@"Draft · 2025-09-15", list);
        Assert.Matches(@"Published · 2025-09-15", list);
        Assert.Contains("1 published and 1 draft", list);
        Assert.Contains("(1 published, 1 draft)", await owner.GetStringAsync("/admin/content"));
        Assert.Contains("Saved Hidden jasper as a draft. It is not shown on your site.", list);
        // Every other public route that could carry a post says nothing about it either.
        foreach (var path in new[] { "/cv", "/talks", "/experience", "/projects", "/about", "/contact", "/og.png".Replace("/og.png", "/"), "/robots.txt" })
        {
            var response = await app.NewClient().GetAsync(path);
            if (response.StatusCode == HttpStatusCode.OK) Assert.DoesNotContain("jasper", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task PublishingAndUnpublishing_FollowOnEverySurfaceAtOnce_IncludingTheCachedSitemap()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, PostFields("Moving target", draft: true, slug: "moving-target"));
        var id = (await RowsAsync(app)).Single().Id;
        var path = $"{Edit}/{id}";
        var hidden = await PublicAsync(app, "moving-target");
        Assert.Equal(HttpStatusCode.NotFound, hidden.PostStatus);
        Assert.DoesNotContain("moving-target", hidden.Sitemap);

        var publish = PostFields("Moving target", draft: false, slug: "moving-target"); publish["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, publish)).StatusCode);
        var shown = await PublicAsync(app, "moving-target");
        Assert.Equal(HttpStatusCode.OK, shown.PostStatus);
        Assert.Contains("Moving target", shown.List);
        Assert.Contains("/blog/moving-target", shown.Sitemap);

        var unpublish = PostFields("Moving target", draft: true, slug: "moving-target"); unpublish["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, unpublish)).StatusCode);
        var gone = await PublicAsync(app, "moving-target");
        Assert.Equal(HttpStatusCode.NotFound, gone.PostStatus);
        Assert.DoesNotContain("Moving target", gone.List);
        Assert.DoesNotContain("moving-target", gone.Sitemap);
        Assert.Single(await RowsAsync(app)); // unpublishing keeps the post
    }

    [Fact]
    public async Task APostSavedWithoutTheDraftField_IsADraft_NotPublishedByAccident()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = PostFields();
        fields.Remove("Form.Draft");

        await ContentFlow.PostAsync(owner, Edit, fields);

        Assert.True((await RowsAsync(app)).Single().Draft, "a post saved without the draft field must not be published");
    }

    // ---------------------------------------------------------------- slugs

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("%2e%2e")]
    [InlineData("a\\b")]
    [InlineData("a/b")]
    [InlineData("has space")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--hyphen")]
    [InlineData("Ünïcode")]
    [InlineData("ａｄｍｉｎ")]            // full-width lookalikes
    [InlineData("аdmin")]                 // a Cyrillic first letter
    [InlineData("new")]
    [InlineData("edit")]
    [InlineData("feed")]
    [InlineData("sitemap")]
    [InlineData("a?b")]
    [InlineData("a#b")]
    public async Task ABadAddressName_IsRefused_AndNothingIsSaved(string slug)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var response = await ContentFlow.PostAsync(owner, Edit, PostFields(slug: slug));

        Assert.Contains("Some fields need attention", await TextAsync(response));
        Assert.Empty(await RowsAsync(app));
    }

    [Fact]
    public async Task AnAddressName_ThatIsTooLong_IsRefused_AndTheLimitIsAccepted()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        Assert.Contains("at most 120", await TextAsync(await ContentFlow.PostAsync(owner, Edit, PostFields(slug: new string('a', 121)))));
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, Edit, PostFields(slug: new string('a', 120)))).StatusCode);
    }

    [Fact]
    public async Task TheAddressName_IsBuiltFromTheTitle_FoldsAccents_AndIsMadeUnique()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        await ContentFlow.PostAsync(owner, Edit, PostFields("Café Münster: a guide!"));
        await ContentFlow.PostAsync(owner, Edit, PostFields("Café Münster: a guide!"));
        await ContentFlow.PostAsync(owner, Edit, PostFields("Sitemap"));
        await ContentFlow.PostAsync(owner, Edit, PostFields("日本語"));

        Assert.Equal(["cafe-munster-a-guide", "cafe-munster-a-guide-2", "sitemap-1", "post"], (await RowsAsync(app)).Select(r => r.Slug));
    }

    [Fact]
    public async Task TwoPostsCannotShareAnAddress_CaseInsensitively_AndAnEditMayKeepItsOwn()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, PostFields("First", slug: "taken-name"));

        var clash = await ContentFlow.PostAsync(owner, Edit, PostFields("Second", slug: "TAKEN-name"));

        Assert.Contains("is already used by another post", WebUtility.HtmlDecode(await TextAsync(clash)));
        var id = (await RowsAsync(app)).Single().Id;
        var keep = PostFields("First, edited", slug: "taken-name"); keep["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{Edit}/{id}");
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, $"{Edit}/{id}", keep)).StatusCode);
    }

    [Fact]
    public async Task ATakenAddress_IsReportedTogetherWithTheOtherProblems_BeforeAnythingIsWritten()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, PostFields("First", slug: "taken-name"));

        // Two things are wrong at once: the form is checked as a whole, so both are named (the database's unique index is only the backstop).
        var response = await ContentFlow.PostAsync(owner, Edit, PostFields("Second", slug: "taken-name", body: ""));

        var html = WebUtility.HtmlDecode(await TextAsync(response));
        Assert.Contains("is already used by another post", html);
        Assert.Contains("is required.", html);
        Assert.Single(await RowsAsync(app));
    }

    [Fact]
    public async Task ALegacyStoredAddress_IsNeverRevalidated_AnEmptiedBoxKeepsIt_AndOnlyAChangeIsChecked()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
        {
            db.BlogPosts.Add(new BlogPost { Title = "Legacy", Slug = "Legacy_Name.v1", Date = new DateOnly(2024, 1, 1), BodyMarkdown = "Old body.", ConcurrencyStamp = "s1" });
            await db.SaveChangesAsync();
        }
        var id = (await RowsAsync(app)).Single().Id;
        var path = $"{Edit}/{id}";

        var same = PostFields("Legacy, edited", slug: "Legacy_Name.v1"); same["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, same)).StatusCode);
        Assert.Equal("Legacy_Name.v1", (await RowsAsync(app)).Single().Slug);

        var emptied = PostFields("Legacy, again", slug: ""); emptied["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, emptied)).StatusCode);
        Assert.Equal("Legacy_Name.v1", (await RowsAsync(app)).Single().Slug);

        var changed = PostFields("Legacy, bad change", slug: "Another_Bad.v2"); changed["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        Assert.Contains("Some fields need attention", await TextAsync(await ContentFlow.PostAsync(owner, path, changed)));
        Assert.Equal("Legacy_Name.v1", (await RowsAsync(app)).Single().Slug);
    }

    [Fact]
    public async Task ARenamedAddress_AnswersTheOrdinaryNotFound_AtTheOldOne()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, PostFields(slug: "old-name"));
        var id = (await RowsAsync(app)).Single().Id;
        Assert.Equal(HttpStatusCode.OK, (await app.NewClient().GetAsync("/blog/old-name")).StatusCode);

        var fields = PostFields(slug: "new-name"); fields["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{Edit}/{id}");
        await ContentFlow.PostAsync(owner, $"{Edit}/{id}", fields);

        var old = await app.NewClient().GetAsync("/blog/old-name");
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
        Assert.Null(old.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, (await app.NewClient().GetAsync("/blog/new-name")).StatusCode);
    }

    [Fact]
    public async Task TwoSavesTakingTheSameAddressAtTheSameMoment_TheSecondGetsTheFriendlyMessage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        app.BeforeOrderedSave = () => ContentFlow.PostAsync(owner, Edit, PostFields("Early", slug: "race-name"));
        var outer = await ContentFlow.PostAsync(owner, Edit, PostFields("Late", slug: "race-name"));
        Assert.Null(app.BeforeOrderedSave);

        Assert.Contains("The web address name \"race-name\" is already used by another post", WebUtility.HtmlDecode(await TextAsync(outer)));
        Assert.Equal(["Early"], (await RowsAsync(app)).Select(r => r.Title));
    }

    // ---------------------------------------------------------------- validation

    [Theory]
    [InlineData("Title", "<b>bold</b>")]
    [InlineData("Title", "")]
    [InlineData("Title", "Control\u0007char")]
    [InlineData("Description", "<script>alert(1)</script>")]
    [InlineData("Description", "Control\u0007char")]
    [InlineData("Date", "")]
    [InlineData("Date", "someday")]
    [InlineData("Date", "2025-02-30")]
    [InlineData("Date", "15/09/2025")]
    [InlineData("Date", "1980-01-01")]
    [InlineData("Date", "2101-01-01")]
    [InlineData("Tags", "ok, <b>bad</b>")]
    [InlineData("Tags", "a;b")]
    [InlineData("Tags", "\"quoted\"")]
    [InlineData("Tags", "one,two,three,four,five,six,seven,eight,nine")]
    [InlineData("Body", "")]
    [InlineData("Body", "   ")]
    [InlineData("Body", "Control\u0007char")]
    public async Task InvalidInput_IsRefused_AndNothingIsSaved(string field, string value)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = PostFields();
        fields["Form." + field] = value;

        var response = await ContentFlow.PostAsync(owner, Edit, fields);

        Assert.Contains("Some fields need attention", await TextAsync(response));
        Assert.Empty(await RowsAsync(app));
    }

    [Theory]
    [InlineData("Title", 201)]
    [InlineData("Description", 301)]
    [InlineData("Body", 60_001)]
    public async Task OverlongInput_IsRefused_AndTheLimitItselfIsAccepted(string field, int tooLong)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = PostFields();
        fields["Form." + field] = new string('a', tooLong);

        Assert.Contains("at most", await TextAsync(await ContentFlow.PostAsync(owner, Edit, fields)));
        Assert.Empty(await RowsAsync(app));
        fields["Form." + field] = new string('a', tooLong - 1);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, Edit, fields)).StatusCode);
    }

    [Fact]
    public async Task Tags_AreSplitTrimmedDeduplicated_AndMarkupIsNeverStored()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        await ContentFlow.PostAsync(owner, Edit, PostFields(tags: " .NET ,C#, c# ,\n Web & API , R_D-2 "));

        Assert.Equal([".NET", "C#", "Web & API", "R_D-2"], (await RowsAsync(app)).Single().Tags);
        var post = await app.NewClient().GetStringAsync("/blog/zebra-quartz-notes");
        Assert.Contains("#Web &amp; API", post);
    }

    [Fact]
    public async Task TheJsonLd_IsEncodedSoNoTextCanCloseTheScript()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        await ContentFlow.PostAsync(owner, Edit, PostFields(description: "5 < 6 & 7 > 3 and a \"quote\" and a backslash \\"));

        var post = await app.NewClient().GetStringAsync("/blog/zebra-quartz-notes");
        var ld = Regex.Matches(post, "<script[^>]*application/ld(?:\\+|&#x2B;)json[^>]*>(.*?)</script>", RegexOptions.Singleline).Select(m => m.Groups[1].Value).Where(j => j.Contains("BlogPosting")).ToList();
        var article = System.Text.Json.JsonDocument.Parse(Assert.Single(ld)).RootElement;
        Assert.Equal("5 < 6 & 7 > 3 and a \"quote\" and a backslash \\", article.GetProperty("description").GetString());
        Assert.DoesNotContain("<", ld[0]);
        var beforeScripts = Regex.Matches(post, "<script").Count;
        Assert.Equal(beforeScripts, Regex.Matches(await app.NewClient().GetStringAsync("/blog"), "<script").Count + 1); // the post page has exactly one script more than the list: its article JSON-LD
    }

    // ---------------------------------------------------------------- stamps

    [Fact]
    public async Task APostSavedFromAStaleForm_OrWithoutAStamp_IsRefused_AndTheNewerVersionIsKept()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var path = $"{Edit}/{seeded.BlogId}";
        var tabOne = await ContentFlow.StampAsync(owner, path);
        var tabTwo = await ContentFlow.StampAsync(owner, path);
        var winner = PostFields("Tab two wins", slug: "seed-post"); winner["Form.Stamp"] = tabTwo;
        await ContentFlow.PostAsync(owner, path, winner);
        var before = await ContentFlow.SnapshotAsync(app);

        var loser = PostFields("Tab one loses", slug: "seed-post"); loser["Form.Stamp"] = tabOne;
        var stale = await TextAsync(await ContentFlow.PostAsync(owner, path, loser));
        var none = await TextAsync(await ContentFlow.PostAsync(owner, path, PostFields("No stamp", slug: "seed-post")));

        Assert.Contains(ContentPageModel.ConflictMessage, stale);
        Assert.Contains("Tab one loses", stale); // what was typed is shown again
        Assert.Contains(ContentPageModel.ConflictMessage, none);
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task EveryPostWrite_MovesTheCacheVersion_AndARefusedOneDoesNot()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        int Version() => app.Services.GetRequiredService<ISiteProfileProvider>().Version;
        var v = Version();

        await ContentFlow.PostAsync(owner, Edit, PostFields());
        Assert.True(Version() > v); v = Version();
        await ContentFlow.PostAsync(owner, Edit, PostFields(title: ""));
        Assert.Equal(v, Version());
        var id = (await RowsAsync(app)).Single().Id;
        await ContentFlow.PostAsync(owner, $"{Edit}/{id}", PostFields("Stale")); // no stamp
        Assert.Equal(v, Version());
        await ContentFlow.PostAsync(owner, $"{Edit}?handler=Preview", ContentFlow.Fields(("Body", "# Preview")), Edit);
        Assert.Equal(v, Version());
        var del = $"{ContentFlow.Blog}/delete/{id}";
        await ContentFlow.PostAsync(owner, del, new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, del) });
        Assert.True(Version() > v);
    }

    // ---------------------------------------------------------------- cover image

    private static Task<HttpResponseMessage> SaveWithCoverAsync(HttpClient owner, string path, Dictionary<string, string> fields, byte[] bytes, string fileName = "cover.jpg", string contentType = "image/jpeg") =>
        ContentFlow.PostMultipartAsync(owner, path, fields, ("Form.CoverFile", bytes, fileName, contentType), path.Split('?')[0]);

    [Fact]
    public async Task ACoverUpload_IsReEncoded_StoredUnderARandomName_NeedsAltText_AndTheAltIsUsedOnThePage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var noAlt = await SaveWithCoverAsync(owner, Edit, PostFields(), Jpeg());
        Assert.Contains("Describe the cover image", await TextAsync(noAlt));
        Assert.Empty(UploadedFiles(app));
        Assert.Empty(await RowsAsync(app));

        var fields = PostFields(); fields["Form.CoverAlt"] = "A red and blue test card";
        var saved = await SaveWithCoverAsync(owner, Edit, fields, Jpeg());

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var row = Assert.Single(await RowsAsync(app));
        Assert.Matches(@"^/uploads/[0-9a-f]{32}\.(jpg|png|webp)$", row.Cover);
        Assert.Equal("A red and blue test card", row.CoverAlt);
        Assert.Single(UploadedFiles(app));
        var post = await app.NewClient().GetStringAsync("/blog/zebra-quartz-notes");
        Assert.Contains($"src=\"{row.Cover}\" alt=\"A red and blue test card\"", post);
        Assert.Equal(HttpStatusCode.OK, (await app.NewClient().GetAsync(row.Cover!)).StatusCode);
    }

    [Theory]
    [InlineData("not an image at all", "cover.jpg", "image/jpeg")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' onload='alert(1)'/>", "cover.svg", "image/svg+xml")]
    public async Task ACoverThatIsNotARasterImage_IsRefused_AndNothingIsStored(string content, string name, string type)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = PostFields(); fields["Form.CoverAlt"] = "x";

        var response = await SaveWithCoverAsync(owner, Edit, fields, System.Text.Encoding.UTF8.GetBytes(content), name, type);

        Assert.Contains("Some fields need attention", await TextAsync(response));
        Assert.Empty(UploadedFiles(app));
        Assert.Empty(await RowsAsync(app));
    }

    [Fact]
    public async Task ReplacingARemovingAndDeleting_DeleteTheOldFile_ButOnlyWhenNothingElseUsesIt()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = PostFields(slug: "with-cover"); fields["Form.CoverAlt"] = "first";
        await SaveWithCoverAsync(owner, Edit, fields, Jpeg());
        var id = (await RowsAsync(app)).Single().Id;
        var first = (await RowsAsync(app)).Single().Cover!;

        // Replaced: the old file goes, the new one stays.
        var replace = PostFields(slug: "with-cover"); replace["Form.CoverAlt"] = "second"; replace["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{Edit}/{id}");
        await SaveWithCoverAsync(owner, $"{Edit}/{id}", replace, TestImages.Split(SKEncodedImageFormat.Png, 120, 80), "c.png", "image/png");
        var second = (await RowsAsync(app)).Single().Cover!;
        Assert.NotEqual(first, second);
        Assert.Equal([Path.GetFileName(second)], UploadedFiles(app));

        // Another post uses the same file: removing it from this one keeps the file.
        using (var db = app.CreateDbContext())
        {
            db.BlogPosts.Add(new BlogPost { Title = "Sharer", Slug = "sharer", Date = new DateOnly(2024, 1, 1), Cover = second, BodyMarkdown = "x", ConcurrencyStamp = "s" });
            await db.SaveChangesAsync();
        }
        var remove = PostFields(slug: "with-cover"); remove["Form.RemoveCover"] = "true"; remove["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{Edit}/{id}");
        await ContentFlow.PostAsync(owner, $"{Edit}/{id}", remove);
        Assert.Null((await RowsAsync(app)).Single(r => r.Id == id).Cover);
        Assert.Equal([Path.GetFileName(second)], UploadedFiles(app));

        // Deleting the sharer: nothing else uses it now, so it goes.
        var sharerId = (await RowsAsync(app)).Single(r => r.Slug == "sharer").Id;
        var del = $"{ContentFlow.Blog}/delete/{sharerId}";
        await ContentFlow.PostAsync(owner, del, new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, del) });
        Assert.Empty(UploadedFiles(app));
    }

    [Fact]
    public async Task AnUploadThatAnotherPostUsesInItsText_OrAsAProjectImage_IsKeptWhenTheCoverGoes()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var name = new string('a', 32) + ".jpg";
        await File.WriteAllBytesAsync(Path.Combine(app.UploadsPath, name), Jpeg());
        var url = "/uploads/" + name;
        using (var db = app.CreateDbContext())
        {
            db.BlogPosts.AddRange(
                new BlogPost { Title = "Has cover", Slug = "has-cover", Date = new DateOnly(2024, 1, 1), Cover = url, CoverAlt = "c", BodyMarkdown = "x", ConcurrencyStamp = "s1" },
                new BlogPost { Title = "Uses it in text", Slug = "uses-it", Date = new DateOnly(2024, 1, 2), BodyMarkdown = $"Look: ![pic]({url})", ConcurrencyStamp = "s2" });
            await db.SaveChangesAsync();
        }
        var id = (await RowsAsync(app)).Single(r => r.Slug == "has-cover").Id;

        var del = $"{ContentFlow.Blog}/delete/{id}";
        await ContentFlow.PostAsync(owner, del, new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, del) });

        Assert.Equal([name], UploadedFiles(app)); // the other post still shows it
    }

    [Fact]
    public async Task ASeededCoverThatIsNotAnUpload_NeedsNoAlt_IsNeverDeleted_AndKeepsTheTitleAsAltOnThePage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
        {
            db.BlogPosts.Add(new BlogPost { Title = "Old cover", Slug = "old-cover", Date = new DateOnly(2024, 1, 1), Cover = "/img/blog/old.png", BodyMarkdown = "x", ConcurrencyStamp = "s" });
            await db.SaveChangesAsync();
        }
        var id = (await RowsAsync(app)).Single().Id;
        Assert.Contains("alt=\"Old cover\"", await app.NewClient().GetStringAsync("/blog/old-cover")); // as before this editor existed

        var fields = PostFields("Old cover", slug: "old-cover"); fields["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{Edit}/{id}");
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, $"{Edit}/{id}", fields)).StatusCode);

        Assert.Equal("/img/blog/old.png", (await RowsAsync(app)).Single().Cover);
    }

    // ---------------------------------------------------------------- preview and safe rendering

    [Fact]
    public async Task ThePreview_RendersTheTypedText_WithTheSameFunction_WritesNothing_AndIsNotCachedOrIndexed()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var before = await ContentFlow.SnapshotAsync(app);
        var body = "## Typed heading\n\nText with **bold** and a [link](https://example.org) and <b>raw</b>.";

        var response = await ContentFlow.PostAsync(owner, $"{Edit}/{seeded.BlogId}?handler=Preview", ContentFlow.Fields(("Body", body), ("Title", "Typed title")), $"{Edit}/{seeded.BlogId}");
        var html = await TextAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(SafeMd.ToHtml(body), html.Replace("\r\n", "\n"));
        Assert.Contains("&lt;b&gt;raw&lt;/b&gt;", html);
        Assert.Contains("Nothing has been saved", html);
        Assert.Contains("Typed title", html); // the form keeps what was typed
        Assert.Contains("no-store", string.Join(",", response.Headers.GetValues("Cache-Control")));
        Assert.Contains("noindex", string.Join(",", response.Headers.GetValues("X-Robots-Tag")));
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
        Assert.Empty(Regex.Matches(html, "<script(?![^>]*\\ssrc=)"));
    }

    private static readonly Regex PreviewButton = new("<button[^>]*\\sformaction=\"([^\"]*)\"[^>]*>Preview</button>", RegexOptions.Compiled);

    [Fact]
    public async Task ThePreviewButton_PostsToThePreviewHandler_OnTheNewFormAndTheEditForm_AndNeverSaves()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        int Version() => app.Services.GetRequiredService<ISiteProfileProvider>().Version;

        foreach (var formPath in new[] { Edit, $"{Edit}/{seeded.BlogId}" })
        {
            var html = await owner.GetStringAsync(formPath);
            var match = PreviewButton.Match(html);
            Assert.True(match.Success, $"{formPath}: the Preview button has no formaction");
            var action = WebUtility.HtmlDecode(match.Groups[1].Value);
            Assert.Contains("handler=Preview", action); // not empty: an empty address posts to Save
            var before = await ContentFlow.SnapshotAsync(app);
            var version = Version();

            var fields = PostFields("Previewed only", body: "## Previewed\n\nText.", slug: "previewed-only");
            if (formPath != Edit) fields["Form.Stamp"] = await ContentFlow.StampAsync(owner, formPath);
            var response = await ContentFlow.PostAsync(owner, action, fields, formPath);

            var page = await TextAsync(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); // the page again, not a redirect after a save
            Assert.Contains("Nothing has been saved", page);
            Assert.Contains("<h2 id=\"previewed\">Previewed</h2>", page);
            Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
            Assert.Equal(version, Version());
        }
    }

    [Fact]
    public async Task ThePreview_IsOwnerOnly_NeedsTheAntiforgeryToken_AndRefusesAnOversizedBody()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = ContentFlow.Fields(("Body", "# Preview"));

        var anonymous = await app.NewClient().PostAsync($"{Edit}?handler=Preview", new FormUrlEncodedContent(fields));
        var noToken = await owner.PostAsync($"{Edit}?handler=Preview", new FormUrlEncodedContent(fields));
        var big = await ContentFlow.PostMultipartAsync(owner, $"{Edit}?handler=Preview", new Dictionary<string, string> { ["Form.Body"] = new string('x', 12 * 1024 * 1024) }, null, Edit);

        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/admin/login", anonymous.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, big.StatusCode);
        var get = await owner.GetAsync($"{Edit}?handler=Preview");
        Assert.DoesNotContain("Nothing has been saved", await TextAsync(get)); // a GET never previews
    }

    public static IEnumerable<object[]> Payloads() => new[]
    {
        "<script>alert(1)</script>", "<img src=x onerror=alert(1)>", "<svg onload=alert(1)>", "<iframe src=\"https://evil.example\"></iframe>",
        "<style>*{display:none}</style>", "[x](javascript:alert(1))", "[x](JaVaScRiPt:alert(1))", "[x](&#106;avascript:alert(1))", "[x](java\tscript:alert(1))",
        "[x](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)", "![x](javascript:alert(1))", "![x](data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSk+)",
        "[x][r]\n\n[r]: javascript:alert(1)", "<javascript:alert(1)>", "| a |\n|---|\n| <img src=x onerror=alert(1)> |", "text[^1]\n\n[^1]: <script>alert(1)</script>",
        "[x]{onclick=alert(1)}", "# Title {onclick=alert(1)}", "<!-- <script>alert(1)</script> -->", "<<script>script>alert(1)</script>", "```\"><script>alert(1)</script>\nx\n```",
    }.Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(Payloads))]
    public async Task TypedMarkdown_CannotInjectScriptOrUnsafeAddresses_NeitherInThePreviewNorOnThePublicPage(string payload)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, PostFields(body: "Intro paragraph.\n\n" + payload, slug: "attack"));

        var preview = await TextAsync(await ContentFlow.PostAsync(owner, $"{Edit}?handler=Preview", ContentFlow.Fields(("Body", "Intro paragraph.\n\n" + payload)), Edit));
        var pubResponse = await app.NewClient().GetAsync("/blog/attack");
        Assert.Equal(HttpStatusCode.OK, pubResponse.StatusCode);
        var pub = await pubResponse.Content.ReadAsStringAsync();

        foreach (var (name, html) in new[] { ("preview", Region(preview, "adm-post-preview", "</section>")), ("public", Region(pub, "article", "</article>")) })
        {
            Assert.True(html.Length > 0, name + " region not found");
            Assert.DoesNotMatch(@"<(script|iframe|style|svg|img\s+[^>]*onerror|object|embed|math|form|input)\b", html);
            Assert.DoesNotMatch(@"(?i)(href|src)=""\s*(javascript|vbscript|data):", html);
            foreach (Match m in Regex.Matches(html, @"<[a-z0-9]+\s+([^>]*)>"))
                foreach (Match a in Regex.Matches(m.Groups[1].Value, @"(?:^|\s)([a-zA-Z:-]+)=""[^""]*"""))
                    Assert.DoesNotMatch("^on", a.Groups[1].Value);
        }
        // The page around the article is untouched: it carries exactly the scripts a harmless post's page carries.
        await ContentFlow.PostAsync(owner, Edit, PostFields("Harmless", slug: "harmless", body: "Intro paragraph.\n\nPlain."));
        var baseline = await app.NewClient().GetStringAsync("/blog/harmless");
        Assert.Equal(Regex.Matches(baseline, "<script[^>]*>").Select(m => m.Value).OrderBy(x => x), Regex.Matches(pub, "<script[^>]*>").Select(m => m.Value).OrderBy(x => x));
    }

    private static string Region(string html, string cssClass, string endTag)
    {
        var start = html.IndexOf($"class=\"{cssClass}", StringComparison.Ordinal);
        if (start < 0) start = html.IndexOf($"class=\"{cssClass} ", StringComparison.Ordinal);
        if (start < 0) start = html.IndexOf($"{cssClass} mt-10", StringComparison.Ordinal);
        if (start < 0) return "";
        var end = html.IndexOf(endTag, start, StringComparison.Ordinal);
        return end < 0 ? html[start..] : html[start..end];
    }

    public static IEnumerable<object[]> UnrenderableBodies() => new[]
    {
        string.Concat(Enumerable.Repeat("- ", 100)) + "x",
        string.Concat(Enumerable.Repeat("# h\n", 201)),
        string.Concat(Enumerable.Repeat("[a](", 1001)),
    }.Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(UnrenderableBodies))]
    public async Task ATextTheRendererCannotTake_IsRefusedByThePreviewAndTheSave_AndAStoredOneStillShowsAs200(string body)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var preview = WebUtility.HtmlDecode(await TextAsync(await ContentFlow.PostAsync(owner, $"{Edit}?handler=Preview", ContentFlow.Fields(("Body", body)), Edit)));
        var save = await ContentFlow.PostAsync(owner, Edit, PostFields(body: body, slug: "unrenderable"));

        Assert.Contains("Some fields need attention", await TextAsync(save));
        Assert.Matches("(nested too deeply|more than 200 headings|more than 1,000 opening square brackets)", preview);
        Assert.DoesNotContain("Nothing has been saved", preview); // no preview was produced
        Assert.Empty(await RowsAsync(app));

        // A row that is already stored (an older post, or one written by the seeder) must not turn the public page into an error.
        using (var db = app.CreateDbContext())
        {
            db.BlogPosts.Add(new BlogPost { Title = "Stored", Slug = "stored", Date = new DateOnly(2024, 1, 1), BodyMarkdown = body, ConcurrencyStamp = "s" });
            await db.SaveChangesAsync();
        }
        var pub = await app.NewClient().GetAsync("/blog/stored");
        Assert.Equal(HttpStatusCode.OK, pub.StatusCode);
        Assert.Contains("<pre>", await pub.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await app.NewClient().GetAsync("/blog")).StatusCode);
    }

    [Fact]
    public async Task OnlyASavedText_IsKeptForThePublicPage_NotAPreviewAndNotARefusedSave()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var previewed = "Preview only body " + Guid.NewGuid();
        var refused = "Refused save body " + Guid.NewGuid();
        var saved = "Saved body " + Guid.NewGuid();

        await ContentFlow.PostAsync(owner, $"{Edit}?handler=Preview", ContentFlow.Fields(("Body", previewed)), Edit);
        await ContentFlow.PostAsync(owner, Edit, PostFields(title: "", body: refused)); // another field is invalid
        Assert.False(SafeMd.IsKept(previewed));
        Assert.False(SafeMd.IsKept(refused));

        await ContentFlow.PostAsync(owner, Edit, PostFields(body: saved));
        Assert.True(SafeMd.IsKept(saved));
        Assert.Single(await RowsAsync(app));
    }

    [Fact]
    public async Task APublicPostLinks_GetNofollowUgc_AndAnOverlongAutolink_DoesNotBreakThePage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, PostFields(body: "See [the site](https://example.org/a) and https://example.org/auto.\n\n" + new string('a', 5000)));

        var post = await app.NewClient().GetStringAsync("/blog/zebra-quartz-notes");

        Assert.Equal(2, Regex.Matches(post, "rel=\"noopener nofollow ugc\"").Count);
    }
}
