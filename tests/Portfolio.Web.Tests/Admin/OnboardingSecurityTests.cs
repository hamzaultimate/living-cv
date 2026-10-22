using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>Who may use the wizard, what a hostile value does, and what a GET or a request without a token can change.</summary>
public class OnboardingSecurityTests
{
    private static async Task<string> SnapshotAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        var profile = await db.SiteProfiles.AsNoTracking().SingleAsync();
        return JsonSerializer.Serialize(profile) + "|" + string.Join(",", (await DemoSite.RowCountsAsync(app)).Values)
            + "|" + string.Join(",", Directory.EnumerateFiles(app.UploadsPath, "*", SearchOption.AllDirectories).Select(Path.GetFileName).OrderBy(x => x));
    }

    private static async Task<HttpClient> DraftedAsync(AdminApp app)
    {
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        await WizardFlow.SaveBasicsAsync(owner);
        await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Jpeg, 32, 32));
        return owner;
    }

    /// <summary>Every state-changing request the wizard has: path and a body that would be valid.</summary>
    private static IEnumerable<(string Path, Dictionary<string, string> Fields)> Posts() =>
    [
        (WizardFlow.Template, new() { ["Key"] = "software" }),
        (WizardFlow.Template + "?handler=Skip", new()),
        (WizardFlow.Template + "?handler=Discard", new()),
        (WizardFlow.Basics, WizardFlow.BasicsFields(name: "Changed Name")),
        (WizardFlow.Basics + "?handler=Skip", new()),
        (WizardFlow.Basics + "?handler=Discard", new()),
        (WizardFlow.Photo + "?handler=Remove", new()),
        (WizardFlow.Photo + "?handler=Skip", new()),
        (WizardFlow.Photo + "?handler=Discard", new()),
        (WizardFlow.Review + "?handler=Save", WizardFlow.ReviewFields("https://changed.example", "rose", StarterChoice.Sample)),
        (WizardFlow.Review + "?handler=Publish", WizardFlow.ReviewFields("https://changed.example", "rose", StarterChoice.Sample)),
        (WizardFlow.Review + "?handler=Skip", new()),
        (WizardFlow.Review + "?handler=Discard", new()),
    ];

    public static TheoryData<string> PostTargets()
    {
        var data = new TheoryData<string>();
        foreach (var (path, _) in Posts()) data.Add(path);
        return data;
    }

    [Theory]
    [MemberData(nameof(PostTargets))]
    public async Task EveryPost_WithoutAnAntiforgeryToken_IsRefused_AndChangesNothing(string path)
    {
        using var app = new AdminApp();
        var owner = await DraftedAsync(app);
        var before = await SnapshotAsync(app);
        var fields = Posts().First(p => p.Path == path).Fields;

        var response = await owner.PostAsync(path, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync(app));
    }

    [Fact]
    public async Task ThePhotoUpload_WithoutAnAntiforgeryToken_IsRefused()
    {
        using var app = new AdminApp();
        var owner = await DraftedAsync(app);
        var before = await SnapshotAsync(app);
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(TestImages.Split(SKEncodedImageFormat.Jpeg, 32, 32));
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        content.Add(file, "Photo", "x.jpg");

        var response = await owner.PostAsync(WizardFlow.Photo + "?handler=Upload", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync(app));
    }

    [Fact]
    public async Task APostWithAnotherSessionsToken_IsRefused()
    {
        using var app = new AdminApp();
        var owner = await DraftedAsync(app);
        var other = app.NewClient();
        var foreign = await AdminApp.GetTokenAsync(other, "/admin/login"); // a token from a client with a different antiforgery cookie
        var before = await SnapshotAsync(app);

        var response = await AdminApp.PostFormAsync(owner, WizardFlow.Review + "?handler=Publish", foreign, WizardFlow.ReviewFields());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync(app));
    }

    [Fact]
    public async Task AGet_NeverChangesAnything_EvenWhenItNamesAHandler()
    {
        using var app = new AdminApp();
        var owner = await DraftedAsync(app);
        await WizardFlow.SaveChoicesAsync(owner, WizardFlow.ReviewFields("https://x.example", "rose", StarterChoice.Sample));
        var before = await SnapshotAsync(app);
        var paths = WizardFlow.Pages.Concat(
        [
            "/admin/welcome/draft-photo", "/admin/welcome/thumb/software", "/admin/welcome/review?tab=%2Fabout&size=mobile", "/admin",
            WizardFlow.Review + "?handler=Publish", WizardFlow.Review + "?handler=Save", WizardFlow.Template + "?handler=Skip", WizardFlow.Template + "?handler=Discard",
            WizardFlow.Photo + "?handler=Remove", WizardFlow.Photo + "?handler=Upload", WizardFlow.Template + "?Key=software", WizardFlow.Basics + "?Form.OwnerName=Hacked",
        ]);

        foreach (var path in paths)
        {
            var response = await owner.GetAsync(path);
            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect, $"GET {path} returned {(int)response.StatusCode}");
        }

        Assert.Equal(before, await SnapshotAsync(app));
    }

    [Fact]
    public async Task AnonymousVisitors_CannotReachAnyWizardRoute_NorChangeAnything()
    {
        using var app = new AdminApp();
        var owner = await DraftedAsync(app);
        var before = await SnapshotAsync(app);
        var anonymous = app.NewClient();

        foreach (var path in WizardFlow.Pages.Concat(["/admin/welcome/draft-photo", "/admin/welcome/thumb/software", "/admin/welcome/thumb/healthcare"]))
        {
            var get = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, get.StatusCode);
            Assert.Contains("/admin/login", get.Headers.Location!.OriginalString);
        }
        foreach (var (path, fields) in Posts())
        {
            var post = await anonymous.PostAsync(path, new FormUrlEncodedContent(fields));
            Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
            Assert.Contains("/admin/login", post.Headers.Location!.OriginalString);
        }
        Assert.NotNull(owner);
        Assert.Equal(before, await SnapshotAsync(app));
    }

    [Fact]
    public async Task AStaleSession_IsRejectedOnEveryWizardRoute_AndCannotPublish()
    {
        using var app = new AdminApp();
        var owner = await DraftedAsync(app);
        var token = await AdminApp.GetTokenAsync(owner, WizardFlow.Review); // fetched while the session was good
        using (var db = app.CreateDbContext())
        {
            (await db.OwnerAccounts.SingleAsync()).SecurityStamp = Guid.NewGuid().ToString("N"); // what reset-owner does
            await db.SaveChangesAsync();
        }
        var before = await SnapshotAsync(app);

        foreach (var path in WizardFlow.Pages.Concat(["/admin/welcome/draft-photo", "/admin/welcome/thumb/software"]))
        {
            var response = await owner.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/admin/login", response.Headers.Location!.OriginalString);
        }
        var publish = await AdminApp.PostFormAsync(owner, WizardFlow.Review + "?handler=Publish", token, WizardFlow.ReviewFields());
        Assert.Equal(HttpStatusCode.Redirect, publish.StatusCode);
        Assert.Contains("/admin/login", publish.Headers.Location!.OriginalString);
        Assert.Equal(before, await SnapshotAsync(app));
        Assert.Null((await WizardFlow.ProfileAsync(app)).OnboardingCompletedAt);
    }

    [Fact]
    public async Task TheWizard_DoesNotReopenSetup_OrLeakTheSetupKey()
    {
        using var app = new AdminApp();
        var owner = await DraftedAsync(app);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/setup")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.NewClient().GetAsync("/setup")).StatusCode);
        foreach (var path in WizardFlow.Pages)
            Assert.DoesNotContain(AdminApp.SetupKey, await owner.GetStringAsync(path));
        Assert.DoesNotContain(AdminApp.SetupKey, await owner.GetStringAsync("/admin"));
    }

    [Fact]
    public async Task WizardResponses_ArePrivate_NoStore_AndOnlyTheReviewStepMayFrameTheSite()
    {
        using var app = new AdminApp();
        var owner = await DraftedAsync(app);

        foreach (var path in WizardFlow.Pages.Concat(["/admin/welcome/draft-photo", "/admin/welcome/thumb/software"]))
        {
            var response = await owner.GetAsync(path);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
            Assert.Contains("noindex", response.Headers.GetValues("X-Robots-Tag").Single());
            var csp = response.Headers.GetValues("Content-Security-Policy").Single();
            Assert.Contains("default-src 'none'", csp);
            Assert.DoesNotContain("script-src", csp);
            Assert.Equal(path == WizardFlow.Review, csp.Contains("frame-src 'self'"));
        }
    }

    [Fact]
    public async Task OversizedBodies_AreRefusedBeforeAnythingIsRead()
    {
        using var app = new AdminApp();
        var owner = await DraftedAsync(app);
        var before = await SnapshotAsync(app);
        var big = new string('a', 2 * 1024 * 1024);

        var basics = await AdminApp.PostFormAsync(owner, WizardFlow.Basics, await AdminApp.GetTokenAsync(owner, WizardFlow.Basics),
            WizardFlow.BasicsFields(summary: big));
        var template = await AdminApp.PostFormAsync(owner, WizardFlow.Template, await AdminApp.GetTokenAsync(owner, WizardFlow.Template),
            new Dictionary<string, string> { ["Key"] = big });
        var review = await AdminApp.PostFormAsync(owner, WizardFlow.Review + "?handler=Save", await AdminApp.GetTokenAsync(owner, WizardFlow.Review),
            WizardFlow.ReviewFields(url: "https://x.example/" + big));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, basics.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, template.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, review.StatusCode);
        Assert.Equal(before, await SnapshotAsync(app));
    }

    // ---------------------------------------------------------------- hostile values

    public static TheoryData<string, string> BadBasics() => new()
    {
        { "Form.OwnerName", "" },
        { "Form.OwnerName", "   " },
        { "Form.OwnerName", new string('n', 201) },
        { "Form.OwnerName", "<script>alert(1)</script>" },
        { "Form.OwnerName", "\"><img src=x onerror=alert(1)>" },
        { "Form.JobTitle", "" },
        { "Form.JobTitle", new string('t', 201) },
        { "Form.JobTitle", "Nurse <b>bold</b>" },
        { "Form.Email", "" },
        { "Form.Email", "not-an-email" },
        { "Form.Email", "a@" },
        { "Form.Email", "two@example.org three@example.org" },
        { "Form.Email", new string('e', 300) + "@example.org" },
        { "Form.Location", new string('l', 121) },
        { "Form.Location", "<iframe src=//evil>" },
        { "Form.Summary", "" },
        { "Form.Summary", new string('s', 601) },
    };

    [Theory]
    [MemberData(nameof(BadBasics))]
    public async Task InvalidBasics_AreRejectedWithTheFormShownAgain_AndNothingIsSaved(string field, string value)
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        var before = await SnapshotAsync(app);
        var fields = WizardFlow.BasicsFields();
        fields[field] = value;

        var response = await WizardFlow.SaveBasicsAsync(owner, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.Equal(before, await SnapshotAsync(app));
        Assert.Null((await WizardFlow.DraftAsync(app))!.OwnerName);
    }

    [Fact]
    public async Task ControlCharactersAndLineBreaks_AreFlattenedToSpaces_AndValuesAreTrimmed()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");

        await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(name: "  Jo\r\nrdan\tAlexander ", summary: "Line one\nline two\u0007 end", location: "\n"));

        var draft = (await WizardFlow.DraftAsync(app))!;
        Assert.Equal("Jo  rdan Alexander", draft.OwnerName);
        Assert.Equal("Line one line two  end", draft.Summary);
        Assert.Null(draft.Location);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("")]
    [InlineData("../software")]
    [InlineData("software/../healthcare")]
    [InlineData("<script>")]
    [InlineData("http://evil.example/healthcare")]
    public async Task AnUnknownTemplateKey_IsRefused_AndNoDraftIsStarted(string key)
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await app.NewClient().GetAsync("/"); // the profile row is created by the first read
        var before = await SnapshotAsync(app);

        var response = await WizardFlow.PickTemplateAsync(owner, key);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose one of the templates", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await SnapshotAsync(app));
        Assert.Null(await WizardFlow.DraftAsync(app));
    }

    public static TheoryData<string, string, string> BadChoices() => new()
    {
        { "https://ok.example", "emerald", StarterChoice.Blank },        // an accent the healthcare template does not offer
        { "https://ok.example", "violet; } body { display:none", StarterChoice.Blank },
        { "https://ok.example", "#ff0000", StarterChoice.Blank },
        { "https://ok.example", "../../violet", StarterChoice.Blank },
        { "https://ok.example", "VIOLET", StarterChoice.Blank },
        { "https://ok.example", new string('a', 41), StarterChoice.Blank },
        { "https://ok.example", "", "everything" },
        { "https://ok.example", "", "" },
        { "javascript:alert(1)", "", StarterChoice.Blank },
        { "ftp://example.org/x", "", StarterChoice.Blank },
        { "/relative", "", StarterChoice.Blank },
        { "https://user:pass@example.org", "", StarterChoice.Blank },
        { "", "", StarterChoice.Blank },
    };

    [Theory]
    [MemberData(nameof(BadChoices))]
    public async Task InvalidReviewChoices_AreRefused_ByBothSaveAndPublish_AndNothingIsPublished(string url, string accent, string content)
    {
        using var app = new AdminApp();
        var owner = await DraftedAsync(app);
        var before = await SnapshotAsync(app);
        var fields = WizardFlow.ReviewFields(url, accent, content);

        var save = await WizardFlow.SaveChoicesAsync(owner, fields);
        var publish = await WizardFlow.PublishAsync(owner, fields);

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        Assert.Equal(before, await SnapshotAsync(app));
        Assert.Null((await WizardFlow.ProfileAsync(app)).OnboardingCompletedAt);
        Assert.Null((await WizardFlow.DraftAsync(app))!.AccentKey);
    }

    [Fact]
    public async Task AnAccentFromTheTemplatesList_IsAccepted_AndStoredAsItsKey()
    {
        using var app = new AdminApp();
        var owner = await DraftedAsync(app);

        var save = await WizardFlow.SaveChoicesAsync(owner, WizardFlow.ReviewFields(accent: "violet"));

        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        Assert.Equal("violet", (await WizardFlow.DraftAsync(app))!.AccentKey);
    }

    // ---------------------------------------------------------------- thumbnails

    [Theory]
    [InlineData("software")]
    [InlineData("healthcare")]
    public async Task TheThumbnailEndpoint_ServesTheTemplatesOwnPng_ToTheOwner(string key)
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var response = await owner.GetAsync($"/admin/welcome/thumb/{key}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Templates", key, "preview.png")), await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("..%2f..%2fappsettings.json")]
    [InlineData("..%2Fsoftware%2Ftemplate.json")]
    [InlineData("%2e%2e%2f%2e%2e%2fappsettings.json")]
    [InlineData("%2e%2e/%2e%2e/appsettings.json")]
    [InlineData("..%5c..%5cappsettings.json")]
    [InlineData("software%2f..%2f..%2fappsettings.json")]
    [InlineData("software%2f..%2fhealthcare")]           // would resolve to a real thumbnail if the key were used as a path
    [InlineData("..%2fsoftware%2f..%2fhealthcare")]
    [InlineData("software%5c..%5chealthcare")]
    [InlineData("software.png")]
    [InlineData("preview.png")]
    [InlineData("%2fetc%2fpasswd")]
    [InlineData("nope")]
    [InlineData("%E2%80%AEsoftware")]
    public async Task TheThumbnailEndpoint_CannotBeMadeToReadAnythingElse(string key)
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var response = await owner.GetAsync($"/admin/welcome/thumb/{key}");

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest, $"{key} returned {(int)response.StatusCode}");
        Assert.DoesNotContain("\"ConnectionStrings\"", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/admin/welcome/thumb/../../appsettings.json")]
    [InlineData("/admin/welcome/thumb/software/../../../appsettings.json")]
    [InlineData("/admin/welcome/thumb/")]
    [InlineData("/admin/welcome/thumb")]
    public async Task TheThumbnailEndpoint_RejectsPathShapedRequests(string path)
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var response = await owner.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("\"ConnectionStrings\"", await response.Content.ReadAsStringAsync());
    }
}
