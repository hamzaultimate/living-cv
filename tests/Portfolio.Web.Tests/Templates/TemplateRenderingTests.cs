using System.Net;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>Every public route, rendered by each template, shows that template's wording and none of the other's.</summary>
public class TemplateRenderingTests
{
    private static async Task<(HttpStatusCode Status, string Html)> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // ---- Software & IT ----

    [Fact]
    public async Task Software_RendersEveryPublicRoute_InItsOwnWording()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var (project, post) = DemoSite.FirstSlugs(app);
        var client = app.NewClient();

        var expectations = new (string Path, string[] Contains)[]
        {
            ("/", ["Case studies", "Selected work", "Testimonials", "Let's talk systems", "ASP.NET Core"]),
            ("/about", ["Currently"]),
            ("/experience", ["Where I've built", "outcome-focused"]),
            ("/projects", ["Case studies", "Read case study", "Mini engineering write-ups"]),
            ($"/projects/{project}", ["Case study", "Stack"]),
            ("/skills", ["The toolkit", "from silicon to cloud"]),
            ("/certifications", ["Credentials &amp; learning", "Earned"]),
            ("/blog", ["Writing", ".NET"]),
            ($"/blog/{post}", ["← blog"]),
            ("/talks", ["Talks &amp; Videos"]),
            ("/cv", ["Selected projects", "Certifications"]),
            ("/contact", ["Let's talk systems", "I read everything"]),
        };

        foreach (var (path, contains) in expectations)
        {
            var (status, html) = await GetAsync(client, path);
            Assert.True(status == HttpStatusCode.OK, $"{path} returned {(int)status}");
            Assert.DoesNotContain("data-template=", html);
            foreach (var text in contains) Assert.Contains(text, html);
            var visible = DemoSite.WithoutScripts(html);
            Assert.False(DemoSite.HealthcareWording.IsMatch(visible), $"{path} shows healthcare wording: {DemoSite.HealthcareWording.Match(visible).Value}");
        }

        var nav = (await GetAsync(client, "/")).Html;
        foreach (var label in new[] { ">about<", ">experience<", ">work<", ">skills<", ">certs<", ">blog<", ">talks<", ">cv<" })
            Assert.Contains(label, nav);
    }

    [Fact]
    public async Task Software_MissingItems_UseItsItemWords()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var client = app.NewClient();

        var project = await GetAsync(client, "/projects/no-such-project");
        var post = await GetAsync(client, "/blog/no-such-post");

        Assert.Equal(HttpStatusCode.NotFound, project.Status);
        Assert.Contains("Case study not found", project.Html);
        Assert.Contains("← Back to work", project.Html);
        Assert.Equal(HttpStatusCode.NotFound, post.Status);
        Assert.Contains("Post not found", post.Html);
        Assert.Contains("← Back to blog", post.Html);
    }

    [Theory]
    [InlineData("/no-such-page")]
    [InlineData("/projects-typo/x")]
    [InlineData("/setup")]
    [InlineData("/admin/no-such-page")]
    public async Task OtherNotFoundResponses_KeepTheirPlainEmpty404(string path)
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync(); // an owner exists, so /setup is closed

        // Signed out, /admin/* redirects to the login page as it always did; only the owner reaches the 404.
        foreach (var client in path.StartsWith("/admin") ? new[] { owner } : new[] { app.NewClient(), owner })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
            Assert.Null(response.Content.Headers.ContentType);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await app.NewClient().GetAsync("/img/missing.png")).StatusCode);
    }

    [Fact]
    public async Task TheNotFoundPage_IsA404ThatSearchEnginesAreToldToIgnore()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);

        var response = await app.NewClient().GetAsync("/not-found");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex,nofollow\" />", html);
        Assert.DoesNotContain("rel=\"canonical\"", html);
        Assert.DoesNotContain("og:url", html);
    }

    // ---- The same data in both templates ----

    [Fact]
    public async Task TheSameData_RendersCorrectlyInBothTemplates()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var (project, _) = DemoSite.FirstSlugs(app);
        var owner = await app.SignedInClientAsync();

        var softwareExperience = await GetAsync(owner, "/experience");
        var softwareProjects = await GetAsync(owner, "/projects");
        Assert.Equal(HttpStatusCode.Redirect, (await DemoSite.SwitchTemplateAsync(owner, "healthcare")).StatusCode);

        var healthcareExperience = await GetAsync(owner, "/experience");
        var healthcareProjects = await GetAsync(owner, "/projects");
        var healthcareDetail = await GetAsync(owner, $"/projects/{project}");
        var healthcareCv = await GetAsync(owner, "/cv");

        // The rows are the same data...
        foreach (var company in new[] { "Nimbus Systems", "Contoso Cloud" })
        {
            Assert.Contains(company, softwareExperience.Html);
            Assert.Contains(company, healthcareExperience.Html);
        }
        Assert.Contains("Telemetry", softwareProjects.Html);
        Assert.Contains("Telemetry", healthcareProjects.Html);
        Assert.Equal(HttpStatusCode.OK, healthcareDetail.Status);
        Assert.Contains("Contoso Cloud", healthcareCv.Html);

        // ...shown in each template's own words.
        Assert.Contains("Where I've built", softwareExperience.Html);
        Assert.DoesNotContain("Where I've built", healthcareExperience.Html);
        Assert.Contains("Clinical experience", healthcareExperience.Html);
        Assert.DoesNotContain("Clinical experience", softwareExperience.Html);
        Assert.Contains("Case studies", softwareProjects.Html);
        Assert.DoesNotContain("Case studies", healthcareProjects.Html);
        Assert.Contains("Research &amp; initiatives", healthcareProjects.Html);
        Assert.Contains("data-template=\"healthcare\"", healthcareProjects.Html);
    }

    // ---- Unknown template on the profile ----

    [Fact]
    public async Task AProfileWithAnUnknownTemplateKey_RendersTheSoftwareTemplate()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        _ = app.Services;
        using (var db = app.CreateDbContext())
        {
            var profile = new Portfolio.Data.Entities.SiteProfile { OwnerName = "Alex Rivera", BrandName = "Alex", TemplateKey = "does-not-exist", Url = "https://owner.example" };
            db.SiteProfiles.Add(profile);
            await db.SaveChangesAsync();
        }

        var (status, html) = await GetAsync(app.NewClient(), "/experience");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Where I've built", html);
        Assert.DoesNotContain("data-template=", html);
    }
}
