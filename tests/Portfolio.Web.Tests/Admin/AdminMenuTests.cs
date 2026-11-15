using System.Text.Json.Nodes;
using Portfolio.Web.Configuration;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The menu model: structure, template wording, hidden sections, counts, the current item and the collapse rules. No HTML is involved.</summary>
public class AdminMenuTests
{
    private static readonly AdminMenu Menu = new();

    private static TemplateView Software(params SectionOption[] sections) =>
        new TemplateResolver(new TemplateCatalog(Path.Combine(AppContext.BaseDirectory, "Templates")))
            .Resolve(new SiteOptions { TemplateKey = "software", Sections = sections.ToList() }, null);

    private static AdminMenuItem Find(AdminMenuModel model, string key) =>
        model.Groups.SelectMany(g => g.Items).SelectMany(i => new[] { i }.Concat(i.Children)).Single(i => i.Key == key);

    [Fact]
    public void TheGroupsAndItems_AreThoseOfTheSpec_InOrder()
    {
        var model = Menu.Build(Software(), "/admin", null);

        Assert.Equal([null, "Your site", "Appearance", "Tools"], model.Groups.Select(g => g.Label));
        Assert.Equal(["dashboard"], model.Groups[0].Items.Select(i => i.Key));
        Assert.Equal(["profile", "content"], model.Groups[1].Items.Select(i => i.Key));
        Assert.Equal(["template", "sections"], model.Groups[2].Items.Select(i => i.Key));
        Assert.Equal(["wizard", "transfer"], model.Groups[3].Items.Select(i => i.Key));
    }

    [Fact]
    public void ProfileAndContent_AreSubMenus_ThatReachEveryPageUnderThem()
    {
        var model = Menu.Build(Software(), "/admin", null);

        Assert.Equal(
            ["/admin/profile", "/admin/profile/hero", "/admin/profile/metrics", "/admin/profile/about", "/admin/photo"],
            Find(model, "profile").Children.Select(c => c.Url));
        Assert.Equal(
            ["/admin/content", .. EditorFields.Sections.Select(s => "/admin/content/" + s)],
            Find(model, "content").Children.Select(c => c.Url));
        Assert.Equal("/admin/profile", Find(model, "profile").Url);
        Assert.Equal("/admin/content", Find(model, "content").Url);
        Assert.Equal("/admin/template", Find(model, "template").Url);
        Assert.Equal("/admin/sections", Find(model, "sections").Url);
        Assert.Equal("/admin/welcome", Find(model, "wizard").Url);
        Assert.Equal("/admin/transfer", Find(model, "transfer").Url);
    }

    [Fact]
    public void Labels_ComeFromTheTemplatesWording_NotFromTheMenu()
    {
        var dir = TemplateDirs.WithExtra("quirky", node =>
        {
            foreach (var section in node["sections"]!.AsArray())
            {
                section!["label"] = "Zone " + section["key"]!.GetValue<string>();
                section.AsObject().Remove("shortLabel");
            }
            node["editor"] = JsonNode.Parse("""
            {
              "hero": { "fields": { "heading": { "label": "Zhero page" } } },
              "metrics": { "fields": { "heading": { "label": "Zmetrics page" } } },
              "about": { "fields": { "heading": { "label": "Zabout page" } } },
              "sections": { "fields": { "heading": { "label": "Zsec map" } } },
              "transfer": { "fields": { "heading": { "label": "Zport" } } }
            }
            """);
        });
        var view = new TemplateResolver(new TemplateCatalog(dir)).Resolve(new SiteOptions { TemplateKey = "quirky" }, null);

        var model = Menu.Build(view, "/admin", null);

        Assert.Equal("Zone experience", Find(model, "content.experience").Label);
        Assert.Equal("Zone testimonials", Find(model, "content.testimonials").Label);
        Assert.Equal("Zhero page", Find(model, "profile.hero").Label);
        Assert.Equal("Zmetrics page", Find(model, "profile.metrics").Label);
        Assert.Equal("Zabout page", Find(model, "profile.about").Label);
        Assert.Equal("Zsec map", Find(model, "sections").Label);
        Assert.Equal("Zport", Find(model, "transfer").Label);
    }

    [Fact]
    public void ASectionTheTemplateSwitchesOff_StaysInTheMenu_FlaggedHidden()
    {
        var model = Menu.Build(Software(new SectionOption { Key = "talks", Enabled = false, Shown = false }), "/admin", null);

        var talks = Find(model, "content.talks");
        Assert.True(talks.Hidden);
        Assert.Equal("/admin/content/talks", talks.Url);
        Assert.All(EditorFields.Sections.Where(s => s != "talks"), s => Assert.False(Find(model, "content." + s).Hidden));
    }

    [Fact]
    public void Counts_AreAttachedToContentItems_AndLeftOffWhenTheyCouldNotBeRead()
    {
        var counts = new Dictionary<string, int> { ["experience"] = 3, ["skills"] = 0 };

        var withCounts = Menu.Build(Software(), "/admin", counts);
        var without = Menu.Build(Software(), "/admin", null);

        Assert.Equal(3, Find(withCounts, "content.experience").Count);
        Assert.Equal(0, Find(withCounts, "content.skills").Count);
        Assert.Null(Find(withCounts, "content.projects").Count); // not in the dictionary
        Assert.All(without.Leaves, l => Assert.Null(l.Count));
    }

    [Theory]
    [InlineData("/admin", "dashboard")]
    [InlineData("/admin/", "dashboard")]
    [InlineData("/admin/profile", "profile.basics")]
    [InlineData("/admin/profile/hero", "profile.hero")]
    [InlineData("/admin/profile/metrics", "profile.metrics")]
    [InlineData("/admin/photo", "profile.photo")]
    [InlineData("/admin/content", "content.overview")]
    [InlineData("/admin/content/", "content.overview")]
    [InlineData("/admin/content/experience", "content.experience")]
    [InlineData("/admin/content/projects/edit/4", "content.projects")]
    [InlineData("/admin/content/projects/images/4", "content.projects")]
    [InlineData("/admin/content/blog/delete/2", "content.blog")]
    [InlineData("/admin/sections", "sections")]
    [InlineData("/admin/template", "template")]
    [InlineData("/admin/welcome", "wizard")]
    [InlineData("/admin/welcome/review", "wizard")]
    [InlineData("/admin/transfer", "transfer")]
    [InlineData("/admin/transfer/confirm", "transfer")]
    [InlineData("/ADMIN/Content/Skills", "content.skills")]
    public void TheCurrentPage_IsTheItemWithTheLongestMatchingAddress(string path, string expected)
    {
        var model = Menu.Build(Software(), path, null);

        Assert.Equal(expected, model.Current?.Key);
        Assert.Single(model.Leaves, l => l.IsCurrent);
    }

    [Theory]
    [InlineData("/admin/profilex")]
    [InlineData("/admin/contents")]
    [InlineData("/admin/nothing-here")]
    [InlineData("/elsewhere")]
    public void AnAddressThatIsNotInTheMenu_MarksNothingCurrent(string path)
    {
        var model = Menu.Build(Software(), path, null);

        Assert.Null(model.Current);
        Assert.DoesNotContain(model.Leaves, l => l.IsCurrent);
    }

    [Fact]
    public void TheParentOfTheCurrentItem_ContainsIt_SoItsSubMenuRendersOpen()
    {
        var model = Menu.Build(Software(), "/admin/content/skills", null);

        Assert.True(Find(model, "content").ContainsCurrent);
        Assert.False(Find(model, "profile").ContainsCurrent);
        Assert.False(Find(model, "content").IsCurrent); // the parent itself is never "the page"
    }

    [Fact]
    public void WithoutATemplate_TheMenuStillBuilds_InNeutralWords()
    {
        var model = Menu.Build(null, "/admin/photo", null);

        Assert.Equal("profile.photo", model.Current?.Key);
        Assert.Equal("Experience", Find(model, "content.experience").Label);
        Assert.Equal("Home page introduction", Find(model, "profile.hero").Label);
        Assert.Equal("Export and import", Find(model, "transfer").Label);
        Assert.False(Find(model, "content.talks").Hidden);
    }

    [Theory]
    [InlineData("/admin/welcome", true)]
    [InlineData("/admin/welcome/basics", true)]
    [InlineData("/admin/welcome/review", true)]
    [InlineData("/ADMIN/WELCOME/photo", true)]
    [InlineData("/admin/welcomex", false)]
    [InlineData("/admin", false)]
    [InlineData("/admin/template", false)]
    public void TheWizardPages_AreRailPages(string path, bool expected) =>
        Assert.Equal(expected, AdminMenu.IsRailPage(path));

    [Theory]
    [InlineData("/admin", "collapsed", true)]
    [InlineData("/admin", null, false)]
    [InlineData("/admin", "", false)]
    [InlineData("/admin", "Collapsed", false)]
    [InlineData("/admin", "collapsed; x", false)]
    [InlineData("/admin", "expanded", false)]
    [InlineData("/admin/welcome", null, true)]
    [InlineData("/admin/welcome/basics", "", true)]
    public void TheCookie_IsOnlyEverComparedWithTheLiteral(string path, string? cookie, bool expected) =>
        Assert.Equal(expected, AdminMenu.IsCollapsed(path, cookie));
}
