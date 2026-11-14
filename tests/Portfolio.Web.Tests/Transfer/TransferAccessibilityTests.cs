using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Web.Tests.Admin;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Transfer;

/// <summary>Structure a screen reader and a keyboard rely on, on every screen of the export and import: labels, headings, live regions, focus, tables and real buttons.</summary>
public class TransferAccessibilityTests
{
    private static int Count(string html, string pattern) => Regex.Matches(html, pattern).Count;

    [Theory]
    [InlineData("software")]
    [InlineData("healthcare")]
    public async Task EveryScreen_IsStructured_ForBothTemplates(string template)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, template);
        await ContentFlow.SeedRowsAsync(app, pairs: true);

        var form = await owner.GetStringAsync(TransferFlow.Page);
        var preview = await (await TransferFlow.PreviewAsync(owner, ImportHarness.Table("skills", new { name = "Seeding", group = "Garden" }, new { name = "New", group = "G" }, new { name = "", group = "G" }))).Content.ReadAsStringAsync();
        var refusal = await (await TransferFlow.PreviewAsync(owner, "{")).Content.ReadAsStringAsync();
        var result = await (await TransferFlow.ConfirmAsync(owner, TransferFlow.TokenOf(preview))).Content.ReadAsStringAsync();
        var expired = await (await TransferFlow.ConfirmAsync(owner, "0123456789abcdef0123456789abcdef")).Content.ReadAsStringAsync();

        foreach (var (name, html) in new[] { ("form", form), ("preview", preview), ("refusal", refusal), ("result", result), ("expired", expired) })
            ContentAccessibilityTests.AssertAccessible(name, html);
    }

    [Fact]
    public async Task TheForm_HasALabelledFileInput_ALabelledCheckbox_AndRealButtonsAndLinks()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await owner.GetAsync("/admin"); // takes the message the wizard leaves behind, which would otherwise be on this page

        var html = await owner.GetStringAsync(TransferFlow.Page);

        Assert.Matches(@"<label for=""Upload"">[^<]+</label>", html);
        Assert.Matches(@"<input type=""file"" id=""Upload"" name=""Upload"" accept="".json,application/json"" required aria-describedby=""Upload-hint""", html);
        Assert.Contains("id=\"Upload-hint\"", html);
        Assert.Matches(@"<label for=""ApplyProfile"">[^<]+</label>", html);
        Assert.Matches(@"<input type=""checkbox"" id=""ApplyProfile"" name=""ApplyProfile"" value=""true""[^>]*aria-describedby=""ApplyProfile-hint""", html);
        Assert.Contains("enctype=\"multipart/form-data\"", html);
        // The export is a link to a file (it changes nothing); the import is a form button.
        Assert.Matches(@"<a class=""adm-btn adm-btn-primary"" href=""/admin/transfer/export"" download>", html);
        Assert.Matches(@"<button type=""submit"" class=""adm-btn adm-btn-primary"">[^<]+</button>", html);
        Assert.Equal(2, Count(html, @"<h2\b")); // the two halves of the page are headings, each naming its section
        Assert.Contains("aria-labelledby=\"export-heading\"", html);
        Assert.Contains("aria-labelledby=\"import-heading\"", html);
        Assert.Equal(0, Count(html, "autofocus")); // nothing steals focus from a plain page load
    }

    [Fact]
    public async Task ThePreview_AnnouncesItself_TakesFocus_AndTellsEachRowsOutcomeInWords()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.SeedRowsAsync(app, pairs: true);

        var html = await (await TransferFlow.PreviewAsync(owner, ImportHarness.Table("skills", new { name = "Seeding", group = "Garden" }, new { name = "Fresh", group = "G" }, new { name = "Seeding", group = "Dup" }, new { name = "<b>", group = "G" }))).Content.ReadAsStringAsync();

        Assert.Matches(@"<h2 id=""preview-heading"" tabindex=""-1"" autofocus>Preview of [^<]+</h2>", html);
        Assert.Equal(1, Count(html, "autofocus"));
        Assert.Matches(@"<p role=""status"">\s*<strong>Nothing has been written yet\.</strong>", html);
        // A table with a caption, column headers with scope and a header cell for each row's name.
        Assert.Matches(@"<caption>[^<]+\(4 in the file\)</caption>", html);
        Assert.Equal(4, Count(html, @"<th scope=""col"">"));
        Assert.Equal(4, Count(html, @"<th scope=""row"">"));
        // Each outcome is a word, never only a colour.
        foreach (var word in new[] { "Already up to date", "Will be added", "Not imported" })
            Assert.Contains(word, html);
        Assert.Contains("class=\"adm-status adm-status-refused\"", html);
        Assert.DoesNotMatch(@"style=""[^""]*color", html);
        Assert.Matches(@"<button type=""submit"" class=""adm-btn adm-btn-primary"">Apply the import</button>", html.Replace("Apply the import", "Apply the import"));
        Assert.Matches(@"<button type=""submit"" class=""adm-btn""[^>]*formaction=""[^""]*handler=Discard""", html);
    }

    [Fact]
    public async Task ARefusedFile_IsAnnouncedAsAnAlert_ThatTakesFocus()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var html = await (await TransferFlow.PreviewAsync(owner, """{ "schemaVersion": 9 }""")).Content.ReadAsStringAsync();

        Assert.Matches(@"<div id=""import-refusal"" class=""adm-flash adm-flash-error"" role=""alert"" tabindex=""-1"" autofocus>", html);
        Assert.Equal(1, Count(html, "autofocus"));
        Assert.DoesNotContain("id=\"preview-heading\"", html);
    }

    [Fact]
    public async Task TheResult_IsAHeadingThatTakesFocus_AStatusLine_AndALinkBack()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var preview = await (await TransferFlow.PreviewAsync(owner, ImportHarness.Table("skills", new { name = "Fresh", group = "G" }))).Content.ReadAsStringAsync();

        var html = await (await TransferFlow.ConfirmAsync(owner, TransferFlow.TokenOf(preview))).Content.ReadAsStringAsync();

        Assert.Matches(@"<h2 id=""result-heading"" tabindex=""-1"" autofocus>Import finished</h2>", html);
        Assert.Equal(1, Count(html, "autofocus"));
        Assert.Matches(@"<p role=""status"">\s*1 added", html);
        Assert.Contains("href=\"/admin/transfer\"", html);
        Assert.Contains("Added", html);
    }

    [Fact]
    public async Task TheDiscardFlash_IsAnnouncedPolitely_OnTheFormPage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await owner.GetAsync("/admin");
        var token = TransferFlow.TokenOf(await (await TransferFlow.PreviewAsync(owner, ImportHarness.Table("skills", new { name = "x", group = "G" }))).Content.ReadAsStringAsync())!;
        await ContentFlow.PostAsync(owner, TransferFlow.Page + "/confirm?handler=Discard", new Dictionary<string, string> { ["Token"] = token }, TransferFlow.Page);

        var html = await owner.GetStringAsync(TransferFlow.Page);

        Assert.Matches(@"<div class=""adm-flash adm-flash-ok"" role=""status"" aria-live=""polite""[^>]*>The preview was discarded\. Nothing was written\.</div>", html);
    }

    [Fact]
    public async Task ThePreviewTables_FoldIntoBlocksOnANarrowScreen_ThroughTheSharedAdminStyles()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var preview = await (await TransferFlow.PreviewAsync(owner, ImportHarness.Table("skills", new { name = "x", group = "G" }))).Content.ReadAsStringAsync();
        Assert.Matches(@"<link rel=""stylesheet"" href=""/css/admin\.[a-z0-9]+\.css"" />", preview);
        var css = File.ReadAllText(Path.Combine(TemplateDirs.Web, "wwwroot", "css", "admin.css"));

        Assert.Contains("class=\"adm-table\"", preview);
        Assert.Contains("@media (max-width: 40rem)", css);
        Assert.Contains(".adm-table thead { position: absolute", css);
        Assert.Contains(".adm-status-refused", css);
        Assert.DoesNotContain("/css/app.", preview); // the admin has its own stylesheet, not the public site's Tailwind build
    }

    // ---- every control does what it says: the real addresses are read from the rendered pages, never written into the test ----

    private static List<string> Targets(string html) =>
        Regex.Matches(html, @"<(?:form|button)\b[^>]*?\s(?:action|formaction)=""([^""]+)""").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).Distinct().ToList();

    private static async Task<HttpResponseMessage> PostToAsync(HttpClient client, string html, string target, string token) =>
        await AdminApp.PostFormAsync(client, target, AdminApp.TokenPattern.Match(html) is { Success: true } m ? WebUtility.HtmlDecode(m.Groups[1].Value) : null,
            new Dictionary<string, string> { ["Token"] = token });

    [Fact]
    public async Task DiscardOnAPreviewWithChanges_GoesWhereItSays_AndEndsThePreview_WithAnAnnouncedMessage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var preview = await (await TransferFlow.PreviewAsync(owner, ImportHarness.Table("skills", new { name = "Fresh", group = "G" }))).Content.ReadAsStringAsync();
        var token = TransferFlow.TokenOf(preview)!;
        var discard = Targets(preview).Single(t => t.Contains("handler=Discard", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("/admin/transfer/confirm", discard);

        var response = await PostToAsync(owner, preview, discard, token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/transfer", response.Headers.Location!.OriginalString);
        var page = await owner.GetStringAsync(TransferFlow.Page);
        Assert.Matches(@"<div class=""adm-flash adm-flash-ok"" role=""status"" aria-live=""polite"" tabindex=""-1"" autofocus=""autofocus"">The preview was discarded\. Nothing was written\.</div>", page);
        Assert.DoesNotContain("id=\"preview-heading\"", page);
        Assert.Contains("so nothing was written", await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync());
        using var db = app.CreateDbContext();
        Assert.Equal(0, await db.Skills.CountAsync());
    }

    [Fact]
    public async Task EveryFormAndButtonOnTheTransferPages_PostsToARealAddress_AndEveryLinkOpens()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.SeedRowsAsync(app);
        var form = await owner.GetStringAsync(TransferFlow.Page);

        // The import form: its real action takes the file.
        var importAction = Targets(form).Single(t => t.Contains("handler=Preview", StringComparison.OrdinalIgnoreCase));
        async Task<string> PreviewPageAsync(string name) => await (await ContentFlow.PostMultipartAsync(owner, importAction, new Dictionary<string, string> { ["ApplyProfile"] = "false" },
            ("Upload", TransferFlow.Utf8(ImportHarness.Table("skills", new { name, group = name == "Seeding" ? "Garden" : "G" })), "a.json", "application/json"), TransferFlow.Page)).Content.ReadAsStringAsync();

        // A session holds one preview, so each is used before the next is made. The one with nothing to write offers only its discard, to the real address.
        var samePage = await PreviewPageAsync("Seeding");
        var sameToken = TransferFlow.TokenOf(samePage)!;
        var sameTargets = Targets(samePage).Where(t => t.Contains("/confirm")).ToList();
        Assert.Equal(["/admin/transfer/confirm?handler=Discard"], sameTargets);
        Assert.Equal(HttpStatusCode.Redirect, (await PostToAsync(owner, samePage, sameTargets[0], sameToken)).StatusCode);

        var changedPage = await PreviewPageAsync("Fresh");

        // Every address on every screen opens or is a post that works: links are fetched, the forms of the previews are posted.
        foreach (var html in new[] { form, changedPage, samePage })
            foreach (var href in Regex.Matches(html, @"<a\b[^>]*\shref=""(/admin[^""]*)""").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).Distinct())
                Assert.True((await owner.GetAsync(href)).StatusCode < HttpStatusCode.BadRequest, $"link {href}");

        // The preview with changes: its apply form and its discard button both work (apply last: it writes).
        var token = TransferFlow.TokenOf(changedPage)!;
        var confirmTargets = Targets(changedPage).Where(t => t.Contains("/confirm")).ToList();
        var apply = confirmTargets.Single(t => !t.Contains("handler=", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.OK, (await PostToAsync(owner, changedPage, apply, token)).StatusCode);
        using var db = app.CreateDbContext();
        Assert.Contains(await db.Skills.Select(x => x.Name).ToListAsync(), n => n == "Fresh");
    }
}
