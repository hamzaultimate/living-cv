using System.Net;
using System.Text.RegularExpressions;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The Review step: a checklist beside the preview, Publish disabled with its reason until the checklist is complete, and a server that refuses an incomplete publish whatever the button says.</summary>
public class WizardReviewTests
{
    private static readonly Regex PublishButton = new(@"<button[^>]*>Publish my site</button>", RegexOptions.Compiled);

    private static async Task ReadyAsync(HttpClient owner, string template = "software")
    {
        await WizardFlow.PickTemplateAsync(owner, template);
        await WizardFlow.SaveBasicsAsync(owner);
        await WizardFlow.SaveChoicesAsync(owner);
    }

    [Fact]
    public async Task TheStep_OpensWithNoDraft_AndSaysWhatIsStillToDo()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var response = await owner.GetAsync(WizardFlow.Review);
        var html = await WizardFlow.BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ready to publish?", html);
        Assert.Contains("Still to do: template, basics.", html); // the address is prefilled from the request, so it counts
        Assert.Null(await WizardFlow.DraftOrNullAsync(app));
    }

    [Fact]
    public async Task Publish_IsDisabled_WithItsReasonBesideIt_UntilTheChecklistIsComplete()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");

        var html = await owner.GetStringAsync(WizardFlow.Review);

        var button = PublishButton.Match(html).Value;
        Assert.Contains("disabled", button);
        Assert.Contains("aria-describedby=\"publish-reason\"", button);
        Assert.Matches(@"<span id=""publish-reason"" class=""adm-hint"">Still to do: basics\.</span>", html);
    }

    [Fact]
    public async Task Publish_IsEnabled_OnceTheTemplate_TheBasicsAndTheAddressAreIn()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await ReadyAsync(owner);

        var html = await owner.GetStringAsync(WizardFlow.Review);

        Assert.DoesNotContain("disabled", PublishButton.Match(html).Value);
        Assert.DoesNotContain("publish-reason", html);
    }

    [Fact]
    public async Task TheChecklist_ListsFourLines_EachLinkingToTheStepThatFixesIt_AndThePhotoIsOptional()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var html = await owner.GetStringAsync(WizardFlow.Review);
        var list = Regex.Match(html, @"<ul class=""adm-readiness"">.*?</ul>", RegexOptions.Singleline).Value;

        Assert.Equal(4, Regex.Matches(list, "<li\\b").Count);
        foreach (var path in new[] { "/admin/welcome", "/admin/welcome/basics", "/admin/welcome/photo", "/admin/welcome/review" })
            Assert.Contains($"href=\"{path}\"", list);
        Assert.Contains(">Optional</span>", list);
        Assert.Contains("To do", list);
    }

    [Fact]
    public async Task AnIncompletePublish_IsRefusedByTheServer_EvenWhenTheButtonIsBypassed_AndReturnsToReview()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software"); // no basics
        var before = WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app));

        var publish = await WizardFlow.PublishAsync(owner);

        Assert.Equal(HttpStatusCode.Redirect, publish.StatusCode);
        Assert.Equal("/admin/welcome/review", WizardFlow.Where(publish));
        var profile = await WizardFlow.ProfileAsync(app);
        Assert.Null(profile.OnboardingCompletedAt);
        Assert.Equal(before, WizardFlow.PublicFingerprint(profile));
        Assert.NotNull(await WizardFlow.DraftOrNullAsync(app)); // the draft is kept
        Assert.Contains("Still to do: basics.", await owner.GetStringAsync(WizardFlow.Review));
    }

    [Fact]
    public async Task AnInvalidEmail_IsSaved_ButRefusedAtPublishing()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(email: "nope"));

        var publish = await WizardFlow.PublishAsync(owner);

        Assert.Equal("/admin/welcome/review", WizardFlow.Where(publish));
        Assert.Null((await WizardFlow.ProfileAsync(app)).OnboardingCompletedAt);
        Assert.Contains("Still needed: email.", await owner.GetStringAsync(WizardFlow.Review));
    }

    [Fact]
    public async Task AnAddressThatIsNotAnAddress_IsSaved_ButRefusedAtPublishing()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner);

        var publish = await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields("jordan.example"));

        Assert.Equal("/admin/welcome/review", WizardFlow.Where(publish));
        Assert.Equal("jordan.example", (await WizardFlow.DraftOrNullAsync(app))!.SiteUrl);
        Assert.Null((await WizardFlow.ProfileAsync(app)).OnboardingCompletedAt);
    }

    [Fact]
    public async Task ACompletePublish_StillWorks_FromAFirstRun()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await ReadyAsync(owner, "healthcare");

        var publish = await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields("https://jordan.example"));

        Assert.Equal("/admin", WizardFlow.Where(publish));
        var profile = await WizardFlow.ProfileAsync(app);
        Assert.NotNull(profile.OnboardingCompletedAt);
        Assert.Equal("healthcare", profile.TemplateKey);
    }

    [Fact]
    public async Task Jumping_SavesTheChoicesUnderTheHardRulesOnly()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var jump = await WizardFlow.JumpAsync(owner, WizardFlow.Review, 2, WizardFlow.ReviewFields("not a url"));

        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(jump));
        Assert.Equal("not a url", (await WizardFlow.DraftOrNullAsync(app))!.SiteUrl);
    }

    [Fact]
    public async Task ATooLongAddress_BlocksTheJump()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var jump = await WizardFlow.JumpAsync(owner, WizardFlow.Review, 2, WizardFlow.ReviewFields("https://" + new string('a', 400) + ".example"));

        Assert.Equal(HttpStatusCode.OK, jump.StatusCode);
        Assert.Contains("id=\"error-summary\"", await WizardFlow.BodyAsync(jump));
        Assert.Null(await WizardFlow.DraftOrNullAsync(app));
    }

    [Fact]
    public async Task AnAccentTheTemplateDoesNotOffer_IsStillRefused_OnAJump()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var jump = await WizardFlow.JumpAsync(owner, WizardFlow.Review, 2, WizardFlow.ReviewFields(accent: "no-such-accent"));

        Assert.Equal(HttpStatusCode.OK, jump.StatusCode);
        Assert.Contains("That choice is not available for this template.", await WizardFlow.BodyAsync(jump));
    }

    [Fact]
    public async Task TheSearchTab_ShowsTheSearchResultCard_InPlaceOfThePreviewFrame()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await ReadyAsync(owner);

        var page = await owner.GetStringAsync(WizardFlow.Review);
        var search = await owner.GetStringAsync(WizardFlow.Review + "?tab=search");

        Assert.Contains("<iframe", page);
        Assert.DoesNotContain("adm-serp-title", page);
        Assert.DoesNotContain("<iframe", search);
        Assert.Contains("adm-serp-title", search);
        Assert.Contains("How it may appear in search results", search);
        Assert.Contains("tab=search", page); // the tab is offered next to the page tabs
        Assert.Matches(@"<a href=""[^""]*tab=search[^""]*"" aria-current=""page"">Search result</a>", search);
    }

    [Theory]
    [InlineData("%3Cscript%3Ealert(1)%3C/script%3E")]
    [InlineData("/no-such-page")]
    [InlineData("SEARCH")]
    public async Task AnUnknownTab_FallsBackToTheHomePage_AndIsNeverReflected(string tab)
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await ReadyAsync(owner);

        var html = await owner.GetStringAsync(WizardFlow.Review + "?tab=" + tab);

        Assert.Contains("<iframe", html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("adm-serp-title", html);
    }

    [Fact]
    public async Task TheStep_IsASplitLayout_InOneCard_WithTheFormBesideThePreview()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync(WizardFlow.Review);

        Assert.Single(Regex.Matches(html, @"class=""adm-card adm-wizard"""));
        Assert.Contains("class=\"adm-review\"", html);
        Assert.Single(Regex.Matches(html, @"<form id=""wizard-form"""));
        Assert.Contains("Review and launch · Step 4 of 4", WebUtility.HtmlDecode(Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value));
        Assert.Contains(">Save and update preview</button>", html);
        Assert.DoesNotContain(">Back</a>", html);
    }

    [Fact]
    public async Task TheIndicator_StillShowsTheReviewStepAsToDo_NeverDone()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await ReadyAsync(owner);

        var states = WizardFlow.StepStates(await owner.GetStringAsync(WizardFlow.Review));

        Assert.Equal(["Done", "Done", "Optional", "To do"], states);
    }
}
