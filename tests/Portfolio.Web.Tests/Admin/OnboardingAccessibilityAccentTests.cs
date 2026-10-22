using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Seeding;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>What can be checked about the wizard's accessibility from its markup and stylesheet, the accent on the public site, and the first-run seeding rule.</summary>
public class OnboardingAccessibilityAccentTests
{
    private static async Task<(AdminApp App, HttpClient Owner)> ReadyAsync()
    {
        var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        await WizardFlow.SaveBasicsAsync(owner);
        return (app, owner);
    }

    private static List<string> Tags(string html, string name) =>
        Regex.Matches(html, $@"<{name}\b[^>]*>", RegexOptions.IgnoreCase).Select(m => m.Value).ToList();

    private static string? Attr(string tag, string attr) =>
        Regex.Match(tag, $@"\s{attr}=""([^""]*)""").Groups[1] is { Success: true } g ? g.Value : null;

    // ---------------------------------------------------------------- markup

    [Fact]
    public async Task EveryVisibleFormControl_HasARealLabelBoundByForAndId()
    {
        var (app, owner) = await ReadyAsync();
        using var _ = app;
        foreach (var path in WizardFlow.Pages)
        {
            var html = await owner.GetStringAsync(path);
            var labels = Regex.Matches(html, @"<label\b[^>]*\sfor=""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
            var controls = WizardFlow.InputPattern.Matches(html).Select(m => m.Value)
                .Where(t => Attr(t, "type") is not ("hidden" or "submit" or "button")).ToList();
            foreach (var control in controls)
            {
                var id = Attr(control, "id");
                Assert.False(string.IsNullOrEmpty(id), $"{path}: control without an id: {control}");
                Assert.True(labels.Contains(id!), $"{path}: no <label for=\"{id}\">");
            }
            Assert.NotEmpty(controls);
            Assert.Equal(labels.Count, labels.Distinct().Count());
            // And no label points at something that is not there.
            foreach (var label in labels) Assert.Contains($"id=\"{label}\"", html);
        }
    }

    [Fact]
    public async Task EveryStep_HasAnOrderedProgressListWithOneCurrentStep_AMainLandmarkAndALiveRegion()
    {
        var (app, owner) = await ReadyAsync();
        using var _ = app;
        var current = new[] { 1, 2, 3, 4 };
        for (var i = 0; i < 4; i++)
        {
            var html = await owner.GetStringAsync(WizardFlow.Pages[i]);

            var list = Regex.Match(html, @"<ol class=""adm-steps"">(.*?)</ol>", RegexOptions.Singleline);
            Assert.True(list.Success, WizardFlow.Pages[i]);
            Assert.Equal(4, Regex.Matches(list.Value, "<li\\b").Count);
            Assert.Single(Regex.Matches(list.Value, "aria-current=\"step\""));
            Assert.Matches(new Regex($@"<li aria-current=""step"">\s*<span class=""adm-step-num"">{current[i]}</span>"), list.Value);
            Assert.Contains("<nav aria-label=\"Setup progress\">", html);
            Assert.Matches(new Regex($@"<div id=""wizard-status"" class=""adm-sr"" role=""status"" aria-live=""polite"" aria-atomic=""true"">Step {current[i]} of 4: "), html);
            Assert.Contains("<main id=\"main-content\"", html);
            Assert.Contains("<html lang=\"en\">", html);
            Assert.Contains($"Step {current[i]} of 4", Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value);
        }
    }

    [Fact]
    public async Task TheStepHeading_TakesFocusOnLoad_AndOnlyOneElementClaimsAutofocus()
    {
        var (app, owner) = await ReadyAsync();
        using var _ = app;
        foreach (var path in WizardFlow.Pages)
        {
            var html = await owner.GetStringAsync(path);

            Assert.Matches(new Regex(@"<h1 id=""step-heading"" tabindex=""-1"" autofocus=""autofocus"">"), html);
            Assert.Single(Regex.Matches(html, @"\sautofocus\b"));
            Assert.Single(Regex.Matches(html, "<h1\\b"));
        }
    }

    [Fact]
    public async Task OnAnError_TheAlertTakesFocusInsteadOfTheHeading_AndTheFieldIsMarkedInvalid()
    {
        var (app, owner) = await ReadyAsync();
        using var _ = app;

        var html = await (await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(email: "nope"))).Content.ReadAsStringAsync();

        Assert.Matches(new Regex(@"<div id=""error-summary""[^>]*role=""alert""[^>]*tabindex=""-1""[^>]*autofocus"), html);
        Assert.Matches(new Regex(@"<h1 id=""step-heading"" tabindex=""-1"">"), html);
        Assert.Single(Regex.Matches(html, @"\sautofocus\b"));
        var email = Tags(html, "input").Single(t => Attr(t, "id") == "Form_Email");
        Assert.Equal("true", Attr(email, "aria-invalid"));
        Assert.Contains("Enter a valid email address.", html);
        Assert.Matches(new Regex(@"aria-describedby=""email-hint Form_Email-error"""), html);
    }

    [Fact]
    public async Task RelatedChoices_AreGroupedInFieldsetsWithLegends()
    {
        var (app, owner) = await ReadyAsync();
        using var _ = app;

        var template = await owner.GetStringAsync(WizardFlow.Template);
        var review = await owner.GetStringAsync(WizardFlow.Review);
        var basics = await owner.GetStringAsync(WizardFlow.Basics);

        Assert.Contains("<legend>Choose a template</legend>", template);
        foreach (var legend in new[] { "Where it will live", "Accent colour", "Starting content" })
            Assert.Contains($"<legend>{legend}</legend>", review);
        Assert.Contains("<legend>About you</legend>", basics);
        foreach (var html in new[] { template, review, basics })
            Assert.Equal(Regex.Matches(html, "<fieldset\\b").Count, Regex.Matches(html, "<legend>").Count);
    }

    [Fact]
    public async Task TheMarkup_NeedsNoScriptOrInlineStyle_SoTheStrictAdminPolicyHolds()
    {
        var (app, owner) = await ReadyAsync();
        using var _ = app;
        foreach (var path in WizardFlow.Pages)
        {
            var html = await owner.GetStringAsync(path);
            Assert.DoesNotContain("<script", html);
            Assert.DoesNotMatch(new Regex(@"\sstyle="""), html);
            Assert.DoesNotMatch(new Regex(@"\son\w+="""), html);
            Assert.DoesNotContain("<style", html);
        }
    }

    [Fact]
    public async Task EveryImage_HasAltText_AndDecorativeSwatchesAreHidden()
    {
        var (app, owner) = await ReadyAsync();
        using var _ = app;
        foreach (var path in WizardFlow.Pages)
        {
            var html = await owner.GetStringAsync(path);
            foreach (var img in Tags(html, "img")) Assert.False(string.IsNullOrWhiteSpace(Attr(img, "alt")), $"{path}: {img}");
            foreach (var svg in Tags(html, "svg")) Assert.Equal("true", Attr(svg, "aria-hidden"));
        }
    }

    [Fact]
    public async Task TheProgressIndicator_OnlyLinksToStepsTheOwnerCanOpen()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var fresh = await owner.GetStringAsync(WizardFlow.Template);
        Assert.DoesNotContain("href=\"/admin/welcome/basics\"><span class=\"adm-step-num\"", fresh);

        await WizardFlow.PickTemplateAsync(owner, "software");
        var afterTemplate = await owner.GetStringAsync(WizardFlow.Template);
        Assert.Contains("href=\"/admin/welcome/basics\"><span class=\"adm-step-num\"", afterTemplate);
        Assert.DoesNotContain("href=\"/admin/welcome/review\"><span class=\"adm-step-num\"", afterTemplate);

        await WizardFlow.SaveBasicsAsync(owner);
        Assert.Contains("href=\"/admin/welcome/review\"><span class=\"adm-step-num\"", await owner.GetStringAsync(WizardFlow.Template));
    }

    [Fact]
    public void TheAdminStylesheet_HasVisibleFocus_ReducedMotionAndANarrowScreenLayout()
    {
        var css = File.ReadAllText(Path.Combine(TemplateDirs.Web, "wwwroot", "css", "admin.css"));

        Assert.Contains("prefers-reduced-motion: reduce", css);
        Assert.Matches(new Regex(@"prefers-reduced-motion: reduce\)[^}]*\{[^}]*transition: none !important; animation: none !important"), css);
        Assert.Contains(".adm h1[tabindex=\"-1\"]:focus { outline: 3px solid", css);
        Assert.Contains(".adm-pick input[type=\"radio\"]:focus-visible", css);
        Assert.Contains("max-width: 40rem", css);
        Assert.Contains(".adm-steps li { flex-basis: 100%; }", css);
        Assert.Matches(new Regex(@"\.adm-thumb \{[^}]*width: 100%"), css);
        Assert.Matches(new Regex(@"\.adm-preview-mobile \{[^}]*max-width: 100%"), css);
        Assert.DoesNotContain("outline: none", css);
        Assert.DoesNotContain("outline: 0", css);
    }

    [Fact]
    public void TheWizardStyles_AreOnlyInTheAdminStylesheet_NotTheTailwindBuild()
    {
        var app = File.ReadAllText(Path.Combine(TemplateDirs.Web, "wwwroot", "css", "app.css"));
        var config = File.ReadAllText(Path.Combine(TemplateDirs.Web, "tailwind.config.js"));

        Assert.DoesNotContain("adm-steps", app);
        Assert.Contains("'!./Pages/Admin/**'", config);
    }

    // ---------------------------------------------------------------- accent on the public site

    [Fact]
    public async Task WithNoAccentPicked_ThePublicPageHasNoAccentOverride()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var html = await app.NewClient().GetStringAsync("/");

        Assert.DoesNotContain("--c-accent", html);
        Assert.DoesNotContain("<style", html);
        Assert.Contains("<link rel=\"stylesheet\" href=\"/css/app.", html);
        Assert.Matches(new Regex("app\\.[^\"]+\\.css\" />\n</head>"), html);
    }

    [Fact]
    public async Task APickedAccent_IsAppliedAsCssVariablesOnly_ForTheTemplateThatOffersIt()
    {
        var (app, owner) = await ReadyAsync();
        using var _ = app;
        await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields(accent: "rose"));

        var html = await app.NewClient().GetStringAsync("/");

        var style = Regex.Match(html, "<style>(.*?)</style>").Groups[1].Value;
        Assert.Equal(":root:root:root:not([data-theme=\"light\"]){--c-accent:251 113 133;--c-accent-strong:253 164 175}:root:root:root[data-theme=\"light\"]{--c-accent:190 18 60;--c-accent-strong:159 18 57}", style);
        Assert.Equal("rose", (await WizardFlow.ProfileAsync(app)).AccentColor);
        Assert.Single(Regex.Matches(html, "<style"));
    }

    [Fact]
    public async Task AStoredAccentTheTemplateDoesNotOffer_IsIgnoredByThePublicPages()
    {
        var (app, owner) = await ReadyAsync();
        using var _ = app;
        await WizardFlow.PublishAsync(owner);
        foreach (var stored in new[] { "emerald", "bogus", "#ff0000", "rose;}body{display:none", "ROSE" })
        {
            using (var db = app.CreateDbContext())
            {
                (await db.SiteProfiles.SingleAsync()).AccentColor = stored;
                await db.SaveChangesAsync();
            }
            app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();

            var html = await app.NewClient().GetStringAsync("/");

            Assert.DoesNotContain("--c-accent", html);
            Assert.DoesNotContain("display:none", html);
        }
    }

    [Fact]
    public async Task SwitchingToATemplateWithoutThePickedAccent_FallsBackToItsOwnColour_WithoutDeletingTheChoice()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner);
        await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields(accent: "emerald"));
        Assert.Contains("--c-accent:52 211 153", await app.NewClient().GetStringAsync("/"));

        await DemoSite.SwitchTemplateAsync(owner, "healthcare");

        Assert.DoesNotContain("--c-accent", await app.NewClient().GetStringAsync("/"));
        Assert.Equal("emerald", (await WizardFlow.ProfileAsync(app)).AccentColor); // kept, so it applies again if the owner switches back
    }

    // ---------------------------------------------------------------- the first-run seeding rule

    [Theory]
    [InlineData(true, false, false, "IfEmpty", true)]    // a claimable new install with no profile row yet
    [InlineData(true, true, false, "IfEmpty", true)]     // the row exists, wizard not finished
    [InlineData(true, true, true, "IfEmpty", false)]     // wizard finished: seeds as before
    [InlineData(false, true, false, "IfEmpty", false)]   // nothing can claim the site (no key): a development checkout seeds as before
    [InlineData(true, true, false, "Authoritative", false)] // authoritative mode always seeds
    public async Task StartupSeeding_WaitsForTheWizard_OnlyOnAClaimableUnfinishedInstall(bool keyConfigured, bool hasRow, bool completed, string mode, bool deferred)
    {
        using var test = new TestDatabase();
        using (var db = test.CreateContext())
        {
            if (hasRow)
            {
                db.SiteProfiles.Add(new Portfolio.Data.Entities.SiteProfile { OnboardingCompletedAt = completed ? DateTimeOffset.UtcNow : null });
                await db.SaveChangesAsync();
            }
        }
        using var check = test.CreateContext();

        var result = await FirstRun.ShouldDeferContentSeedAsync(check, keyConfigured, Enum.Parse<SeedMode>(mode));

        Assert.Equal(deferred, result);
    }

    [Fact]
    public async Task AnExistingOwner_CountsAsClaimable_EvenAfterTheSetupKeyWasRemoved()
    {
        using var test = new TestDatabase();
        using (var db = test.CreateContext())
        {
            db.OwnerAccounts.Add(new Portfolio.Data.Entities.OwnerAccount { UserName = "owner", PasswordHash = "x", SecurityStamp = "s" });
            db.SiteProfiles.Add(new Portfolio.Data.Entities.SiteProfile());
            await db.SaveChangesAsync();
        }
        using var check = test.CreateContext();

        Assert.True(await FirstRun.ShouldDeferContentSeedAsync(check, setupKeyConfigured: false, SeedMode.IfEmpty));
    }
}
