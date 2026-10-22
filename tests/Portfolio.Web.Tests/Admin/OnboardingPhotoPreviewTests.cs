using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The wizard's photo step (through the existing upload hardening) and the owner-only draft preview.</summary>
public class OnboardingPhotoPreviewTests
{
    private static string Drafts(AdminApp app) => Path.Combine(app.UploadsPath, ".drafts");

    private static string[] DraftFiles(AdminApp app) => Directory.Exists(Drafts(app)) ? Directory.GetFiles(Drafts(app)) : [];

    private static async Task<HttpClient> AtPhotoStepAsync(AdminApp app, string template = "healthcare")
    {
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, template);
        await WizardFlow.SaveBasicsAsync(owner);
        return owner;
    }

    // ---------------------------------------------------------------- photo

    [Fact]
    public async Task AValidPhoto_IsReEncodedIntoTheHiddenDraftArea_AndIsNotPublic()
    {
        using var app = new AdminApp();
        var owner = await AtPhotoStepAsync(app);
        var before = WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app));
        var original = TestImages.WithJpegSegments(TestImages.Split(SKEncodedImageFormat.Jpeg, 64, 64), TestImages.JpegComment("SECRET-GPS"));

        var response = await WizardFlow.UploadPhotoAsync(owner, original, "holiday photo.JPG");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var file = Assert.Single(DraftFiles(app));
        Assert.Matches(new Regex(@"^[0-9a-f]{32}\.jpg$"), Path.GetFileName(file));
        Assert.DoesNotContain("SECRET-GPS", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(file)));
        Assert.Equal(Path.GetFileName(file), (await WizardFlow.DraftAsync(app))!.PhotoDraft);
        Assert.Equal(before, WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app)));
        Assert.Empty(Directory.GetFiles(app.UploadsPath));
        Assert.DoesNotContain("/uploads/", await app.NewClient().GetStringAsync("/"));
        var page = await owner.GetStringAsync(WizardFlow.Photo);
        Assert.Contains("/admin/welcome/draft-photo?v=", page);
        Assert.Contains("alt=\"Your draft profile photo\"", page);
    }

    [Fact]
    public async Task TheDraftPhoto_IsServedOnlyToTheOwner_AsAnImage()
    {
        using var app = new AdminApp();
        var owner = await AtPhotoStepAsync(app);
        await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Png, 40, 40), "a.png", "image/png");

        var served = await owner.GetAsync("/admin/welcome/draft-photo");
        var anonymous = await app.NewClient().GetAsync("/admin/welcome/draft-photo");

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType!.MediaType);
        Assert.NotNull(SKBitmap.Decode(await served.Content.ReadAsByteArrayAsync()));
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/admin/login", anonymous.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task TheDraftPhotoUrl_TakesNoParameter_SoNothingInTheRequestSelectsAFile()
    {
        using var app = new AdminApp();
        var owner = await AtPhotoStepAsync(app);
        await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Png, 40, 40), "a.png", "image/png");
        File.WriteAllText(Path.Combine(app.UploadsPath, "secret.txt"), "secret");
        var expected = await owner.GetByteArrayAsync("/admin/welcome/draft-photo");

        foreach (var url in new[] { "/admin/welcome/draft-photo?name=../secret.txt", "/admin/welcome/draft-photo?file=..%2fsecret.txt", "/admin/welcome/draft-photo/../secret.txt" })
        {
            var response = await owner.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.OK) Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync());
            else Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task ReplacingTheDraftPhoto_DeletesTheOldFile_AndRemovingDeletesTheLast()
    {
        using var app = new AdminApp();
        var owner = await AtPhotoStepAsync(app);
        await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Jpeg, 40, 40));
        var first = Assert.Single(DraftFiles(app));

        await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Png, 50, 50), "b.png", "image/png");

        var second = Assert.Single(DraftFiles(app));
        Assert.NotEqual(first, second);
        Assert.False(File.Exists(first));

        var remove = await WizardFlow.PostAsync(owner, WizardFlow.Photo + "?handler=Remove", new Dictionary<string, string>(), WizardFlow.Photo);
        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        Assert.Empty(DraftFiles(app));
        Assert.Null((await WizardFlow.DraftAsync(app))!.PhotoDraft);
        Assert.Contains("monogram", await owner.GetStringAsync(WizardFlow.Photo));
    }

    [Fact]
    public async Task AbandonedDraftPhotos_AreCleanedUp_OnTheNextUpload()
    {
        using var app = new AdminApp();
        var owner = await AtPhotoStepAsync(app);
        await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Jpeg, 40, 40));
        Directory.CreateDirectory(Drafts(app));
        var orphan = Path.Combine(Drafts(app), new string('a', 32) + ".jpg");
        File.WriteAllBytes(orphan, [1, 2, 3]);
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddDays(-5));

        await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Jpeg, 41, 41));

        Assert.False(File.Exists(orphan));
        Assert.Single(DraftFiles(app));
    }

    public static TheoryData<string, string> HostileUploads()
    {
        var data = new TheoryData<string, string>();
        data.Add("svg", "x.svg");
        data.Add("text-as-jpg", "x.jpg");
        data.Add("html-as-png", "x.png");
        data.Add("truncated-jpeg", "x.jpg");
        data.Add("path-name-with-text", "../../evil.jpg");
        data.Add("empty", "x.jpg");
        data.Add("gif", "x.gif");
        return data;
    }

    private static byte[] HostileBytes(string kind) => kind switch
    {
        "svg" => System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"),
        "text-as-jpg" => System.Text.Encoding.UTF8.GetBytes("this is not an image"),
        "html-as-png" => System.Text.Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"),
        "truncated-jpeg" => TestImages.Split(SKEncodedImageFormat.Jpeg, 64, 64)[..40],
        "path-name-with-text" => System.Text.Encoding.UTF8.GetBytes("not an image"),
        "gif" => System.Text.Encoding.ASCII.GetBytes("GIF89a\x01\0\x01\0\0\0\0;"),
        _ => [],
    };

    [Theory]
    [MemberData(nameof(HostileUploads))]
    public async Task HostileUploads_AreRefusedByTheExistingHardening_AndLeaveNoFileBehind(string kind, string fileName)
    {
        using var app = new AdminApp();
        var owner = await AtPhotoStepAsync(app);
        var before = WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app));
        var draftBefore = (await WizardFlow.ProfileAsync(app)).OnboardingDraft;

        var response = await WizardFlow.UploadPhotoAsync(owner, HostileBytes(kind), fileName, "image/jpeg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("role=\"alert\"", await response.Content.ReadAsStringAsync());
        Assert.Empty(DraftFiles(app));
        Assert.Empty(Directory.GetFiles(app.UploadsPath, "*", SearchOption.AllDirectories));
        Assert.Equal(before, WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app)));
        Assert.Equal(draftBefore, (await WizardFlow.ProfileAsync(app)).OnboardingDraft);
    }

    [Fact]
    public async Task AValidImageWithAHostileFileName_IsStoredUnderAGeneratedName_NeverTheClientsPath()
    {
        using var app = new AdminApp();
        var owner = await AtPhotoStepAsync(app);

        var response = await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Jpeg, 40, 40), "../../../etc/evil.php.jpg");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Matches(new Regex(@"^[0-9a-f]{32}\.jpg$"), Path.GetFileName(Assert.Single(DraftFiles(app))));
        Assert.Equal(1, Directory.GetFiles(app.UploadsPath, "*", SearchOption.AllDirectories).Length);
        Assert.False(File.Exists(Path.Combine(app.UploadsPath, "..", "evil.php.jpg")));
    }

    [Fact]
    public async Task AnOversizedUpload_IsRefused_AndLeavesNoFile()
    {
        using var app = AdminApp.Create(AdminApp.SetupKey, new Dictionary<string, string?> { ["Uploads:MaxBytes"] = "20000" });
        var owner = await AtPhotoStepAsync(app);

        var response = await WizardFlow.UploadPhotoAsync(owner, TestImages.Noise(300, 300));

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.RequestEntityTooLarge);
        Assert.Empty(DraftFiles(app));
    }

    [Fact]
    public async Task Publishing_PromotesThePhotoToAPublicFile_ForBothThemes_AndRemovesTheDraft()
    {
        using var app = new AdminApp();
        var owner = await AtPhotoStepAsync(app);
        await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Jpeg, 64, 64));

        await WizardFlow.PublishAsync(owner);

        var profile = await WizardFlow.ProfileAsync(app);
        Assert.Matches(new Regex(@"^/uploads/[0-9a-f]{32}\.jpg$"), profile.PhotoUrl);
        Assert.Equal(profile.PhotoUrl, profile.PhotoUrlLight);
        Assert.Empty(DraftFiles(app));
        var anonymous = app.NewClient();
        var image = await anonymous.GetAsync(profile.PhotoUrl!);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType!.MediaType);
        var home = await anonymous.GetStringAsync("/");
        Assert.Contains($"src=\"{profile.PhotoUrl}\"", home);
        Assert.Contains("profile-photo--light", home);
        Assert.DoesNotContain("draft-photo", home);
    }

    [Fact]
    public async Task WithoutAPhoto_TheSiteShowsTheMonogram_NotThePlaceholderAvatar()
    {
        using var app = new AdminApp();
        var owner = await AtPhotoStepAsync(app, "software");

        await WizardFlow.PublishAsync(owner);

        var profile = await WizardFlow.ProfileAsync(app);
        Assert.Null(profile.PhotoUrl);
        var home = await app.NewClient().GetStringAsync("/");
        Assert.Contains(">JA<", home);
        Assert.DoesNotContain("avatar.svg", home);
    }

    [Fact]
    public async Task APhotoUploadedBeforeTheWizardFinished_IsKeptWhenNoNewPhotoIsAdded()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        var token = await AdminApp.GetTokenAsync(owner, "/admin/photo");
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        var file = new ByteArrayContent(TestImages.Split(SKEncodedImageFormat.Jpeg, 40, 40));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "Photo", "x.jpg");
        Assert.Equal(HttpStatusCode.Redirect, (await owner.PostAsync("/admin/photo?handler=Upload", form)).StatusCode);
        var existing = (await WizardFlow.ProfileAsync(app)).PhotoUrl;
        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        await WizardFlow.SaveBasicsAsync(owner);

        await WizardFlow.PublishAsync(owner);

        Assert.Equal(existing, (await WizardFlow.ProfileAsync(app)).PhotoUrl);
        Assert.True(File.Exists(Path.Combine(app.UploadsPath, existing!["/uploads/".Length..])));
    }

    [Fact]
    public async Task ReRunning_WithANewPhoto_ReplacesAndDeletesThePreviousPublicFile()
    {
        using var app = new AdminApp();
        var owner = await AtPhotoStepAsync(app);
        await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Jpeg, 40, 40));
        await WizardFlow.PublishAsync(owner);
        var old = (await WizardFlow.ProfileAsync(app)).PhotoUrl!;

        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        await WizardFlow.SaveBasicsAsync(owner);
        await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Png, 50, 50), "n.png", "image/png");
        await WizardFlow.PublishAsync(owner);

        var now = (await WizardFlow.ProfileAsync(app)).PhotoUrl!;
        Assert.NotEqual(old, now);
        Assert.False(File.Exists(Path.Combine(app.UploadsPath, old["/uploads/".Length..])));
        Assert.True(File.Exists(Path.Combine(app.UploadsPath, now["/uploads/".Length..])));
        Assert.Single(Directory.GetFiles(app.UploadsPath));
    }

    [Fact]
    public async Task Skipping_RemovesTheDraftPhoto()
    {
        using var app = new AdminApp();
        var owner = await AtPhotoStepAsync(app);
        await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Jpeg, 40, 40));
        Assert.Single(DraftFiles(app));

        await WizardFlow.PostAsync(owner, WizardFlow.Photo + "?handler=Skip", new Dictionary<string, string>(), WizardFlow.Photo);

        Assert.Empty(DraftFiles(app));
    }

    // ---------------------------------------------------------------- draft preview

    private static async Task<AdminApp> DraftWithAccentAsync(string accent = "violet", bool photo = true)
    {
        var app = AdminApp.Create(AdminApp.SetupKey, new Dictionary<string, string?> { ["Site:GoogleAnalyticsId"] = "G-TEST12345" });
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(name: "Preview Person"));
        if (photo) await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Jpeg, 40, 40));
        await WizardFlow.SaveChoicesAsync(owner, WizardFlow.ReviewFields(accent: accent));
        return app;
    }

    [Fact]
    public async Task TheOwner_SeesTheDraftIdentityTemplateAccentAndPhoto_WithoutPublishing()
    {
        using var app = await DraftWithAccentAsync();
        var owner = await app.NewClientSignedInAsync();
        var before = WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app));

        var response = await owner.GetAsync("/?preview=healthcare&draft=1");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Preview Person", html);
        Assert.Contains("data-template=\"healthcare\"", html);
        Assert.Contains("--c-accent:167 139 250", html);
        Assert.Contains("/admin/welcome/draft-photo", html);
        Assert.Contains("Previewing your draft in the Healthcare template", html);
        Assert.Equal(before, WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app)));
        Assert.Null((await WizardFlow.ProfileAsync(app)).OnboardingCompletedAt);
    }

    [Fact]
    public async Task TheDraftPreview_KeepsTheGuaranteesOfAnyPreview()
    {
        using var app = await DraftWithAccentAsync();
        var owner = await app.NewClientSignedInAsync();
        var normal = await app.NewClient().GetStringAsync("/");
        Assert.Contains("googletagmanager.com", normal); // analytics is on for visitors, so its absence below means something

        var response = await owner.GetAsync("/about?preview=healthcare&draft=1");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal("noindex, nofollow, noarchive", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow, noarchive\"", html);
        Assert.DoesNotContain("googletagmanager", html);
        Assert.DoesNotContain("rel=\"canonical\"", html);
        Assert.DoesNotContain("og:url", html);
    }

    [Fact]
    public async Task EveryoneElse_GetsTheSavedSite_ForTheDraftPreviewAddress()
    {
        using var app = await DraftWithAccentAsync();
        var anonymous = app.NewClient();
        var plain = DemoSite.Normalise(await anonymous.GetStringAsync("/"));

        var spoofed = await anonymous.GetAsync("/?preview=healthcare&draft=1");
        var html = await spoofed.Content.ReadAsStringAsync();

        Assert.Equal(plain, DemoSite.Normalise(html));
        Assert.DoesNotContain("Preview Person", html);
        Assert.DoesNotContain("Previewing", html);
        Assert.DoesNotContain("--c-accent", html);
        Assert.False(spoofed.Headers.Contains("X-Robots-Tag"));
        Assert.Equal("DENY", spoofed.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task AStaleSession_GetsNoDraftPreview()
    {
        using var app = await DraftWithAccentAsync();
        var owner = await app.NewClientSignedInAsync();
        Assert.Contains("Preview Person", await owner.GetStringAsync("/?preview=healthcare&draft=1"));
        using (var db = app.CreateDbContext())
        {
            (await db.OwnerAccounts.SingleAsync()).SecurityStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
        }

        var html = await owner.GetStringAsync("/?preview=healthcare&draft=1");

        Assert.DoesNotContain("Preview Person", html);
        Assert.DoesNotContain("Previewing", html);
    }

    [Theory]
    [InlineData("/?preview=healthcare&draft=0")]
    [InlineData("/?preview=healthcare&draft=true")]
    [InlineData("/?preview=healthcare&draft=1&draft=1")]
    [InlineData("/?preview=healthcare&preview=software&draft=1")]
    [InlineData("/?draft=1")]
    public async Task OnlyAnExactDraftPreviewRequest_ShowsTheDraft(string path)
    {
        using var app = await DraftWithAccentAsync();
        var owner = await app.NewClientSignedInAsync();

        var html = await owner.GetStringAsync(path);

        Assert.DoesNotContain("Preview Person", html);
        Assert.DoesNotContain("Previewing your draft", html);
    }

    [Fact]
    public async Task GeneratedFiles_AndAdminPages_NeverShowTheDraft_EvenWithThePreviewParameters()
    {
        using var app = await DraftWithAccentAsync();
        var owner = await app.NewClientSignedInAsync();

        var sitemap = await owner.GetStringAsync("/sitemap.xml?preview=healthcare&draft=1");
        var cv = await owner.GetAsync("/cv.pdf?preview=healthcare&draft=1");
        var admin = await owner.GetStringAsync("/admin/profile?preview=healthcare&draft=1");

        Assert.DoesNotContain("healthcare", sitemap);
        Assert.Contains("owner.example", sitemap);
        Assert.DoesNotContain("preview-person", cv.Content.Headers.ContentDisposition!.FileName!.ToLowerInvariant());
        Assert.DoesNotContain("Preview Person", admin);
    }

    [Fact]
    public async Task WithoutADraft_TheDraftParameterIsAnOrdinaryTemplatePreview()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = await owner.GetStringAsync("/?preview=healthcare&draft=1");

        Assert.Contains("Previewing the Healthcare template. Nothing is saved or public.", html);
        Assert.DoesNotContain("Previewing your draft", html);
    }

    [Fact]
    public async Task InsideADraftPreview_LinksKeepBothParameters()
    {
        using var app = await DraftWithAccentAsync();
        var owner = await app.NewClientSignedInAsync();

        var html = await owner.GetStringAsync("/?preview=healthcare&draft=1");

        Assert.Contains("searchParams.set('draft', '1')", html);
        var normalPreview = await owner.GetStringAsync("/?preview=healthcare");
        Assert.DoesNotContain("searchParams.set('draft'", normalPreview);
    }

    [Fact]
    public async Task TheReviewStep_FramesTheDraftPreview_AndTheSearchPreviewUsesTheManifestPatterns()
    {
        using var app = await DraftWithAccentAsync();
        var owner = await app.NewClientSignedInAsync();

        var html = System.Net.WebUtility.HtmlDecode(await owner.GetStringAsync(WizardFlow.Review));

        Assert.Contains("<iframe class=\"adm-preview\" src=\"/?preview=healthcare&draft=1\"", html);
        Assert.Contains("Preview Person — Registered Nurse", html);
        Assert.Contains("Preview Person, Registered Nurse. Registrations, clinical experience and professional references.", html);
        Assert.Contains("Before you publish.", html);
    }

    [Fact]
    public async Task APublishedSite_IsNoLongerAffectedByTheDraftParameter()
    {
        using var app = await DraftWithAccentAsync(photo: false);
        var owner = await app.NewClientSignedInAsync();
        await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields(accent: "violet"));

        var html = await owner.GetStringAsync("/?preview=healthcare&draft=1");

        Assert.DoesNotContain("Previewing your draft", html); // the draft is gone; this is a plain template preview of the live site
        Assert.Contains("Preview Person", html);
        Assert.Contains("--c-accent:167 139 250", await app.NewClient().GetStringAsync("/"));
    }
}
