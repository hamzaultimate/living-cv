using System.Net;
using System.Text.RegularExpressions;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>Structure a screen reader and a keyboard rely on: labels bound to inputs, groups with legends, live regions, real buttons and focus.</summary>
public class ContentAccessibilityTests
{
    private static readonly Regex Control = new(@"<(input|select|textarea)\b([^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $@"\s{name}=""([^""]*)""", RegexOptions.IgnoreCase);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : "";
    }

    /// <summary>Asserts the structural rules that apply to any content editor page.</summary>
    private static void AssertAccessible(string path, string html)
    {
        var ids = Regex.Matches(html, @"\sid=""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
        Assert.True(ids.Count == ids.Distinct().Count(), $"{path}: duplicate ids {string.Join(", ", ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key))}");

        var labelFor = Regex.Matches(html, @"<label\b[^>]*\sfor=""([^""]+)""").Select(m => m.Groups[1].Value).ToHashSet();
        foreach (Match m in Control.Matches(html))
        {
            var tag = m.Value;
            var type = Attr(tag, "type");
            if (type is "hidden" or "submit") continue;
            var id = Attr(tag, "id");
            Assert.True(id.Length > 0, $"{path}: a control has no id: {tag}");
            Assert.True(labelFor.Contains(id), $"{path}: control {id} has no label bound to it");
            foreach (var described in Attr(tag, "aria-describedby").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                Assert.True(ids.Contains(described), $"{path}: {id} is described by a missing element {described}");
        }

        foreach (Match fieldset in Regex.Matches(html, @"<fieldset\b[^>]*>(.*?)</fieldset>", RegexOptions.Singleline))
            Assert.StartsWith("<legend", fieldset.Groups[1].Value.TrimStart());

        // Anything that changes data is a form button, never a link.
        foreach (Match a in Regex.Matches(html, @"<a\b[^>]*\shref=""([^""]*)"""))
            Assert.DoesNotMatch(@"handler=(Move|Remove|Upload|Skip|Publish)", a.Groups[1].Value);

        Assert.Contains("<main id=\"main-content\"", html);
        Assert.Contains("class=\"adm-skip\"", html);
        Assert.Matches(@"<h1\b", html);
        Assert.Single(Regex.Matches(html, @"<h1\b"));
        foreach (Match button in Regex.Matches(html, @"<button\b([^>]*)>(.*?)</button>", RegexOptions.Singleline))
            Assert.True(Regex.Replace(button.Groups[2].Value, "<[^>]+>", "").Trim().Length > 0 || Attr(button.Value, "aria-label").Length > 0, $"{path}: a button has no name");
    }

    [Fact]
    public async Task EveryEditorPage_HasLabelsLegendsAndRealButtons()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app);
        var pages = new[]
        {
            "/admin/content", ContentFlow.Experience, ContentFlow.Skills, ContentFlow.Projects, ContentFlow.Certifications,
            ContentFlow.Experience + "/edit", $"{ContentFlow.Experience}/edit/{seeded.ExperienceId}", $"{ContentFlow.Experience}/delete/{seeded.ExperienceId}",
            ContentFlow.Skills + "/edit", $"{ContentFlow.Skills}/edit/{seeded.SkillId}", $"{ContentFlow.Skills}/delete/{seeded.SkillId}",
            ContentFlow.Projects + "/edit", $"{ContentFlow.Projects}/edit/{seeded.ProjectId}", $"{ContentFlow.Projects}/delete/{seeded.ProjectId}", $"{ContentFlow.Projects}/images/{seeded.ProjectId}",
            ContentFlow.Certifications + "/edit", $"{ContentFlow.Certifications}/edit/{seeded.CertificationId}", $"{ContentFlow.Certifications}/delete/{seeded.CertificationId}",
        };

        foreach (var path in pages) AssertAccessible(path, await owner.GetStringAsync(path));
    }

    [Fact]
    public async Task InvalidForms_AnnounceTheirErrors_MarkTheFields_AndMoveFocusToTheSummary()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = ContentFlow.RoleFields(company: "", start: "someday");

        var response = await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", fields);
        var html = await response.Content.ReadAsStringAsync();

        AssertAccessible("invalid experience form", html);
        Assert.Contains("<div id=\"error-summary\" class=\"adm-flash adm-flash-error\" role=\"alert\" tabindex=\"-1\" autofocus", html);
        Assert.Contains("<a href=\"#Form_Company\">Company is required.</a>", html);
        Assert.Contains("<a href=\"#Form_Start\">", html);
        Assert.Matches(@"id=""Form_Company""[^>]*aria-invalid=""true""", html);
        Assert.Matches(@"id=""Form_Start""[^>]*aria-invalid=""true""", html);
        Assert.Contains("id=\"Form_Company-error\"", html);
        // The fields that were fine are not marked.
        Assert.DoesNotMatch(@"id=""Form_Title""[^>]*aria-invalid", html);
        // The values the owner typed are still there.
        Assert.Contains("value=\"someday\"", html);
    }

    [Fact]
    public async Task AFlashMessage_IsALivePoliteRegion_ThatTakesFocus()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields());

        var html = await owner.GetStringAsync(ContentFlow.Experience);

        Assert.Matches(@"<div class=""adm-flash adm-flash-ok"" role=""status"" aria-live=""polite"" tabindex=""-1"" autofocus=""autofocus"">Saved ", html);
    }

    [Fact]
    public async Task ARefusedChange_IsAnAlertThatTakesFocus()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        Assert.Equal(HttpStatusCode.Redirect, (await owner.GetAsync($"{ContentFlow.Experience}/delete/999")).StatusCode); // does not exist: redirects with an error message
        var html = await owner.GetStringAsync(ContentFlow.Experience);

        Assert.Matches(@"<div class=""adm-flash adm-flash-error"" role=""alert"" tabindex=""-1"" autofocus=""autofocus"">That role no longer exists\.", html);
    }

    [Fact]
    public async Task TheConfirmationPage_TakesFocus_NamesWhatGoes_AndOffersAWayBack()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app);

        var html = await owner.GetStringAsync($"{ContentFlow.Experience}/delete/{seeded.ExperienceId}");

        Assert.Contains("<h1 id=\"delete-heading\" tabindex=\"-1\" autofocus>Remove this role?</h1>", html);
        Assert.Contains("Seed Role at Seed Co", html);
        Assert.Contains("<button type=\"submit\" class=\"adm-btn adm-btn-danger\">Remove role</button>", html);
        Assert.Contains("<a class=\"adm-btn\" href=\"/admin/content/experience\">Keep it</a>", html);
    }

    [Fact]
    public async Task ReorderingButtons_AreNamedAfterTheirRow_AndTheEdgesAreDisabled()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields("A Co", "First"));
        await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields("B Co", "Second"));

        var html = await owner.GetStringAsync(ContentFlow.Experience);

        Assert.Contains("aria-label=\"Move up: First\"", html);
        Assert.Contains("aria-label=\"Move down: Second\"", html);
        Assert.Matches(@"<button type=""submit"" class=""adm-btn"" disabled=""disabled"" aria-label=""Move up: First"">", html);
        Assert.Matches(@"<button type=""submit"" class=""adm-btn"" disabled=""disabled"" aria-label=""Move down: Second"">", html);
        Assert.DoesNotMatch(@"disabled=""disabled"" aria-label=""Move down: First""", html);
        // Every move button is inside a POST form with an antiforgery token.
        foreach (Match form in Regex.Matches(html, @"<form method=""post""[^>]*>(.*?)</form>", RegexOptions.Singleline))
            Assert.Contains("__RequestVerificationToken", form.Groups[1].Value);
    }

    [Fact]
    public async Task TheAdminNavigation_PointsAtContent_AndMarksTheCurrentPage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var hub = await owner.GetStringAsync("/admin/content");
        var list = await owner.GetStringAsync(ContentFlow.Skills);
        var dashboard = await owner.GetStringAsync("/admin");

        Assert.Contains("<a href=\"/admin/content\" aria-current=\"page\">Content</a>", hub);
        Assert.Contains("<a href=\"/admin/content\" aria-current=\"page\">Content</a>", list);
        Assert.Contains("<a href=\"/admin/content\">Content</a>", dashboard);
        Assert.Contains("href=\"/admin/content\"", dashboard);
        Assert.Contains("Add, edit, reorder and remove your experience, skills, work and certifications.", dashboard);
    }

    [Fact]
    public async Task EveryCheckbox_PostsTrueFirst_WhenTicked_SoTheBinderReadsTheTick()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app);

        foreach (var path in new[] { ContentFlow.Experience + "/edit", $"{ContentFlow.Projects}/edit/{seeded.ProjectId}" })
        {
            var html = await owner.GetStringAsync(path);
            var boxes = Regex.Matches(html, @"<input type=""checkbox""[^>]*\sname=""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
            Assert.NotEmpty(boxes);
            foreach (var name in boxes)
                // A browser posts the fields in document order: a ticked box then the "false" fallback, so the first value is "true".
                Assert.Matches(new Regex($@"<input type=""checkbox""[^>]*\sname=""{Regex.Escape(name)}""[^>]*>\s*<input type=""hidden"" name=""{Regex.Escape(name)}"" value=""false"" />"), html);
        }

        // What the browser sends for a ticked box: the tick first, then the fallback.
        var role = new List<KeyValuePair<string, string>>
        {
            new("Form.Company", "Tick Co"), new("Form.Title", "Ticker"), new("Form.Start", "2022"), new("Form.Summary", "s"),
            new("Form.Current", "true"), new("Form.Current", "false"),
        };
        var token = await AdminApp.GetTokenAsync(owner, ContentFlow.Experience + "/edit");
        role.Add(new("__RequestVerificationToken", token));
        var response = await owner.PostAsync(ContentFlow.Experience + "/edit", new FormUrlEncodedContent(role));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var db = app.CreateDbContext();
        Assert.True(db.Experiences.Single(e => e.Company == "Tick Co").Current);
    }
}
