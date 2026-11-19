using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>
/// The checks for every template on the credentials archetype (registration and qualifications first, then practice), through data only. Driven by <see cref="Keys"/> and
/// <see cref="Wordings"/>: a further template on this archetype is covered by adding its key and its wording entry here.
/// </summary>
public class CredentialsArchetypeTests
{
    /// <summary>Every template on the credentials archetype.</summary>
    public static readonly string[] Keys = ["healthcare"];

    public static TheoryData<string> KeyData => new(Keys);

    public static TheoryData<string, string> DisabledSectionData
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var key in Keys)
                foreach (var path in new[] { "/blog", "/blog/anything", "/talks" }) data.Add(key, path);
            return data;
        }
    }

    /// <summary>
    /// What one template's own pages must say. Each string is wording from that template's manifest or starter, so a change of wording there is a change here too.
    /// <see cref="Foreign"/> is wording that belongs to another profession and must never appear on this template's pages (null when there is none to guard).
    /// </summary>
    internal sealed record Wording(
        string[] Home, string[] About, string[] Experience, string[] Projects, string DetailSlug, string[] Detail,
        string[] Skills, string[] Certifications, string[] Cv, string[] Contact,
        string[] NavLabels, string FooterNote, string MissingBackLink,
        (string Path, string Text)[] EmptyPages, string EmptyHome,
        string LongTitle, string GuidancePhrase, Regex? Foreign);

    internal static readonly Dictionary<string, Wording> Wordings = new()
    {
        ["healthcare"] = new Wording(
            Home: ["Licences &amp; registrations", "Clinical experience", "Clinical skills", "Research &amp; initiatives", "References", "Sample profile (fictional)"],
            About: ["Safe, compassionate critical care"],
            Experience: ["Clinical experience", "where I have practised"],
            Projects: ["Research &amp; initiatives", "Read initiative"],
            DetailSlug: "early-mobility-programme",
            Detail: ["Initiative", "Tools &amp; methods", "Aim", "Method"],
            Skills: ["Clinical skills", "Procedures", "Documentation"],
            Certifications: ["Licences, registrations &amp; training", "Current", "Registration"],
            Cv: ["Clinical experience", "Licences &amp; registrations", "Research &amp; initiatives"],
            Contact: ["Please do not send patient information", "roles, referrals or collaboration"],
            NavLabels: [">about<", ">registrations<", ">experience<", ">skills<", ">initiatives<", ">cv<"],
            FooterNote: "contains no patient information",
            MissingBackLink: "← Back to research &amp; initiatives",
            EmptyPages:
            [
                ("/projects", "No initiatives yet"),
                ("/experience", "No roles have been added yet."),
                ("/skills", "No skills have been added yet."),
                ("/certifications", "No registrations yet"),
            ],
            EmptyHome: "Initiatives coming soon",
            LongTitle: "Advanced Clinical Practitioner in Paediatric Intensive Care and Retrieval Medicine",
            GuidancePhrase: "Never publish patient-identifiable information",
            Foreign: null),
    };

    internal static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "Templates");
    internal static TemplateDefinition Definition(string key) => new TemplateCatalog(TemplatesDir).Get(key);

    private static async Task<(HttpStatusCode Status, string Html)> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------- the public pages, in the template's own wording

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task EveryPublicRoute_RendersInTheTemplatesOwnWording_WithNoSoftwareWording(string key)
    {
        var w = Wordings[key];
        var theme = Definition(key).Theme.Default;
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);
        var client = app.NewClient();

        var expectations = new (string Path, string[] Contains)[]
        {
            ("/", w.Home),
            ("/about", w.About),
            ("/experience", w.Experience),
            ("/projects", w.Projects),
            ($"/projects/{w.DetailSlug}", w.Detail),
            ("/skills", w.Skills),
            ("/certifications", w.Certifications),
            ("/cv", w.Cv),
            ("/contact", w.Contact),
        };

        foreach (var (path, contains) in expectations)
        {
            var (status, html) = await GetAsync(client, path);
            Assert.True(status == HttpStatusCode.OK, $"{path} returned {(int)status}");
            Assert.Contains($"<html lang=\"en\" data-template=\"{theme}\">", html);
            foreach (var text in contains) Assert.Contains(text, html);
            var visible = DemoSite.WithoutScripts(html);
            Assert.False(DemoSite.SoftwareWording.IsMatch(visible), $"{path} shows software wording: {DemoSite.SoftwareWording.Match(visible).Value}");
            if (w.Foreign is not null) Assert.False(w.Foreign.IsMatch(visible), $"{path} shows another profession's wording: {w.Foreign.Match(visible).Value}");
        }

        var nav = (await GetAsync(client, "/")).Html;
        foreach (var label in w.NavLabels) Assert.Contains(label, nav);
        Assert.DoesNotContain(">blog<", nav);
        Assert.DoesNotContain(">talks<", nav);
        Assert.DoesNotContain("ASP.NET", nav);
        Assert.Contains(w.FooterNote, nav);
    }

    [Theory]
    [MemberData(nameof(DisabledSectionData))]
    public async Task SectionsTheTemplateDoesNotUse_AnswerNotFound(string key, string path)
    {
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);

        var (status, html) = await GetAsync(app.NewClient(), path);

        Assert.Equal(HttpStatusCode.NotFound, status);
        // A styled page that is the same for every disabled section, with nothing that says a section exists or is switched off.
        var (otherStatus, otherHtml) = await GetAsync(app.NewClient(), path == "/talks" ? "/blog" : "/talks");
        Assert.Equal(HttpStatusCode.NotFound, otherStatus);
        Assert.Contains("<html", html);
        Assert.Contains("Page not found", html);
        Assert.Equal(DemoSite.Normalise(otherHtml), DemoSite.Normalise(html));
        var body = DemoSite.WithoutScripts(html);
        Assert.DoesNotMatch(@"(?i)blog|talk|article|presentation|disabled", body.Substring(body.IndexOf("<main", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task ProjectImages_UseNeutralWording(string key)
    {
        var w = Wordings[key];
        var title = new TemplateCatalog(TemplatesDir).GetStarter(key)!.Projects.Single(p => p.Slug == w.DetailSlug).Title;
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);
        using (var db = app.CreateDbContext())
        {
            // Give the first initiative a cover and a gallery with and without captions, whatever pictures the starter carries.
            var project = await db.Projects.SingleAsync(p => p.Slug == w.DetailSlug);
            project.HeroImage = "/img/projects/_placeholder.svg";
            project.HeroImageAlt = null;
            project.Screenshots = [new ProjectImage { Url = "/img/projects/_placeholder.svg", Caption = "Run chart" }, new ProjectImage { Url = "/img/projects/_placeholder.svg" }];
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        var client = app.NewClient();

        foreach (var path in new[] { "/", "/projects", $"/projects/{w.DetailSlug}" })
        {
            var (status, html) = await GetAsync(client, path);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.DoesNotContain("screenshot", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<img", html);
        }
        var detail = (await GetAsync(client, $"/projects/{w.DetailSlug}")).Html;
        Assert.Contains("aria-label=\"Images\"", detail);
        Assert.Contains("aria-label=\"Open image: Run chart\"", detail);
        Assert.Contains("aria-label=\"Open image\"", detail);
        Assert.Contains("alt=\"Run chart\"", detail);
        Assert.Contains($"alt=\"{title} image\"", detail);
        Assert.Contains($"alt=\"{title} image\"", (await GetAsync(client, "/projects")).Html);
        Assert.Contains($"alt=\"{title} image\"", (await GetAsync(client, "/")).Html);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task MetaDescriptions_StayWithin160Characters_ForALongNameAndTitle(string key)
    {
        var w = Wordings[key];
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);
        using (var db = app.CreateDbContext())
        {
            var profile = await db.SiteProfiles.SingleAsync();
            profile.OwnerName = "Dr Alexandra Wilhelmina Featherstonehaugh-Montgomery";
            profile.JobTitle = w.LongTitle;
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
        Assert.StartsWith($"Dr Alexandra Wilhelmina Featherstonehaugh-Montgomery, {w.LongTitle[..30]}", home);
        Assert.EndsWith("…", home);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task AMissingInitiative_UsesTheTemplatesItemWord(string key)
    {
        var w = Wordings[key];
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);

        var (status, html) = await GetAsync(app.NewClient(), "/projects/no-such-initiative");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("Initiative not found", html);
        Assert.Contains(w.MissingBackLink, html);
        Assert.DoesNotContain("Case study", html);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task AnEmptySite_ShowsTheTemplatesEmptyStates_NotSoftwareOnes(string key)
    {
        var w = Wordings[key];
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);
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

        foreach (var (path, text) in w.EmptyPages)
        {
            var (status, html) = await GetAsync(client, path);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Contains(text, html);
            var visible = DemoSite.WithoutScripts(html);
            Assert.False(DemoSite.SoftwareWording.IsMatch(visible), $"{path}: {DemoSite.SoftwareWording.Match(visible).Value}");
            if (w.Foreign is not null) Assert.False(w.Foreign.IsMatch(visible), $"{path}: {w.Foreign.Match(visible).Value}");
        }
        var home = await GetAsync(client, "/");
        Assert.Contains(w.EmptyHome, home.Html);
        Assert.DoesNotMatch(DemoSite.SoftwareWording, DemoSite.WithoutScripts(home.Html));
    }
}
