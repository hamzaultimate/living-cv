using System.Net;
using System.Text;
using Portfolio.Web.Tests.Support;
using Portfolio.Web.Transfer;
using Xunit;

namespace Portfolio.Web.Tests.Transfer;

/// <summary>Hostile files and requests, through the real pages: every one is answered, none writes anything, and nothing the file says is echoed unescaped.</summary>
public class TransferSecurityTests
{
    private static async Task<(AdminApp App, HttpClient Owner)> SiteAsync()
    {
        var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.SeedRowsAsync(app, pairs: true);
        await ProfileFlow.SeedTextAsync(app);
        return (app, owner);
    }

    private static string Nested(int depth) => """{ "schemaVersion": 1, "x": """ + new string('[', depth) + new string(']', depth) + " }";

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("""{ "schemaVersion": 1, "content": { "skills": [ { "name": "a" """)]
    [InlineData("""{ "schemaVersion": 1, "schemaVersion": 1 }""")]
    [InlineData("""{ "schemaVersion": 2 }""")]
    [InlineData("""{ "content": {} }""")]
    [InlineData("""{ "schemaVersion": "1" }""")]
    [InlineData("[1,2,3]")]
    [InlineData("""{ "schemaVersion": 1, "content": { "skills": { "name": "x" } } }""")]
    [InlineData("""{ "schemaVersion": 1, "profile": "x" }""")]
    public async Task AMalformedWrongVersionOrWronglyShapedFile_IsAnsweredWithAReason_AndWritesNothing(string json)
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var before = await ContentFlow.SnapshotAsync(app);

        var response = await TransferFlow.PreviewAsync(owner, json, applyProfile: true);
        var page = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"import-refusal\"", page);
        Assert.Contains("Nothing was read.", page);
        Assert.Null(TransferFlow.TokenOf(page)); // there is nothing to confirm
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task DeepNestingBinaryJunkAndANonUtf8File_AreRefused_WithoutErrors()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var before = await ContentFlow.SnapshotAsync(app);

        foreach (var bytes in new[]
                 {
                     TransferFlow.Utf8(Nested(TransferLimits.MaxJsonDepth + 5)),
                     TransferFlow.Utf8(Nested(100_000)),
                     new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0xFD },
                     Encoding.Latin1.GetBytes("{ \"schemaVersion\": 1, \"profile\": { \"ownerName\": \"café\" } }"),
                     Encoding.Unicode.GetBytes("{ \"schemaVersion\": 1 }"),
                 })
        {
            var response = await TransferFlow.PreviewAsync(owner, bytes, applyProfile: true);
            var page = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(page.Contains("id=\"import-refusal\"") || TransferFlow.TokenOf(page) is not null, "answered with a refusal or a plan");
        }
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task AFileInUtf8WithAByteOrderMark_IsPreviewedLikeAnyOther()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(TransferFlow.Utf8(ImportHarness.Table("skills", new { name = "With BOM", group = "G" }))).ToArray();

        var page = await (await TransferFlow.PreviewAsync(owner, bytes)).Content.ReadAsStringAsync();

        Assert.Contains("With BOM", page);
        Assert.NotNull(TransferFlow.TokenOf(page));
    }

    [Fact]
    public async Task TheFileSizeCap_IsEnforcedByTheReadAndByTheRequestBodyLimit()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var before = await ContentFlow.SnapshotAsync(app);
        var padding = TransferLimits.MaxFileBytes - ImportHarness.Table("skills", new { name = "Big", group = "G" }).Length;
        var atCap = TransferFlow.Utf8(ImportHarness.Table("skills", new { name = "Big", group = "G" }) + new string(' ', padding));

        var ok = await (await TransferFlow.PreviewAsync(owner, atCap)).Content.ReadAsStringAsync();
        Assert.Equal(TransferLimits.MaxFileBytes, atCap.Length);
        Assert.NotNull(TransferFlow.TokenOf(ok));

        // One byte over the file cap, still under the request cap: the page refuses it.
        var over = await TransferFlow.PreviewAsync(owner, atCap.Append((byte)' ').ToArray());
        var overPage = await over.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, over.StatusCode);
        Assert.Contains("larger than 2 MB", overPage);

        // Past the body limit of the whole request: refused before anything is read.
        var huge = await TransferFlow.PreviewAsync(owner, new byte[(int)TransferLimits.MaxRequestBytes + 1024]);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task TheOtherTransferPosts_CapTheirBodiesSmall()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var big = new Dictionary<string, string> { ["Token"] = new string('x', 100 * 1024) };

        var confirm = await ContentFlow.PostAsync(owner, TransferFlow.Page + "/confirm", big, TransferFlow.Page);
        var discard = await ContentFlow.PostAsync(owner, TransferFlow.Page + "/confirm?handler=Discard", big, TransferFlow.Page);
        var export = await ContentFlow.PostAsync(owner, TransferFlow.Export, big, TransferFlow.Page);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, discard.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, export.StatusCode);
    }

    [Fact]
    public async Task EveryTransferPost_WithTheRealFileTokenAndSession_ReallyWouldHaveSucceeded_SoTheRefusalsInTheReplaysAreTheGuardsThemselves()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;

        var preview = await TransferFlow.PreviewAsync(owner, ImportHarness.Table("skills", new { name = "Succeeds", group = "G" }));
        var token = TransferFlow.TokenOf(await preview.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.NotNull(token);
        var confirm = await TransferFlow.ConfirmAsync(owner, token);

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Contains("1 added", await confirm.Content.ReadAsStringAsync());
        var second = await TransferFlow.PreviewAsync(owner, ImportHarness.Table("skills", new { name = "Discard me", group = "G" }));
        var discard = await ContentFlow.PostAsync(owner, TransferFlow.Page + "/confirm?handler=Discard", new Dictionary<string, string> { ["Token"] = TransferFlow.TokenOf(await second.Content.ReadAsStringAsync())! }, TransferFlow.Page);
        Assert.Equal(HttpStatusCode.Redirect, discard.StatusCode);
    }

    [Fact]
    public async Task NamesAndValuesFromTheFile_AreEncodedInThePreview_WhateverTheyContain()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var file = ImportHarness.File(content: new
        {
            skills = new object[] { new { name = "<img src=x onerror=alert(1)>", group = "G" } },
            experience = new object[] { new { company = "<script>alert(1)</script>", title = "T", start = "2020", end = "2021", summary = "S." } },
            projects = new object[] { new { title = "\"><svg/onload=alert(1)>", summary = "S." }, new { title = new string('x', 500), summary = "S.", slug = "ok-slug" } },
            blog = new object[] { new { title = "A fine title", slug = "<b>x</b>", date = "2025-01-01", body = "Text." } },
        });

        var page = await (await TransferFlow.PreviewAsync(owner, file, applyProfile: true)).Content.ReadAsStringAsync();
        var named = await TransferFlow.PreviewAsync(owner, TransferFlow.Utf8(ImportHarness.Table("skills", new { name = "x", group = "y" })), false, "../../etc/pass<script>wd\u0007.json");
        var namedPage = await named.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<img src=x", page);
        Assert.DoesNotContain("<script>alert", page);
        Assert.DoesNotContain("<svg/onload", page);
        Assert.DoesNotContain("<b>x</b>", page);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", page);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", page);
        Assert.DoesNotContain("../../etc", namedPage);
        Assert.DoesNotContain("pass<script>", namedPage);
        Assert.Contains("pass&lt;script&gt;wd", namedPage);
        Assert.False(namedPage.Contains('\u0007'), "the control character is gone from the page"); // ordinal: a culture-aware search ignores control characters
        Assert.Contains("&#x2026;", page); // a very long name is cut for the preview (the ellipsis is written as an entity)
    }

    [Fact]
    public async Task NoScriptAndNoInlineHandler_AppearOnAnyTransferPage_PreviewResultAndRefusalIncluded()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var preview = await TransferFlow.PreviewAsync(owner, ImportHarness.Table("skills", new { name = "Fine", group = "G" }));
        var previewPage = await preview.Content.ReadAsStringAsync();
        var refusal = await TransferFlow.PreviewAsync(owner, "{");
        var result = await TransferFlow.ConfirmAsync(owner, TransferFlow.TokenOf(previewPage));

        foreach (var response in new[] { preview, refusal, result })
        {
            var csp = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
            var html = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("script-src", csp);
            Assert.Contains("default-src 'none'", csp);
            Assert.DoesNotContain("unsafe-inline", csp);
            Assert.DoesNotMatch(@"<script\b", html);
            Assert.DoesNotMatch(@"\son[a-z]+\s*=", html);
            Assert.DoesNotContain("javascript:", html);
            Assert.Contains("no-store", string.Join(",", response.Headers.GetValues("Cache-Control")));
        }
    }

    [Fact]
    public async Task APostWithNoFile_OrTwoFileParts_IsAnsweredCleanly()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var before = await ContentFlow.SnapshotAsync(app);

        var none = await ContentFlow.PostMultipartAsync(owner, TransferFlow.Page + "?handler=Preview", new Dictionary<string, string> { ["ApplyProfile"] = "false" }, null, TransferFlow.Page);
        Assert.Contains("Choose a file to import", await none.Content.ReadAsStringAsync());

        var token = await AdminApp.GetTokenAsync(owner, TransferFlow.Page);
        var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        content.Add(new ByteArrayContent(TransferFlow.Utf8(ImportHarness.Table("skills", new { name = "One", group = "G" }))), "Upload", "a.json");
        content.Add(new ByteArrayContent(TransferFlow.Utf8(ImportHarness.Table("skills", new { name = "Two", group = "G" }))), "Upload", "b.json");
        var two = await owner.PostAsync(TransferFlow.Page + "?handler=Preview", content);

        Assert.True(two.StatusCode < HttpStatusCode.InternalServerError, "two parts do not crash");
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }
}
