using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Transfer;

/// <summary>The export file: what it holds, what it leaves out, its order, and that fetching it changes nothing.</summary>
public class ExportTests
{
    /// <summary>Two rows in every table, a profile with text, two stored section settings (one for a key no template lists) and an upload that really exists.</summary>
    private static async Task<(HttpClient Owner, string RealUpload)> SeededSiteAsync(AdminApp app, string template = "software")
    {
        var owner = await ContentFlow.BlankSiteAsync(app, template);
        await ContentFlow.SeedRowsAsync(app, pairs: true);
        await ProfileFlow.SeedTextAsync(app);
        var real = "/uploads/" + new string('a', 32) + ".png";
        await File.WriteAllBytesAsync(Path.Combine(app.UploadsPath, new string('a', 32) + ".png"), TestImages.Split(SkiaSharp.SKEncodedImageFormat.Png, 8, 8));
        using var db = app.CreateDbContext();
        var p = await db.SiteProfiles.SingleAsync();
        p.Sections = [new SectionSetting { Key = "blog", Label = "Journal", Shown = true }, new SectionSetting { Key = "gallery", Label = "Stray", Enabled = false, Shown = false, SortOrder = 3 }];
        p.PhotoUrl = real;
        await db.SaveChangesAsync();
        var project = await db.Projects.FirstAsync(x => x.Slug == "seed-project");
        project.HeroImage = real;
        project.HeroImageAlt = "A real picture";
        await db.SaveChangesAsync();
        app.Services.GetRequiredService<Portfolio.Web.Services.ISiteProfileProvider>().Invalidate();
        return (owner, real);
    }

    [Theory]
    [InlineData("software")]
    [InlineData("healthcare")]
    public async Task TheFile_HoldsEveryTable_AndTheProfile_ForBothTemplates(string template)
    {
        using var app = new AdminApp();
        var (owner, real) = await SeededSiteAsync(app, template);

        using var doc = await TransferFlow.ExportJsonAsync(owner);
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("living-cv-export", root.GetProperty("format").GetString());
        Assert.Equal(template, root.GetProperty("profile").GetProperty("templateKey").GetString());
        Assert.Equal("Casey Morgan", root.GetProperty("profile").GetProperty("ownerName").GetString());
        Assert.Equal("Seed headline", root.GetProperty("profile").GetProperty("hero").GetProperty("headline").GetString());
        Assert.Equal(2, root.GetProperty("profile").GetProperty("metrics").GetArrayLength());
        Assert.Equal(2, root.GetProperty("profile").GetProperty("about").GetProperty("valueProps").GetArrayLength());

        var content = root.GetProperty("content");
        foreach (var table in new[] { "experience", "skills", "projects", "certifications", "talks", "testimonials" })
            Assert.True(content.GetProperty(table).GetArrayLength() >= 2, $"{table} has two rows");
        var blog = content.GetProperty("blog");
        Assert.Equal(2, blog.GetArrayLength());
        Assert.Contains(blog.EnumerateArray(), b => b.GetProperty("draft").GetBoolean() && b.GetProperty("slug").GetString() == "seed-draft");
        Assert.Contains(blog.EnumerateArray(), b => !b.GetProperty("draft").GetBoolean() && b.GetProperty("slug").GetString() == "seed-post");
        Assert.Equal(real, content.GetProperty("projects")[0].GetProperty("heroImage").GetString());
    }

    [Fact]
    public async Task TheSectionSettings_AreExported_ShownAndStrayKeysIncluded()
    {
        using var app = new AdminApp();
        var (owner, _) = await SeededSiteAsync(app);

        using var doc = await TransferFlow.ExportJsonAsync(owner);
        var sections = doc.RootElement.GetProperty("profile").GetProperty("sections").EnumerateArray().ToList();

        Assert.Equal(2, sections.Count);
        var blog = sections.Single(s => s.GetProperty("key").GetString() == "blog");
        Assert.Equal("Journal", blog.GetProperty("label").GetString());
        Assert.True(blog.GetProperty("shown").GetBoolean());
        var stray = sections.Single(s => s.GetProperty("key").GetString() == "gallery");
        Assert.False(stray.GetProperty("enabled").GetBoolean());
        Assert.False(stray.GetProperty("shown").GetBoolean());
        Assert.Equal(3, stray.GetProperty("sortOrder").GetInt32());
    }

    [Fact]
    public async Task NoStampAndNoDatabaseId_IsEverWritten()
    {
        using var app = new AdminApp();
        var (owner, _) = await SeededSiteAsync(app);
        var text = await TransferFlow.ExportTextAsync(owner);

        using var db = app.CreateDbContext();
        var stamps = (await db.Experiences.Select(x => x.ConcurrencyStamp).ToListAsync())
            .Concat(await db.Skills.Select(x => x.ConcurrencyStamp).ToListAsync())
            .Concat(await db.Projects.Select(x => x.ConcurrencyStamp).ToListAsync())
            .Concat(await db.Certifications.Select(x => x.ConcurrencyStamp).ToListAsync())
            .Concat(await db.Talks.Select(x => x.ConcurrencyStamp).ToListAsync())
            .Concat(await db.Testimonials.Select(x => x.ConcurrencyStamp).ToListAsync())
            .Concat(await db.BlogPosts.Select(x => x.ConcurrencyStamp).ToListAsync())
            .Append((await db.SiteProfiles.SingleAsync()).ConcurrencyStamp).ToList();
        Assert.True(stamps.Count >= 14);
        foreach (var stamp in stamps) Assert.DoesNotContain(stamp!, text);
        Assert.DoesNotContain("tamp", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"id\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onboarding", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheFile_IsIndented_AndKeepsReadableText_AndNullsAreWritten()
    {
        using var app = new AdminApp();
        var (owner, _) = await SeededSiteAsync(app);
        using (var db = app.CreateDbContext())
        {
            var post = await db.BlogPosts.FirstAsync(b => b.Slug == "seed-post");
            post.BodyMarkdown = "Latency < 50ms & \"quoted\" — naïve café <b>bold</b>";
            await db.SaveChangesAsync();
        }

        var text = await TransferFlow.ExportTextAsync(owner);

        Assert.Contains("\n  \"schemaVersion\": 1,", text);
        Assert.Contains("\"body\": \"Latency < 50ms & \\\"quoted\\\" — naïve café <b>bold</b>\"", text);
        Assert.Contains("\"location\": \"Leeds, UK\"", text);
        Assert.Contains("\"role\": null", text); // a field with no value is still written, so an import can clear it
        Assert.Contains("\"status\": \"Earned\"", text);
    }

    [Fact]
    public async Task Rows_AreInTheirPublicOrder_AndTwoExportsOfTheSameDataAreTheSameFile()
    {
        using var app = new AdminApp();
        var (owner, _) = await SeededSiteAsync(app);

        var first = await TransferFlow.ExportTextAsync(owner);
        var second = await TransferFlow.ExportTextAsync(owner);
        Assert.Equal(first, second);

        using var doc = JsonDocument.Parse(first);
        var content = doc.RootElement.GetProperty("content");
        Assert.Equal(["Seed Co", "Second Co"], content.GetProperty("experience").EnumerateArray().Select(r => r.GetProperty("company").GetString()!).ToArray());
        Assert.Equal(["Seed Project", "Second Project"], content.GetProperty("projects").EnumerateArray().Select(r => r.GetProperty("title").GetString()!).ToArray());
        Assert.Equal(["Seed Talk", "Second Talk"], content.GetProperty("talks").EnumerateArray().Select(r => r.GetProperty("title").GetString()!).ToArray()); // newest first
        Assert.Equal(["seed-draft", "seed-post"], content.GetProperty("blog").EnumerateArray().Select(r => r.GetProperty("slug").GetString()!).ToArray()); // newest first, drafts included
    }

    [Fact]
    public async Task TheUploadsManifest_ListsReferencedFiles_WhetherTheyExistOrNot_AndWhereTheyAreUsed()
    {
        using var app = new AdminApp();
        var (owner, real) = await SeededSiteAsync(app);

        using var doc = await TransferFlow.ExportJsonAsync(owner);
        var uploads = doc.RootElement.GetProperty("uploads").EnumerateArray().ToList();

        var existing = uploads.Single(u => u.GetProperty("path").GetString() == real);
        Assert.True(existing.GetProperty("exists").GetBoolean());
        var uses = existing.GetProperty("usedBy").EnumerateArray().Select(u => $"{u.GetProperty("table").GetString()}:{u.GetProperty("entry").GetString()}:{u.GetProperty("field").GetString()}").ToList();
        Assert.Contains("profile::photoUrl", uses);
        Assert.Contains("projects:seed-project:heroImage", uses);
        // The gallery files the seed rows point at were never stored, and the manifest says so.
        var missing = uploads.Where(u => !u.GetProperty("exists").GetBoolean()).ToList();
        Assert.Equal(2, missing.Count);
        Assert.Equal(missing.Select(u => u.GetProperty("path").GetString()).OrderBy(x => x, StringComparer.Ordinal), missing.Select(u => u.GetProperty("path").GetString()));
        Assert.DoesNotContain(uploads, u => u.GetProperty("path").GetString()!.StartsWith("/img/"));
    }

    [Fact]
    public async Task TheResponse_IsADownloadThatIsNotStoredOrSniffed()
    {
        using var app = new AdminApp();
        var (owner, _) = await SeededSiteAsync(app);

        var response = await owner.GetAsync(TransferFlow.Export);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Matches(@"^site-export-\d{4}-\d{2}-\d{2}\.json$", response.Content.Headers.ContentDisposition.FileName);
        Assert.Contains("no-store", string.Join(",", response.Headers.GetValues("Cache-Control")));
        Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("noindex", string.Join(",", response.Headers.GetValues("X-Robots-Tag")));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task FetchingTheExport_NeverChangesAnything_NotEvenTheProfileRowOrAStamp()
    {
        using var app = new AdminApp();
        var (owner, _) = await SeededSiteAsync(app);
        var before = await ContentFlow.SnapshotAsync(app);

        for (var i = 0; i < 3; i++) await TransferFlow.ExportTextAsync(owner);
        // The same address with a handler or a body-shaped query is still only a read.
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(TransferFlow.Export + "?handler=Delete&Form.OwnerName=Hacked")).StatusCode);

        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task AnonymousVisitors_AndStaleSessions_CannotFetchIt()
    {
        using var app = new AdminApp();
        var (owner, _) = await SeededSiteAsync(app);

        var anonymous = await app.NewClient().GetAsync(TransferFlow.Export);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/admin/login", anonymous.Headers.Location!.OriginalString);

        using (var db = app.CreateDbContext())
        {
            (await db.OwnerAccounts.SingleAsync()).SecurityStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
        }
        var stale = await owner.GetAsync(TransferFlow.Export);
        Assert.Equal(HttpStatusCode.Redirect, stale.StatusCode);
        Assert.Contains("/admin/login", stale.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task APostToTheExportAddress_IsNotAnExport()
    {
        using var app = new AdminApp();
        var (owner, _) = await SeededSiteAsync(app);
        var before = await ContentFlow.SnapshotAsync(app);

        var token = await AdminApp.GetTokenAsync(owner, "/admin");
        var post = await AdminApp.PostFormAsync(owner, TransferFlow.Export, token, new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task AnExportOrAPreview_OfASiteWithNoStoredProfile_SavesNothing_NotEvenTheProfileRow()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
        {
            db.SiteProfiles.RemoveRange(db.SiteProfiles);
            await db.SaveChangesAsync();
        }

        var export = await owner.GetAsync(TransferFlow.Export);
        var preview = await TransferFlow.PreviewAsync(owner, ImportHarness.File(profile: new { ownerName = "X" }), applyProfile: true);

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var check = app.CreateDbContext();
        Assert.Equal(0, await check.SiteProfiles.CountAsync());
    }

    [Fact]
    public async Task TheExportPage_SetsTheNoStoreAndNosniffHeadersItself_NotOnlyThroughTheAdminMiddleware()
    {
        using var app = new AdminApp();
        await ContentFlow.BlankSiteAsync(app);
        using var scope = app.Services.CreateScope();
        var model = new Portfolio.Web.Pages.Admin.Transfer.ExportModel(scope.ServiceProvider.GetRequiredService<Portfolio.Web.Transfer.ExportService>(), TimeProvider.System);
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = scope.ServiceProvider };
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = context };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Contains("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions.ToString());
    }
}
