using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;
using Portfolio.Web.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class ProjectEditorTests
{
    private const string Edit = ContentFlow.Projects + "/edit";

    private static Dictionary<string, string> Project(string title = "Ward Handover Redesign", string summary = "Cut handover time by a third.", string slug = "") =>
        ContentFlow.Fields(("Title", title), ("Summary", summary), ("Slug", slug));

    private static async Task<List<Project>> RowsAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return await db.Projects.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
    }

    private static string[] UploadedFiles(AdminApp app) => Directory.GetFiles(app.UploadsPath).Select(Path.GetFileName).Select(n => n!).OrderBy(n => n).ToArray();

    private static byte[] Jpeg(string? comment = null)
    {
        var jpeg = TestImages.Split(SKEncodedImageFormat.Jpeg, 160, 90);
        return comment is null ? jpeg : TestImages.WithJpegSegments(jpeg, TestImages.JpegComment(comment));
    }

    private static Task<HttpResponseMessage> SaveWithHeroAsync(HttpClient owner, string path, Dictionary<string, string> fields, byte[] bytes, string fileName = "cover.jpg", string contentType = "image/jpeg") =>
        ContentFlow.PostMultipartAsync(owner, path, fields, ("Form.HeroFile", bytes, fileName, contentType), path.Split('?')[0]);

    // ---------------------------------------------------------------- create, edit, slugs

    [Fact]
    public async Task CreatingAProject_Persists_BuildsTheSlug_AndGoesPublic()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = Project();
        fields["Form.Role"] = "Lead";
        fields["Form.Org"] = "Northwind";
        fields["Form.Period"] = "2023";
        fields["Form.Category"] = "Quality";
        fields["Form.Problem"] = "Handover was slow.";
        fields["Form.Approach"] = "Structured the note.";
        fields["Form.ImpactText"] = "Handover 33% faster\nFewer repeat questions";
        fields["Form.TechText"] = "Audit\nSurveys";
        fields["Form.Featured"] = "true";
        fields["Form.Links[0].Label"] = "Write-up";
        fields["Form.Links[0].Url"] = "https://example.com/writeup";

        var create = await ContentFlow.PostAsync(owner, Edit, fields);

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var row = Assert.Single(await RowsAsync(app));
        Assert.Equal("ward-handover-redesign", row.Slug);
        Assert.Equal(["Handover 33% faster", "Fewer repeat questions"], row.Impact);
        Assert.Equal(["Audit", "Surveys"], row.Tech);
        Assert.Equal("https://example.com/writeup", Assert.Single(row.Links).Url);
        Assert.True(row.Featured);
        var detail = await app.NewClient().GetAsync("/projects/ward-handover-redesign");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var html = await detail.Content.ReadAsStringAsync();
        Assert.Contains("Ward Handover Redesign", html);
        Assert.Contains("Handover was slow.", html);
        Assert.Contains("Write-up", html);
        Assert.Contains("Ward Handover Redesign", await app.NewClient().GetStringAsync("/projects"));
        Assert.Contains("Ward Handover Redesign", await app.NewClient().GetStringAsync("/")); // featured
    }

    [Fact]
    public async Task TheSitemapAndTheStructuredData_FollowACreatedProject()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var anonymous = app.NewClient();
        var before = await anonymous.GetStringAsync("/sitemap.xml");
        Assert.DoesNotContain("/projects/ward-handover-redesign", before);
        Assert.Equal(before, await anonymous.GetStringAsync("/sitemap.xml")); // cached

        await ContentFlow.PostAsync(owner, Edit, Project());

        var after = await anonymous.GetStringAsync("/sitemap.xml");
        Assert.Contains("https://casey.example/projects/ward-handover-redesign", after);
        var detail = await anonymous.GetStringAsync("/projects/ward-handover-redesign");
        Assert.Contains("\"@type\":\"CreativeWork\"", detail);
        Assert.Contains("\"name\":\"Ward Handover Redesign\"", detail);
    }

    [Fact]
    public async Task CollidingTitles_GetNumberedSlugs()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        for (var i = 0; i < 3; i++) await ContentFlow.PostAsync(owner, Edit, Project("Same Title"));

        Assert.Equal(["same-title", "same-title-2", "same-title-3"], (await RowsAsync(app)).Select(r => r.Slug));
    }

    [Theory]
    [InlineData("New", "new-1")]
    [InlineData("ADMIN", "admin-1")]
    [InlineData("../../etc/passwd", "etc-passwd")]
    [InlineData("a/b?c#d", "a-b-c-d")]
    [InlineData("%2e%2e/secret", "2e-2e-secret")]
    [InlineData("日本語", "project")]
    public async Task GeneratedSlugs_AreAlwaysSafe(string title, string expected)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        await ContentFlow.PostAsync(owner, Edit, Project(title));

        Assert.Equal(expected, Assert.Single(await RowsAsync(app)).Slug);
    }

    [Theory]
    [InlineData("../admin", "can only use")]
    [InlineData("a/b", "can only use")]
    [InlineData("a b", "can only use")]
    [InlineData("a.b", "can only use")]
    [InlineData("%2e%2e", "can only use")]
    [InlineData("--x", "can only use")]
    [InlineData("a\\b", "can only use")]
    [InlineData("new", "reserved")]
    [InlineData("Admin", "reserved")]
    [InlineData("edit", "reserved")]
    public async Task TypedSlugs_AreCheckedStrictly(string typed, string expected)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var response = await ContentFlow.PostAsync(owner, Edit, Project(slug: typed));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
        Assert.Empty(await RowsAsync(app));
    }

    [Fact]
    public async Task ATypedSlugThatIsTaken_IsRefusedWithAFriendlyMessage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, Project("First", slug: "shared-name"));

        var response = await ContentFlow.PostAsync(owner, Edit, Project("Second", slug: "Shared-Name"));

        Assert.Contains("already used by another", await response.Content.ReadAsStringAsync());
        Assert.Single(await RowsAsync(app));
    }

    [Fact]
    public async Task TheSlug_StaysPutWhenTheTitleChanges_AndChangesOnlyWhenTheOwnerChangesIt()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, Project("Original Title"));
        var id = (await RowsAsync(app)).Single().Id;
        var path = $"{Edit}/{id}";

        // The form shows the current slug and posts it back with a new title.
        var form = await owner.GetStringAsync(path);
        Assert.Contains("value=\"original-title\"", form);
        var retitled = Project("Brand New Title", slug: "original-title");
        retitled["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        await ContentFlow.PostAsync(owner, path, retitled);
        Assert.Equal("original-title", (await RowsAsync(app)).Single().Slug);

        // Emptying the box keeps the slug too: a save never renames the address by accident.
        var emptied = Project("Brand New Title", slug: "");
        emptied["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        await ContentFlow.PostAsync(owner, path, emptied);
        Assert.Equal("original-title", (await RowsAsync(app)).Single().Slug);

        var renamed = Project("Brand New Title", slug: "fresh-address");
        renamed["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        await ContentFlow.PostAsync(owner, path, renamed);
        Assert.Equal("fresh-address", (await RowsAsync(app)).Single().Slug);
        var anonymous = app.NewClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/projects/original-title")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/projects/fresh-address")).StatusCode);
        var sitemap = await anonymous.GetStringAsync("/sitemap.xml");
        Assert.Contains("/projects/fresh-address", sitemap);
        Assert.DoesNotContain("/projects/original-title", sitemap);
    }

    [Fact]
    public async Task EditingAProject_KeepsItsSlugUnique_AgainstTheOthers()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, Project("Alpha"));
        await ContentFlow.PostAsync(owner, Edit, Project("Beta"));
        var beta = (await RowsAsync(app)).Single(r => r.Title == "Beta");
        var fields = Project("Beta", slug: "alpha");
        fields["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{Edit}/{beta.Id}");

        var response = await ContentFlow.PostAsync(owner, $"{Edit}/{beta.Id}", fields);

        Assert.Contains("already used by another", await response.Content.ReadAsStringAsync());
        Assert.Equal("beta", (await RowsAsync(app)).Single(r => r.Id == beta.Id).Slug);
    }

    // ---------------------------------------------------------------- validation

    [Theory]
    [InlineData("Form.Title", "", "Title is required")]
    [InlineData("Form.Summary", "", "Summary is required")]
    [InlineData("Form.Title", "<script>alert(1)</script>", "plain text")]
    [InlineData("Form.Summary", "<img src=x onerror=alert(1)>", "plain text")]
    [InlineData("Form.Role", "<b>Lead</b>", "plain text")]
    [InlineData("Form.Problem", "<iframe src=x>", "plain text")]
    [InlineData("Form.Approach", "<!-- hi -->", "plain text")]
    [InlineData("Form.ImpactText", "ok\n<svg onload=x>", "plain text")]
    [InlineData("Form.Links[0].Label", "<b>x</b>", "plain text")]
    [InlineData("Form.Links[0].Url", "javascript:alert(1)", "http:// or https://")]
    [InlineData("Form.Links[0].Url", "data:text/html,<script>alert(1)</script>", "http:// or https://")]
    [InlineData("Form.Links[0].Url", "//evil.example", "http:// or https://")]
    [InlineData("Form.Links[0].Url", "/relative", "http:// or https://")]
    public async Task InvalidInput_IsRefused_AndNothingIsSaved(string field, string value, string expected)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = Project();
        fields["Form.Links[0].Label"] = "Write-up";
        fields["Form.Links[0].Url"] = "https://example.com";
        fields[field] = value;

        var response = await ContentFlow.PostAsync(owner, Edit, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(expected, html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Empty(await RowsAsync(app));
    }

    [Fact]
    public async Task TooLongOrTooManyValues_AreRefused()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        foreach (var (field, value) in new[]
                 {
                     ("Form.Title", new string('t', 201)), ("Form.Summary", new string('s', 601)), ("Form.Problem", new string('p', 3001)),
                     ("Form.Category", new string('c', 81)), ("Form.Period", new string('d', 61)),
                     ("Form.ImpactText", string.Join("\n", Enumerable.Range(0, 9).Select(i => "result " + i))),
                     ("Form.TechText", string.Join("\n", Enumerable.Range(0, 21).Select(i => "tool " + i))),
                     ("Form.Links[0].Url", "https://example.com/" + new string('u', 300)),
                 })
        {
            var fields = Project();
            fields["Form.Links[0].Label"] = "x";
            fields["Form.Links[0].Url"] = "https://example.com";
            fields[field] = value;
            Assert.Equal(HttpStatusCode.OK, (await ContentFlow.PostAsync(owner, Edit, fields)).StatusCode);
        }
        Assert.Empty(await RowsAsync(app));
    }

    [Fact]
    public async Task ALinkNeedsBothHalves()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = Project();
        fields["Form.Links[0].Label"] = "";
        fields["Form.Links[0].Url"] = "";
        fields["Form.Links[1].Label"] = "Only a label";
        fields["Form.Links[1].Url"] = "";

        var response = await ContentFlow.PostAsync(owner, Edit, fields);

        Assert.Contains("Give a web address for link 2", await response.Content.ReadAsStringAsync());
        Assert.Empty(await RowsAsync(app));
    }

    // ---------------------------------------------------------------- reorder, remove

    [Fact]
    public async Task Reordering_Persists_AndTheListingFollows()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        foreach (var t in new[] { "Alpha Project", "Beta Project", "Gamma Project" }) await ContentFlow.PostAsync(owner, Edit, Project(t));
        var ids = (await RowsAsync(app)).Select(r => r.Id).ToList();

        await ContentFlow.PostAsync(owner, $"{ContentFlow.Projects}?handler=Move&id={ids[2]}", new Dictionary<string, string> { ["direction"] = "up" }, ContentFlow.Projects);
        await ContentFlow.PostAsync(owner, $"{ContentFlow.Projects}?handler=Move&id={ids[2]}", new Dictionary<string, string> { ["direction"] = "up" }, ContentFlow.Projects);

        Assert.Equal(["Gamma Project", "Alpha Project", "Beta Project"], (await RowsAsync(app)).Select(r => r.Title));
        var listing = await app.NewClient().GetStringAsync("/projects");
        Assert.True(listing.IndexOf("Gamma Project", StringComparison.Ordinal) < listing.IndexOf("Alpha Project", StringComparison.Ordinal));
        Assert.True(listing.IndexOf("Alpha Project", StringComparison.Ordinal) < listing.IndexOf("Beta Project", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARemovedProject_Answers404_LikeAnyUnknownAddress_AndLeavesTheSitemap()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, Project("Temporary"));
        var anonymous = app.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/projects/temporary")).StatusCode);
        Assert.Contains("/projects/temporary", await anonymous.GetStringAsync("/sitemap.xml"));
        var id = (await RowsAsync(app)).Single().Id;

        var confirm = await owner.GetStringAsync($"{ContentFlow.Projects}/delete/{id}");
        Assert.Contains("/projects/temporary", confirm);
        Assert.Contains("stops working", confirm);
        await ContentFlow.PostAsync(owner, $"{ContentFlow.Projects}/delete/{id}", new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Projects}/delete/{id}") });

        var gone = await anonymous.GetAsync("/projects/temporary");
        var never = await anonymous.GetAsync("/projects/never-existed");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(never.StatusCode, gone.StatusCode);
        Assert.DoesNotContain("/projects/temporary", await anonymous.GetStringAsync("/sitemap.xml"));
        Assert.DoesNotContain("Temporary", await anonymous.GetStringAsync("/projects"));
    }

    // ---------------------------------------------------------------- cover image

    [Fact]
    public async Task ACoverImage_IsReEncoded_StoredUnderAGeneratedName_AndShownWithItsAltText()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = Project();
        fields["Form.HeroAlt"] = "Two nurses reviewing a handover sheet";

        var save = await SaveWithHeroAsync(owner, Edit, fields, Jpeg("SECRET-GPS"), "../../holiday photo.JPG");

        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var row = Assert.Single(await RowsAsync(app));
        Assert.Matches(new Regex(@"^/uploads/[0-9a-f]{32}\.jpg$"), row.HeroImage);
        Assert.Equal("Two nurses reviewing a handover sheet", row.HeroImageAlt);
        var file = Assert.Single(UploadedFiles(app));
        Assert.Equal(row.HeroImage, "/uploads/" + file);
        Assert.DoesNotContain("SECRET-GPS", System.Text.Encoding.Latin1.GetString(await File.ReadAllBytesAsync(Path.Combine(app.UploadsPath, file))));
        var detail = await app.NewClient().GetStringAsync($"/projects/{row.Slug}");
        Assert.Contains($"src=\"{row.HeroImage}\" alt=\"Two nurses reviewing a handover sheet\"", detail);
        Assert.Equal(HttpStatusCode.OK, (await app.NewClient().GetAsync(row.HeroImage)).StatusCode);
    }

    [Fact]
    public async Task ACoverImageWithoutAltText_IsRefused_AndNothingIsStored()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var response = await SaveWithHeroAsync(owner, Edit, Project(), Jpeg());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Describe the cover image", await response.Content.ReadAsStringAsync());
        Assert.Empty(await RowsAsync(app));
        Assert.Empty(UploadedFiles(app));
    }

    [Theory]
    [InlineData("notes.txt", "text/plain", "this is not an image")]
    [InlineData("vector.svg", "image/svg+xml", "<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    [InlineData("page.html.jpg", "image/jpeg", "<html><script>alert(1)</script></html>")]
    [InlineData("empty.jpg", "image/jpeg", "")]
    public async Task ANonImage_IsRefused_AndNothingIsStoredOrSaved(string fileName, string contentType, string content)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = Project();
        fields["Form.HeroAlt"] = "alt";

        var response = await SaveWithHeroAsync(owner, Edit, fields, System.Text.Encoding.UTF8.GetBytes(content), fileName, contentType);

        // An empty file field means "no new image"; anything else must be a real raster image.
        if (content.Length == 0) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        else
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(await RowsAsync(app));
        }
        Assert.Empty(UploadedFiles(app));
    }

    [Fact]
    public async Task AnOversizedImage_IsRefused()
    {
        using var app = AdminApp.Create(AdminApp.SetupKey, new Dictionary<string, string?> { ["Uploads:MaxBytes"] = "20000" });
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = Project();
        fields["Form.HeroAlt"] = "alt";

        var response = await SaveWithHeroAsync(owner, Edit, fields, TestImages.Noise(120, 120));

        Assert.Contains("larger than", await response.Content.ReadAsStringAsync());
        Assert.Empty(await RowsAsync(app));
        Assert.Empty(UploadedFiles(app));
    }

    [Fact]
    public async Task ReplacingTheCoverImage_DeletesTheOldUpload_AndRemovingItDeletesTheNewOne()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = Project();
        fields["Form.HeroAlt"] = "First picture";
        await SaveWithHeroAsync(owner, Edit, fields, Jpeg());
        var created = Assert.Single(await RowsAsync(app));
        var firstFile = Assert.Single(UploadedFiles(app));
        var path = $"{Edit}/{created.Id}";

        var replace = Project();
        replace["Form.HeroAlt"] = "Second picture";
        replace["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        Assert.Equal(HttpStatusCode.Redirect, (await SaveWithHeroAsync(owner, path, replace, TestImages.Split(SKEncodedImageFormat.Png, 120, 80), "b.png", "image/png")).StatusCode);

        var second = Assert.Single(await RowsAsync(app));
        Assert.NotEqual(created.HeroImage, second.HeroImage);
        Assert.Matches(new Regex(@"^/uploads/[0-9a-f]{32}\.png$"), second.HeroImage);
        var secondFile = Assert.Single(UploadedFiles(app));
        Assert.NotEqual(firstFile, secondFile);
        Assert.Equal("Second picture", second.HeroImageAlt);

        var remove = Project();
        remove["Form.RemoveHero"] = "true";
        remove["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        await ContentFlow.PostAsync(owner, path, remove);

        var third = Assert.Single(await RowsAsync(app));
        Assert.Null(third.HeroImage);
        Assert.Null(third.HeroImageAlt);
        Assert.Empty(UploadedFiles(app));
    }

    [Fact]
    public async Task KeepingTheCoverImage_NeedsItsAltText_ButANewFileIsNotRequired()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = Project();
        fields["Form.HeroAlt"] = "A picture";
        await SaveWithHeroAsync(owner, Edit, fields, Jpeg());
        var id = (await RowsAsync(app)).Single().Id;
        var path = $"{Edit}/{id}";

        var noAlt = Project();
        noAlt["Form.HeroAlt"] = "";
        noAlt["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        var refused = await ContentFlow.PostAsync(owner, path, noAlt);
        Assert.Contains("Describe the cover image", await refused.Content.ReadAsStringAsync());

        var better = Project();
        better["Form.HeroAlt"] = "A better description";
        better["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, better)).StatusCode);
        Assert.Equal("A better description", (await RowsAsync(app)).Single().HeroImageAlt);
        Assert.Single(UploadedFiles(app));
    }

    [Fact]
    public async Task AnImageThatShipsWithTheSite_NeedsNoAltText_AndIsNeverDeleted()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
        {
            db.Projects.Add(new Project { Title = "Bundled", Slug = "bundled", Summary = "s", HeroImage = "/img/projects/edge.svg", SortOrder = 1 });
            await db.SaveChangesAsync();
        }
        var id = (await RowsAsync(app)).Single().Id;
        var path = $"{Edit}/{id}";
        var untouched = Project("Bundled", slug: "bundled");
        untouched["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);

        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, untouched)).StatusCode);
        Assert.Equal("/img/projects/edge.svg", (await RowsAsync(app)).Single().HeroImage);

        var remove = Project("Bundled", slug: "bundled");
        remove["Form.RemoveHero"] = "true";
        remove["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        await ContentFlow.PostAsync(owner, path, remove);
        Assert.Null((await RowsAsync(app)).Single().HeroImage);
        Assert.Empty(UploadedFiles(app));
    }

    // ---------------------------------------------------------------- gallery images

    private static async Task<HttpResponseMessage> AddImageAsync(HttpClient owner, int id, string caption, byte[]? bytes = null, string fileName = "shot.jpg", string contentType = "image/jpeg")
    {
        var path = $"{ContentFlow.Projects}/images/{id}";
        var stamp = await ContentFlow.StampAsync(owner, path);
        return await ContentFlow.PostMultipartAsync(owner, $"{path}?handler=Upload", new Dictionary<string, string> { ["stamp"] = stamp, ["Caption"] = caption },
            ("Image", bytes ?? Jpeg(), fileName, contentType), path);
    }

    [Fact]
    public async Task GalleryImages_AreAddedWithADescription_ShownOnThePage_AndRemovedFromStorageWithTheImage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, Project());
        var id = (await RowsAsync(app)).Single().Id;

        Assert.Equal(HttpStatusCode.Redirect, (await AddImageAsync(owner, id, "The old handover sheet")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await AddImageAsync(owner, id, "The new handover sheet")).StatusCode);

        var row = (await RowsAsync(app)).Single();
        Assert.Equal(["The old handover sheet", "The new handover sheet"], row.Screenshots.Select(s => s.Caption));
        Assert.All(row.Screenshots, s => Assert.Matches(new Regex(@"^/uploads/[0-9a-f]{32}\.jpg$"), s.Url));
        Assert.Equal(2, UploadedFiles(app).Length);
        var detail = await app.NewClient().GetStringAsync("/projects/ward-handover-redesign");
        Assert.Contains("alt=\"The old handover sheet\"", detail);
        Assert.Contains("data-lightbox-gallery", detail);
        var page = await owner.GetStringAsync($"{ContentFlow.Projects}/images/{id}");
        Assert.Contains("alt=\"The new handover sheet\"", page);

        var first = row.Screenshots[0].Url;
        await ContentFlow.PostAsync(owner, $"{ContentFlow.Projects}/images/{id}?handler=Move",
            new Dictionary<string, string> { ["stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Projects}/images/{id}"), ["url"] = first, ["direction"] = "down" }, $"{ContentFlow.Projects}/images/{id}");
        Assert.Equal(["The new handover sheet", "The old handover sheet"], (await RowsAsync(app)).Single().Screenshots.Select(s => s.Caption));

        await ContentFlow.PostAsync(owner, $"{ContentFlow.Projects}/images/{id}?handler=Remove",
            new Dictionary<string, string> { ["stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Projects}/images/{id}"), ["url"] = first }, $"{ContentFlow.Projects}/images/{id}");
        Assert.Equal(["The new handover sheet"], (await RowsAsync(app)).Single().Screenshots.Select(s => s.Caption));
        Assert.Single(UploadedFiles(app));
        Assert.DoesNotContain(Path.GetFileName(first), UploadedFiles(app));
    }

    [Fact]
    public async Task AGalleryImage_NeedsADescription_ARealImage_AndRoom()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, Project());
        var id = (await RowsAsync(app)).Single().Id;

        Assert.Contains("Describe the image", await (await AddImageAsync(owner, id, "")).Content.ReadAsStringAsync());
        Assert.Contains("plain text", await (await AddImageAsync(owner, id, "<b>x</b>")).Content.ReadAsStringAsync());
        Assert.Contains("at most", await (await AddImageAsync(owner, id, new string('c', 201))).Content.ReadAsStringAsync());
        var notImage = await AddImageAsync(owner, id, "fine", System.Text.Encoding.UTF8.GetBytes("<svg onload=alert(1)>"), "x.svg", "image/svg+xml");
        Assert.Equal(HttpStatusCode.OK, notImage.StatusCode);
        Assert.Empty((await RowsAsync(app)).Single().Screenshots);
        Assert.Empty(UploadedFiles(app));

        for (var i = 0; i < 12; i++) Assert.Equal(HttpStatusCode.Redirect, (await AddImageAsync(owner, id, "shot " + i)).StatusCode);
        var full = await AddImageAsync(owner, id, "one too many");
        Assert.Contains("at most 12", await full.Content.ReadAsStringAsync());
        Assert.Equal(12, (await RowsAsync(app)).Single().Screenshots.Count);
        Assert.Equal(12, UploadedFiles(app).Length);
    }

    [Fact]
    public async Task RemovingAProject_DeletesOnlyItsOwnUploads()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        // An unrelated upload that the profile still points at.
        var photo = "/uploads/" + new string('a', 32) + ".jpg";
        await File.WriteAllBytesAsync(Path.Combine(app.UploadsPath, Path.GetFileName(photo)), Jpeg());
        using (var db = app.CreateDbContext())
        {
            (await db.SiteProfiles.SingleAsync()).PhotoUrl = photo;
            await db.SaveChangesAsync();
        }
        var fields = Project("Doomed");
        fields["Form.HeroAlt"] = "cover";
        await SaveWithHeroAsync(owner, Edit, fields, Jpeg());
        await ContentFlow.PostAsync(owner, Edit, Project("Survivor"));
        var doomed = (await RowsAsync(app)).Single(r => r.Title == "Doomed");
        await AddImageAsync(owner, doomed.Id, "gallery");
        Assert.Equal(3, UploadedFiles(app).Length);

        var confirm = await owner.GetStringAsync($"{ContentFlow.Projects}/delete/{doomed.Id}");
        Assert.Contains("2 image file(s) you uploaded for it will be deleted", confirm);
        Assert.Equal(3, UploadedFiles(app).Length);
        await ContentFlow.PostAsync(owner, $"{ContentFlow.Projects}/delete/{doomed.Id}", new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Projects}/delete/{doomed.Id}") });

        Assert.Equal(new[] { Path.GetFileName(photo) }, UploadedFiles(app));
        Assert.Equal(["Survivor"], (await RowsAsync(app)).Select(r => r.Title));
        using var check = app.CreateDbContext();
        Assert.Equal(photo, (await check.SiteProfiles.SingleAsync()).PhotoUrl);
    }

    [Fact]
    public async Task AnUploadAnotherProjectStillUses_IsKept()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = Project("Original");
        fields["Form.HeroAlt"] = "cover";
        await SaveWithHeroAsync(owner, Edit, fields, Jpeg());
        var original = (await RowsAsync(app)).Single();
        using (var db = app.CreateDbContext())
        {
            db.Projects.Add(new Project { Title = "Sharer", Slug = "sharer", Summary = "s", HeroImage = original.HeroImage, SortOrder = 2 });
            await db.SaveChangesAsync();
        }

        await ContentFlow.PostAsync(owner, $"{ContentFlow.Projects}/delete/{original.Id}", new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Projects}/delete/{original.Id}") });

        Assert.Single(UploadedFiles(app)); // the sharer still shows it
    }

    [Fact]
    public async Task TheUploadsFolder_StaysFlat_AndTheDraftAreaIsNotReachable()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = Project();
        fields["Form.HeroAlt"] = "cover";
        await SaveWithHeroAsync(owner, Edit, fields, Jpeg(), "../../.drafts/x.jpg");
        await ContentFlow.PostAsync(owner, Edit, Project("Another"));
        await AddImageAsync(owner, (await RowsAsync(app)).First().Id, "shot", fileName: "..\\..\\evil.jpg");

        Assert.Empty(Directory.GetDirectories(app.UploadsPath));
        Assert.All(Directory.GetFiles(app.UploadsPath), f => Assert.Matches(@"^[0-9a-f]{32}\.(jpg|png|webp)$", Path.GetFileName(f)));
        var anonymous = app.NewClient();
        foreach (var path in new[] { "/uploads/.drafts/x.jpg", "/uploads/.drafts/", "/uploads/", "/uploads/..%2f..%2fappsettings.json", "/uploads/%2e%2e/x" })
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(path)).StatusCode);
    }

    // ---------------------------------------------------------------- concurrency

    [Fact]
    public async Task AStaleProjectForm_IsRefused_AndALateImageUploadDoesNotOverwriteIt()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, Project("Original"));
        var id = (await RowsAsync(app)).Single().Id;
        var path = $"{Edit}/{id}";
        var tabOne = await ContentFlow.StampAsync(owner, path);
        var tabTwo = await ContentFlow.StampAsync(owner, path);
        var one = Project("From tab one", slug: "original");
        one["Form.Stamp"] = tabOne;
        await ContentFlow.PostAsync(owner, path, one);
        var two = Project("From tab two", slug: "original");
        two["Form.Stamp"] = tabTwo;

        var refused = await ContentFlow.PostAsync(owner, path, two);

        Assert.Contains("changed or removed somewhere else", await refused.Content.ReadAsStringAsync());
        Assert.Equal("From tab one", (await RowsAsync(app)).Single().Title);

        // An upload from a gallery page opened before that edit is refused too, and its file is not left behind.
        var late = await ContentFlow.PostMultipartAsync(owner, $"{ContentFlow.Projects}/images/{id}?handler=Upload",
            new Dictionary<string, string> { ["stamp"] = tabOne, ["Caption"] = "late" }, ("Image", Jpeg(), "late.jpg", "image/jpeg"), $"{ContentFlow.Projects}/images/{id}");
        Assert.Equal(HttpStatusCode.Redirect, late.StatusCode);
        Assert.Empty((await RowsAsync(app)).Single().Screenshots);
        Assert.Empty(UploadedFiles(app));
    }
}
