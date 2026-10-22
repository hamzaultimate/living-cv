using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Portfolio.Web.Services;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>Manifest rules from the authoring guide, enforced over every template found on disk and over deliberately broken ones.</summary>
public class TemplateManifestTests
{
    private static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "Templates");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private sealed class CapturingLogger : ILogger<TemplateCatalog>
    {
        public List<string> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error) Errors.Add(formatter(state, exception));
        }
    }

    private static IEnumerable<string> TemplateFolders() =>
        Directory.EnumerateDirectories(TemplatesDir).Where(d => !Path.GetFileName(d).StartsWith('_') && File.Exists(Path.Combine(d, "template.json")));

    private static TemplateDefinition Load(string key) =>
        JsonSerializer.Deserialize<TemplateDefinition>(File.ReadAllText(Path.Combine(TemplatesDir, key, "template.json")), Json)!;

    /// <summary>A throwaway Templates folder with the shipped software template plus one extra manifest built by <paramref name="mutate"/>.</summary>
    private static (string Dir, CapturingLogger Log, TemplateCatalog Catalog) CatalogWith(string folder, string baseTemplate, Action<JsonObject> mutate, string? starter = null)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(dir, "software"));
        File.Copy(Path.Combine(TemplatesDir, "software", "template.json"), Path.Combine(dir, "software", "template.json"));
        Directory.CreateDirectory(Path.Combine(dir, folder));
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(TemplatesDir, baseTemplate, "template.json")), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!.AsObject();
        mutate(node);
        File.WriteAllText(Path.Combine(dir, folder, "template.json"), node.ToJsonString());
        if (starter is not null) File.WriteAllText(Path.Combine(dir, folder, "starter.json"), starter);
        var log = new CapturingLogger();
        return (dir, log, new TemplateCatalog(dir, log));
    }

    [Fact]
    public void ShipsExactlyTheSoftwareAndHealthcareTemplates()
    {
        Assert.Equal(new[] { "healthcare", "software" }, TemplateFolders().Select(Path.GetFileName).OrderBy(x => x).ToArray());
        var catalog = new TemplateCatalog(TemplatesDir);
        Assert.Equal(new[] { "software", "healthcare" }, catalog.All.Select(t => t.Key));
    }

    [Fact]
    public void EveryManifestOnDisk_IsValid_WithAUniqueKeyMatchingItsFolder()
    {
        var folders = TemplateFolders().ToList();
        Assert.NotEmpty(folders);

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            var name = Path.GetFileName(folder);
            var template = Load(name);
            Assert.Empty(TemplateValidator.Validate(template, name));
            Assert.True(keys.Add(template.Key), $"Duplicate key {template.Key}");
            Assert.Contains(template.Archetype, Archetypes.Implemented);
            Assert.Equal(template.Sections.Count, template.Sections.Select(s => s.Key).Distinct().Count());
            Assert.All(template.Sections, s => Assert.Contains(s.Key, SectionRegistry.Keys));
        }
    }

    [Fact]
    public void EveryTemplateWithStarterContent_HasFictionalStarterContent()
    {
        var withStarter = 0;
        foreach (var folder in TemplateFolders())
        {
            var template = Load(Path.GetFileName(folder));
            if (template.Starter is null) continue;
            withStarter++;
            var starter = TemplateStarter.Parse(File.ReadAllText(Path.Combine(folder, template.Starter.File)));
            Assert.Empty(TemplateValidator.ValidateStarter(starter));
            Assert.NotEmpty(starter.Experiences);
            Assert.NotEmpty(starter.Certifications);
            Assert.All(starter.Certifications, c => Assert.DoesNotContain("http", c.CredentialUrl ?? ""));
            Assert.All(starter.Experiences, e => Assert.Contains("fictional", e.Company, StringComparison.OrdinalIgnoreCase));
        }
        Assert.True(withStarter >= 1, "At least the healthcare template ships starter content.");
    }

    [Fact]
    public void SensitiveTemplates_CarryGuidanceAgainstPublishingPatientOrClientData()
    {
        var healthcare = Load("healthcare");

        Assert.True(healthcare.Sensitive);
        Assert.Contains("patient", healthcare.Guidance!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("identif", healthcare.Guidance!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("patient", healthcare.Copy.Contact.Lead, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Healthcare_DoesNotInheritAnySoftwareWording()
    {
        var software = Load("software");
        var healthcare = Load("healthcare");

        // Every copy string differs from the software one, except the few that are neutral by design.
        var neutral = new[] { "Skills", "Experience", "Links", "featured", "In progress", "Planned", "View certificate", "Watch →", "Curriculum vitae — {name}, {title}.", "Writing", "Coming soon" };
        foreach (var group in typeof(TemplateCopy).GetProperties())
            foreach (var prop in group.PropertyType.GetProperties().Where(p => p.PropertyType == typeof(string)))
            {
                var sw = (string?)prop.GetValue(group.GetValue(software.Copy));
                var hc = (string?)prop.GetValue(group.GetValue(healthcare.Copy));
                if (neutral.Contains(hc)) continue;
                Assert.True(sw != hc, $"copy.{group.Name}.{prop.Name} is identical in both templates: {hc}");
            }
    }

    // ---- Validation rules ----

    [Fact]
    public void Validator_RejectsEachBrokenRule()
    {
        static List<string> Errors(Action<TemplateDefinition> edit, string folder = "software")
        {
            var t = Load("software");
            edit(t);
            return TemplateValidator.Validate(t, folder);
        }

        Assert.Contains(Errors(t => t.Key = "Software Two"), e => e.Contains("kebab-case"));
        Assert.Contains(Errors(t => t.Key = "other"), e => e.Contains("does not match folder"));
        Assert.Contains(Errors(t => t.Archetype = "mosaic"), e => e.Contains("unknown"));
        Assert.Contains(Errors(t => t.Archetype = "gallery"), e => e.Contains("no layout yet"));
        Assert.Contains(Errors(t => t.Sections.Add(new TemplateSection { Key = "wiki", Label = "wiki" })), e => e.Contains("'wiki'"));
        Assert.Contains(Errors(t => t.Sections.Add(new TemplateSection { Key = "about", Label = "about" })), e => e.Contains("listed twice"));
        Assert.Contains(Errors(t => t.Sections[0].Label = ""), e => e.Contains("needs a label"));
        Assert.Contains(Errors(t => t.Sections.First(s => s.Key == "projects").ItemLabel = null), e => e.Contains("itemLabel"));
        Assert.Contains(Errors(t => t.Seo.Title = new string('x', 80)), e => e.Contains("seo.title") && e.Contains("65"));
        Assert.Contains(Errors(t => t.Seo.Description = new string('x', 170)), e => e.Contains("seo.description") && e.Contains("160"));
        Assert.Contains(Errors(t => t.Seo.AboutDescription = ""), e => e.Contains("seo.aboutDescription"));
        Assert.Contains(Errors(t => t.Copy.Skills.Heading = ""), e => e.Contains("copy.skills.heading is required"));
        Assert.Contains(Errors(t => t.Copy.Home.WorkHeading = "<b>x</b>"), e => e.Contains("plain text"));
        Assert.Contains(Errors(t => t.Copy.Contact.Description = new string('x', 200)), e => e.Contains("copy.contact.description"));
        Assert.Contains(Errors(t => t.SkillGroups.Add("backend")), e => e.Contains("repeat"));
        Assert.Contains(Errors(t => t.Theme.ThemeColor = "navy"), e => e.Contains("themeColor"));
        Assert.Contains(Errors(t => t.Theme.Default = "My Theme"), e => e.Contains("theme.default"));
        Assert.Contains(Errors(t => t.Starter = new TemplateStarterRef { File = "../x.json" }), e => e.Contains("starter.file"));
        Assert.Contains(Errors(t => t.Name = ""), e => e.Contains("name is required"));
    }

    [Fact]
    public void Validator_RequiresGuidanceForSensitiveProfessions_EvenIfTheFlagIsForgotten()
    {
        var flagged = Load("healthcare");
        flagged.Guidance = " ";
        Assert.Contains(TemplateValidator.Validate(flagged, "healthcare"), e => e.Contains("guidance"));

        var forgotten = Load("healthcare");
        forgotten.Sensitive = false;
        Assert.Contains(TemplateValidator.Validate(forgotten, "healthcare"), e => e.Contains("\"sensitive\": true"));

        var legal = Load("software");
        legal.Key = "legal";
        Assert.Contains(TemplateValidator.Validate(legal, "legal"), e => e.Contains("sensitive"));
    }

    // ---- Bad manifests never crash; they are skipped with a logged error ----

    public static TheoryData<string, Action<JsonObject>> BrokenManifests => new()
    {
        { "typo in a property name", n => n["archetyp"] = "showcase" },
        { "unknown archetype", n => n["archetype"] = "mosaic" },
        { "archetype without a layout", n => n["archetype"] = "gallery" },
        { "unknown section", n => n["sections"]!.AsArray().Add(new JsonObject { ["key"] = "wiki", ["label"] = "wiki" }) },
        { "seo title too long", n => n["seo"]!["title"] = new string('x', 90) },
        { "missing copy string", n => n["copy"]!["home"]!.AsObject().Remove("workHeading") },
        { "sensitive without guidance", n => { n["sensitive"] = true; n.Remove("guidance"); } },
        { "starter file missing", n => n["starter"] = new JsonObject { ["file"] = "nope.json" } },
        { "image alt without {title}", n => n["copy"]!["projects"]!["imageAlt"] = "screenshot" },
        { "key not kebab case", n => n["key"] = "Broken Key" },
    };

    [Theory]
    [MemberData(nameof(BrokenManifests))]
    public void BrokenManifest_IsSkippedWithALoggedError_AndTheDefaultStillLoads(string why, Action<JsonObject> mutate)
    {
        var (dir, log, catalog) = CatalogWith("broken", "software", n =>
        {
            n["key"] = "broken";
            mutate(n);
        });
        try
        {
            Assert.Equal(new[] { "software" }, catalog.All.Select(t => t.Key));
            Assert.True(log.Errors.Count == 1, $"'{why}' should log exactly one error");
            Assert.Equal("software", catalog.Get("broken").Key);
            Assert.False(catalog.TryGet("broken", out _));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void StarterContent_MustDeclareItselfFictional_ToLoad()
    {
        var (dir, log, catalog) = CatalogWith("realistic", "healthcare", n => n["key"] = "realistic",
            starter: """{ "fictional": false, "profile": { "ownerName": "A", "jobTitle": "B" } }""");
        try
        {
            Assert.DoesNotContain(catalog.All, t => t.Key == "realistic");
            Assert.Contains(log.Errors, e => e.Contains("realistic"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void AValidThirdManifest_IsPickedUpWithNoCodeChange()
    {
        var healthcareStarter = File.ReadAllText(Path.Combine(TemplatesDir, "healthcare", "starter.json"));
        var (dir, log, catalog) = CatalogWith("legal-lite", "healthcare", n =>
        {
            n["key"] = "legal-lite";
            n["name"] = "Legal (test)";
        }, starter: healthcareStarter);
        try
        {
            Assert.Empty(log.Errors);
            Assert.Equal(new[] { "software", "legal-lite" }, catalog.All.Select(t => t.Key));
            Assert.NotNull(catalog.GetStarter("legal-lite"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void DuplicateKeys_AreRejected_NotMerged()
    {
        var (dir, log, catalog) = CatalogWith("copycat", "software", n => n["key"] = "software");
        try
        {
            Assert.Single(catalog.All);
            Assert.Single(log.Errors);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---- Unknown or hostile keys fall back safely ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-such-template")]
    [InlineData("../software")]
    [InlineData("software/../healthcare/x")]
    [InlineData("healthcare\u0000")]
    [InlineData("<script>")]
    public void UnknownOrHostileKeys_FallBackToSoftware_AndAreNotAllowListed(string? key)
    {
        var catalog = new TemplateCatalog(TemplatesDir);

        Assert.Equal("software", catalog.Get(key).Key);
        Assert.False(catalog.TryGet(key, out var found));
        Assert.Null(found);
    }

    [Fact]
    public void KnownKeys_AreFoundIgnoringCaseAndSurroundingSpace()
    {
        var catalog = new TemplateCatalog(TemplatesDir);

        Assert.True(catalog.TryGet(" Healthcare ", out var found));
        Assert.Equal("healthcare", found!.Key);
    }

    // ---- The schema file documents the same shape the code enforces ----

    [Fact]
    public void SchemaFile_ListsExactlyThePropertiesOfTheModel()
    {
        var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(TemplatesDir, "template.schema.json")))!.AsObject();
        var props = schema["properties"]!.AsObject();

        Assert.Equal(Names(typeof(TemplateDefinition)), props.Select(p => p.Key).OrderBy(x => x).ToArray());
        Assert.Equal(Names(typeof(TemplateTheme)), props["theme"]!["properties"]!.AsObject().Select(p => p.Key).OrderBy(x => x).ToArray());
        Assert.Equal(Names(typeof(TemplateSection)), props["sections"]!["items"]!["properties"]!.AsObject().Select(p => p.Key).OrderBy(x => x).ToArray());
        Assert.Equal(Names(typeof(TemplateSeo)), props["seo"]!["properties"]!.AsObject().Select(p => p.Key).OrderBy(x => x).ToArray());
        Assert.Equal(Names(typeof(TemplateStarterRef)), props["starter"]!["properties"]!.AsObject().Select(p => p.Key).OrderBy(x => x).ToArray());

        var copy = props["copy"]!["properties"]!.AsObject();
        Assert.Equal(Names(typeof(TemplateCopy)), copy.Select(p => p.Key).OrderBy(x => x).ToArray());
        foreach (var group in typeof(TemplateCopy).GetProperties())
            Assert.Equal(Names(group.PropertyType), copy[Camel(group.Name)]!["properties"]!.AsObject().Select(p => p.Key).OrderBy(x => x).ToArray());

        Assert.Equal(SectionRegistry.Keys.OrderBy(x => x), props["sections"]!["items"]!["properties"]!["key"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).OrderBy(x => x));
        Assert.Equal(Archetypes.Known.OrderBy(x => x), props["archetype"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).OrderBy(x => x));
    }

    private static string[] Names(Type type) =>
        type.GetProperties()
            .Select(p => p.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false)
                .Cast<System.Text.Json.Serialization.JsonPropertyNameAttribute>().FirstOrDefault()?.Name ?? Camel(p.Name))
            .OrderBy(x => x).ToArray();

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    [Fact]
    public void EveryShippedManifest_UsesOnlyPropertiesTheSchemaKnows_AndDeclaresTheSchema()
    {
        foreach (var folder in TemplateFolders())
        {
            var node = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "template.json")),
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
            Assert.Equal("../template.schema.json", node["$schema"]!.GetValue<string>());
            var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(TemplatesDir, "template.schema.json")))!["required"]!.AsArray().Select(n => n!.GetValue<string>());
            foreach (var required in schema) Assert.True(node.ContainsKey(required), $"{folder} lacks required '{required}'");
        }
    }
}
