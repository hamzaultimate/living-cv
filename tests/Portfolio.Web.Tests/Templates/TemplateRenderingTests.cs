using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
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

    // ---- Healthcare ----

    [Fact]
    public async Task Healthcare_RendersEveryPublicRoute_InItsOwnWording_WithNoSoftwareWording()
    {
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, "healthcare");
        var client = app.NewClient();

        var expectations = new (string Path, string[] Contains)[]
        {
            ("/", ["Licences &amp; registrations", "Clinical experience", "Clinical skills", "Research &amp; initiatives", "References", "Sample profile (fictional)"]),
            ("/about", ["Safe, compassionate critical care"]),
            ("/experience", ["Clinical experience", "where I have practised"]),
            ("/projects", ["Research &amp; initiatives", "Read initiative"]),
            ("/projects/early-mobility-programme", ["Initiative", "Tools &amp; methods", "Aim", "Method"]),
            ("/skills", ["Clinical skills", "Procedures", "Documentation"]),
            ("/certifications", ["Licences, registrations &amp; training", "Current", "Registration"]),
            ("/cv", ["Clinical experience", "Licences &amp; registrations", "Research &amp; initiatives"]),
            ("/contact", ["Please do not send patient information", "roles, referrals or collaboration"]),
        };

        foreach (var (path, contains) in expectations)
        {
            var (status, html) = await GetAsync(client, path);
            Assert.True(status == HttpStatusCode.OK, $"{path} returned {(int)status}");
            Assert.Contains("<html lang=\"en\" data-template=\"healthcare\">", html);
            foreach (var text in contains) Assert.Contains(text, html);
            var visible = DemoSite.WithoutScripts(html);
            Assert.False(DemoSite.SoftwareWording.IsMatch(visible), $"{path} shows software wording: {DemoSite.SoftwareWording.Match(visible).Value}");
        }

        var nav = (await GetAsync(client, "/")).Html;
        foreach (var label in new[] { ">about<", ">registrations<", ">experience<", ">skills<", ">initiatives<", ">cv<" })
            Assert.Contains(label, nav);
        Assert.DoesNotContain(">blog<", nav);
        Assert.DoesNotContain(">talks<", nav);
        Assert.DoesNotContain("ASP.NET", nav);
        Assert.Contains("contains no patient information", nav);
    }

    [Theory]
    [InlineData("/blog")]
    [InlineData("/blog/anything")]
    [InlineData("/talks")]
    public async Task Healthcare_SectionsItDoesNotUse_AnswerNotFound(string path)
    {
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, "healthcare");

        var (status, html) = await GetAsync(app.NewClient(), path);

        Assert.Equal(HttpStatusCode.NotFound, status);
        // The same styled page as any unknown address, with nothing that says a section exists or is switched off.
        var (unknownStatus, unknownHtml) = await GetAsync(app.NewClient(), "/no-such-page");
        Assert.Equal(HttpStatusCode.NotFound, unknownStatus);
        Assert.Contains("<html", html);
        Assert.Contains("Page not found", html);
        Assert.Equal(DemoSite.Normalise(unknownHtml), DemoSite.Normalise(html));
        var body = DemoSite.WithoutScripts(html);
        Assert.DoesNotMatch(@"(?i)blog|talk|article|presentation|disabled", body.Substring(body.IndexOf("<main", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("/no-such-page")]
    [InlineData("/not-found")]
    public async Task AnUnknownAddress_AnswersTheStyledNotFoundPage_WithA404(string path)
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);

        var response = await app.NewClient().GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex,nofollow\" />", html);
        Assert.Equal(HttpStatusCode.NotFound, (await app.NewClient().GetAsync("/img/missing.png")).StatusCode);
    }

    [Fact]
    public async Task Healthcare_ProjectImages_UseNeutralWording()
    {
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, "healthcare");
        using (var db = app.CreateDbContext())
        {
            // The starter initiatives have no images, so give one a cover and a gallery with and without captions.
            var project = await db.Projects.SingleAsync(p => p.Slug == "early-mobility-programme");
            project.HeroImage = "/img/projects/_placeholder.svg";
            project.Screenshots = [new ProjectImage { Url = "/img/projects/_placeholder.svg", Caption = "Run chart" }, new ProjectImage { Url = "/img/projects/_placeholder.svg" }];
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        var client = app.NewClient();

        foreach (var path in new[] { "/", "/projects", "/projects/early-mobility-programme" })
        {
            var (status, html) = await GetAsync(client, path);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.DoesNotContain("screenshot", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<img", html);
        }
        var detail = (await GetAsync(client, "/projects/early-mobility-programme")).Html;
        Assert.Contains("aria-label=\"Images\"", detail);
        Assert.Contains("aria-label=\"Open image: Run chart\"", detail);
        Assert.Contains("aria-label=\"Open image\"", detail);
        Assert.Contains("alt=\"Run chart\"", detail);
        Assert.Contains("alt=\"Early mobility programme (sample) image\"", detail);
        Assert.Contains("alt=\"Early mobility programme (sample) image\"", (await GetAsync(client, "/projects")).Html);
        Assert.Contains("alt=\"Early mobility programme (sample) image\"", (await GetAsync(client, "/")).Html);
    }

    [Fact]
    public async Task Healthcare_MetaDescriptions_StayWithin160Characters_ForALongNameAndTitle()
    {
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, "healthcare");
        using (var db = app.CreateDbContext())
        {
            var profile = await db.SiteProfiles.SingleAsync();
            profile.OwnerName = "Dr Alexandra Wilhelmina Featherstonehaugh-Montgomery";
            profile.JobTitle = "Advanced Clinical Practitioner in Paediatric Intensive Care and Retrieval Medicine";
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        var client = app.NewClient();

        foreach (var path in new[] { "/", "/about", "/experience", "/skills", "/certifications", "/projects", "/contact", "/cv" })
        {
            var html = (await GetAsync(client, path)).Html;
            var description = WebUtility.HtmlDecode(Regex.Match(html, "<meta name=\"description\" content=\"([^\"]*)\"").Groups[1].Value);
            Assert.False(string.IsNullOrWhiteSpace(description), $"{path} has no description");
            Assert.True(description.Length <= 160, $"{path} description is {description.Length} characters: {description}");
            Assert.DoesNotMatch(@"\b(is a|is an)\b", description);
        }
        var home = WebUtility.HtmlDecode(Regex.Match((await GetAsync(client, "/")).Html, "<meta name=\"description\" content=\"([^\"]*)\"").Groups[1].Value);
        Assert.StartsWith("Dr Alexandra Wilhelmina Featherstonehaugh-Montgomery, Advanced Clinical Practitioner", home);
        Assert.EndsWith("…", home);
    }

    [Fact]
    public async Task Healthcare_MissingInitiative_UsesItsItemWord()
    {
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, "healthcare");

        var (status, html) = await GetAsync(app.NewClient(), "/projects/no-such-initiative");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("Initiative not found", html);
        Assert.Contains("← Back to research &amp; initiatives", html);
        Assert.DoesNotContain("Case study", html);
    }

    [Fact]
    public async Task Healthcare_EmptySite_ShowsItsEmptyStates_NotSoftwareOnes()
    {
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, "healthcare");
        using (var db = app.CreateDbContext())
        {
            db.Projects.RemoveRange(db.Projects);
            db.Experiences.RemoveRange(db.Experiences);
            db.Skills.RemoveRange(db.Skills);
            db.Certifications.RemoveRange(db.Certifications);
            db.Testimonials.RemoveRange(db.Testimonials);
            await db.SaveChangesAsync();
        }
        var client = app.NewClient();

        foreach (var (path, text) in new[]
                 {
                     ("/projects", "No initiatives yet"),
                     ("/experience", "No roles have been added yet."),
                     ("/skills", "No skills have been added yet."),
                     ("/certifications", "No registrations yet"),
                 })
        {
            var (status, html) = await GetAsync(client, path);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Contains(text, html);
            Assert.False(DemoSite.SoftwareWording.IsMatch(DemoSite.WithoutScripts(html)), $"{path}: {DemoSite.SoftwareWording.Match(DemoSite.WithoutScripts(html)).Value}");
        }
        var home = await GetAsync(client, "/");
        Assert.Contains("Initiatives coming soon", home.Html);
        Assert.DoesNotMatch(DemoSite.SoftwareWording, DemoSite.WithoutScripts(home.Html));
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
