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

/// <summary>The shared template picker on wizard step 1: every template selectable, saved to the draft only through the action button.</summary>
public class TemplatePickerWizardTests
{
    private static readonly TemplateCatalog Catalog = new(Path.Combine(AppContext.BaseDirectory, "Templates"));

    public static IEnumerable<object[]> Keys => Catalog.All.Select(t => new object[] { t.Key });

    private static string Plain(string html) => WebUtility.HtmlDecode(html);

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task EveryTemplate_CanBeSelectedAndPreviewed_OnTheWizardStep(string key)
    {
        var t = Catalog.Get(key);
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var html = Plain(await owner.GetStringAsync($"{WizardFlow.Template}?show={key}"));

        Assert.Contains($"<h2 id=\"picker-heading\">{t.Name}", html);
        Assert.Contains($"src=\"/?preview={key}\"", html);
        Assert.Contains($"href=\"/admin/welcome/thumb/{key}\"", html);
        Assert.Contains($">Use {t.Name} and continue</button>", html);
        Assert.Contains($"<strong>Pages:</strong> {string.Join(", ", t.Sections.Where(s => s.Enabled && s.Key != SectionRegistry.Contact).Select(s => s.Label))}, contact", html);
        if (string.IsNullOrWhiteSpace(t.Guidance)) Assert.DoesNotContain("Before you publish", html);
        else Assert.Contains(t.Guidance, html);
    }

    [Fact]
    public async Task BrowsingNeverSaves_AndTheActionButtonSavesTheChoiceToTheDraft()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        await owner.GetStringAsync($"{WizardFlow.Template}?show=healthcare&Key=healthcare");
        Assert.Null(await WizardFlow.DraftAsync(app));

        var posted = await WizardFlow.PickTemplateAsync(owner, "healthcare");

        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(posted));
        Assert.Equal("healthcare", (await WizardFlow.DraftAsync(app))!.TemplateKey);
    }

    [Fact]
    public async Task TheChosenTemplate_IsTaggedSelected_AndOffersKeep_WhileOthersOfferUse()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        var before = Plain(await owner.GetStringAsync($"{WizardFlow.Template}?show=healthcare"));
        Assert.DoesNotContain("adm-pill\">Selected<", before); // nothing is chosen until there is a draft
        await WizardFlow.PickTemplateAsync(owner, "healthcare");

        var chosen = Plain(await owner.GetStringAsync($"{WizardFlow.Template}?show=healthcare"));
        var other = Plain(await owner.GetStringAsync($"{WizardFlow.Template}?show=software"));

        Assert.Contains(">Keep Healthcare and continue</button>", chosen);
        Assert.Single(Regex.Matches(chosen, @"</a><span class=""adm-pill"">Selected</span>"));
        Assert.Contains($">Use {Catalog.Get("software").Name} and continue</button>", other);
        Assert.DoesNotContain(">Keep ", other);
        Assert.Contains("<a href=\"/admin/welcome?show=healthcare&size=desktop\"", other); // the names box links back to the chosen one
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("%22%3E%3Cscript%3E")]
    [InlineData("../software")]
    public async Task AnUnknownShow_FallsBackToTheTemplateTheDraftNames(string show)
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "healthcare");

        var html = await owner.GetStringAsync($"{WizardFlow.Template}?show={show}&tab=//evil.example/x");

        Assert.Contains("src=\"/?preview=healthcare\"", html);
        Assert.DoesNotContain("evil.example", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public async Task APostedKeyOutsideTheCatalog_IsRefused_AndTheDraftIsUntouched()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var response = await WizardFlow.PickTemplateAsync(owner, "no-such-template");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose one of the templates.", await response.Content.ReadAsStringAsync());
        Assert.Null(await WizardFlow.DraftAsync(app));
    }

    [Fact]
    public async Task OnlyTheTemplateStepAndTheReviewStep_MayFrameThePublicSite()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner);
        static string Csp(HttpResponseMessage r) => string.Join(";", r.Headers.GetValues("Content-Security-Policy"));

        Assert.Contains("frame-src 'self'", Csp(await owner.GetAsync(WizardFlow.Template)));
        Assert.Contains("frame-src 'self'", Csp(await owner.GetAsync(WizardFlow.Review)));
        Assert.DoesNotContain("frame-src", Csp(await owner.GetAsync(WizardFlow.Basics)));
        Assert.DoesNotContain("frame-src", Csp(await owner.GetAsync(WizardFlow.Photo)));
    }

    [Fact]
    public async Task ATemplateWithoutAThumbnail_OffersNoStaticPictureLink()
    {
        var dir = TemplateDirs.WithExtra("plain", n => n["name"] = "Plain");
        try
        {
            using var app = AdminApp.WithTemplates(dir);
            var owner = await app.SignedInClientAsync();

            var html = await owner.GetStringAsync($"{WizardFlow.Template}?show=plain");

            Assert.DoesNotContain("/admin/welcome/thumb/plain", html);
            Assert.DoesNotContain("static picture", html);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
