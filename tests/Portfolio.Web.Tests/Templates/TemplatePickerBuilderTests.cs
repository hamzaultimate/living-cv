using Portfolio.Web.Configuration;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>The picker model: grouping, tags, the untrusted show/tab/size values, links and details. No HTML is involved.</summary>
public class TemplatePickerBuilderTests
{
    private static readonly TemplateCatalog Catalog = new(Path.Combine(AppContext.BaseDirectory, "Templates"));
    private static readonly IReadOnlyList<(string Path, string Label)> Pages = [("/", "Home"), ("/about", "About"), ("/blog", "Blog")];

    private static TemplatePickerModel Build(string key = "software", string? tab = null, string? size = null, string? marked = "software", string mark = "In use", ITemplateCatalog? catalog = null)
    {
        catalog ??= Catalog;
        return TemplatePickerBuilder.Build(catalog, new TemplatePickerInput("/admin/template", catalog.Get(key), Pages, tab, size, marked, mark));
    }

    [Fact]
    public void EveryTemplate_AppearsOnce_UnderItsProfession_WithTheDefaultFirst()
    {
        var model = Build();
        var keys = model.Groups.SelectMany(g => g.Entries).Select(e => e.Key).ToList();

        Assert.Equal(Catalog.All.Select(t => t.Key).OrderBy(k => k), keys.OrderBy(k => k));
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(Catalog.All.Select(t => t.Profession).Distinct(StringComparer.OrdinalIgnoreCase), model.Groups.Select(g => g.Profession));
        Assert.Equal("software", model.Groups[0].Entries[0].Key);
        foreach (var group in model.Groups)
            Assert.All(group.Entries, e => Assert.Equal(group.Profession, Catalog.Get(e.Key).Profession, ignoreCase: true));
    }

    [Fact]
    public void OnlyTheMarkedTemplate_GetsTheTag_AndOnlyTheSelectedOneIsSelected()
    {
        var model = Build("healthcare", marked: "software");
        var entries = model.Groups.SelectMany(g => g.Entries).ToList();

        Assert.Equal(["software"], entries.Where(e => e.Tag == "In use").Select(e => e.Key));
        Assert.Equal(["healthcare"], entries.Where(e => e.IsSelected).Select(e => e.Key));
        Assert.Null(model.SelectedTag);
        Assert.Equal("In use", Build("software").SelectedTag);
        Assert.All(Build(marked: null).Groups.SelectMany(g => g.Entries), e => Assert.Null(e.Tag));
        Assert.Equal("Selected", Build("healthcare", marked: "healthcare", mark: "Selected").SelectedTag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-such-template")]
    [InlineData("../software")]
    [InlineData("\"><script>")]
    public void AnUnknownShow_FallsBackToTheGivenTemplate(string? show) =>
        Assert.Equal("healthcare", TemplatePickerBuilder.Select(Catalog, show, "healthcare").Key);

    [Fact]
    public void AKnownShow_SelectsThatTemplate_AndAnUnknownFallbackFallsBackToTheDefault()
    {
        Assert.Equal("design-ux", TemplatePickerBuilder.Select(Catalog, "design-ux", "software").Key);
        Assert.Equal(TemplateCatalog.DefaultKey, TemplatePickerBuilder.Select(Catalog, null, "also-unknown").Key);
    }

    [Theory]
    [InlineData("/blog", "/blog")]
    [InlineData("/about", "/about")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("//evil.example/x", "/")]
    [InlineData("/not-a-page", "/")]
    [InlineData("https://evil.example/", "/")]
    public void TheTab_OnlyEverSelectsAPageOfTheList(string? tab, string expected)
    {
        var model = Build("healthcare", tab);

        Assert.Equal(expected, model.PreviewPath);
        Assert.Equal($"{expected}?preview=healthcare", model.PreviewUrl);
        Assert.Equal([expected], model.Pages.Where(p => p.IsCurrent).Select(p => p.Path));
    }

    [Theory]
    [InlineData("mobile", true)]
    [InlineData("desktop", false)]
    [InlineData(null, false)]
    [InlineData("MOBILE", false)]
    [InlineData("huge", false)]
    public void TheSize_IsOneOfTwoWidths_AndEveryLinkKeepsIt(string? size, bool mobile)
    {
        var model = Build("healthcare", "/blog", size);
        var word = mobile ? "mobile" : "desktop";

        Assert.Equal(mobile, model.Mobile);
        Assert.All(model.Groups.SelectMany(g => g.Entries), e => Assert.EndsWith($"&size={word}", e.Url));
        Assert.All(model.Pages, p => Assert.EndsWith($"&size={word}", p.Url));
        Assert.Equal("/admin/template?show=healthcare&tab=%2Fblog&size=desktop", model.DesktopUrl);
        Assert.Equal("/admin/template?show=healthcare&tab=%2Fblog&size=mobile", model.MobileUrl);
    }

    [Fact]
    public void TheLinks_AreBuiltFromTheBasePath_AndEscapeTheirValues()
    {
        var model = Build("healthcare");

        Assert.Equal("/admin/template?show=design-ux&size=desktop", model.Groups.SelectMany(g => g.Entries).Single(e => e.Key == "design-ux").Url);
        Assert.Equal("/admin/template?show=healthcare&tab=%2Fabout&size=desktop", model.Pages.Single(p => p.Path == "/about").Url);

        var wizard = TemplatePickerBuilder.Build(Catalog, new TemplatePickerInput("/admin/welcome", Catalog.Get("healthcare"), Pages, null, null, null, "Selected"));
        Assert.StartsWith("/admin/welcome?show=", wizard.Groups[0].Entries[0].Url);
    }

    [Fact]
    public void TheDetails_ComeFromTheManifest_ForEveryTemplate()
    {
        foreach (var t in Catalog.All)
        {
            var details = Build(t.Key).Details;
            var expectedPages = t.Sections.Where(s => s.Enabled && s.Key != SectionRegistry.Contact).Select(s => s.Label).Append("contact");

            Assert.Equal(expectedPages, details.Pages);
            Assert.Equal(t.Sections.FirstOrDefault(s => s.Key == SectionRegistry.Projects && s.Enabled)?.ItemLabel, details.ItemLabel);
            Assert.Equal(StarterContent.Offers(t), details.HasSample);
            Assert.Equal(t.Guidance, details.Guidance);
        }
        Assert.Equal("Initiative", Build("healthcare").Details.ItemLabel);
        Assert.Contains("patient-identifiable", Build("healthcare").Details.Guidance);
        Assert.True(string.IsNullOrWhiteSpace(Build("software").Details.Guidance));
    }

    [Fact]
    public void TheThumbnailUrl_IsOnlySetForATemplateThatShipsOne()
    {
        Assert.Equal("/admin/welcome/thumb/healthcare", Build("healthcare").ThumbUrl);

        var dir = TemplateDirs.WithExtra("plain", n => n["name"] = "Plain"); // this copy ships no preview.png for "plain"
        try
        {
            var catalog = new TemplateCatalog(dir);
            Assert.Null(Build("plain", catalog: catalog).ThumbUrl);
            Assert.Equal("/admin/welcome/thumb/software", Build("software", catalog: catalog).ThumbUrl);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void PagesOf_StartsAtHome_AndEveryLabelStartsWithACapital()
    {
        var view = new TemplateResolver(Catalog).Resolve(new SiteOptions { TemplateKey = "software" }, null);

        var pages = TemplatePickerBuilder.PagesOf(view);

        Assert.Equal(("/", "Home"), pages[0]);
        Assert.Equal(pages.Count, pages.Select(p => p.Path).Distinct().Count());
        Assert.All(pages, p => Assert.True(char.IsUpper(p.Label[0]), p.Label));
    }
}
