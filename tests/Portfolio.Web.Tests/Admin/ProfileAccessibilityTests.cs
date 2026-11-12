using System.Net;
using System.Text.RegularExpressions;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>Structure a screen reader and a keyboard rely on, for the identity page and the hero, metrics and About editors.</summary>
public class ProfileAccessibilityTests
{
    private static readonly string[] Pages = [ProfileFlow.Identity, ProfileFlow.Hero, ProfileFlow.Metrics, ProfileFlow.About];

    [Fact]
    public async Task EveryProfilePage_HasLabelsLegendsAndRealButtons_WithAndWithoutRows()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        foreach (var path in Pages) ContentAccessibilityTests.AssertAccessible(path + " (no rows)", await owner.GetStringAsync(path));

        await ProfileFlow.SeedTextAsync(app);
        foreach (var path in Pages) ContentAccessibilityTests.AssertAccessible(path + " (rows)", await owner.GetStringAsync(path));

        var rows = ProfileFlow.MetricsFields(("1", "a", "b"), ("2", "c", "d"));
        ContentAccessibilityTests.AssertAccessible("metrics after a row button", await (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics + "?handler=Add", rows, tokenFrom: ProfileFlow.Metrics)).Content.ReadAsStringAsync());
        var cards = ProfileFlow.AboutFields(cards: [("a", "b"), ("c", "d")]);
        ContentAccessibilityTests.AssertAccessible("about after a row button", await (await ProfileFlow.PostAsync(owner, ProfileFlow.About + "?handler=Remove&index=0", cards, tokenFrom: ProfileFlow.About)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InvalidForms_AnnounceTheirErrors_MarkTheFields_AndMoveFocusToTheSummary()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var html = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, ProfileFlow.MetricsFields(("1", "u", "l"), ("", "orphan", "")))).Content.ReadAsStringAsync();

        ContentAccessibilityTests.AssertAccessible("invalid metrics form", html);
        Assert.Contains("<div id=\"error-summary\" class=\"adm-flash adm-flash-error\" role=\"alert\" tabindex=\"-1\" autofocus>", html);
        Assert.Contains("<a href=\"#Form_Rows_1__Value\">Metric 2 value is required.</a>", html);
        Assert.Matches(@"id=""Form_Rows_1__Value""[^>]*aria-invalid=""true""", html);
        Assert.Contains("id=\"Form_Rows_1__Value-error\"", html);
        Assert.DoesNotMatch(@"id=""Form_Rows_0__Value""[^>]*aria-invalid", html); // the row that was fine is not marked
        Assert.Contains("value=\"orphan\"", html);

        var about = await (await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields(headline: "", cards: [("t", "")]))).Content.ReadAsStringAsync();
        Assert.Contains("<a href=\"#Form_Headline\">Headline is required.</a>", about);
        Assert.Contains("<a href=\"#Form_Cards_0__Body\">Card 1 text is required.</a>", about);
        Assert.Matches(@"id=""Form_Cards_0__Body""[^>]*aria-invalid=""true""", about);
    }

    [Fact]
    public async Task ASaveMessage_IsALivePoliteRegionThatTakesFocus_AndAConflictIsAnAlertThatTakesFocus()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var stale = await ProfileFlow.StampAsync(owner, ProfileFlow.Hero);
        await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields());

        var saved = await owner.GetStringAsync(ProfileFlow.Hero);
        Assert.Matches(@"<div class=""adm-flash adm-flash-ok"" role=""status"" aria-live=""polite"" tabindex=""-1"" autofocus=""autofocus"">Home page hero saved\.", saved);

        var conflict = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields(), stamp: stale)).Content.ReadAsStringAsync();
        Assert.Contains("<div id=\"error-summary\" class=\"adm-flash adm-flash-error\" role=\"alert\" tabindex=\"-1\" autofocus>", conflict);
        Assert.Contains("<strong>Nothing was saved.</strong>", conflict);
    }

    [Fact]
    public async Task TheRowButtons_AreNamedAfterTheirRow_TheEdgesAreDisabled_EveryOneIsAPostWithAToken_AndSaveComesFirst()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ProfileFlow.SeedTextAsync(app);

        foreach (var (path, name) in new[] { (ProfileFlow.Metrics, "Metric"), (ProfileFlow.About, "Card") })
        {
            var html = await owner.GetStringAsync(path);

            Assert.Matches($@"<button[^>]*disabled=""disabled""[^>]*aria-label=""Move up: {name} 1""", html);
            Assert.Matches($@"<button[^>]*disabled=""disabled""[^>]*aria-label=""Move down: {name} 2""", html);
            Assert.DoesNotMatch($@"disabled=""disabled""[^>]*aria-label=""Move down: {name} 1""", html);
            Assert.Contains($"aria-label=\"Remove: {name} 2\"", html);
            // The form's default button (what Enter in a text box presses) is the first one in the form, and it saves.
            var form = Regex.Match(html, @"<form method=""post""[^>]*data-unsaved-warning[^>]*>(.*?)</form>", RegexOptions.Singleline).Groups[1].Value;
            Assert.Contains("__RequestVerificationToken", form);
            var first = Regex.Match(form, @"<button\b[^>]*>(.*?)</button>", RegexOptions.Singleline);
            Assert.DoesNotContain("formaction", first.Value);
            Assert.Matches(@"^Save ", first.Groups[1].Value);
            // Every row button is a submit button with a handler address, never a link.
            foreach (Match button in Regex.Matches(form, @"<button\b[^>]*formaction=""([^""]*)""[^>]*>"))
                Assert.Matches(@"handler=(Move|Remove|Add)", button.Groups[1].Value);
        }
    }

    [Fact]
    public async Task TheAdminNavigation_MarksTheProfileForItsSubPages_AndTheDashboardLinksToThem()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        foreach (var path in ProfileFlow.Parts)
            Assert.Contains("<a href=\"/admin/profile\" aria-current=\"page\">Profile</a>", await owner.GetStringAsync(path));
        var dashboard = await owner.GetStringAsync("/admin");
        foreach (var path in ProfileFlow.Parts) Assert.Contains($"href=\"{path}\"", dashboard);
        var identity = await owner.GetStringAsync(ProfileFlow.Identity);
        foreach (var path in ProfileFlow.Parts) Assert.Contains($"href=\"{path}\"", identity);
    }

    [Fact]
    public async Task AGetOfAnEditor_HasNoStatusRegion_UntilARowButtonOrAnErrorMakesOne()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await owner.GetStringAsync("/admin"); // takes the "published" message left over from the wizard

        var html = await owner.GetStringAsync(ProfileFlow.Metrics);

        Assert.DoesNotContain("role=\"status\"", html);
        Assert.DoesNotContain("autofocus", html);
        Assert.Contains("data-has-errors=\"false\"", html);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(ProfileFlow.Metrics)).StatusCode);
    }
}
