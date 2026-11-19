using System.Net;
using System.Text.RegularExpressions;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The Basics step opens without a draft, saves what was typed (even half of it) when the owner jumps, and refuses only what breaks a hard rule.</summary>
public class WizardBasicsJumpTests
{
    [Fact]
    public async Task TheStep_OpensWithNoDraft_AndNoTemplateChosen()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var response = await owner.GetAsync(WizardFlow.Basics);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await WizardFlow.DraftOrNullAsync(app)); // reading saves nothing
    }

    [Fact]
    public async Task Jumping_SavesWhatWasTyped_IncludingIncompleteInput_AndGoesToTheRequestedStep()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await WizardFlow.PickTemplateAsync(owner, "healthcare");

        var jump = await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 4, new() { ["Form.OwnerName"] = "Part Person", ["Form.Email"] = "half@" });

        Assert.Equal("/admin/welcome/review", WizardFlow.Where(jump));
        var draft = await WizardFlow.DraftOrNullAsync(app);
        Assert.Equal("Part Person", draft!.OwnerName);
        Assert.Equal("half@", draft.Email); // not a valid address, and that is fine until publishing
        Assert.Null(draft.JobTitle);
        Assert.Equal("healthcare", draft.TemplateKey);
    }

    [Fact]
    public async Task Jumping_WithNoDraft_CreatesOne_AndSavesTheTypedFields()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var jump = await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 3, new() { ["Form.OwnerName"] = "First Typed" });

        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(jump));
        Assert.Equal("First Typed", (await WizardFlow.DraftOrNullAsync(app))!.OwnerName);
    }

    [Fact]
    public async Task ContinueWithoutAGoto_GoesToThePhotoStep()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var response = await WizardFlow.SaveBasicsAsync(owner);

        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(response));
    }

    [Theory]
    [InlineData("99")]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task AnUnknownGoto_FallsBackToThePhotoStep(string requested)
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        var token = await AdminApp.GetTokenAsync(owner, WizardFlow.Basics);

        var response = await AdminApp.PostFormAsync(owner, WizardFlow.Basics, token, new Dictionary<string, string> { ["goto"] = requested, ["Form.OwnerName"] = "X" });

        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(response));
    }

    [Fact]
    public async Task MarkupInAField_BlocksTheJump_ShowsTheError_AndSavesNothing()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var response = await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 4, new() { ["Form.OwnerName"] = "<b>Jordan</b>" });
        var html = await WizardFlow.BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"error-summary\"", html);
        Assert.Contains("Use plain text only", html);
        Assert.Null(await WizardFlow.DraftOrNullAsync(app));
    }

    [Fact]
    public async Task ATooLongSummary_BlocksTheJump()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var response = await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 3, new() { ["Form.Summary"] = new string('a', BasicsForm.SummaryMax + 1) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await WizardFlow.DraftOrNullAsync(app));
    }

    [Fact]
    public async Task EmptyFields_AreSaved_AsEmpty_NotAsErrors()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(name: "Keep Me"));

        var jump = await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 1, new() { ["Form.OwnerName"] = "", ["Form.Email"] = "" });

        Assert.Equal("/admin/welcome", WizardFlow.Where(jump));
        var draft = await WizardFlow.DraftOrNullAsync(app);
        Assert.Null(draft!.OwnerName);
        Assert.Null(draft.Email);
    }

    [Fact]
    public async Task WhenNothingHasBeenTypedYet_TheTemplatesSuggestionsStillFillTheTitleAndSummary()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await WizardFlow.PickTemplateAsync(owner, "software");

        var html = await owner.GetStringAsync(WizardFlow.Basics);

        Assert.Contains("value=\"Software Engineer\"", Regex.Match(html, @"<input[^>]*id=""Form_JobTitle""[^>]*>").Value);
    }

    [Fact]
    public async Task TheStatus_ChangesFromToDo_ToNeedsAttention_ToDone_AsTheBasicsFillIn()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        Assert.Equal("To do", WizardFlow.StepStates(await owner.GetStringAsync(WizardFlow.Basics))[1]);

        await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 2, new() { ["Form.OwnerName"] = "Jordan" });
        Assert.Equal("Needs attention", WizardFlow.StepStates(await owner.GetStringAsync(WizardFlow.Basics))[1]);

        await WizardFlow.SaveBasicsAsync(owner);
        Assert.Equal("Done", WizardFlow.StepStates(await owner.GetStringAsync(WizardFlow.Basics))[1]);
    }

    [Fact]
    public async Task EnterInATextField_SubmitsThroughTheHiddenDefaultButton_NotThroughStepOne()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync(WizardFlow.Basics);

        Assert.Equal("3", WizardFlow.StepValues(html)[0]);
        Assert.Equal("3", WizardFlow.StepValues(html)[^1]); // and the card's own Continue is the same button
    }

    [Fact]
    public async Task TheStep_IsOneCard_WithItsFormAndAContinueButton_AndNoBackButton()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync(WizardFlow.Basics);

        Assert.Single(Regex.Matches(html, @"class=""adm-card adm-wizard"""));
        Assert.Single(Regex.Matches(html, @"<form id=""wizard-form"""));
        Assert.Contains(@"name=""goto"" value=""3"" class=""adm-btn adm-btn-primary"">Continue</button>", html);
        Assert.DoesNotContain(">Back</a>", html);
        Assert.DoesNotContain("Continue to photo", html);
        Assert.Contains("Basics · Step 2 of 4", WebUtility.HtmlDecode(Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value));
    }

    [Fact]
    public async Task TheFields_AreNotMarkedRequired_BecauseOnlyPublishingNeedsThem()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync(WizardFlow.Basics);

        foreach (var tag in WizardFlow.InputPattern.Matches(html).Select(m => m.Value).Where(t => t.Contains("Form_")))
            Assert.DoesNotContain(" required", tag);
        Assert.Contains("needed before you can publish", html);
    }

    [Fact]
    public async Task AFieldThatBreaksARule_KeepsWhatTheOwnerTyped_OnTheRedisplayedPage()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await WizardFlow.BodyAsync(await WizardFlow.JumpAsync(owner, WizardFlow.Basics, 4,
            new() { ["Form.OwnerName"] = "<b>x</b>", ["Form.Location"] = "Bristol" }));

        Assert.Contains("value=\"Bristol\"", html);
    }
}
