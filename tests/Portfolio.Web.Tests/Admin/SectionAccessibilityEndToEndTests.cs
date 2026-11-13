using System.Net;
using System.Text.RegularExpressions;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>Structure a keyboard and a screen reader rely on for the sections page, and the whole story on a blank wizard-finished site in both templates.</summary>
public class SectionAccessibilityEndToEndTests
{
    [Theory]
    [InlineData("software")]
    [InlineData("healthcare")]
    public async Task ThePage_HasLabelsLegendsNamedRowButtons_AndSaveComesFirst(string template)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, template);
        var html = await owner.GetStringAsync(SectionsFlow.Path);

        ContentAccessibilityTests.AssertAccessible(SectionsFlow.Path, html);
        Assert.Contains("<a href=\"/admin/sections\" aria-current=\"page\">Sections</a>", html);
        Assert.Contains("aria-label=\"Move up: About\"", html);
        Assert.Matches(@"<button[^>]*disabled=""disabled""[^>]*aria-label=""Move up: About""", html);
        Assert.DoesNotContain("aria-label=\"Remove", html); // sections are shown, hidden and moved, never removed
        var form = Regex.Match(html, @"<form method=""post""[^>]*data-unsaved-warning[^>]*>(.*?)</form>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Matches(@"^Save ", Regex.Match(form, @"<button\b[^>]*>(.*?)</button>", RegexOptions.Singleline).Groups[1].Value);
        Assert.Contains("__RequestVerificationToken", form);
        // Every switch is a labelled checkbox; its error and hint ids exist.
        Assert.Equal(template == "software" ? 9 : 9, Regex.Matches(html, @"type=""checkbox""[^>]*id=""Form_Rows_\d+__Enabled""|id=""Form_Rows_\d+__Enabled""[^>]*type=""checkbox""").Count);
    }

    [Fact]
    public async Task InvalidAndStaleSaves_AnnounceThemselves_MarkTheField_AndMoveFocusToTheSummary_AndASaveIsAPoliteLiveMessage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await owner.GetAsync("/admin");

        var bad = await (await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("projects", label: "<b>x</b>"))).Content.ReadAsStringAsync();
        ContentAccessibilityTests.AssertAccessible("invalid sections form", bad);
        Assert.Contains("<div id=\"error-summary\" class=\"adm-flash adm-flash-error\" role=\"alert\" tabindex=\"-1\" autofocus>", bad);
        Assert.Matches(@"id=""Form_Rows_2__Label""[^>]*aria-invalid=""true""", bad);
        Assert.Contains("value=\"&lt;b&gt;x&lt;/b&gt;\"", bad);

        await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("projects", label: "Casework"));
        Assert.Matches(@"<div class=""adm-flash adm-flash-ok"" role=""status"" aria-live=""polite"" tabindex=""-1"" autofocus=""autofocus"">Site sections saved\.", await owner.GetStringAsync(SectionsFlow.Path));

        var moved = await (await ProfileFlow.PostAsync(owner, SectionsFlow.Path + "?handler=Move&index=2&direction=down", SectionsFlow.Fields(await SectionsFlow.RowsAsync(owner)), tokenFrom: SectionsFlow.Path)).Content.ReadAsStringAsync();
        Assert.Contains("role=\"status\"", moved);
        Assert.Contains("data-has-errors=\"true\"", moved);
        Assert.Matches(@"id=""row-3-down""[^>]*autofocus=""autofocus""|autofocus=""autofocus""[^>]*id=""row-3-down""", moved);
    }

    [Theory]
    [InlineData("software")]
    [InlineData("healthcare")]
    public async Task ABlankSite_ControlsItsSectionsFromTheAdmin_AndEverySurfaceFollowsAtOnce_InBothTemplates(string template)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, template);
        var rows = await ContentFlow.SeedRowsAsync(app, pairs: true);
        await owner.GetAsync("/admin");
        var visitor = app.NewClient();
        Assert.Contains("/skills", SectionsFlow.SitemapPaths(await visitor.GetStringAsync("/sitemap.xml"))); // primes the cached sitemap
        var og = await visitor.GetByteArrayAsync("/og.png");

        // Rename one, hide one, move one, and (healthcare) switch the articles on.
        var save = await SectionsFlow.SaveAsync(owner, r => SectionsFlow.MoveTo("skills", 0)(SectionsFlow.Set("blog", true)(SectionsFlow.Set("experience", false)(SectionsFlow.Set("contact", label: "Reach me")(r)))));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        var home = await visitor.GetStringAsync("/");
        var nav = SectionsFlow.NavOf(home).Split(',').Select(x => x.Split('=')[0]).ToList();
        Assert.Equal("/skills", nav[0]);
        Assert.DoesNotContain("/experience", nav);
        Assert.Contains("/blog", nav);
        Assert.Contains("Reach me", home);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/experience")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/blog")).StatusCode);
        var paths = SectionsFlow.SitemapPaths(await visitor.GetStringAsync("/sitemap.xml"));
        Assert.DoesNotContain("/experience", paths);
        Assert.Contains("/blog/seed-post", paths);
        Assert.Equal("/skills", paths[1]); // after the home page, in the owner's order
        Assert.DoesNotContain("Seed Role", await visitor.GetStringAsync("/cv"));
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/cv.pdf")).StatusCode);
        Assert.Equal(og, await visitor.GetByteArrayAsync("/og.png")); // the share image does not read the sections
        using (var db = app.CreateDbContext())
            Assert.Equal(2, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.Experiences)); // hiding deleted nothing

        // Everything back as the template has it: nothing is stored, every surface is as before.
        await SectionsFlow.SaveAsync(owner, r => r.Select(x => x with { Label = "" }).ToList());
        var back = await SectionsFlow.RowsAsync(owner);
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, SectionsFlow.Path, SectionsFlow.Fields(back.Select(x => new SectionsFlow.Row(x.Key, "", template == "software" || x.Key is not ("blog" or "talks")))
            .OrderBy(x => (template == "software" ? new[] { "about", "experience", "projects", "skills", "certifications", "blog", "talks", "testimonials", "contact" } : new[] { "about", "certifications", "experience", "skills", "projects", "blog", "talks", "testimonials", "contact" }).ToList().IndexOf(x.Key))))).StatusCode);
        var left = await SectionsFlow.StoredAsync(app);
        Assert.DoesNotContain(left, s => s.SortOrder != 0 || s.Label != null); // no names and no order left; explicit show/hide choices are kept
        Assert.Equal(template == "software" ? new[] { "experience" } : new[] { "blog", "experience" }, left.Select(s => s.Key).OrderBy(x => x));
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/experience")).StatusCode);
        Assert.Equal(template == "software" ? HttpStatusCode.OK : HttpStatusCode.NotFound, (await visitor.GetAsync("/blog")).StatusCode);
    }
}
