using System.Net;
using System.Text.RegularExpressions;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The card frame every step shares (indicator, status, saved-at line, bottom bar) and the Template step's jump.</summary>
public class WizardFrameTests
{
    [Fact]
    public async Task EveryPage_OffersAllFourSteps_AsSubmitButtons_AfterAHiddenDefaultButton()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        foreach (var (page, next) in new[] { (WizardFlow.Template, "2"), (WizardFlow.Basics, "3"), (WizardFlow.Photo, "4"), (WizardFlow.Review, "4") })
        {
            var html = await owner.GetStringAsync(page);
            var values = WizardFlow.StepValues(html);

            Assert.Equal(next, values[0]); // Enter in a text field submits through the first submit button, so that must be "the next step", never step 1
            Assert.Equal(["1", "2", "3", "4"], values.Skip(1).Take(4));
            Assert.Matches(@"<button type=""submit"" form=""wizard-form"" name=""goto"" value=""" + next + @""" class=""adm-sr"" tabindex=""-1"" aria-hidden=""true"">", html);
        }
    }

    [Fact]
    public async Task EveryStep_SaysItsStatusInWords_FromTheDraft()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        Assert.Equal(["To do", "To do", "Optional", "To do"], WizardFlow.StepStates(await owner.GetStringAsync(WizardFlow.Template)));

        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        Assert.Equal(["Done", "To do", "Optional", "To do"], WizardFlow.StepStates(await owner.GetStringAsync(WizardFlow.Template)));
    }

    [Fact]
    public async Task TheCurrentStep_IsMarkedByAriaCurrent_NotOnlyByColour()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync(WizardFlow.Template);

        Assert.Single(Regex.Matches(html, "aria-current=\"step\""));
        // The current step is a focusable button, so it carries aria-current itself: that is what a keyboard or screen-reader user lands on.
        Assert.Matches(@"<li class=""adm-step adm-step-todo adm-step-current"">\s*<button type=""submit"" form=""wizard-form"" name=""goto"" value=""1"" class=""adm-step-btn"" aria-current=""step"">", html);
    }

    [Fact]
    public async Task TheTemplateStep_IsOneCard_WithAFormTheIndicatorSubmits()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync(WizardFlow.Template);

        Assert.Single(Regex.Matches(html, @"class=""adm-card adm-wizard"""));
        Assert.Single(Regex.Matches(html, @"<form id=""wizard-form"""));
        Assert.Contains("Template · Step 1 of 4", WebUtility.HtmlDecode(Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value));
    }

    [Fact]
    public async Task TheSavedAtLine_AppearsOnlyWithADraft_AsATimeWithItsZone()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        Assert.DoesNotContain("Draft saved at", await owner.GetStringAsync(WizardFlow.Template));

        await WizardFlow.PickTemplateAsync(owner, "software");

        Assert.Matches(@"Draft saved at <time datetime=""[^""]+"">\d\d:\d\d UTC</time>", await owner.GetStringAsync(WizardFlow.Template));
    }

    [Fact]
    public async Task TheBottomBar_OffersSkipOnAFirstRun_AndStartOverOnlyWithADraft()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var fresh = await owner.GetStringAsync(WizardFlow.Template);
        Assert.Contains(">Skip setup</button>", fresh);
        Assert.DoesNotContain("Start over", fresh);

        await WizardFlow.PickTemplateAsync(owner, "software");
        Assert.Contains(">Start over</button>", await owner.GetStringAsync(WizardFlow.Template));
    }

    [Fact]
    public async Task TheBottomBar_OffersLeaveWithoutPublishing_OnAReRun()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var html = await owner.GetStringAsync(WizardFlow.Template);

        Assert.Contains(">Leave without publishing</button>", html);
        Assert.DoesNotContain(">Skip setup</button>", html);
    }

    [Fact]
    public async Task JumpingFromTheTemplateStep_WithNoDraft_CreatesOneOnTheDefaultTemplate_AndGoesWhereAsked()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var jump = await WizardFlow.JumpAsync(owner, WizardFlow.Template, 3);

        Assert.Equal(HttpStatusCode.Redirect, jump.StatusCode);
        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(jump));
        Assert.Equal(TemplateCatalog.DefaultKey, (await WizardFlow.DraftOrNullAsync(app))!.TemplateKey);
    }

    [Fact]
    public async Task JumpingFromTheTemplateStep_KeepsTheDraftsTemplate()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await WizardFlow.PickTemplateAsync(owner, "healthcare");

        await WizardFlow.JumpAsync(owner, WizardFlow.Template, 4);

        Assert.Equal("healthcare", (await WizardFlow.DraftOrNullAsync(app))!.TemplateKey);
    }

    [Fact]
    public async Task OnAReRun_JumpingFromTheTemplateStep_KeepsTheSitesCurrentTemplate()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, "healthcare");
        Assert.Null(await WizardFlow.DraftOrNullAsync(app)); // published: the draft is gone

        await WizardFlow.JumpAsync(owner, WizardFlow.Template, 2);

        Assert.Equal("healthcare", (await WizardFlow.DraftOrNullAsync(app))!.TemplateKey);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("2.0")]
    public async Task AnUnknownGoto_FallsBackToTheNextStep(string requested)
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        var token = await AdminApp.GetTokenAsync(owner, WizardFlow.Template);

        var response = await AdminApp.PostFormAsync(owner, WizardFlow.Template + "?handler=Jump", token, new Dictionary<string, string> { ["goto"] = requested });

        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(response));
    }

    [Fact]
    public async Task JumpingSavesNothingPublic()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await app.NewClient().GetStringAsync("/"); // the first public request creates the profile row
        var before = WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app));

        await WizardFlow.JumpAsync(owner, WizardFlow.Template, 4);

        Assert.Equal(before, WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app)));
    }

    private static string ContinueSetupHref(string dashboard) =>
        Regex.Match(dashboard, @"<a class=""adm-btn adm-btn-primary"" href=""([^""]+)"">Continue setup</a>").Groups[1].Value;

    [Fact]
    public async Task ContinueSetup_OpensTheFirstStep_WithNoDraft()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        Assert.Equal("/admin/welcome", ContinueSetupHref(await owner.GetStringAsync("/admin")));
    }

    [Fact]
    public async Task ContinueSetup_OpensTheBasics_AfterOnlyATemplate_AndWhileTheyAreIncomplete()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");
        Assert.Equal("/admin/welcome/basics", ContinueSetupHref(await owner.GetStringAsync("/admin")));

        await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 1, new() { ["Form.OwnerName"] = "Half Done" });
        Assert.Equal("/admin/welcome/basics", ContinueSetupHref(await owner.GetStringAsync("/admin")));
    }

    [Fact]
    public async Task ContinueSetup_SkipsTheOptionalPhoto_AndOpensTheReview_OnceTheBasicsAreDone()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner);

        Assert.Equal("/admin/welcome/review", ContinueSetupHref(await owner.GetStringAsync("/admin")));
    }
}
