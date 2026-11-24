using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Configuration;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using QuestPDF.Fluent;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>What visitors get on /cv and /cv.pdf: the template's section order, wording and accent.</summary>
public class CvOutputTests
{
    private static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "Templates");

    static CvOutputTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false; // as AdminApp does
    }

    // ---------------------------------------------------------------- /cv (HTML)

    /// <summary>The template's sample content. Software has no starter file (its content is the demo persona in the seed), so it is seeded instead.</summary>
    private static async Task ApplyContentAsync(AdminApp app, string key)
    {
        if (key == "software") await DemoSite.SeedAsync(app);
        else await DemoSite.ApplyStarterAsync(app, key);
    }

    private static string HeadingOf(CvCopy cv, string key) => key switch
    {
        "experience" => cv.ExperienceHeading,
        "skills" => cv.SkillsHeading,
        "projects" => cv.ProjectsHeading,
        _ => cv.CertificationsHeading,
    };

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public async Task TheCvPage_ListsItsSectionsInTheTemplatesOrder_WithItsHeadings(string key)
    {
        using var app = new AdminApp();
        await ApplyContentAsync(app, key);
        var cv = new TemplateCatalog(TemplatesDir).Get(key).Copy.Cv;

        var html = await app.NewClient().GetStringAsync("/cv");

        var positions = CvManifestTests.Expected[key].Order
            .Select(k => (Key: k, At: html.IndexOf($">{TemplateView.Text(HeadingOf(cv, k))}</h2>", StringComparison.Ordinal))).ToList();
        Assert.All(positions, p => Assert.True(p.At >= 0, $"{key}: the {p.Key} heading is missing from /cv."));
        Assert.Equal(positions.Select(p => p.At).OrderBy(x => x), positions.Select(p => p.At));
        Assert.Equal(CvManifestTests.Expected[key].Order.Length, Regex.Matches(html, "<h2 class=\"mt-12 text-xl\">").Count);
    }

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public async Task TheCvPage_PrintsTheTemplatesCertificationStatusWords_NotTheEnumNames(string key)
    {
        using var app = new AdminApp();
        await ApplyContentAsync(app, key);
        var words = new TemplateCatalog(TemplatesDir).Get(key).Copy.Certifications;
        List<Certification> certs;
        using (var db = app.CreateDbContext()) certs = db.Certifications.ToList();

        var html = WebUtility.HtmlDecode(await app.NewClient().GetStringAsync("/cv")); // Razor encodes apostrophes and typographic marks; compare the words, not the entities

        Assert.NotEmpty(certs);
        foreach (var c in certs)
        {
            var word = c.Status switch { CertificationStatus.Earned => words.StatusEarned, CertificationStatus.InProgress => words.StatusInProgress, _ => words.StatusPlanned };
            Assert.Contains($"{c.Issuer} ({word})", html);
        }
        Assert.DoesNotContain("(InProgress)", html);
    }

    [Fact]
    public async Task AnOwnerPickedAccent_StylesTheCvPage_ThroughTheSameVariablesAsEveryPage()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app); // software has no starter file: its content is the demo persona
        await app.Services.GetRequiredService<ISiteProfileProvider>().GetAsync(); // the first read saves the profile row that the configuration describes
        using (var db = app.CreateDbContext())
        {
            db.SiteProfiles.Single().AccentColor = "rose";
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        var css = new TemplateResolver(new TemplateCatalog(TemplatesDir)).Resolve(new SiteOptions { TemplateKey = "software", AccentColor = "rose" }).AccentStyle!;

        var html = await app.NewClient().GetStringAsync("/cv");

        Assert.Contains(css, html);
    }

    // ---------------------------------------------------------------- /cv.pdf

    private static CvData Data(IReadOnlyList<string>? order = null, string? accent = null) => new(
        "Casey Morgan", "Specialist", "A short summary.", new[] { "casey@owner.example" },
        new List<Experience> { new() { Company = "Example Co", Title = "Lead", Start = "2020", End = "2024", Summary = "Ran the place." } },
        new[] { new Skill { Name = "Planning", Group = "Core" } }.GroupBy(s => s.Group).ToList(),
        new List<Project> { new() { Title = "Rota", Slug = "rota", Summary = "A rota." } },
        new List<Certification> { new() { Name = "Cert", Issuer = "Body", Status = CertificationStatus.Earned } },
        "https://owner.example", null, order, accent);

    /// <summary>The PDF with its creation time masked: QuestPDF stamps the time of rendering, so two renders a second apart differ in that field alone.</summary>
    private static string Mask(byte[] pdf) => Regex.Replace(Encoding.Latin1.GetString(pdf), @"D:\d{14}[^)]*\)", "D:X)");

    private static string Pdf(CvData data) => Mask(new CvDocument(data).GeneratePdf());

    [Fact]
    public void ThePdf_WithNoOrderAndNoAccent_IsThePdfOfTodaysDefaults()
    {
        Assert.Equal(Pdf(Data(CvSections.Default, CvSections.DefaultAccent)), Pdf(Data()));
    }

    [Fact]
    public void ThePdf_FollowsTheSectionOrder()
    {
        var experienceFirst = Pdf(Data(new[] { "experience", "skills", "projects", "certifications" }));
        var certificationsFirst = Pdf(Data(new[] { "certifications", "projects", "skills", "experience" }));

        Assert.NotEqual(experienceFirst, certificationsFirst);
        Assert.Equal(certificationsFirst, Pdf(Data(new[] { "certifications", "projects", "skills", "experience" })));
    }

    [Fact]
    public void ThePdf_LeavesOutASectionThatIsNotInTheOrder()
    {
        Assert.NotEqual(Pdf(Data(new[] { "experience", "skills" })), Pdf(Data(new[] { "experience" })));
        Assert.NotEqual(Pdf(Data(new[] { "experience" })), Pdf(Data(Array.Empty<string>())));
    }

    [Theory]
    [InlineData("experience")]
    [InlineData("skills")]
    [InlineData("projects")]
    [InlineData("certifications")]
    public void ThePdf_DrawsEachSectionKeyAsItsOwnSection(string key)
    {
        // The order names one section, over full content ...
        var named = Pdf(Data(new[] { key }));
        // ... must draw what a CV holding only that section's content draws in the default order.
        var full = Data();
        var onlyThat = full with
        {
            Experience = key == "experience" ? full.Experience : new List<Experience>(),
            SkillGroups = key == "skills" ? full.SkillGroups : new List<IGrouping<string, Skill>>(),
            Projects = key == "projects" ? full.Projects : new List<Project>(),
            Certifications = key == "certifications" ? full.Certifications : new List<Certification>(),
        };

        Assert.Equal(Pdf(onlyThat), named);
        Assert.NotEqual(Pdf(Data(Array.Empty<string>())), named); // the section drew something
    }

    [Fact]
    public void ThePdf_UsesTheAccentOfTheData()
    {
        var teal = Pdf(Data(accent: "#115e59"));
        var brown = Pdf(Data(accent: "#92400e"));

        Assert.NotEqual(teal, brown);
        Assert.NotEqual(teal, Pdf(Data())); // not the default blue
        Assert.Equal(teal, Pdf(Data(accent: "#115e59")));
    }

    [Fact]
    public async Task TheServedPdf_FollowsTheOwnersAccentPick_OnceTheProfileIsSaved()
    {
        // A blank site: no experience highlights or impact lines, so no "▸" glyph (the test host's known font gap).
        using var app = new AdminApp();
        await ContentFlow.BlankSiteAsync(app);
        var visitor = app.NewClient();
        var before = Mask(await visitor.GetByteArrayAsync("/cv.pdf"));
        Assert.Equal(before, Mask(await visitor.GetByteArrayAsync("/cv.pdf"))); // unchanged profile: same document

        using (var db = app.CreateDbContext())
        {
            db.SiteProfiles.Single().AccentColor = "rose";
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        var after = Mask(await visitor.GetByteArrayAsync("/cv.pdf"));

        Assert.NotEqual(before, after);
    }
}
