using Portfolio.Web.Configuration;
using Portfolio.Web.Services;
using Xunit;

namespace Portfolio.Web.Tests.Services;

public class TemplateCatalogTests
{
    private static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "Templates");

    private static SiteOptions Demo() => new()
    {
        OwnerName = "Alex Rivera",
        JobTitle = "Senior Software Engineer",
        Summary = "A summary.",
    };

    [Fact]
    public void SoftwarePreset_IsShippedAndIsTheDefault()
    {
        var catalog = new TemplateCatalog(TemplatesDir);

        var preset = catalog.Get("software");

        Assert.Equal("software", preset.Key);
        Assert.Equal("showcase", preset.Archetype);
        Assert.Same(preset, catalog.Get(null));
        Assert.Same(preset, catalog.Get("no-such-template"));
        Assert.Same(preset, catalog.Get("SOFTWARE"));
    }

    [Fact]
    public void EveryShippedTemplateFolder_LoadsWithoutBeingSkipped_AndTheDefaultIsListedFirst()
    {
        var catalog = new TemplateCatalog(TemplatesDir);
        var folders = Directory.EnumerateDirectories(TemplatesDir).Select(d => Path.GetFileName(d)).Where(n => !n.StartsWith('_')).OrderBy(n => n).ToList();

        Assert.Equal(folders, catalog.All.Select(t => t.Key).OrderBy(k => k).ToList());
        Assert.Contains("healthcare", folders);
        Assert.Equal("software", catalog.All.First().Key);
        Assert.NotNull(catalog.GetStarter("healthcare"));
    }

    [Fact]
    public void ABrokenDefaultManifest_StopsTheCatalogFromLoading()
    {
        var dir = Directory.CreateTempSubdirectory("lcv-templates-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "software"));
            File.WriteAllText(Path.Combine(dir, "software", "template.json"), "{ \"key\": \"software\", \"typo\": true }");

            var ex = Assert.Throws<InvalidOperationException>(() => new TemplateCatalog(dir));
            Assert.Contains("software", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("Jo", "Nurse")]
    [InlineData("Dr Alexandra Wilhelmina Featherstonehaugh-Montgomery", "Advanced Clinical Practitioner in Paediatric Intensive Care and Retrieval Medicine")]
    public void ApplyDescription_NeverExceeds160Characters_AndCutsAtAWordBoundary(string name, string title)
    {
        var site = new SiteOptions { OwnerName = name, JobTitle = title, Summary = new string('x', 40) + " " + new string('y', 200) };

        foreach (var pattern in new[] { "{name}, {title}. Registrations, clinical experience and professional references.", "{summary}", "Short." })
        {
            var text = TemplateSeo.ApplyDescription(pattern, site);
            Assert.True(text.Length <= 160, text);
            if (TemplateSeo.Apply(pattern, site).Length <= 160) Assert.Equal(TemplateSeo.Apply(pattern, site), text);
            else Assert.EndsWith("…", text);
        }
        var cut = TemplateSeo.ApplyDescription("{name}, {title}. Registrations, clinical experience and professional references.", new SiteOptions { OwnerName = name + name, JobTitle = title + " and Education", Summary = "s" });
        Assert.DoesNotMatch(@"[\s,;:.\-—]…$", cut);
    }

    [Fact]
    public void ApplyDescription_NeverSplitsASurrogatePair_WhenThereIsNoSpaceToCutAt()
    {
        // 158 characters then an emoji (two UTF-16 units) puts the cut inside the pair.
        var site = new SiteOptions { OwnerName = "N", JobTitle = "T", Summary = new string('a', 158) + "\U0001F600" + new string('b', 20) };

        var text = TemplateSeo.ApplyDescription("{summary}", site);

        Assert.True(text.Length <= 160);
        Assert.EndsWith("…", text);
        Assert.DoesNotContain(text, c => char.IsSurrogate(c));
    }

    [Fact]
    public void SoftwarePreset_KeepsTheCvSkillGroupOrder()
    {
        var preset = new TemplateCatalog(TemplatesDir).Get("software");

        Assert.Equal(new[] { "Backend", "Architecture", "Cloud/DevOps", "Data", "AI", "Embedded/IoT" }, preset.SkillGroups);
    }

    [Fact]
    public void SoftwarePreset_ProducesTheExactHomeAndAboutCopy()
    {
        var seo = new TemplateCatalog(TemplatesDir).Get("software").Seo;
        var site = Demo();

        Assert.Equal("Alex Rivera — Senior Software Engineer, .NET & AI", TemplateSeo.Apply(seo.Title, site));
        Assert.Equal(
            "Alex Rivera is a Senior Software Engineer with 9+ years building high-performance .NET systems, now building agentic AI products in production.",
            TemplateSeo.Apply(seo.Description, site));
        Assert.Equal(
            "Alex Rivera — Senior Software Engineer with 9+ years building reliable, high-performance .NET systems, now bringing applied AI into production.",
            TemplateSeo.Apply(seo.AboutDescription, site));
    }

    [Fact]
    public void SoftwarePreset_SeoStaysWithinSearchEngineLimitsForTheDemoPersona()
    {
        var seo = new TemplateCatalog(TemplatesDir).Get("software").Seo;
        var site = Demo();

        Assert.InRange(TemplateSeo.Apply(seo.Title, site).Length, 1, 65);
        Assert.InRange(TemplateSeo.Apply(seo.Description, site).Length, 70, 160);
        Assert.InRange(TemplateSeo.Apply(seo.AboutDescription, site).Length, 70, 160);
    }

    [Fact]
    public void SummaryPlaceholder_IsReplaced()
    {
        Assert.Equal("Alex Rivera: A summary.", TemplateSeo.Apply("{name}: {summary}", Demo()));
    }

    [Fact]
    public void GroupSkills_FollowsPresetOrderAndPutsUnlistedGroupsLast()
    {
        var preset = new TemplateCatalog(TemplatesDir).Get("software");
        var skills = new[]
        {
            new Portfolio.Data.Entities.Skill { Name = "Other", Group = "Zzz" },
            new Portfolio.Data.Entities.Skill { Name = "ESP32", Group = "Embedded/IoT" },
            new Portfolio.Data.Entities.Skill { Name = "C#", Group = "Backend" },
            new Portfolio.Data.Entities.Skill { Name = "Azure", Group = "Cloud/DevOps" },
        };

        var groups = PortfolioContentService.GroupSkills(skills, preset.SkillGroups);

        Assert.Equal(new[] { "Backend", "Cloud/DevOps", "Embedded/IoT", "Zzz" }, groups.Select(g => g.Key));
    }

    [Fact]
    public void BrokenManifest_IsSkippedAndDefaultStillLoads()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "software"));
            File.Copy(Path.Combine(TemplatesDir, "software", "template.json"), Path.Combine(dir, "software", "template.json"));
            Directory.CreateDirectory(Path.Combine(dir, "broken"));
            File.WriteAllText(Path.Combine(dir, "broken", "template.json"), "{ not json");

            var catalog = new TemplateCatalog(dir);

            Assert.Equal(new[] { "software" }, catalog.All.Select(t => t.Key));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static string TempTemplates()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(dir, "software"));
        File.Copy(Path.Combine(TemplatesDir, "software", "template.json"), Path.Combine(dir, "software", "template.json"));
        return dir;
    }

    private static string Manifest(string key) =>
        File.ReadAllText(Path.Combine(TemplatesDir, "software", "template.json"))
            .Replace("\"key\": \"software\"", $"\"key\": \"{key}\"");

    [Fact]
    public void FoldersStartingWithUnderscore_AreSkipped()
    {
        var dir = TempTemplates();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "_draft"));
            File.WriteAllText(Path.Combine(dir, "_draft", "template.json"), Manifest("_draft"));

            Assert.Equal(new[] { "software" }, new TemplateCatalog(dir).All.Select(t => t.Key));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void NestedManifests_AreNotDiscovered()
    {
        var dir = TempTemplates();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "software", "nested", "deep"));
            File.WriteAllText(Path.Combine(dir, "software", "nested", "template.json"), Manifest("nested"));
            File.WriteAllText(Path.Combine(dir, "software", "nested", "deep", "template.json"), Manifest("deep"));

            Assert.Equal(new[] { "software" }, new TemplateCatalog(dir).All.Select(t => t.Key));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ManifestKey_MustMatchItsFolderName()
    {
        var dir = TempTemplates();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "chef"));
            File.WriteAllText(Path.Combine(dir, "chef", "template.json"), Manifest("baker"));

            var catalog = new TemplateCatalog(dir);

            Assert.Equal(new[] { "software" }, catalog.All.Select(t => t.Key));
            Assert.Equal("software", catalog.Get("baker").Key);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MissingDefaultTemplate_Throws()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try { Assert.Throws<InvalidOperationException>(() => new TemplateCatalog(dir)); }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
