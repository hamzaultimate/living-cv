using System.Text.RegularExpressions;
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
}
