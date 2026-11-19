using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;
using G = Portfolio.Web.Tests.Templates.GalleryArchetypeTests;

namespace Portfolio.Web.Tests.Templates;

/// <summary>
/// Every template on the showcase layout other than Software & IT (which is guarded by the byte-for-byte parity snapshots). Driven by <see cref="Keys"/>: a further showcase
/// template is covered by adding its key here. The admin and cross-cutting paths run from <see cref="ArchetypeTemplateFlowTests"/>, which includes these keys.
/// </summary>
public class ShowcaseArchetypeTests
{
    /// <summary>Every shipped showcase template except Software & IT.</summary>
    public static readonly string[] Keys = ["data-analytics", "product-management"];

    public static TheoryData<string> KeyData => new(Keys);

    public static TheoryData<string, string> KeyAndContent
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var key in Keys) foreach (var content in new[] { "blank", "sample", "full" }) data.Add(key, content);
            return data;
        }
    }

    private static string ItemOf(TemplateDefinition t) => t.Sections.Single(s => s.Key == "projects").ItemLabel!;

    // ---------------------------------------------------------------- the manifest and its files

    [Theory]
    [MemberData(nameof(KeyData))]
    public void AShowcaseTemplate_HasItsThemeStarterAndThumbnail(string key)
    {
        var template = G.Definition(key);
        var catalog = new TemplateCatalog(G.TemplatesDir);

        Assert.Equal(key, template.Key);
        Assert.Equal(Archetypes.Showcase, template.Archetype);
        Assert.True(File.Exists(Path.Combine(G.Layouts, "showcase", "_Home.cshtml")));
        Assert.Equal(key, template.Theme.Default);
        Assert.True(File.Exists(Path.Combine(TemplateDirs.Web, "Styles", "themes", key + ".css")));
        Assert.NotNull(catalog.GetStarter(key));
        Assert.NotNull(catalog.GetPreviewImagePath(key));
        Assert.Equal(4, template.Theme.Accents.Count);
        Assert.Contains(template.Sections, s => s.Key == SectionRegistry.Projects && !string.IsNullOrWhiteSpace(s.ItemLabel));
        Assert.False(Archetypes.ShowsClientsOnHome(template.Archetype));
        Assert.False(template.Sensitive);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void TheMenuOrder_IsTheShowcaseOrder_WithTalksOffAndItsYouTubeFieldHidden(string key)
    {
        var template = G.Definition(key);

        Assert.Equal(["about", "experience", "projects", "skills", "certifications", "blog", "talks", "testimonials", "contact"], template.Sections.Select(s => s.Key));
        Assert.Contains(template.Sections, s => s.Key == "talks" && !s.Enabled);
        Assert.True(template.Editor.Talks.Fields.TryGetValue("youtubeId", out var field) && field!.Hidden, $"{key} must hide the YouTube field");
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void TheThumbnail_IsARealScreenshotOfTheRightSize(string key)
    {
        var path = new TemplateCatalog(G.TemplatesDir).GetPreviewImagePath(key)!;
        var bytes = File.ReadAllBytes(path);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
        Assert.Equal(1200, (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19]);
        Assert.Equal(800, (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23]);
        Assert.True(bytes.Length > 30_000, "a screenshot of a rendered page is not this small");
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void TheStarterContent_IsFictional_UsesOnlyBundledPictures_AndDescribesEveryOne(string key)
    {
        var starter = new TemplateCatalog(G.TemplatesDir).GetStarter(key)!;

        Assert.True(starter.Fictional);
        Assert.Contains("fictional", starter.Profile.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("example.org", starter.Profile.Email);
        Assert.True(starter.Profile.Metrics.Count >= 3, "the metrics row needs at least three figures");
        Assert.True(starter.Projects.Count(p => p.Featured) >= 3, "the home page needs at least three featured projects");
        Assert.Contains(starter.Projects, p => !p.Featured);
        Assert.Contains(starter.Projects, p => p.Screenshots.Count > 0);
        Assert.All(starter.Projects, p => Assert.Contains("(sample)", p.Title));
        Assert.All(starter.Experiences, e => Assert.Contains("fictional", e.Company, StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(starter.Skills);
        Assert.NotEmpty(starter.Certifications);
        Assert.NotEmpty(starter.Testimonials);
        foreach (var name in starter.Experiences.Select(e => e.Company.Replace("(fictional)", "", StringComparison.OrdinalIgnoreCase).Trim()).Concat(starter.Projects.Select(p => p.Org ?? "")))
            ResultsArchetypeTests.AssertNoLegalFormSuffix(name);

        var images = new List<string>();
        foreach (var p in starter.Projects)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.HeroImage), $"{p.Slug} has no cover");
            Assert.False(string.IsNullOrWhiteSpace(p.HeroImageAlt), $"{p.Slug} cover has no description");
            Assert.False(Media.IsVideo(p.HeroImage));
            images.Add(p.HeroImage!);
            foreach (var shot in p.Screenshots)
            {
                Assert.False(string.IsNullOrWhiteSpace(shot.Caption), $"{p.Slug} has a picture without a description");
                images.Add(shot.Url);
            }
            Assert.All(p.Links, l => Assert.StartsWith("https://example.org/", l.Url));
        }
        foreach (var url in images)
        {
            Assert.StartsWith($"/img/starter/{key}/", url);
            var file = Path.Combine(G.Wwwroot, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(file), $"{url} is not bundled under wwwroot");
            Assert.True(new FileInfo(file).Length < 60 * 1024, $"{url} is larger than a starter placeholder should be");
            Assert.DoesNotMatch(@"(?i)<script|<text|<image|https?://(?!www\.w3\.org)", File.ReadAllText(file));
        }
        Assert.Equal(images.Count, images.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void AShowcaseTemplate_DoesNotInheritSoftwareWording(string key)
    {
        var software = new TemplateCatalog(G.TemplatesDir).Get("software");
        var template = G.Definition(key);
        var neutral = new[] { "Skills", "Experience", "Links", "featured", "View certificate", "Curriculum vitae — {name}, {title}.", "Selected work" };

        foreach (var group in typeof(TemplateCopy).GetProperties())
            foreach (var prop in group.PropertyType.GetProperties().Where(p => p.PropertyType == typeof(string)))
            {
                var a = (string?)prop.GetValue(group.GetValue(software.Copy));
                var b = (string?)prop.GetValue(group.GetValue(template.Copy));
                if (neutral.Contains(b) || string.IsNullOrEmpty(b)) continue;
                Assert.True(a != b, $"copy.{group.Name}.{prop.Name} is identical to software's: {b}");
            }
        Assert.DoesNotMatch(@"\.NET|GitHub|software|developer|clinical|patient|licen[cs]e", JsonSerializer.Serialize(template.Copy));
        Assert.DoesNotMatch(@"(?i)tech stack|throughput|uptime|\brps\b|distributed|\.NET|case stud", JsonSerializer.Serialize(template.Editor));
    }

    // ---------------------------------------------------------------- rendering: blank, sample and full content

    [Theory]
    [MemberData(nameof(KeyAndContent))]
    public async Task EveryPublicPage_AnswersInTheShowcaseLayout_ForBlankSampleAndFullContent(string key, string content)
    {
        using var app = await G.SiteAsync(key, content);
        var client = app.NewClient();
        var home = await G.GetAsync(client, "/");

        Assert.Contains($"data-template=\"{G.Definition(key).Theme.Default}\"", home);
        Assert.Contains("grid-backdrop", home);          // the showcase hero
        Assert.DoesNotContain("data-results", home);
        foreach (var path in new[] { "/", "/projects", "/about", "/experience", "/skills", "/certifications", "/blog", "/contact", "/cv" })
            Assert.Single(Regex.Matches(G.MainOf(await G.GetAsync(client, path)), "<h1[ >]"));
        await G.GetAsync(client, "/talks", HttpStatusCode.NotFound);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task ABlankSite_ShowsTheTemplatesEmptyStatesAndItsNotFoundWords(string key)
    {
        using var app = await G.SiteAsync(key, "blank");
        var template = G.Definition(key);
        var client = app.NewClient();

        Assert.Contains(G.Escaped(template.Copy.Home.WorkEmptyTitle), await G.GetAsync(client, "/"));
        Assert.Contains(G.Escaped(template.Copy.Projects.EmptyTitle), await G.GetAsync(client, "/projects"));
        var missing = await G.GetAsync(client, "/projects/nothing-here", HttpStatusCode.NotFound);
        Assert.Contains(G.Escaped(ItemOf(template) + " not found"), missing);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task ResponseHeaders_EqualSoftwares_OnTheHomeAndTheWorkPage(string key)
    {
        using var site = await G.SiteAsync(key, "sample");
        using var software = new AdminApp();
        await DemoSite.SeedAsync(software);
        string[] volatileHeaders = ["Date", "ETag", "Content-Length", "Age", "Last-Modified", "Expires"];
        string Flat(HttpResponseMessage r) => string.Join("\n", r.Headers.Concat(r.Content.Headers)
            .Where(h => !volatileHeaders.Contains(h.Key, StringComparer.OrdinalIgnoreCase))
            .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase).Select(h => h.Key + ": " + string.Join(",", h.Value)));

        foreach (var path in new[] { "/", "/projects" })
            Assert.Equal(Flat(await software.NewClient().GetAsync(path)), Flat(await site.NewClient().GetAsync(path)));
    }

    // ---------------------------------------------------------------- labels come from the manifest

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task ThePages_ReadTheirHeadingsAndEmptyStatesFromTheManifest(string key)
    {
        var original = G.Definition(key);
        var dir = TemplateDirs.WithExtra("zork-showcase", node =>
        {
            node["name"] = "Zork Showcase";
            foreach (var (group, value) in node["copy"]!.AsObject().ToList())
                foreach (var (prop, _) in value!.AsObject().ToList())
                    value[prop] = prop switch
                    {
                        "imageAlt" => $"zork-{group}-{prop} {{title}}",
                        "description" => $"zork-{group}-{prop} for {{name}}",
                        _ => $"zork-{group}-{prop}",
                    };
            foreach (var section in node["sections"]!.AsArray())
            {
                section!["label"] = "zorkword" + section["key"];
                if (section["shortLabel"] is not null) section["shortLabel"] = "zorkshort" + section["key"];
                if (section["itemLabel"] is not null) section["itemLabel"] = "Zorkitem" + section["key"];
            }
        }, baseTemplate: key, starterFrom: key);
        try
        {
            using var app = AdminApp.WithTemplates(dir);
            await ContentFlow.BlankSiteAsync(app, "zork-showcase");
            var client = app.NewClient();

            var home = await G.GetAsync(client, "/");
            var projects = await G.GetAsync(client, "/projects");
            Assert.Contains("zork-home-workEmptyTitle", home);
            Assert.Contains("zork-projects-emptyTitle", projects);
            Assert.Contains("zork-projects-heading", projects);
            Assert.Contains("zork-experience-heading", await G.GetAsync(client, "/experience"));
            Assert.Contains("zork-skills-heading", await G.GetAsync(client, "/skills"));
            Assert.Contains("zork-certifications-heading", await G.GetAsync(client, "/certifications"));
            Assert.Contains("zork-contact-heading", await G.GetAsync(client, "/contact"));
            var missing = await G.GetAsync(client, "/projects/none", HttpStatusCode.NotFound);
            Assert.Contains("Zorkitemprojects not found", missing);

            // None of the template's real wording survives on the pages the shared layout draws.
            var visible = WebUtility.HtmlDecode(DemoSite.WithoutScripts(string.Concat(home, projects, missing)));
            var words = original.Copy.Home.GetType().GetProperties().Select(p => (string?)p.GetValue(original.Copy.Home))
                .Concat(original.Copy.Projects.GetType().GetProperties().Select(p => (string?)p.GetValue(original.Copy.Projects)))
                .Where(s => !string.IsNullOrWhiteSpace(s) && !s.Contains("{title}") && s.Contains(' ')).Distinct().ToList();
            Assert.NotEmpty(words);
            foreach (var w in words) Assert.DoesNotContain(w!, visible);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
