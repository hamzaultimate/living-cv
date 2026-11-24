using System.Text.Json;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>The CV fields of the manifest (copy.cv.order and copy.cv.accent): what the validator accepts, and what every shipped template sets.</summary>
public class CvManifestTests
{
    private static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "Templates");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    internal static TemplateDefinition Load(string key) =>
        JsonSerializer.Deserialize<TemplateDefinition>(File.ReadAllText(Path.Combine(TemplatesDir, key, "template.json")), Json)!;

    private static List<string> Errors(Action<CvCopy> change)
    {
        var t = Load("software");
        change(t.Copy.Cv);
        return TemplateValidator.Validate(t, "software");
    }

    [Fact]
    public void TheDefaults_AreTodaysOrderAndTodaysPdfColour()
    {
        Assert.Equal(new[] { "experience", "skills", "projects", "certifications" }, CvSections.Default);
        Assert.Equal("#1D4ED8", CvSections.DefaultAccent);
        var cv = new CvCopy();
        Assert.Equal(CvSections.Default, cv.OrderList());
        Assert.Equal(CvSections.DefaultAccent, cv.AccentText());
    }

    [Fact]
    public void AnOrderIsHonoured_AndASubsetMayLeaveSectionsOut()
    {
        Assert.Equal(new[] { "projects", "experience" }, new CvCopy { Order = ["projects", "experience"] }.OrderList());
        Assert.Equal("#115e59", new CvCopy { Accent = "#115e59" }.AccentText());
    }

    [Fact]
    public void Validator_AcceptsNoOrderAndNoAccent_AndAnyKnownSubset()
    {
        Assert.Empty(Errors(cv => { cv.Order = null; cv.Accent = ""; }));
        Assert.Empty(Errors(cv => cv.Order = ["certifications", "experience"]));
        Assert.Empty(Errors(cv => cv.Accent = "#1D4ED8"));
        Assert.Empty(Errors(cv => cv.Accent = "#abcdef"));
    }

    [Fact]
    public void Validator_RejectsASectionKeyTheCvCannotShow()
    {
        var errors = Errors(cv => cv.Order = ["experience", "blog"]);
        Assert.Contains(errors, e => e.Contains("copy.cv.order") && e.Contains("'blog'"));
        Assert.Contains(Errors(cv => cv.Order = ["about"]), e => e.Contains("copy.cv.order") && e.Contains("'about'"));
        Assert.Contains(Errors(cv => cv.Order = ["Experience"]), e => e.Contains("copy.cv.order") && e.Contains("'Experience'"));
    }

    [Fact]
    public void Validator_RejectsARepeatedKey_AndABlankEntry()
    {
        Assert.Contains(Errors(cv => cv.Order = ["experience", "skills", "experience"]), e => e.Contains("copy.cv.order") && e.Contains("'experience'") && e.Contains("twice"));
        Assert.Contains(Errors(cv => cv.Order = ["experience", ""]), e => e.Contains("copy.cv.order"));
        Assert.Contains(Errors(cv => cv.Order = ["experience", null!]), e => e.Contains("copy.cv.order"));
    }

    /// <summary>What each shipped template's CV must do. A new template adds its row here; the test below fails until it does.</summary>
    internal static readonly Dictionary<string, (string[] Order, string Accent)> Expected = new()
    {
        ["software"] = (new[] { "experience", "skills", "projects", "certifications" }, "#1D4ED8"),
        ["data-analytics"] = (new[] { "experience", "projects", "skills", "certifications" }, "#115e59"),
        ["product-management"] = (new[] { "experience", "projects", "skills", "certifications" }, "#92400e"),
        ["design-ux"] = (new[] { "projects", "experience", "skills", "certifications" }, "#92320e"),
        ["photography-film"] = (new[] { "projects", "experience", "skills", "certifications" }, "#6b4608"),
        ["marketing-content"] = (new[] { "projects", "experience", "skills", "certifications" }, "#9d174d"),
        ["sales-bd"] = (new[] { "projects", "experience", "skills", "certifications" }, "#065f46"),
        ["finance-accounting"] = (new[] { "experience", "certifications", "projects", "skills" }, "#15375c"),
        ["healthcare"] = (new[] { "certifications", "experience", "skills", "projects" }, "#115e59"),
    };

    [Fact]
    public void TheExpectationTable_CoversEveryTemplateInTheCatalog() =>
        Assert.Equal(new TemplateCatalog(TemplatesDir).All.Select(t => t.Key).OrderBy(x => x), Expected.Keys.OrderBy(x => x));

    [Fact]
    public void Software_SetsTheDefaultsExplicitly()
    {
        Assert.Equal(CvSections.Default, Expected["software"].Order);
        Assert.Equal(CvSections.DefaultAccent, Expected["software"].Accent);
    }

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public void EveryManifest_SetsItsCvOrderAndAccent(string key)
    {
        var cv = Load(key).Copy.Cv;
        Assert.NotNull(cv.Order);
        Assert.Equal(Expected[key].Order, cv.Order);
        Assert.Equal(Expected[key].Accent, cv.Accent);
    }

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public void EveryCvOrder_HasOnlyKnownUniqueKeys(string key)
    {
        var order = Load(key).Copy.Cv.OrderList();
        Assert.All(order, k => Assert.True(CvSections.IsKnown(k), $"{key}: '{k}' is not a CV section."));
        Assert.Equal(order.Count, order.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public void TheCvAccent_AndEveryPickableAccent_ReachBodyTextContrastOnWhite(string key)
    {
        var t = Load(key);
        Assert.True(Contrast.OnWhite(t.Copy.Cv.AccentText()) >= 4.5, $"{key}: copy.cv.accent {t.Copy.Cv.AccentText()} is {Contrast.OnWhite(t.Copy.Cv.AccentText()):F2}:1 on white, below 4.5:1.");
        Assert.NotEmpty(t.Theme.Accents);
        foreach (var a in t.Theme.Accents)
            Assert.True(Contrast.OnWhite(a.Light.Strong) >= 4.5, $"{key} {a.Key}: light.strong {a.Light.Strong} is {Contrast.OnWhite(a.Light.Strong):F2}:1 on white, below 4.5:1.");
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#gggggg")]
    [InlineData("1D4ED8")]
    [InlineData("url(#x)")]
    [InlineData("#1D4ED8;}")]
    public void Validator_RejectsAnAccentThatIsNotSixDigitHex(string accent) =>
        Assert.Contains(Errors(cv => cv.Accent = accent), e => e.Contains("copy.cv.accent"));
}
