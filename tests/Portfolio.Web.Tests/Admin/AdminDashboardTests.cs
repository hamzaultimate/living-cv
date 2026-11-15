using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;
using Portfolio.Web.Configuration;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The dashboard: a status row, a "Finish your site" checklist and the counts, each left out on its own when its data cannot be read.</summary>
public class AdminDashboardTests
{
    private static TemplateView Software(params SectionOption[] sections) =>
        new TemplateResolver(new TemplateCatalog(Path.Combine(AppContext.BaseDirectory, "Templates")))
            .Resolve(new SiteOptions { TemplateKey = "software", Sections = sections.ToList() }, null);

    private static OnboardingState State(bool finished, string? photo) =>
        new(new SiteProfile { OnboardingCompletedAt = finished ? DateTimeOffset.UtcNow : null, PhotoUrl = photo }, null);

    private static ContentCounts Counts(int each, int drafts = 0) =>
        new(EditorFields.Sections.ToDictionary(s => s, _ => each), drafts);

    private static string Block(string html, string heading) =>
        Regex.Match(html, $@"<section[^>]*aria-labelledby=""{heading}"".*?</section>", RegexOptions.Singleline).Value;

    [Fact]
    public void WhenNothingCanBeRead_NoBlockIsBuilt()
    {
        var model = AdminDashboard.Build(null, null, "/admin/welcome", null);

        Assert.Null(model.Status);
        Assert.Null(model.Checklist);
        Assert.Null(model.Counts);
    }

    [Fact]
    public void EachBlock_IsLeftOutOnItsOwn_WhenOnlyItsDataIsMissing()
    {
        var withoutCounts = AdminDashboard.Build(Software(), State(true, "/uploads/p.jpg"), "/admin/welcome", null);
        var withoutState = AdminDashboard.Build(Software(), null, "/admin/welcome", Counts(1));

        Assert.NotNull(withoutCounts.Status);
        Assert.Null(withoutCounts.Checklist);
        Assert.Null(withoutCounts.Counts);
        Assert.Null(withoutState.Status);
        Assert.Null(withoutState.Checklist);
        Assert.NotNull(withoutState.Counts);
    }

    [Fact]
    public void TheStatus_NamesTheTemplate_AndWhetherSetupIsPending()
    {
        var pending = AdminDashboard.Build(Software(), State(false, null), "/admin/welcome", Counts(0)).Status!;
        var done = AdminDashboard.Build(Software(), State(true, null), "/admin/welcome", Counts(0)).Status!;

        Assert.True(pending.SetupPending);
        Assert.False(done.SetupPending);
        Assert.Equal(Software().Definition.Name, pending.TemplateName);
    }

    [Fact]
    public void TheChecklist_ListsSetupPhotoAndEveryEnabledSectionWithNoEntries_EachWithItsFix()
    {
        var tasks = AdminDashboard.Build(Software(), State(false, null), "/admin/welcome/basics", Counts(0)).Checklist!;

        Assert.Contains(tasks, t => t.Url == "/admin/welcome/basics");
        Assert.Contains(tasks, t => t.Url == "/admin/photo");
        foreach (var s in EditorFields.Sections)
            Assert.Contains(tasks, t => t.Url == $"/admin/content/{s}/edit");
    }

    [Fact]
    public void TheChecklist_LeavesOutWhatIsDone_AndASectionTheTemplateSwitchesOff()
    {
        var counts = new ContentCounts(EditorFields.Sections.ToDictionary(s => s, s => s == "experience" ? 3 : 0), 0);

        var tasks = AdminDashboard.Build(Software(new SectionOption { Key = "talks", Enabled = false, Shown = false }), State(true, "/uploads/p.jpg"), "/admin/welcome", counts).Checklist!;

        Assert.DoesNotContain(tasks, t => t.Url == "/admin/welcome");
        Assert.DoesNotContain(tasks, t => t.Url == "/admin/photo");
        Assert.DoesNotContain(tasks, t => t.Url == "/admin/content/experience/edit");
        Assert.DoesNotContain(tasks, t => t.Url == "/admin/content/talks/edit");
        Assert.Contains(tasks, t => t.Url == "/admin/content/skills/edit");
    }

    [Fact]
    public void TheChecklist_IsEmpty_WhenNothingIsOutstanding() =>
        Assert.Empty(AdminDashboard.Build(Software(), State(true, "/uploads/p.jpg"), "/admin/welcome", Counts(2)).Checklist!);

    [Fact]
    public void TheCounts_UseTheTemplatesWording_AndCallOutBlogDrafts()
    {
        var counts = new ContentCounts(EditorFields.Sections.ToDictionary(s => s, s => s == "blog" ? 5 : 1), BlogDrafts: 2);

        var rows = AdminDashboard.Build(Software(), State(true, null), "/admin/welcome", counts).Counts!;

        Assert.Equal(EditorFields.Sections, rows.Select(r => r.Section));
        Assert.Equal(new EditorView(Software(), "projects").SectionLabel, rows.Single(r => r.Section == "projects").Label);
        Assert.Equal("/admin/content/blog", rows.Single(r => r.Section == "blog").Url);
        Assert.Equal("3 published, 2 drafts", rows.Single(r => r.Section == "blog").Detail);
        Assert.Null(rows.Single(r => r.Section == "skills").Detail);
    }

    [Fact]
    public void ASingleDraft_IsNotPluralised()
    {
        var counts = new ContentCounts(EditorFields.Sections.ToDictionary(s => s, s => s == "blog" ? 2 : 0), BlogDrafts: 1);

        Assert.Equal("1 published, 1 draft", AdminDashboard.Build(Software(), State(true, null), "/admin/welcome", counts).Counts!.Single(r => r.Section == "blog").Detail);
    }

    [Fact]
    public async Task TheDashboardPage_ShowsStatusChecklistAndCounts_InsteadOfTiles()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var html = await owner.GetStringAsync("/admin");

        Assert.Contains("id=\"status-heading\"", html);
        Assert.Contains("id=\"todo-heading\"", html);
        Assert.Contains("id=\"counts-heading\"", html);
        Assert.Contains("href=\"/admin/photo\"", Block(html, "todo-heading"));
        Assert.DoesNotContain("adm-tile", html);
        Assert.Matches(@"<a href=""/"" target=""_blank""", html); // the link to the public site
    }

    [Fact]
    public async Task AnOutstandingLine_GoesAway_WhenItsSectionGetsAnEntry()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        Assert.Contains("href=\"/admin/content/experience/edit\"", Block(await owner.GetStringAsync("/admin"), "todo-heading"));

        await ContentFlow.SeedRowsAsync(app);

        Assert.DoesNotContain("href=\"/admin/content/experience/edit\"", Block(await owner.GetStringAsync("/admin"), "todo-heading"));
    }

    [Fact]
    public async Task WhileSetupIsPending_TheContinueCalloutStays_AndTheStatusSaysSo()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var html = await owner.GetStringAsync("/admin");

        Assert.Contains("Continue setup", html);
        Assert.Contains("Skip setup", html);
        Assert.Contains("Not finished", html);
    }

    [Fact]
    public async Task TheDashboard_StillOpens_WhenTheCountsCannotBeRead_AndLeavesThoseBlocksOut()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"Experiences\"");

        var response = await owner.GetAsync("/admin");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"status-heading\"", html);
        Assert.DoesNotContain("id=\"todo-heading\"", html);
        Assert.DoesNotContain("id=\"counts-heading\"", html);
    }
}
