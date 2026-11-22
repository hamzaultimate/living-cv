using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The Photo step opens without a draft, and a file that was chosen but not uploaded is uploaded as part of the jump.</summary>
public class WizardPhotoJumpTests
{
    private static byte[] Jpeg() => TestImages.Split(SKEncodedImageFormat.Jpeg, 64, 64);

    [Fact]
    public async Task TheStep_OpensWithNoDraft()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(WizardFlow.Photo)).StatusCode);
        Assert.Null(await WizardFlow.DraftOrNullAsync(app));
    }

    [Fact]
    public async Task Jumping_WithAChosenFile_UploadsItFirst_ThenGoesWhereAsked()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var jump = await WizardFlow.JumpFromPhotoAsync(owner, 4, Jpeg());

        Assert.Equal("/admin/welcome/review", WizardFlow.Where(jump));
        var draft = await WizardFlow.DraftOrNullAsync(app);
        Assert.NotNull(draft!.PhotoDraft);
        Assert.Equal("Done", WizardFlow.StepStates(await owner.GetStringAsync(WizardFlow.Photo))[2]);
    }

    [Fact]
    public async Task Jumping_WithNoFile_JustMovesOn_AndAddsNoPhoto()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var jump = await WizardFlow.JumpFromPhotoAsync(owner, 2);

        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(jump));
        Assert.Null((await WizardFlow.DraftOrNullAsync(app))?.PhotoDraft);
    }

    [Fact]
    public async Task AFileThatIsNotAnImage_KeepsTheOwnerOnTheStep_WithTheError_AndSavesNothing()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var jump = await WizardFlow.JumpFromPhotoAsync(owner, 4, Encoding.UTF8.GetBytes("this is not an image"));
        var html = await WizardFlow.BodyAsync(jump);

        Assert.Equal(HttpStatusCode.OK, jump.StatusCode);
        Assert.Contains("id=\"error-summary\"", html);
        Assert.Null((await WizardFlow.DraftOrNullAsync(app))?.PhotoDraft);
    }

    [Fact]
    public async Task ContinueWithNoGoto_GoesToTheReviewStep()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        var token = await AdminApp.GetTokenAsync(owner, WizardFlow.Photo);
        var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };

        var response = await owner.PostAsync(WizardFlow.Photo, content);

        Assert.Equal("/admin/welcome/review", WizardFlow.Where(response));
    }

    [Fact]
    public async Task TheUploadHandler_StillAddsThePhoto_AndStaysOnTheStep()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var upload = await WizardFlow.UploadPhotoAsync(owner, Jpeg());

        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(upload));
        Assert.NotNull((await WizardFlow.DraftOrNullAsync(app))!.PhotoDraft);
    }

    [Fact]
    public async Task TheStep_IsOneCard_WithTheCurrentPhotoBesideTheUpload_AndNoBackButton()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync(WizardFlow.Photo);

        Assert.Single(Regex.Matches(html, @"class=""adm-card adm-wizard"""));
        Assert.Contains("class=\"adm-photo-step\"", html);
        Assert.Single(Regex.Matches(html, @"<form id=""wizard-form"""));
        Assert.Contains("enctype=\"multipart/form-data\"", html);
        Assert.Contains("name=\"goto\" value=\"4\" class=\"adm-btn adm-btn-primary\">Continue</button>", html);
        Assert.DoesNotContain(">Back</a>", html);
        Assert.DoesNotContain("Skip photo and continue", html);
        Assert.DoesNotContain(" required", WizardFlow.InputPattern.Matches(html).Select(m => m.Value).Single(t => t.Contains("type=\"file\"")));
        Assert.Contains("Photo · Step 3 of 4", WebUtility.HtmlDecode(Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value));
    }

    [Fact]
    public async Task TheRemoveButton_IsItsOwnForm_NotNestedInTheStepsForm()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        await WizardFlow.UploadPhotoAsync(owner, Jpeg());

        var html = await owner.GetStringAsync(WizardFlow.Photo);

        var wizardForm = Regex.Match(html, @"<form id=""wizard-form"".*?</form>", RegexOptions.Singleline).Value;
        Assert.DoesNotContain("Remove photo", wizardForm);
        Assert.Contains(">Remove photo</button>", html);
    }
}
