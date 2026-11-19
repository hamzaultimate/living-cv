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
    public static readonly string[] Keys = ["healthcare", "education"];

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
        ["education"] = new Wording(
            Home: ["Teaching qualifications &amp; registration", "Teaching experience", "Teaching skills", "Curriculum &amp; initiatives", "References", "Sample profile (fictional)"],
            About: ["Careful planning and inclusive classrooms"],
            Experience: ["Teaching experience", "where I have taught"],
            Projects: ["Curriculum &amp; initiatives", "Read initiative"],
            DetailSlug: "reading-for-pleasure-programme",
            Detail: ["Initiative", "Methods &amp; resources", "Aim", "Approach"],
            Skills: ["Teaching skills", "Pastoral and inclusion", "Technology"],
            Certifications: ["Qualifications, registration &amp; training", "Current", "Qualifications"],
            Cv: ["Teaching experience", "Qualifications &amp; registration", "Curriculum &amp; initiatives"],
            Contact: ["Please do not send pupil information", "teaching roles, mentoring or collaboration"],
            NavLabels: [">about<", ">qualifications<", ">experience<", ">skills<", ">initiatives<", ">cv<"],
            FooterNote: "contains no pupil information",
            MissingBackLink: "← Back to curriculum &amp; initiatives",
            EmptyPages:
            [
                ("/projects", "No initiatives yet"),
                ("/experience", "No roles have been added yet."),
                ("/skills", "No skills have been added yet."),
                ("/certifications", "No qualifications yet"),
            ],
            EmptyHome: "Initiatives coming soon",
            LongTitle: "Advanced Skills Practitioner in Inclusive Education and Learning Support",
            GuidancePhrase: "Never publish pupil-identifiable information",
            Foreign: new Regex(@"(?i)clinical|patients?|nurs(?:e|es|ing)|healthcare|hospital|wards?", RegexOptions.Compiled)),
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

    // ---------------------------------------------------------------- the manifest, theme, starter and thumbnail

    private static string ThemeCss(string key) => File.ReadAllText(Path.Combine(TemplateDirs.Web, "Styles", "themes", Definition(key).Theme.Default! + ".css"));

    [Theory]
    [MemberData(nameof(KeyData))]
    public void ATemplateOnTheCredentialsArchetype_HasItsThemeStarterAndThumbnail(string key)
    {
        var catalog = new TemplateCatalog(TemplatesDir);
        var template = catalog.Get(key);

        Assert.Equal(key, template.Key);
        Assert.Equal(Archetypes.Credentials, template.Archetype);
        Assert.Contains(Archetypes.Credentials, Archetypes.Implemented);
        Assert.True(template.Sensitive, $"{key} must be sensitive: its content can identify people other than the owner");
        Assert.False(string.IsNullOrWhiteSpace(template.Guidance));
        Assert.True(template.Seo.CredentialsJsonLd, $"{key} must publish held qualifications in its structured data");
        Assert.NotNull(catalog.GetPreviewImagePath(key));
        Assert.NotNull(catalog.GetStarter(key));
        Assert.Empty(TemplateValidator.Validate(template, key));

        var css = ThemeCss(key);
        var theme = template.Theme.Default!;
        Assert.Contains($":root[data-template=\"{theme}\"][data-theme=\"light\"]", css);
        Assert.Contains($":root[data-template=\"{theme}\"][data-theme=\"dark\"]", css);
        foreach (var token in new[] { "--c-bg", "--c-surface", "--c-surface-2", "--c-border", "--c-text", "--c-muted", "--c-accent", "--c-accent-strong" })
            Assert.Equal(2, Regex.Matches(css, Regex.Escape(token) + ":").Count); // once for dark, once for light
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void EveryCredentialsPreset_HidesTheTalksYouTubeField_BecauseNoLayoutPlaysVideoForIt(string key)
    {
        Assert.True(Definition(key).Editor.Talks.Fields.TryGetValue("youtubeId", out var field), $"{key} does not mention the youtubeId field");
        Assert.True(field!.Hidden, $"{key} must hide the YouTube field");
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void TheStarterContent_IsFictional_UsesOnlyBundledPictures_AndNamesNoPerson(string key)
    {
        var starter = new TemplateCatalog(TemplatesDir).GetStarter(key)!;

        Assert.True(starter.Fictional);
        Assert.Contains("fictional", starter.Profile.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("example.org", starter.Profile.Email);
        Assert.NotEmpty(starter.Projects);
        Assert.Contains(starter.Projects, p => p.Featured);
        Assert.All(starter.Projects, p => Assert.Contains("(sample)", p.Title));
        Assert.All(starter.Experiences, e => Assert.Contains("fictional", e.Company, StringComparison.OrdinalIgnoreCase));
        foreach (var name in starter.Experiences.Select(e => e.Company.Replace("(fictional)", "", StringComparison.OrdinalIgnoreCase).Trim())
                     .Concat(starter.Projects.Select(p => (p.Org ?? "").Replace("(fictional)", "", StringComparison.OrdinalIgnoreCase).Trim())))
            ResultsArchetypeTests.AssertNoLegalFormSuffix(name);
        Assert.All(starter.Projects, p => Assert.All(p.Links, l => Assert.StartsWith("https://example.org/", l.Url)));

        // Pictures: bundled under the template's own folder, described, small, and nothing that needs a font, a script or a download.
        var images = new List<string>();
        foreach (var p in starter.Projects.Where(p => !string.IsNullOrWhiteSpace(p.HeroImage)))
        {
            Assert.False(string.IsNullOrWhiteSpace(p.HeroImageAlt), $"{p.Slug} cover has no description");
            images.Add(p.HeroImage!);
            foreach (var shot in p.Screenshots)
            {
                Assert.False(string.IsNullOrWhiteSpace(shot.Caption), $"{p.Slug} has a picture without a description");
                images.Add(shot.Url);
            }
        }
        foreach (var url in images)
        {
            Assert.StartsWith($"/img/starter/{key}/", url);
            var file = Path.Combine(TemplateDirs.Web, "wwwroot", url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(file), $"{url} is not bundled under wwwroot");
            Assert.True(new FileInfo(file).Length < 60 * 1024, $"{url} is larger than a starter placeholder should be");
            Assert.DoesNotMatch(@"(?i)<script|<text|<image|https?://(?!www\.w3\.org)", File.ReadAllText(file));
        }
        Assert.Equal(images.Count, images.Distinct().Count());

        // No entry names a pupil, student or child: a heuristic for a capitalised name straight after one of those words.
        var text = File.ReadAllText(Path.Combine(TemplatesDir, key, "starter.json"));
        Assert.DoesNotMatch(@"\b(?:[Pp]upil|[Ss]tudent|[Cc]hild|[Ll]earner)s?\s+(?:named\s+)?[A-Z][a-z]{2,}\b", text);
    }

    // ---------------------------------------------------------------- structured data and editor guidance

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task OnlyHeldQualifications_AreListedAsCredentials_InThePersonMarkup(string key)
    {
        var starter = new TemplateCatalog(TemplatesDir).GetStarter(key)!;
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);

        var home = await app.NewClient().GetStringAsync("/");

        Assert.Contains("hasCredential", home);
        var markup = home[home.IndexOf("hasCredential", StringComparison.Ordinal)..];
        markup = markup[..markup.IndexOf("</script>", StringComparison.Ordinal)];
        var held = starter.Certifications.Where(c => c.Status == CertificationStatus.Earned).ToList();
        var notHeld = starter.Certifications.Where(c => c.Status != CertificationStatus.Earned).ToList();
        Assert.NotEmpty(held);
        Assert.NotEmpty(notHeld); // the starter must have a course in progress or planned, or this proves nothing
        foreach (var c in held) Assert.Contains(System.Text.Json.JsonEncodedText.Encode(c.Name).ToString(), markup);
        foreach (var c in notHeld) Assert.DoesNotContain(System.Text.Json.JsonEncodedText.Encode(c.Name).ToString(), markup);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task TheGuidance_IsShownOnEveryEditor_OfASensitiveCredentialsTemplate(string key)
    {
        var phrase = Wordings[key].GuidancePhrase;
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, key);
        await ContentFlow.SeedRowsAsync(app);

        foreach (var path in new[]
                 {
                     "/admin/content", ContentFlow.Experience, ContentFlow.Experience + "/edit", ContentFlow.Skills, ContentFlow.Skills + "/edit",
                     ContentFlow.Projects, ContentFlow.Projects + "/edit", ContentFlow.Certifications, ContentFlow.Certifications + "/edit",
                     ContentFlow.Talks, ContentFlow.Talks + "/edit", ContentFlow.Testimonials, ContentFlow.Testimonials + "/edit",
                     ContentFlow.Blog, ContentFlow.Blog + "/edit",
                 })
        {
            var html = WebUtility.HtmlDecode(await owner.GetStringAsync(path));
            Assert.Contains(phrase, html);
            Assert.Contains("Before you publish", html);
        }
    }

    // ---------------------------------------------------------------- Education

    [Fact]
    public void Education_ListsItsSections_QualificationsFirst_WithArticlesAndWorkshopsOff()
    {
        var template = Definition("education");

        Assert.Equal("School teacher", template.Profession);
        Assert.Equal(new[] { "about", "certifications", "experience", "skills", "projects", "blog", "talks", "testimonials", "contact" }, template.Sections.Select(s => s.Key));
        Assert.Equal(new[] { "blog", "talks" }, template.Sections.Where(s => !s.Enabled).Select(s => s.Key));
        Assert.Equal("qualifications", template.Sections.Single(s => s.Key == "certifications").ShortLabel);
        Assert.Equal("Initiative", template.Sections.Single(s => s.Key == "projects").ItemLabel);
        Assert.Equal(new[] { "Teaching and learning", "Curriculum and assessment", "Pastoral and inclusion", "Technology", "Languages" }, template.SkillGroups);
        Assert.Contains("Never publish pupil-identifiable information", template.Guidance);
    }

    [Fact]
    public async Task Education_MenuFollowsTheSectionOrder()
    {
        using var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, "education");

        var nav = (await GetAsync(app.NewClient(), "/")).Html;

        var positions = new[] { ">about<", ">qualifications<", ">experience<", ">skills<", ">initiatives<", ">cv<" }.Select(l => nav.IndexOf(l, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    [Fact]
    public void Education_UsesItsOwnDistinctTheme_NotHealthcares()
    {
        var education = Definition("education");
        var healthcare = Definition("healthcare");

        Assert.NotEqual(healthcare.Theme.Default, education.Theme.Default);
        Assert.NotEqual(healthcare.Theme.Accents.Select(a => a.Key), education.Theme.Accents.Select(a => a.Key));
        Assert.Equal(new[] { "green", "terracotta", "blue", "gold" }, education.Theme.Accents.Select(a => a.Key));
        Assert.NotEqual(healthcare.Theme.ThemeColor, education.Theme.ThemeColor);
    }
}
