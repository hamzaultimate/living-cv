using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>Switching template from /admin: nothing is deleted, hidden sections disappear everywhere, and cached output follows at once.</summary>
public class TemplateSwitchTests
{
    private static async Task<string> TemplateKeyAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return (await db.SiteProfiles.SingleAsync()).TemplateKey;
    }

    private static IEnumerable<string> SitemapPaths(string xml) =>
        Regex.Matches(xml, "<loc>https://owner.example([^<]*)</loc>").Select(m => m.Groups[1].Value);

    private static IEnumerable<JsonElement> LdBlocks(string html) =>
        Regex.Matches(html, "<script type=\"application/ld&#x2B;json\">(.*?)</script>", RegexOptions.Singleline)
            .Select(m => JsonDocument.Parse(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).RootElement.Clone());

    [Fact]
    public async Task Switching_KeepsEveryRow_AndOnlyHidesWhatTheTemplateDoesNotUse()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();
        var (project, post) = DemoSite.FirstSlugs(app);
        _ = await owner.GetStringAsync("/"); // the first page view creates the profile row from configuration
        var before = await DemoSite.RowCountsAsync(app);
        Assert.All(before.Where(c => c.Key != "profiles"), c => Assert.True(c.Value > 0, $"The demo persona should have {c.Key}."));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/blog/{post}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/talks")).StatusCode);

        var switched = await DemoSite.SwitchTemplateAsync(owner, "healthcare");

        Assert.Equal(HttpStatusCode.Redirect, switched.StatusCode);
        Assert.Equal("healthcare", await TemplateKeyAsync(app));
        Assert.Equal(before, await DemoSite.RowCountsAsync(app));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/blog/{post}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/blog")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/talks")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/projects/{project}")).StatusCode);

        // Switching back brings the hidden content straight back: it was never dropped.
        Assert.Equal(HttpStatusCode.Redirect, (await DemoSite.SwitchTemplateAsync(owner, "software")).StatusCode);
        Assert.Equal(before, await DemoSite.RowCountsAsync(app));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/blog/{post}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/talks")).StatusCode);
    }

    [Fact]
    public async Task Switching_ChangesOnlyTheTemplateKey_OnTheProfile()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();
        _ = await owner.GetStringAsync("/");
        SiteProfile Snapshot() { using var db = app.CreateDbContext(); return db.SiteProfiles.AsNoTracking().Single(); }
        var before = Snapshot();

        await DemoSite.SwitchTemplateAsync(owner, "healthcare");
        var after = Snapshot();

        Assert.Equal("healthcare", after.TemplateKey);
        Assert.Equal(before.OwnerName, after.OwnerName);
        Assert.Equal(before.Summary, after.Summary);
        Assert.Equal(before.Hero.Headline, after.Hero.Headline);
        Assert.Equal(before.Metrics.Count, after.Metrics.Count);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.PhotoUrl, after.PhotoUrl);
    }

    [Fact]
    public async Task Sitemap_FollowsTheEnabledSections_ImmediatelyAfterASwitch()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();
        var anonymous = app.NewClient();
        var (project, post) = DemoSite.FirstSlugs(app);

        var before = SitemapPaths(await anonymous.GetStringAsync("/sitemap.xml")).ToList();
        Assert.Contains("/blog", before);
        Assert.Contains($"/blog/{post}", before);
        Assert.Contains("/talks", before);

        // The output is cached: a row added behind the app's back does not show up yet.
        using (var db = app.CreateDbContext())
        {
            db.Projects.Add(new Project { Title = "Late addition", Slug = "late-addition", Summary = "x" });
            await db.SaveChangesAsync();
        }
        Assert.DoesNotContain("/projects/late-addition", SitemapPaths(await anonymous.GetStringAsync("/sitemap.xml")));

        var version = app.Services.GetRequiredService<ISiteProfileProvider>().Version;
        await DemoSite.SwitchTemplateAsync(owner, "healthcare");

        // The switch evicts the cache and moves the profile version, so the very next request is rebuilt.
        Assert.NotEqual(version, app.Services.GetRequiredService<ISiteProfileProvider>().Version);
        var after = SitemapPaths(await anonymous.GetStringAsync("/sitemap.xml")).ToList();
        Assert.DoesNotContain("/blog", after);
        Assert.DoesNotContain("/talks", after);
        Assert.DoesNotContain(after, p => p.StartsWith("/blog/"));
        Assert.Contains("/projects/late-addition", after);
        Assert.Contains("/certifications", after);
        Assert.Contains($"/projects/{project}", after);
        Assert.Contains("/cv", after);
        Assert.Equal(new[] { "/", "/about", "/certifications", "/experience", "/skills", "/projects", "/cv", "/contact" }, after.Take(8));
    }

    [Fact]
    public async Task SoftwareSitemap_KeepsItsOriginalOrderAndEntries()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);

        var paths = SitemapPaths(await app.NewClient().GetStringAsync("/sitemap.xml")).ToList();

        Assert.Equal(new[] { "/", "/about", "/experience", "/projects", "/skills", "/certifications", "/blog", "/talks", "/cv", "/contact" }, paths.Take(10));
        Assert.Contains(paths, p => p.StartsWith("/projects/"));
        Assert.Contains(paths, p => p.StartsWith("/blog/"));
    }

    [Fact]
    public async Task JsonLd_FollowsTheTemplateAndItsEnabledSections()
    {
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, "healthcare");
        var client = app.NewClient();

        var home = LdBlocks(await client.GetStringAsync("/")).ToList();
        var person = home.Single(b => b.GetProperty("@type").GetString() == "Person");
        var credentials = person.GetProperty("hasCredential").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        Assert.Contains("Registered Nurse registration (sample)", credentials);
        Assert.Equal(5, credentials.Count);
        Assert.Equal("Example Nursing Council", person.GetProperty("hasCredential")[0].GetProperty("recognizedBy").GetProperty("name").GetString());

        // A breadcrumb exists only for pages that exist.
        var projects = LdBlocks(await client.GetStringAsync("/projects")).Single(b => b.GetProperty("@type").GetString() == "BreadcrumbList");
        Assert.Equal("Initiatives", projects.GetProperty("itemListElement")[1].GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/blog")).StatusCode);
    }

    [Fact]
    public async Task SoftwareHome_HasNoCredentialMarkup_SoItsStructuredDataIsUnchanged()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);

        var person = LdBlocks(await app.NewClient().GetStringAsync("/")).Single(b => b.GetProperty("@type").GetString() == "Person");

        Assert.False(person.TryGetProperty("hasCredential", out _));
    }

    [Fact]
    public async Task TheOwnersSectionSettings_HideASectionEverywhere()
    {
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, "healthcare");
        using (var db = app.CreateDbContext())
        {
            var profile = await db.SiteProfiles.SingleAsync();
            profile.Sections = [new SectionSetting { Key = "certifications", Enabled = false }, new SectionSetting { Key = "skills", Label = "abilities" }];
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        var client = app.NewClient();

        var home = await client.GetStringAsync("/");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/certifications")).StatusCode);
        Assert.DoesNotContain("href=\"/certifications\"", home);
        Assert.DoesNotContain("hasCredential", home);
        Assert.DoesNotContain("id=\"credentials\"", home);
        Assert.DoesNotContain("/certifications", SitemapPaths(await client.GetStringAsync("/sitemap.xml")));
        Assert.Contains(">abilities<", home);

        var cv = await client.GetStringAsync("/cv");
        Assert.DoesNotContain("Licences &amp; registrations", cv);
        Assert.Contains("Clinical experience", cv);
    }

    [Fact]
    public async Task ASectionSetting_CannotEnableASectionTheTemplateDoesNotHave()
    {
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, "healthcare");
        using (var db = app.CreateDbContext())
        {
            var profile = await db.SiteProfiles.SingleAsync();
            profile.Sections = [new SectionSetting { Key = "blog", Enabled = true }, new SectionSetting { Key = "talks", Enabled = true }];
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();

        Assert.Equal(HttpStatusCode.NotFound, (await app.NewClient().GetAsync("/blog")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.NewClient().GetAsync("/talks")).StatusCode);
    }

    // ---- The switch form itself ----

    [Theory]
    [InlineData("")]
    [InlineData("no-such-template")]
    [InlineData("../software")]
    [InlineData("healthcare;drop table")]
    public async Task SwitchingToAKeyOutsideTheCatalog_ChangesNothing(string key)
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        _ = await owner.GetStringAsync("/");
        var before = await DemoSite.RowCountsAsync(app);

        var response = await DemoSite.SwitchTemplateAsync(owner, key);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("software", await TemplateKeyAsync(app));
        Assert.Equal(before, await DemoSite.RowCountsAsync(app));
        Assert.Contains("not available", await owner.GetStringAsync("/admin/template"));
    }

    [Fact]
    public async Task SwitchingWithoutAnAntiforgeryToken_IsRefused_AndGetNeverChangesAnything()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var noToken = await AdminApp.PostFormAsync(owner, "/admin/template", null, new Dictionary<string, string> { ["Key"] = "healthcare" });
        var get = await owner.GetAsync("/admin/template?Key=healthcare&show=healthcare");

        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("software", await TemplateKeyAsync(app));
    }

    [Fact]
    public async Task TheSwitcher_ShowsTheGuidanceForSensitiveTemplates_AndWhatStaysHidden()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();

        var html = System.Net.WebUtility.HtmlDecode(await owner.GetStringAsync("/admin/template?show=healthcare"));

        Assert.Contains("Never publish patient-identifiable information", html);
        Assert.Contains("Hidden but kept:", html);
        Assert.Contains("Blog posts (", html);
        Assert.Contains("Talks (", html);
        Assert.Contains("src=\"/?preview=healthcare\"", html);
        Assert.Contains("In use", html);
        Assert.Contains("Use Healthcare", html);
        Assert.DoesNotContain("Use Software", html);
    }

    [Fact]
    public async Task TheSwitcher_IgnoresUntrustedShowAndTabValues()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync("/admin/template?show=%22%3E%3Cscript%3E&tab=//evil.example/x");

        Assert.Contains("src=\"/?preview=software\"", html);
        Assert.DoesNotContain("evil.example", html);
        Assert.DoesNotContain("<script>", html);

        var tabs = await owner.GetStringAsync("/admin/template?show=healthcare&tab=/blog");
        Assert.Contains("src=\"/?preview=healthcare\"", tabs);
    }

    [Fact]
    public async Task OnlyThisPage_MayFrameThePublicSite()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var template = await owner.GetAsync("/admin/template");
        var profile = await owner.GetAsync("/admin/profile");

        Assert.Contains("frame-src 'self'", string.Join(";", template.Headers.GetValues("Content-Security-Policy")));
        Assert.DoesNotContain("frame-src", string.Join(";", profile.Headers.GetValues("Content-Security-Policy")));
    }
}
