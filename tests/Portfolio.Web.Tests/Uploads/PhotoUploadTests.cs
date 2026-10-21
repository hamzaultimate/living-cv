using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Web.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Portfolio.Web.Tests.Uploads;

public class PhotoUploadTests
{
    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] content, string fileName, string contentType = "image/png",
        string? token = null, bool sendToken = true)
    {
        token ??= sendToken ? await AdminApp.GetTokenAsync(client, "/admin/photo") : null;
        var form = new MultipartFormDataContent();
        if (token is not null) form.Add(new StringContent(token), "__RequestVerificationToken");
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "Photo", fileName);
        return await client.PostAsync("/admin/photo?handler=Upload", form);
    }

    private static async Task<string?> StoredPhotoAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return (await db.SiteProfiles.SingleAsync()).PhotoUrl;
    }

    private static string FileFor(AdminApp app, string url) => Path.Combine(app.UploadsPath, url["/uploads/".Length..]);

    private static async Task<string> Errors(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(Regex.Replace(await response.Content.ReadAsStringAsync(), "<[^>]+>", " "));

    [Fact]
    public async Task AValidUpload_IsReEncoded_StoredUnderAGeneratedName_AndShownOnTheSite()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();
        var original = TestImages.WithJpegSegments(TestImages.Split(SKEncodedImageFormat.Jpeg, 64, 64), TestImages.JpegComment("SECRET-GPS"));

        var response = await UploadAsync(client, original, "holiday photo.JPG", "image/jpeg");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var url = await StoredPhotoAsync(app);
        Assert.Matches(new Regex(@"^/uploads/[0-9a-f]{32}\.jpg$"), url);
        var stored = await File.ReadAllBytesAsync(FileFor(app, url!));
        Assert.DoesNotContain("SECRET-GPS", System.Text.Encoding.Latin1.GetString(stored));
        Assert.Single(Directory.GetFiles(app.UploadsPath));

        var home = await app.NewClient().GetStringAsync("/");
        Assert.Contains($"src=\"{url}\"", home);
        Assert.DoesNotContain(">AR<", home);
    }

    [Fact]
    public async Task TheClientFileNameAndTypeAreIgnored_TheDetectedFormatDecidesTheExtension()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();

        var response = await UploadAsync(client, TestImages.Split(SKEncodedImageFormat.Png, 32, 32), "../../../etc/cron.d/evil.php", "text/html");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var url = await StoredPhotoAsync(app);
        Assert.Matches(new Regex(@"^/uploads/[0-9a-f]{32}\.png$"), url);
        Assert.Equal(url!["/uploads/".Length..], Path.GetFileName(Directory.GetFiles(app.UploadsPath).Single()));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(app.UploadsPath)!, "evil*", SearchOption.TopDirectoryOnly));
    }

    [Theory]
    [InlineData("selfie.png", "image/png", "<!doctype html><script>alert(1)</script>")]
    [InlineData("selfie.jpg", "image/jpeg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("vector.svg", "image/svg+xml", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("anim.gif", "image/gif", "GIF89a\u0001\0\u0001\0")]
    [InlineData("doc.jpg", "image/jpeg", "%PDF-1.7")]
    [InlineData("empty.png", "image/png", "")]
    public async Task NonImages_AreRejected_WhateverTheyAreCalled(string fileName, string contentType, string body)
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();

        var response = await UploadAsync(client, System.Text.Encoding.Latin1.GetBytes(body), fileName, contentType);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Directory.GetFiles(app.UploadsPath));
        Assert.Equal("/img/avatar.svg", await StoredPhotoAsync(app));
    }

    [Fact]
    public async Task ADecompressionBomb_IsRejectedThroughTheWebForm()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();

        var response = await UploadAsync(client, TestImages.SolidGrayPng(7000, 7000), "big.png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("too large in pixels", await Errors(response));
        Assert.Empty(Directory.GetFiles(app.UploadsPath));
    }

    [Fact]
    public async Task AnOversizedFile_IsRefusedByTheConfiguredCap()
    {
        using var app = AdminApp.Create(AdminApp.SetupKey, new Dictionary<string, string?> { ["Uploads:MaxBytes"] = "100000" });
        var client = await app.SignedInClientAsync();
        var noise = TestImages.Noise(200, 200);
        Assert.InRange(noise.Length, 100_001, 160_000);

        var response = await UploadAsync(client, noise, "noise.png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("larger than", await Errors(response));
        Assert.Empty(Directory.GetFiles(app.UploadsPath));
    }

    [Fact]
    public async Task ABodyFarOverTheCap_IsRefusedWith413BeforeItIsRead()
    {
        using var app = AdminApp.Create(AdminApp.SetupKey, new Dictionary<string, string?> { ["Uploads:MaxBytes"] = "100000" });
        var client = await app.SignedInClientAsync();
        var token = await AdminApp.GetTokenAsync(client, "/admin/photo");

        var response = await UploadAsync(client, new byte[2_000_000], "huge.png", token: token);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(Directory.GetFiles(app.UploadsPath));
    }

    /// <summary>Hides its length, so the request goes out chunked with no Content-Length header.</summary>
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) { length = -1; return false; }
    }

    private static HttpRequestMessage ChunkedUpload(string token, byte[] file)
    {
        var boundary = "----lcv" + Guid.NewGuid().ToString("N");
        var head = System.Text.Encoding.ASCII.GetBytes(
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"__RequestVerificationToken\"\r\n\r\n{token}\r\n" +
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"Photo\"; filename=\"p.png\"\r\nContent-Type: image/png\r\n\r\n");
        var tail = System.Text.Encoding.ASCII.GetBytes($"\r\n--{boundary}--\r\n");
        var body = new UnknownLengthContent([.. head, .. file, .. tail]);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=" + boundary);
        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/photo?handler=Upload") { Content = body };
        request.Headers.TransferEncodingChunked = true;
        return request;
    }

    [Fact]
    public async Task ABodyWithNoContentLength_IsCutOffAtTheCap_AndNothingIsStored()
    {
        using var app = AdminApp.Create(AdminApp.SetupKey, new Dictionary<string, string?> { ["Uploads:MaxBytes"] = "100000" });
        var client = await app.SignedInClientAsync();

        // Control: the same chunked, length-less framing works for a small valid image.
        var ok = ChunkedUpload(await AdminApp.GetTokenAsync(client, "/admin/photo"), TestImages.Split(SKEncodedImageFormat.Png, 32, 32));
        Assert.Equal(HttpStatusCode.Redirect, (await client.SendAsync(ok)).StatusCode);
        Assert.Null(ok.Content!.Headers.ContentLength);
        var before = await StoredPhotoAsync(app);
        var filesBefore = Directory.GetFiles(app.UploadsPath).Length;

        var huge = ChunkedUpload(await AdminApp.GetTokenAsync(client, "/admin/photo"), new byte[2_000_000]);
        var response = await client.SendAsync(huge);

        Assert.Null(huge.Content!.Headers.ContentLength);
        // The test server has no network layer, so it is the form reader's limit that stops the body (a 400 from the
        // antiforgery check that reads it); Kestrel's own limit would answer 413. Either way nothing is stored.
        Assert.True((int)response.StatusCode >= 400, $"expected a refusal, got {(int)response.StatusCode}");
        Assert.Equal(before, await StoredPhotoAsync(app));
        Assert.Equal(filesBefore, Directory.GetFiles(app.UploadsPath).Length);
    }

    [Fact]
    public async Task ReplacingThePhoto_DeletesTheOldFile()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();
        await UploadAsync(client, TestImages.Split(SKEncodedImageFormat.Png, 32, 32), "a.png");
        var first = await StoredPhotoAsync(app);

        await UploadAsync(client, TestImages.Split(SKEncodedImageFormat.Webp, 32, 32), "b.webp");
        var second = await StoredPhotoAsync(app);

        Assert.NotEqual(first, second);
        Assert.False(File.Exists(FileFor(app, first!)));
        Assert.True(File.Exists(FileFor(app, second!)));
    }

    [Fact]
    public async Task RemovingThePhoto_DeletesTheFile_AndTheHomePageShowsTheMonogram()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();
        await UploadAsync(client, TestImages.Split(SKEncodedImageFormat.Png, 32, 32), "a.png");
        var url = await StoredPhotoAsync(app);

        var token = await AdminApp.GetTokenAsync(client, "/admin/photo");
        var response = await AdminApp.PostFormAsync(client, "/admin/photo?handler=Remove", token, []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await StoredPhotoAsync(app));
        Assert.False(File.Exists(FileFor(app, url!)));
        var home = await app.NewClient().GetStringAsync("/");
        Assert.DoesNotContain("/uploads/", home);
        Assert.Contains(">AR<", home);
    }

    [Fact]
    public async Task ReplacingTheBundledPlaceholder_NeverTouchesFilesOutsideTheUploadsFolder()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();
        using (var db = app.CreateDbContext())
        {
            await client.GetStringAsync("/");
            var row = await db.SiteProfiles.SingleAsync();
            Assert.Equal("/img/avatar.svg", row.PhotoUrl);
        }
        var bundled = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Portfolio.Web", "wwwroot", "img", "avatar.svg"));
        Assert.True(File.Exists(bundled), "fixture path moved: " + bundled);

        await UploadAsync(client, TestImages.Split(SKEncodedImageFormat.Png, 32, 32), "a.png");

        Assert.Matches(new Regex(@"^/uploads/"), await StoredPhotoAsync(app));
        Assert.True(File.Exists(bundled));
    }

    [Fact]
    public async Task Upload_RequiresSignIn_AndAnAntiforgeryToken()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();
        var png = TestImages.Split(SKEncodedImageFormat.Png, 32, 32);

        var anonymous = await UploadAsync(app.NewClient(), png, "a.png", token: "forged");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);

        var noToken = await UploadAsync(client, png, "a.png", sendToken: false);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);

        Assert.Empty(Directory.GetFiles(app.UploadsPath));

        await UploadAsync(client, png, "a.png");
        var kept = await StoredPhotoAsync(app);
        var removeWithoutToken = await AdminApp.PostFormAsync(client, "/admin/photo?handler=Remove", token: null, []);
        Assert.Equal(HttpStatusCode.BadRequest, removeWithoutToken.StatusCode);
        Assert.Equal(kept, await StoredPhotoAsync(app));
        Assert.True(File.Exists(FileFor(app, kept!)));
    }

    [Fact]
    public async Task Upload_WithNoFile_ShowsAnError()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();
        var token = await AdminApp.GetTokenAsync(client, "/admin/photo");

        var response = await client.PostAsync("/admin/photo?handler=Upload", new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose an image", await Errors(response));
    }

    [Fact]
    public async Task UploadedFiles_AreServedAsImmutableImages_WithNosniff()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();
        await UploadAsync(client, TestImages.Split(SKEncodedImageFormat.Webp, 32, 32), "a.webp");
        var url = await StoredPhotoAsync(app);

        var response = await app.NewClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        var cache = response.Headers.CacheControl!;
        Assert.True(cache.Public);
        Assert.True(cache.MaxAge >= TimeSpan.FromDays(365));
        Assert.Contains("immutable", response.Headers.GetValues("Cache-Control").Single());
    }

    [Theory]
    [InlineData("/uploads/")]
    [InlineData("/uploads")]
    [InlineData("/uploads/notes.txt")]
    [InlineData("/uploads/.hidden.webp.tmp")]
    [InlineData("/uploads/../appsettings.json")]
    [InlineData("/uploads/%2e%2e/appsettings.json")]
    [InlineData("/uploads/..%2fappsettings.json")]
    [InlineData("/uploads/0123456789abcdef0123456789abcdef.svg")]
    public async Task OnlyGeneratedImages_AreServed(string path)
    {
        using var app = new AdminApp();
        File.WriteAllText(Path.Combine(app.UploadsPath, "notes.txt"), "secret");
        File.WriteAllText(Path.Combine(app.UploadsPath, ".hidden.webp.tmp"), "partial");
        File.WriteAllText(Path.Combine(app.UploadsPath, "0123456789abcdef0123456789abcdef.svg"), "<svg/>");

        var response = await app.NewClient().GetAsync(path);

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest, $"{path} returned {(int)response.StatusCode}");
    }

    [Fact]
    public async Task ThePhotoPage_ShowsTheLimitsAndIsNotCached()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();

        var response = await client.GetAsync("/admin/photo");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("up to 5 MB", html);
        Assert.Contains("no-store", string.Join(",", response.Headers.GetValues("Cache-Control")));
        Assert.Contains("enctype=\"multipart/form-data\"", html);
    }
}
