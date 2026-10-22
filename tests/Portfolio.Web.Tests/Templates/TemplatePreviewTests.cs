using System.Net;
using Microsoft.EntityFrameworkCore;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>?preview=&lt;key&gt; is for the signed-in owner only: nobody else can see it, nothing about it is public, indexed, cached or saved.</summary>
public class TemplatePreviewTests
{
    private static async Task<string> TemplateKeyAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return await db.SiteProfiles.Select(p => p.TemplateKey).SingleAsync();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/about")]
    [InlineData("/experience")]
    [InlineData("/projects")]
    [InlineData("/blog")]
    [InlineData("/cv")]
    public async Task Anonymous_PreviewParameter_ChangesNothing(string path)
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var client = app.NewClient();

        var normal = await client.GetAsync(path);
        var previewed = await client.GetAsync($"{path}?preview=healthcare");

        Assert.Equal(normal.StatusCode, previewed.StatusCode);
        Assert.Equal(DemoSite.Normalise(await normal.Content.ReadAsStringAsync()), DemoSite.Normalise(await previewed.Content.ReadAsStringAsync()));
        Assert.False(previewed.Headers.Contains("X-Robots-Tag"));
        Assert.DoesNotContain("no-store", previewed.Headers.CacheControl?.ToString() ?? "");
        Assert.Equal("DENY", previewed.Headers.GetValues("X-Frame-Options").Single());
        var html = await previewed.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Previewing", html);
        Assert.DoesNotContain("data-template", html);
        Assert.DoesNotContain("noindex", html);
    }

    [Fact]
    public async Task ASignedOutOwner_AndAStaleSession_GetNoPreview()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();
        Assert.Contains("data-template=\"healthcare\"", await owner.GetStringAsync("/?preview=healthcare"));

        // Rotating the stamp (what reset-owner does) ends the session: the same cookie must no longer preview.
        using (var db = app.CreateDbContext())
        {
            var account = await db.OwnerAccounts.SingleAsync();
            account.SecurityStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
        }

        var response = await owner.GetAsync("/?preview=healthcare");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("data-template", html);
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
    }

    [Fact]
    public async Task Owner_SeesThePreview_WhichIsNotIndexedCachedOrFramedByOthers()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();

        var response = await owner.GetAsync("/experience?preview=healthcare");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"en\" data-template=\"healthcare\">", html);
        Assert.Contains("Clinical experience", html);
        Assert.Contains("Previewing the Healthcare template", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow, noarchive\" />", html);
        Assert.Contains("noindex", response.Headers.GetValues("X-Robots-Tag").Single());
        var cache = response.Headers.CacheControl!;
        Assert.True(cache.NoStore);
        Assert.True(cache.NoCache);
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("no-cache", response.Headers.GetValues("Pragma"));
    }

    [Fact]
    public async Task APreview_IsNeverSaved_AndNeverReachesAnyoneElse()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();
        var visitor = app.NewClient();
        var publicBefore = DemoSite.Normalise(await visitor.GetStringAsync("/"));

        _ = await owner.GetStringAsync("/?preview=healthcare");
        _ = await owner.GetStringAsync("/projects?preview=healthcare");

        Assert.Equal("software", await TemplateKeyAsync(app));
        Assert.Equal(publicBefore, DemoSite.Normalise(await visitor.GetStringAsync("/")));
        var ownerNormal = await owner.GetStringAsync("/");
        Assert.DoesNotContain("data-template", ownerNormal);
        Assert.DoesNotContain("Previewing", ownerNormal);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("../healthcare")]
    [InlineData("")]
    [InlineData("healthcare%00")]
    [InlineData("%3Cscript%3E")]
    public async Task Owner_PreviewOfAKeyOutsideTheCatalog_ShowsTheSavedSite(string key)
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();

        var response = await owner.GetAsync($"/?preview={key}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("data-template", html);
        Assert.DoesNotContain("Previewing", html);
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
    }

    [Fact]
    public async Task Owner_PreviewedSections_FollowTheTemplateBeingPreviewed()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/blog")).StatusCode);
        var hidden = await owner.GetAsync("/blog?preview=healthcare");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/certifications?preview=healthcare")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/blog")).StatusCode);
    }

    [Fact]
    public async Task Preview_DoesNotReachTheCachedGeneratedOutput()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();
        var visitor = app.NewClient();
        var plain = await visitor.GetStringAsync("/sitemap.xml");

        var previewed = await owner.GetAsync("/sitemap.xml?preview=healthcare");
        var afterwards = await visitor.GetAsync("/sitemap.xml");

        Assert.Equal(plain, await previewed.Content.ReadAsStringAsync());
        Assert.Contains("/blog", plain);
        Assert.False(previewed.Headers.Contains("X-Robots-Tag"));
        Assert.False(afterwards.Headers.Contains("X-Robots-Tag"));
        Assert.Equal(plain, await afterwards.Content.ReadAsStringAsync());
        Assert.DoesNotContain("preview", plain);
    }

    [Fact]
    public async Task Preview_LinksStayInThePreview()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync("/?preview=healthcare");

        Assert.Contains("searchParams.set('preview', key)", html);
        Assert.Contains("var key = \"healthcare\";", html);
    }

    [Fact]
    public async Task APreviewPage_CarriesNoAnalytics_WhileTheSamePageNormallyDoes()
    {
        using var app = AdminApp.Create(AdminApp.SetupKey, new Dictionary<string, string?> { ["Site:GoogleAnalyticsId"] = "G-PREVIEW1" });
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();

        var normal = await owner.GetStringAsync("/about");
        var previewed = await owner.GetStringAsync("/about?preview=healthcare");

        Assert.Contains("googletagmanager.com/gtag/js?id=G-PREVIEW1", normal);
        Assert.Contains("Previewing the Healthcare template", previewed);
        Assert.DoesNotContain("googletagmanager", previewed);
        Assert.DoesNotContain("gtag(", previewed);
        Assert.DoesNotContain("G-PREVIEW1", previewed);
    }

    [Fact]
    public async Task OnlyTheBaseLook_PreloadsItsFonts()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();

        var software = await owner.GetStringAsync("/about");
        var healthcare = await owner.GetStringAsync("/about?preview=healthcare");

        Assert.Contains("space-grotesk-latin", software);
        Assert.Contains("jetbrains-mono-latin", software);
        Assert.DoesNotContain("woff2", healthcare);
    }

    [Fact]
    public async Task OnlyAGetForAPublicPage_CanPreview()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();

        // A real POST is not a preview, even with a valid key. The page may refuse it; either way nothing says "preview".
        var post = await AdminApp.PostFormAsync(owner, "/about?preview=healthcare", null, new Dictionary<string, string>());
        Assert.False(post.Headers.Contains("X-Robots-Tag"));
        Assert.Equal("DENY", post.Headers.GetValues("X-Frame-Options").Single());
        Assert.DoesNotContain("Previewing the ", await post.Content.ReadAsStringAsync());
        Assert.DoesNotContain("no-store", post.Headers.CacheControl?.ToString() ?? "");

        // HEAD is not a GET for this purpose: no preview headers either.
        var head = await owner.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/about?preview=healthcare"));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.False(head.Headers.Contains("X-Robots-Tag"));
        Assert.Equal("DENY", head.Headers.GetValues("X-Frame-Options").Single());
        Assert.DoesNotContain("no-store", head.Headers.CacheControl?.ToString() ?? "");

        // The same GET does preview, so the checks above are not passing for some other reason.
        var get = await owner.GetAsync("/about?preview=healthcare");
        Assert.Contains("noindex", get.Headers.GetValues("X-Robots-Tag").Single());
    }

    [Theory]
    [InlineData("/admin?preview=healthcare")]
    [InlineData("/admin/template?preview=healthcare")]
    public async Task TheAdminPages_IgnoreThePreviewParameter_AndStayUnframable(string path)
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();

        var response = await owner.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Previewing the ", html);
        Assert.DoesNotContain("data-template=\"healthcare\"", html);
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }
}
