using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The shared template picker on /admin/template: every template selectable and previewable, tags, the Switch action and its refusal.</summary>
public class TemplatePickerAdminPageTests
{
    private static readonly TemplateCatalog Catalog = new(Path.Combine(AppContext.BaseDirectory, "Templates"));

    public static IEnumerable<object[]> Keys => Catalog.All.Select(t => new object[] { t.Key });

    private static string Plain(string html) => WebUtility.HtmlDecode(html);

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task EveryTemplate_CanBeSelectedAndPreviewed_WithItsOwnDetails(string key)
    {
        var t = Catalog.Get(key);
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = Plain(await owner.GetStringAsync($"/admin/template?show={key}"));

        Assert.Contains($"<h2 id=\"picker-heading\">{t.Name}", html);
        Assert.Contains($"src=\"/?preview={key}\"", html);
        Assert.Contains($"{t.Profession} · {TemplateView.Cap(t.Archetype)} layout", html);
        Assert.Contains(t.Description, html);
        Assert.Contains($"<strong>Pages:</strong> {string.Join(", ", t.Sections.Where(s => s.Enabled && s.Key != SectionRegistry.Contact).Select(s => s.Label))}, contact", html);
        if (string.IsNullOrWhiteSpace(t.Guidance)) Assert.DoesNotContain("Before you publish", html);
        else Assert.Contains(t.Guidance, html);
    }

    [Fact]
    public async Task TheNamesBox_ListsEveryTemplateByProfession_AndTagsTheOneInUse()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = Plain(await owner.GetStringAsync("/admin/template?show=healthcare&size=mobile"));

        Assert.Equal(Catalog.All.Count, Regex.Matches(html, @"href=""/admin/template\?show=[a-z-]+&size=mobile""").Count);
        foreach (var profession in Catalog.All.Select(t => t.Profession).Distinct()) Assert.Contains($">{profession}</h3>", html);
        Assert.Single(Regex.Matches(html, @"</a><span class=""adm-pill"">In use</span>"));
        Assert.Contains("<a href=\"/admin/template?show=healthcare&size=mobile\" aria-current=\"true\">Healthcare</a>", html);
        Assert.Contains("<details class=\"adm-picker-names\">", html);
        Assert.Contains("<nav aria-label=\"Templates\">", html);
    }

    [Fact]
    public async Task TheSwitchButton_NamesTheTemplate_AndIsAbsentForTheOneInUse()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var other = Plain(await owner.GetStringAsync("/admin/template?show=healthcare"));
        var current = Plain(await owner.GetStringAsync("/admin/template"));

        Assert.Contains(">Switch to Healthcare</button>", other);
        Assert.DoesNotMatch(@"<button\b[^>]*\bdisabled\b[^>]*>Switch to", other);
        Assert.Single(Regex.Matches(other, "name=\"Key\""));
        Assert.Contains("Nothing is saved or visible to visitors until you choose &quot;Switch to Healthcare&quot;", await owner.GetStringAsync("/admin/template?show=healthcare"));
        Assert.DoesNotContain("Switch to", current);
        Assert.Contains("In use", current);
    }

    [Fact]
    public async Task WhenASwitchWouldEmptyTheMenu_TheButtonIsDisabled_AndGivesAReason()
    {
        // "dormant" lists only about and contact, both off by default: with only the blog on, nothing can be kept there.
        var dir = TemplateDirs.WithExtra("dormant", node =>
        {
            node["name"] = "Dormant";
            node["sections"] = JsonNode.Parse("""[ { "key": "about", "label": "about", "enabled": false }, { "key": "contact", "label": "contact", "enabled": false } ]""");
        });
        try
        {
            using var app = AdminApp.WithTemplates(dir);
            var owner = await ContentFlow.BlankSiteAsync(app, "software");
            await owner.GetAsync("/admin");
            await SectionsFlow.SaveAsync(owner, r => r.Select(x => x with { Enabled = x.Key == "blog" }).ToList());

            var html = Plain(await owner.GetStringAsync("/admin/template?show=dormant"));

            Assert.Matches(@"<button\b[^>]*\bdisabled\b[^>]*>Switch to Dormant</button>", html);
            Assert.Contains("aria-describedby=\"picker-action-reason\"", html);
            Assert.Contains("id=\"picker-action-reason\"", html);
            Assert.Contains("<strong>Cannot be switched to yet:</strong>", html);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ThePreviewFrame_HasATitleAndNoContent_AndAStaticPictureLinkFollowsIt()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = Plain(await owner.GetStringAsync("/admin/template?show=healthcare"));

        Assert.Matches(@"<iframe class=""adm-preview adm-picker-frame""[^>]* title=""Preview of the Healthcare template""", html);
        // Browsers never show an iframe's children, so the frame must be empty.
        Assert.Matches(@"<iframe\b[^>]*></iframe>", html);
        var frame = html[html.IndexOf("<iframe", StringComparison.Ordinal)..html.IndexOf("</iframe>", StringComparison.Ordinal)];
        Assert.DoesNotContain("<img", frame);
        Assert.DoesNotContain("<div", frame);
        // The picture is a visible link after the frame instead.
        Assert.Contains("href=\"/admin/welcome/thumb/healthcare\"", html);
        Assert.Matches(@"<a href=""/admin/welcome/thumb/healthcare""[^>]*target=""_blank""", html);
        Assert.Contains("See a static picture of the home page", html);
    }

    [Fact]
    public async Task ATemplateWithoutAThumbnail_ShowsNoStaticPictureLink()
    {
        var dir = TemplateDirs.WithExtra("plain", n => n["name"] = "Plain");
        try
        {
            using var app = AdminApp.WithTemplates(dir);
            var owner = await app.SignedInClientAsync();

            var html = Plain(await owner.GetStringAsync("/admin/template?show=plain"));

            Assert.Contains("<h2 id=\"picker-heading\">Plain", html);
            Assert.DoesNotContain("static picture", html);
            Assert.DoesNotContain("/admin/welcome/thumb/plain", html);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

/// <summary>admin.css is one hand-written file; the picker's layout rules must exist and must not rely on inline style.</summary>
public class TemplatePickerCssTests
{
    private static string Css()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Portfolio.Web", "wwwroot", "css", "admin.css"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, "src", "Portfolio.Web", "wwwroot", "css", "admin.css"));
    }

    [Fact]
    public void ThePickerStyles_PutTheNamesOnTheRight_CollapseThemBelowFortyRem_AndKeepThemOpenBesideThePreviewAbove()
    {
        var css = Css();

        Assert.Contains(".adm-picker {", css);
        Assert.Contains("grid-template-areas: \"detail names\"", css);
        Assert.Contains("grid-template-areas: \"names\" \"detail\"", css);
        Assert.Contains("@supports selector(::details-content)", css);
        Assert.Contains(".adm-picker-names::details-content { content-visibility: visible;", css);
        Assert.Matches(new Regex(@"@media \(max-width: 40rem\) \{\s*\.adm-picker \{"), css);
        Assert.Contains("overflow-y: auto", css); // the names box scrolls inside itself
    }
}
