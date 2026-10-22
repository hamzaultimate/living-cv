using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>
/// Themes are CSS variables only, shipped in the one compiled stylesheet. These tests read the sources and the generated
/// wwwroot/css/app.css (rebuilt by every build), so a theme or a utility class that never reached the stylesheet fails here.
/// </summary>
public class ThemeStylesheetTests
{
    private static readonly string Web = Path.Combine(RepoRoot(), "src", "Portfolio.Web");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Portfolio.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Css => File.ReadAllText(Path.Combine(Web, "wwwroot", "css", "app.css"));

    private static IEnumerable<(string Key, string Theme)> Themes() =>
        Directory.EnumerateDirectories(Path.Combine(Web, "Templates")).Where(d => !Path.GetFileName(d).StartsWith('_'))
            .Select(d => JsonDocument.Parse(File.ReadAllText(Path.Combine(d, "template.json")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }).RootElement)
            .Where(r => r.TryGetProperty("theme", out var t) && t.TryGetProperty("default", out var d) && !string.IsNullOrWhiteSpace(d.GetString()))
            .Select(r => (r.GetProperty("key").GetString()!, r.GetProperty("theme").GetProperty("default").GetString()!));

    private static readonly string[] Tokens = ["bg", "surface", "surface-2", "border", "text", "muted", "accent", "accent-strong"];

    private static Dictionary<string, (int R, int G, int B)> Block(string themeCss, string theme, string mode)
    {
        var match = Regex.Match(themeCss, $@":root\[data-template=""{Regex.Escape(theme)}""\]\[data-theme=""{mode}""\]\s*\{{(?<body>[^}}]*)\}}");
        Assert.True(match.Success, $"Theme '{theme}' has no {mode} block.");
        var values = new Dictionary<string, (int, int, int)>();
        foreach (Match m in Regex.Matches(match.Groups["body"].Value, @"--c-(?<name>[a-z0-9-]+):\s*(?<r>\d+)\s+(?<g>\d+)\s+(?<b>\d+);"))
            values[m.Groups["name"].Value] = (int.Parse(m.Groups["r"].Value), int.Parse(m.Groups["g"].Value), int.Parse(m.Groups["b"].Value));
        foreach (var token in Tokens) Assert.True(values.ContainsKey(token), $"Theme '{theme}' {mode} lacks --c-{token}.");
        return values;
    }

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

    [Fact]
    public void EveryTemplateTheme_HasAStylesheet_ImportedByTheBuild_WithBothColourModes()
    {
        var themes = Themes().ToList();
        Assert.NotEmpty(themes);
        var app = File.ReadAllText(Path.Combine(Web, "Styles", "app.css"));

        foreach (var (key, theme) in themes)
        {
            var file = Path.Combine(Web, "Styles", "themes", theme + ".css");
            Assert.True(File.Exists(file), $"{key}: Styles/themes/{theme}.css is missing.");
            Assert.Contains($"@import \"./themes/{theme}.css\";", app);
            var css = File.ReadAllText(file);
            Block(css, theme, "dark");
            Block(css, theme, "light");
            Assert.DoesNotMatch(@"\.[a-z]", Regex.Replace(css, @"/\*.*?\*/|""[^""]*""|\d+\.\d+", "", RegexOptions.Singleline));
        }
    }

    [Fact]
    public void EveryThemeStylesheet_IsReferencedByATemplate()
    {
        var used = Themes().Select(t => t.Theme).ToHashSet();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Web, "Styles", "themes"), "*.css"))
            Assert.Contains(Path.GetFileNameWithoutExtension(file), used);
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public void ThemeColours_MeetBodyTextContrast(string mode)
    {
        foreach (var (key, theme) in Themes())
        {
            var c = Block(File.ReadAllText(Path.Combine(Web, "Styles", "themes", theme + ".css")), theme, mode);
            foreach (var foreground in new[] { "text", "muted", "accent", "accent-strong" })
                foreach (var background in new[] { "bg", "surface", "surface-2" })
                    Assert.True(Contrast(c[foreground], c[background]) >= 4.5,
                        $"{key} {mode}: {foreground} on {background} is {Contrast(c[foreground], c[background]):F2}:1, below 4.5:1.");
            // The preview banner and primary buttons put the page background on the accent colour.
            Assert.True(Contrast(c["bg"], c["accent"]) >= 4.5, $"{key} {mode}: bg on accent is below 4.5:1.");
        }
    }

    [Fact]
    public void TheGeneratedStylesheet_ContainsEveryThemeAndTheVariableDrivenFontsAndRadii()
    {
        var css = Css;

        foreach (var (_, theme) in Themes())
        {
            Assert.Matches($@"\[data-template=[""']?{Regex.Escape(theme)}[""']?\]\[data-theme=[""']?dark", css);
            Assert.Matches($@"\[data-template=[""']?{Regex.Escape(theme)}[""']?\]\[data-theme=[""']?light", css);
        }
        // Software keeps its fonts and radii as the fallback of each variable, so it looks as it always did.
        Assert.Contains("var(--font-display,\"Space Grotesk\")", css);
        Assert.Contains("var(--font-sans,Inter)", css);
        Assert.Contains("var(--font-mono,\"JetBrains Mono\")", css);
        Assert.Contains(".rounded-lg{border-radius:var(--radius-lg,.5rem)}", css);
        Assert.Contains(".rounded-md{border-radius:var(--radius-md,.375rem)}", css);
    }

    [Fact]
    public void TheBaseLookIsStillDefinedOnRoot_SoSoftwareNeedsNoThemeBlock()
    {
        var css = Css;

        Assert.Contains("--c-bg:11 15 20", css);
        Assert.Contains("--c-accent:96 165 250", css);
        Assert.DoesNotContain("data-template=software", css);
    }

    /// <summary>The Tailwind scan covers Pages/**, so every utility the new layout, blocks and preview banner use must be in the compiled sheet.</summary>
    [Fact]
    public void EveryClassUsedByTheTemplateLayouts_IsInTheGeneratedStylesheet()
    {
        var css = Css;
        var files = Directory.EnumerateFiles(Path.Combine(Web, "Pages", "Shared", "Layouts"), "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(Web, "Pages", "Shared", "Blocks"), "*.cshtml"))
            .Append(Path.Combine(Web, "Pages", "Shared", "_Layout.cshtml"))
            .ToList();
        Assert.True(files.Count >= 6);

        // Marker classes have no rule of their own, and a few classes are only ever set by script.
        var markers = new HashSet<string> { "group", "peer" };
        var missing = new SortedSet<string>();
        var checkedClasses = 0;
        foreach (var file in files)
            foreach (Match attr in Regex.Matches(File.ReadAllText(file), @"\bclass=""([^""@]*)"""))
                foreach (var token in attr.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (markers.Contains(token) || token.StartsWith("group/", StringComparison.Ordinal)) continue;
                    checkedClasses++;
                    var selector = "." + Regex.Replace(token, @"([^a-zA-Z0-9_-])", @"\$1");
                    if (!css.Contains(selector, StringComparison.Ordinal)) missing.Add($"{Path.GetFileName(file)}: {token}");
                }

        Assert.True(checkedClasses > 150, "Expected to check the classes of several layout files.");
        Assert.True(missing.Count == 0, "Classes missing from wwwroot/css/app.css (Tailwind did not generate them):" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }
}
