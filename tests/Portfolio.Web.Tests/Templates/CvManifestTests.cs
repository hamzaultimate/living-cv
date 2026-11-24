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
