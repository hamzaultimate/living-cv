using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Portfolio.Web.Configuration;
using Portfolio.Web.Services;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>The manifest parts the onboarding wizard reads: title and summary suggestions, accent colours, the sample content source and the thumbnail.</summary>
public class TemplateOnboardingManifestTests
{
    private static string Web
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Portfolio.slnx"))) dir = dir.Parent;
            return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Repository root not found."), "src", "Portfolio.Web");
        }
    }

    private static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "Templates");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private static TemplateDefinition Load(string key) =>
        JsonSerializer.Deserialize<TemplateDefinition>(File.ReadAllText(Path.Combine(TemplatesDir, key, "template.json")), Json)!;

    private static List<string> Errors(Action<TemplateDefinition> edit, string key = "software")
    {
        var t = Load(key);
        edit(t);
        return TemplateValidator.Validate(t, key);
    }

    private static TemplateAccent Accent(string key = "teal-two") => new()
    {
        Key = key, Label = "Teal",
        Dark = new TemplateAccentTone { Accent = "#2dd4bf", Strong = "#5eead4" },
        Light = new TemplateAccentTone { Accent = "#0f766e", Strong = "#115e59" },
    };

    // ---- heroSuggestions ----

    [Fact]
    public void EveryShippedTemplate_OffersATitleAndSummarySuggestion()
    {
        foreach (var key in new[] { "software", "healthcare" })
        {
            var s = Load(key).HeroSuggestions;
            Assert.False(string.IsNullOrWhiteSpace(s.Title), key);
            Assert.False(string.IsNullOrWhiteSpace(s.Summary), key);
        }
        Assert.NotEqual(Load("software").HeroSuggestions.Title, Load("healthcare").HeroSuggestions.Title);
    }

    [Fact]
    public void Validator_RejectsBrokenSuggestions()
    {
        Assert.Contains(Errors(t => t.HeroSuggestions.Title = ""), e => e.Contains("heroSuggestions.title is required"));
        Assert.Contains(Errors(t => t.HeroSuggestions.Summary = " "), e => e.Contains("heroSuggestions.summary is required"));
        Assert.Contains(Errors(t => t.HeroSuggestions.Title = new string('x', 121)), e => e.Contains("heroSuggestions.title") && e.Contains("120"));
        Assert.Contains(Errors(t => t.HeroSuggestions.Summary = new string('x', 401)), e => e.Contains("heroSuggestions.summary") && e.Contains("400"));
        Assert.Contains(Errors(t => t.HeroSuggestions.Title = "<b>Nurse</b>"), e => e.Contains("heroSuggestions.title must be plain text"));
        Assert.Contains(Errors(t => t.HeroSuggestions.Summary = "a <script> b"), e => e.Contains("heroSuggestions.summary must be plain text"));
    }

    [Fact]
    public void AManifestWithoutSuggestions_IsSkippedByTheCatalog_NotLoadedWithEmptyOnes()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            foreach (var key in new[] { "software", "healthcare" })
            {
                Directory.CreateDirectory(Path.Combine(dir, key));
                File.Copy(Path.Combine(TemplatesDir, key, "template.json"), Path.Combine(dir, key, "template.json"));
            }
            File.Copy(Path.Combine(TemplatesDir, "healthcare", "starter.json"), Path.Combine(dir, "healthcare", "starter.json"));
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "healthcare", "template.json")),
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!.AsObject();
            node.Remove("heroSuggestions");
            File.WriteAllText(Path.Combine(dir, "healthcare", "template.json"), node.ToJsonString());

            var catalog = new TemplateCatalog(dir);

            Assert.False(catalog.TryGet("healthcare", out _));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---- accents ----

    [Fact]
    public void EveryShippedTemplate_OffersValidAccents_AndTheyAreNotJustCopiedBetweenTemplates()
    {
        foreach (var key in new[] { "software", "healthcare" })
        {
            var t = Load(key);
            Assert.NotEmpty(t.Theme.Accents);
            Assert.Empty(TemplateValidator.Validate(t, key));
            Assert.Equal(t.Theme.Accents.Count, t.Theme.Accents.Select(a => a.Key).Distinct().Count());
        }
        Assert.NotEqual(Load("software").Theme.Accents.Select(a => a.Key), Load("healthcare").Theme.Accents.Select(a => a.Key));
    }

    [Fact]
    public void Validator_RejectsBrokenAccents()
    {
        Assert.Contains(Errors(t => t.Theme.Accents.Add(Accent("Not Kebab"))), e => e.Contains("kebab-case"));
        Assert.Contains(Errors(t => t.Theme.Accents.Add(Accent("../x"))), e => e.Contains("kebab-case"));
        Assert.Contains(Errors(t => t.Theme.Accents.Add(Accent(""))), e => e.Contains("kebab-case"));
        Assert.Contains(Errors(t => t.Theme.Accents.Add(Accent(new string('a', 41)))), e => e.Contains("at most 40"));
        Assert.Contains(Errors(t => { t.Theme.Accents.Add(Accent("dup")); t.Theme.Accents.Add(Accent("dup")); }), e => e.Contains("twice"));
        Assert.Contains(Errors(t => { var a = Accent(); a.Dark.Accent = "red"; t.Theme.Accents.Add(a); }), e => e.Contains("dark.accent"));
        Assert.Contains(Errors(t => { var a = Accent(); a.Dark.Accent = "#12345g"; t.Theme.Accents.Add(a); }), e => e.Contains("dark.accent"));
        Assert.Contains(Errors(t => { var a = Accent(); a.Light.Strong = "#fff"; t.Theme.Accents.Add(a); }), e => e.Contains("light.strong"));
        Assert.Contains(Errors(t => { var a = Accent(); a.Light.Accent = "#0f766e;} body{display:none"; t.Theme.Accents.Add(a); }), e => e.Contains("light.accent"));
        Assert.Contains(Errors(t => { var a = Accent(); a.Label = "<i>Teal</i>"; t.Theme.Accents.Add(a); }), e => e.Contains("plain text"));
        Assert.Contains(Errors(t => { var a = Accent(); a.Label = ""; t.Theme.Accents.Add(a); }), e => e.Contains("label is required"));
        Assert.Contains(Errors(t => { for (var i = 0; i < 9; i++) t.Theme.Accents.Add(Accent($"a{i}")); }), e => e.Contains("limit is 8"));
    }

    [Fact]
    public void AccentStyle_IsNull_WhenNoAccentIsPicked_SoTheStylesheetIsUntouched()
    {
        var catalog = new TemplateCatalog(TemplatesDir);
        var resolver = new TemplateResolver(catalog);

        Assert.Null(resolver.Resolve(new SiteOptions { TemplateKey = "software" }).AccentStyle);
        Assert.Null(resolver.Resolve(new SiteOptions { TemplateKey = "software", AccentColor = "" }).AccentStyle);
        Assert.Null(resolver.Resolve(new SiteOptions { TemplateKey = "software", AccentColor = "   " }).AccentStyle);
    }

    [Fact]
    public void AccentStyle_SetsOnlyTheTwoAccentVariables_ForAKeyTheTemplateOffers()
    {
        var resolver = new TemplateResolver(new TemplateCatalog(TemplatesDir));

        var css = resolver.Resolve(new SiteOptions { TemplateKey = "software", AccentColor = "violet" }).AccentStyle!;

        Assert.Contains("--c-accent:167 139 250", css);
        Assert.Contains("--c-accent-strong:196 181 253", css);
        Assert.Contains("--c-accent:109 40 217", css);
        // Nothing but those two custom properties is ever declared, and nothing outside a selector block.
        var declared = Regex.Matches(css, @"(--?[a-z-]+):").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
        Assert.Equal(["--c-accent", "--c-accent-strong"], declared);
        Assert.Matches(@"^(:root:root:root(:not\(\[data-theme=""light""\]\)|\[data-theme=""light""\])\{--c-accent:\d+ \d+ \d+;--c-accent-strong:\d+ \d+ \d+\})+$", css);
    }

    [Theory]
    [InlineData("emerald")]                         // offered by software, not by healthcare
    [InlineData("nope")]
    [InlineData("#ff0000")]
    [InlineData("red;}body{display:none")]
    [InlineData("VIOLET")]
    [InlineData("../violet")]
    public void AccentStyle_IgnoresAKeyTheTemplateDoesNotOffer(string stored)
    {
        var resolver = new TemplateResolver(new TemplateCatalog(TemplatesDir));

        Assert.Null(resolver.Resolve(new SiteOptions { TemplateKey = "healthcare", AccentColor = stored }).AccentStyle);
    }

    [Fact]
    public void AccentStyle_RefusesToEmitAColourThatIsNotAHexValue_EvenIfAManifestSlippedItThrough()
    {
        var catalog = new TemplateCatalog(TemplatesDir);
        var definition = catalog.Get("software");
        var view = new TemplateView(new SiteOptions { AccentColor = "evil" }, new TemplateDefinition
        {
            Key = "x", Theme = new TemplateTheme { Accents = [new TemplateAccent { Key = "evil", Label = "Evil",
                Dark = new TemplateAccentTone { Accent = "red;}body{x:y", Strong = "#ffffff" }, Light = new TemplateAccentTone { Accent = "#ffffff", Strong = "#ffffff" } }] },
        }, [], false);

        Assert.NotNull(definition);
        Assert.Null(view.AccentStyle);
    }

    // ---- accent contrast: every tone must keep 4.5:1 on the template's own backgrounds, in both modes ----

    private static (int R, int G, int B) Hex(string hex) =>
        (int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber), int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber), int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber));

    private static double Luminance((int R, int G, int B) c)
    {
        static double Channel(int v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast((int R, int G, int B) a, (int R, int G, int B) b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>The three background tokens of a template in a colour mode, read from the stylesheet source that defines them.</summary>
    private static List<(int R, int G, int B)> Backgrounds(TemplateDefinition t, string mode)
    {
        string css, selectorPattern;
        if (string.IsNullOrWhiteSpace(t.Theme.Default))
        {
            css = File.ReadAllText(Path.Combine(Web, "Styles", "app.css"));
            selectorPattern = mode == "dark" ? @":root, :root\[data-theme=""dark""\]" : @":root\[data-theme=""light""\]";
        }
        else
        {
            css = File.ReadAllText(Path.Combine(Web, "Styles", "themes", t.Theme.Default + ".css"));
            selectorPattern = $@":root\[data-template=""{Regex.Escape(t.Theme.Default)}""\]\[data-theme=""{mode}""\]";
        }
        var block = Regex.Match(css, selectorPattern + @"\s*\{(?<body>[^}]*)\}");
        Assert.True(block.Success, $"{t.Key}: no {mode} token block found");
        return new[] { "bg", "surface", "surface-2" }.Select(token =>
        {
            var m = Regex.Match(block.Groups["body"].Value, $@"--c-{token}:\s*(\d+)\s+(\d+)\s+(\d+);");
            Assert.True(m.Success, $"{t.Key} {mode}: --c-{token} not found");
            return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        }).ToList();
    }

    [Theory]
    [InlineData("software")]
    [InlineData("healthcare")]
    public void EveryAccentTone_KeepsBodyTextContrast_OnItsTemplateBackgrounds(string key)
    {
        var t = Load(key);
        foreach (var accent in t.Theme.Accents)
            foreach (var (mode, tone) in new[] { ("dark", accent.Dark), ("light", accent.Light) })
            {
                var backgrounds = Backgrounds(t, mode);
                foreach (var colour in new[] { tone.Accent, tone.Strong })
                    foreach (var background in backgrounds)
                        Assert.True(Contrast(Hex(colour), background) >= 4.5,
                            $"{key} {accent.Key} {mode}: {colour} on ({background.R} {background.G} {background.B}) is {Contrast(Hex(colour), background):F2}:1, below 4.5:1.");
            }
    }

    // ---- sample content source ----

    [Fact]
    public void Validator_AcceptsExactlyOneStarterSource()
    {
        Assert.Empty(Errors(t => t.Starter = new TemplateStarterRef { Source = "seed" }));
        Assert.Contains(Errors(t => t.Starter = new TemplateStarterRef { Source = "seed", File = "starter.json" }), e => e.Contains("either a file or a source"));
        Assert.Contains(Errors(t => t.Starter = new TemplateStarterRef { Source = "http://elsewhere/x.json" }), e => e.Contains("starter.source must be"));
        Assert.Contains(Errors(t => t.Starter = new TemplateStarterRef { Source = "" }), e => e.Contains("starter.source must be"));
        Assert.Contains(Errors(t => t.Starter = new TemplateStarterRef()), e => e.Contains("starter.file"));
    }

    [Fact]
    public void SoftwareUsesTheBuiltInDemoPersona_AndHealthcareItsOwnFile()
    {
        Assert.True(Load("software").Starter!.IsSeed());
        Assert.Null(Load("software").Starter!.File);
        Assert.Equal("starter.json", Load("healthcare").Starter!.File);
        Assert.False(Load("healthcare").Starter!.IsSeed());
        var catalog = new TemplateCatalog(TemplatesDir);
        Assert.Null(catalog.GetStarter("software"));
        Assert.NotNull(catalog.GetStarter("healthcare"));
    }

    // ---- thumbnails ----

    [Fact]
    public void EveryShippedTemplate_HasA1200x800PngThumbnail()
    {
        var catalog = new TemplateCatalog(TemplatesDir);
        foreach (var t in catalog.All)
        {
            var path = catalog.GetPreviewImagePath(t.Key);
            Assert.NotNull(path);
            var bytes = File.ReadAllBytes(path!);
            Assert.True(bytes.AsSpan(0, 8).SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]), $"{t.Key}: not a PNG");
            var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
            var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
            Assert.Equal((1200, 800), (width, height));
            Assert.True(bytes.Length < 600 * 1024, $"{t.Key}: thumbnail is {bytes.Length} bytes");
        }
    }

    [Theory]
    [InlineData("../software")]
    [InlineData("software/../healthcare")]
    [InlineData("..%2fsoftware")]
    [InlineData("software\\..\\healthcare")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    [InlineData("nope")]
    public void ThePreviewImagePath_ResolvesOnlyForAKeyThatLoaded(string key)
    {
        Assert.Null(new TemplateCatalog(TemplatesDir).GetPreviewImagePath(key));
    }

    [Fact]
    public void ThePreviewImagePath_IsAlwaysTheFixedFileInsideTheTemplateFolder()
    {
        var catalog = new TemplateCatalog(TemplatesDir);
        foreach (var t in catalog.All)
        {
            var path = Path.GetFullPath(catalog.GetPreviewImagePath(t.Key)!);
            Assert.Equal(Path.Combine(Path.GetFullPath(TemplatesDir), t.Key, "preview.png"), path);
        }
    }
}
