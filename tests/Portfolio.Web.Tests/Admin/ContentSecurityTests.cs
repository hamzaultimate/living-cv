using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The content editors and the profile editors (identity, hero, metrics, About) are owner-only, every change is a POST with an antiforgery token, and a GET never changes anything.</summary>
public class ContentSecurityTests
{
    /// <summary>Two rows in every content table and a profile with two metrics, two cards and two paragraphs, so a request that touched "one of them" would show.</summary>
    private static async Task<(ContentFlow.Seeded Rows, string Stamp)> SeedAsync(AdminApp app)
    {
        var rows = await ContentFlow.SeedRowsAsync(app, pairs: true);
        await ProfileFlow.SeedTextAsync(app);
        using (var db = app.CreateDbContext())
        {
            // Two stored section settings (a renamed one and a hidden one), so a request that touched "one of them" would show.
            var p = await db.SiteProfiles.SingleAsync();
            p.Sections = [new Portfolio.Data.Entities.SectionSetting { Key = "blog", Label = "Journal" }, new Portfolio.Data.Entities.SectionSetting { Key = "talks", Enabled = false, Shown = false }];
            await db.SaveChangesAsync();
        }
        return (rows, (await ProfileFlow.ProfileAsync(app)).ConcurrencyStamp!);
    }

    private static readonly string[] SoftwareKeys = ["about", "experience", "projects", "skills", "certifications", "blog", "talks", "testimonials", "contact"];

    /// <summary>The sections page as software draws it untouched.</summary>
    private static IEnumerable<SectionsFlow.Row> SoftwareRows => SoftwareKeys.Select(k => new SectionsFlow.Row(k, "", true));

    private static string[] Gets(ContentFlow.Seeded s) =>
    [
        ProfileFlow.Identity, ProfileFlow.Hero, ProfileFlow.Metrics, ProfileFlow.About, SectionsFlow.Path,
        TransferFlow.Page, TransferFlow.Export, TransferFlow.Page + "/confirm",
        "/admin/content",
        ContentFlow.Experience, ContentFlow.Experience + "/edit", $"{ContentFlow.Experience}/edit/{s.ExperienceId}", $"{ContentFlow.Experience}/delete/{s.ExperienceId}",
        ContentFlow.Skills, ContentFlow.Skills + "/edit", $"{ContentFlow.Skills}/edit/{s.SkillId}", $"{ContentFlow.Skills}/delete/{s.SkillId}",
        ContentFlow.Projects, ContentFlow.Projects + "/edit", $"{ContentFlow.Projects}/edit/{s.ProjectId}", $"{ContentFlow.Projects}/delete/{s.ProjectId}", $"{ContentFlow.Projects}/images/{s.ProjectId}",
        ContentFlow.Certifications, ContentFlow.Certifications + "/edit", $"{ContentFlow.Certifications}/edit/{s.CertificationId}", $"{ContentFlow.Certifications}/delete/{s.CertificationId}",
        ContentFlow.Talks, ContentFlow.Talks + "/edit", $"{ContentFlow.Talks}/edit/{s.TalkId}", $"{ContentFlow.Talks}/delete/{s.TalkId}",
        ContentFlow.Testimonials, ContentFlow.Testimonials + "/edit", $"{ContentFlow.Testimonials}/edit/{s.TestimonialId}", $"{ContentFlow.Testimonials}/delete/{s.TestimonialId}",
        ContentFlow.Blog, ContentFlow.Blog + "/edit", $"{ContentFlow.Blog}/edit/{s.BlogId}", $"{ContentFlow.Blog}/delete/{s.BlogId}",
    ];

    /// <summary>Every state-changing request the editors accept, with fields that would succeed for the signed-in owner.</summary>
    private static (string Path, Dictionary<string, string> Fields)[] Posts(ContentFlow.Seeded s, string stamp) =>
    [
        (ProfileFlow.Identity, new() { ["Form.OwnerName"] = "Changed Name", ["Form.BrandName"] = "Changed", ["Form.Url"] = "https://changed.example", ["Form.Stamp"] = stamp }),
        (ProfileFlow.Hero, ProfileFlow.HeroFields(headline: "Changed headline").WithStamp(stamp)),
        (ProfileFlow.Metrics, ProfileFlow.MetricsFields(("1", "x", "changed")).WithStamp(stamp)),
        (ProfileFlow.Metrics + "?handler=Add", ProfileFlow.MetricsFields(("1", "x", "changed")).WithStamp(stamp)),
        (ProfileFlow.Metrics + "?handler=Remove&index=0", ProfileFlow.MetricsFields(("1", "x", "changed")).WithStamp(stamp)),
        (ProfileFlow.Metrics + "?handler=Move&index=1&direction=up", ProfileFlow.MetricsFields(("1", "x", "a"), ("2", "y", "b")).WithStamp(stamp)),
        (ProfileFlow.About, ProfileFlow.AboutFields(headline: "Changed about").WithStamp(stamp)),
        (ProfileFlow.About + "?handler=Add", ProfileFlow.AboutFields().WithStamp(stamp)),
        (ProfileFlow.About + "?handler=Remove&index=0", ProfileFlow.AboutFields(cards: [("T", "B")]).WithStamp(stamp)),
        (ProfileFlow.About + "?handler=Move&index=1&direction=up", ProfileFlow.AboutFields(cards: [("T", "B"), ("U", "C")]).WithStamp(stamp)),
        (SectionsFlow.Path, SectionsFlow.Fields(SoftwareRows.Select(r => r.Key == "talks" ? r with { Enabled = false, Label = "Changed" } : r)).WithStamp(stamp)),
        (SectionsFlow.Path + "?handler=Move&index=1&direction=up", SectionsFlow.Fields(SoftwareRows).WithStamp(stamp)),
        (TransferFlow.Page + "?handler=Preview", new() { ["ApplyProfile"] = "true" }),
        (TransferFlow.Page + "/confirm", new() { ["Token"] = "0123456789abcdef0123456789abcdef" }),
        (TransferFlow.Page + "/confirm?handler=Discard", new() { ["Token"] = "0123456789abcdef0123456789abcdef" }),
        (TransferFlow.Export, new()),
        ($"{ContentFlow.Experience}/edit", ContentFlow.RoleFields("New Co", "New Role")),
        ($"{ContentFlow.Experience}/edit/{s.ExperienceId}", ContentFlow.RoleFields("Seed Co", "Renamed Role")),
        ($"{ContentFlow.Experience}?handler=Move&id={s.ExperienceId}", new() { ["direction"] = "down" }),
        ($"{ContentFlow.Experience}/delete/{s.ExperienceId}", new()),
        ($"{ContentFlow.Skills}/edit", ContentFlow.Fields(("Name", "New skill"), ("Group", "Garden"))),
        ($"{ContentFlow.Skills}/edit/{s.SkillId}", ContentFlow.Fields(("Name", "Renamed skill"), ("Group", "Garden"))),
        ($"{ContentFlow.Skills}?handler=Move&id={s.SkillId}", new() { ["direction"] = "down" }),
        ($"{ContentFlow.Skills}/delete/{s.SkillId}", new()),
        ($"{ContentFlow.Projects}/edit", ContentFlow.Fields(("Title", "New project"), ("Summary", "New summary."))),
        ($"{ContentFlow.Projects}/edit/{s.ProjectId}", ContentFlow.Fields(("Title", "Renamed project"), ("Summary", "New summary."))),
        ($"{ContentFlow.Projects}?handler=Move&id={s.ProjectId}", new() { ["direction"] = "down" }),
        ($"{ContentFlow.Projects}/delete/{s.ProjectId}", new()),
        ($"{ContentFlow.Projects}/images/{s.ProjectId}?handler=Remove", new() { ["url"] = s.ScreenshotUrl }),
        ($"{ContentFlow.Projects}/images/{s.ProjectId}?handler=Move", new() { ["url"] = s.ScreenshotUrl, ["direction"] = "down" }),
        ($"{ContentFlow.Projects}/images/{s.ProjectId}?handler=Upload", new() { ["Caption"] = "Sneaky" }),
        ($"{ContentFlow.Certifications}/edit", ContentFlow.Fields(("Name", "New cert"), ("Issuer", "Board"), ("Status", "Earned"))),
        ($"{ContentFlow.Certifications}/edit/{s.CertificationId}", ContentFlow.Fields(("Name", "Renamed cert"), ("Issuer", "Board"), ("Status", "Earned"))),
        ($"{ContentFlow.Certifications}?handler=Move&id={s.CertificationId}", new() { ["direction"] = "down" }),
        ($"{ContentFlow.Certifications}/delete/{s.CertificationId}", new()),
        ($"{ContentFlow.Talks}/edit", ContentFlow.Fields(("Title", "New talk"))),
        ($"{ContentFlow.Talks}/edit/{s.TalkId}", ContentFlow.Fields(("Title", "Renamed talk"))),
        ($"{ContentFlow.Talks}?handler=Move&id={s.TalkId}", new() { ["direction"] = "down" }),
        ($"{ContentFlow.Talks}/delete/{s.TalkId}", new()),
        ($"{ContentFlow.Testimonials}/edit", ContentFlow.Fields(("Quote", "A new quote that is long enough."), ("Author", "New Person"))),
        ($"{ContentFlow.Testimonials}/edit/{s.TestimonialId}", ContentFlow.Fields(("Quote", "A changed quote that is long enough."), ("Author", "Seed Person"))),
        ($"{ContentFlow.Testimonials}?handler=Move&id={s.TestimonialId}", new() { ["direction"] = "down" }),
        ($"{ContentFlow.Testimonials}/delete/{s.TestimonialId}", new()),
        ($"{ContentFlow.Blog}/edit", ContentFlow.Fields(("Title", "New post"), ("Date", "2025-01-01"), ("Body", "A body."))),
        ($"{ContentFlow.Blog}/edit/{s.BlogId}", ContentFlow.Fields(("Title", "Renamed post"), ("Date", "2025-01-01"), ("Body", "A body."), ("Stamp", "x"))),
        ($"{ContentFlow.Blog}/edit/{s.BlogId}?handler=Preview", ContentFlow.Fields(("Body", "# Preview"))),
        ($"{ContentFlow.Blog}/edit?handler=Preview", ContentFlow.Fields(("Body", "# Preview"))),
        ($"{ContentFlow.Blog}/delete/{s.BlogId}", new()),
    ];

    [Fact]
    public async Task AnonymousVisitors_AreSentToTheLogin_OnEveryRoute_AndNothingChanges()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var (seeded, stamp) = await SeedAsync(app);
        var before = await ContentFlow.SnapshotAsync(app);
        var anonymous = app.NewClient();

        foreach (var path in Gets(seeded))
        {
            var get = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, get.StatusCode);
            Assert.Contains("/admin/login", get.Headers.Location!.OriginalString);
        }
        foreach (var (path, fields) in Posts(seeded, stamp))
        {
            var post = await anonymous.PostAsync(path, new FormUrlEncodedContent(fields));
            Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
            Assert.Contains("/admin/login", post.Headers.Location!.OriginalString);
        }

        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task EveryPost_WithoutAnAntiforgeryToken_IsRefused_AndChangesNothing()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var (seeded, stamp) = await SeedAsync(app);
        var before = await ContentFlow.SnapshotAsync(app);

        foreach (var (path, fields) in Posts(seeded, stamp))
        {
            var response = await owner.PostAsync(path, new FormUrlEncodedContent(fields));
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"POST {path} returned {(int)response.StatusCode}");
        }

        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task AForeignAntiforgeryToken_IsRefused()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var other = app.NewClient();
        var foreign = await AdminApp.GetTokenAsync(other, "/admin/login");
        var before = await ContentFlow.SnapshotAsync(app);

        var response = await AdminApp.PostFormAsync(owner, $"{ContentFlow.Experience}/edit", foreign, ContentFlow.RoleFields());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task AStaleSession_IsRejectedOnEveryRoute_AndCannotWrite()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var (seeded, stamp) = await SeedAsync(app);
        var token = await AdminApp.GetTokenAsync(owner, $"{ContentFlow.Experience}/edit"); // fetched while the session was good
        using (var db = app.CreateDbContext())
        {
            (await db.OwnerAccounts.SingleAsync()).SecurityStamp = Guid.NewGuid().ToString("N"); // what reset-owner does
            await db.SaveChangesAsync();
        }
        var before = await ContentFlow.SnapshotAsync(app);

        foreach (var path in Gets(seeded))
        {
            var response = await owner.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/admin/login", response.Headers.Location!.OriginalString);
        }
        foreach (var (path, fields) in Posts(seeded, stamp))
        {
            var response = await AdminApp.PostFormAsync(owner, path, token, fields);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/admin/login", response.Headers.Location!.OriginalString);
        }

        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task AGet_NeverChangesAnything_EvenWithAHandlerNamedInTheQuery()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var (seeded, stamp) = await SeedAsync(app);
        var before = await ContentFlow.SnapshotAsync(app);

        foreach (var path in Gets(seeded))
            Assert.True((await owner.GetAsync(path)).StatusCode < HttpStatusCode.InternalServerError, path);
        var attempts = new[]
        {
            $"{ContentFlow.Experience}?handler=Move&id={seeded.ExperienceId}&direction=down",
            $"{ContentFlow.Experience}/delete/{seeded.ExperienceId}?handler=Delete",
            $"{ContentFlow.Skills}/delete/{seeded.SkillId}?confirm=true",
            $"{ContentFlow.Projects}/delete/{seeded.ProjectId}?handler=Post",
            $"{ContentFlow.Projects}/images/{seeded.ProjectId}?handler=Remove&url={seeded.ScreenshotUrl}",
            $"{ContentFlow.Projects}/images/{seeded.ProjectId}?handler=Move&url={seeded.ScreenshotUrl}&direction=down",
            $"{ContentFlow.Skills}?handler=Move&id={seeded.SkillId}&direction=down",
            $"{ContentFlow.Projects}?handler=Move&id={seeded.ProjectId}&direction=down",
            $"{ContentFlow.Certifications}?handler=Move&id={seeded.CertificationId}&direction=down",
            $"{ContentFlow.Certifications}/delete/{seeded.CertificationId}?confirm=1",
            $"{ContentFlow.Certifications}/edit/{seeded.CertificationId}?Form.Name=Hacked&Form.Issuer=Hacked",
            $"{ContentFlow.Talks}?handler=Move&id={seeded.TalkId}&direction=down",
            $"{ContentFlow.Talks}/delete/{seeded.TalkId}?confirm=1",
            $"{ContentFlow.Talks}/edit/{seeded.TalkId}?Form.Title=Hacked",
            $"{ContentFlow.Testimonials}?handler=Move&id={seeded.TestimonialId}&direction=down",
            $"{ContentFlow.Testimonials}/delete/{seeded.TestimonialId}?confirm=1",
            $"{ContentFlow.Testimonials}/edit/{seeded.TestimonialId}?Form.Author=Hacked&Form.Quote=Hacked",
            $"{ContentFlow.Blog}/delete/{seeded.BlogId}?confirm=1",
            $"{ContentFlow.Blog}/delete/{seeded.DraftId}?handler=Post",
            $"{ContentFlow.Blog}/edit/{seeded.DraftId}?Form.Title=Hacked&Form.Draft=false&Form.Body=Hacked",
            $"{ContentFlow.Blog}/edit/{seeded.BlogId}?handler=Preview&Form.Body=Hacked",
            $"{ContentFlow.Blog}?handler=Move&id={seeded.BlogId}&direction=down",
            $"{ContentFlow.Blog}?handler=Publish&id={seeded.DraftId}",
            $"{ProfileFlow.Hero}?Form.Headline=Hacked&Form.Stamp={stamp}",
            $"{ProfileFlow.Hero}?handler=Post&Form.Headline=Hacked&Form.Stamp={stamp}",
            $"{ProfileFlow.Metrics}?handler=Add",
            $"{ProfileFlow.Metrics}?handler=Remove&index=0",
            $"{ProfileFlow.Metrics}?handler=Move&index=1&direction=up",
            $"{ProfileFlow.Metrics}?Form.Rows[0].Value=Hacked&Form.Stamp={stamp}",
            $"{ProfileFlow.About}?handler=Add",
            $"{ProfileFlow.About}?handler=Remove&index=0",
            $"{ProfileFlow.About}?handler=Move&index=1&direction=up",
            $"{ProfileFlow.About}?Form.Headline=Hacked&Form.Stamp={stamp}",
            $"{TransferFlow.Page}?handler=Preview&ApplyProfile=true",
            $"{TransferFlow.Page}/confirm?handler=Discard&Token=0123456789abcdef0123456789abcdef",
            $"{TransferFlow.Page}/confirm?Token=0123456789abcdef0123456789abcdef",
            $"{TransferFlow.Export}?handler=Delete&Form.OwnerName=Hacked",
            $"{SectionsFlow.Path}?handler=Move&index=1&direction=up",
            $"{SectionsFlow.Path}?Form.Rows[6].Enabled=false&Form.Rows[6].Key=talks&Form.Stamp={stamp}",
            $"{ProfileFlow.Identity}?Form.OwnerName=Hacked&Form.BrandName=Hacked&Form.Url=https://hacked.example&Form.Stamp={stamp}",
        };
        foreach (var path in attempts)
            Assert.True((await owner.GetAsync(path)).StatusCode < HttpStatusCode.InternalServerError, path);

        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task OnlyTheEditForms_MayLoadAScript_AndNothingIsEverInline()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var (seeded, stamp) = await SeedAsync(app);

        foreach (var path in Gets(seeded))
        {
            var response = await owner.GetAsync(path);
            var csp = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
            var isEdit = path.Contains("/edit") || path.Contains("/images/") || ProfileFlow.Parts.Contains(path) || path == SectionsFlow.Path; // the forms that carry the unsaved changes warning
            Assert.Equal(isEdit, csp.Contains("script-src 'self'"));
            Assert.DoesNotContain("unsafe-inline", csp);
            Assert.DoesNotContain("unsafe-eval", csp);
            Assert.Contains("default-src 'none'", csp);
            Assert.Contains("frame-ancestors 'none'", csp);
            Assert.Contains("no-store", string.Join(",", response.Headers.GetValues("Cache-Control")));
            Assert.Contains("noindex", string.Join(",", response.Headers.GetValues("X-Robots-Tag")));

            var html = await response.Content.ReadAsStringAsync();
            // No inline script or handler: the only script an admin page may carry is a src file.
            Assert.DoesNotMatch(@"<script(?![^>]*\ssrc=)", html);
            Assert.DoesNotMatch(@"\son[a-z]+\s*=", html);
            Assert.DoesNotContain("javascript:", html);
        }
    }

    [Fact]
    public async Task TheUnsavedChangesScript_IsServedAsAStaticFile_AndLoadedOnlyByEditForms()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var edit = await owner.GetStringAsync($"{ContentFlow.Experience}/edit");
        var list = await owner.GetStringAsync(ContentFlow.Experience);

        var src = System.Text.RegularExpressions.Regex.Match(edit, "<script src=\"(/js/admin-unsaved[^\"]*\\.js)\"");
        Assert.True(src.Success, "the edit form loads the unsaved changes script");
        Assert.DoesNotContain("admin-unsaved", list);
        Assert.Contains("data-unsaved-warning", edit);
        var script = await app.NewClient().GetAsync(src.Groups[1].Value);
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Contains("beforeunload", await script.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnOversizedPost_IsRefusedBeforeItIsRead()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var token = await AdminApp.GetTokenAsync(owner, $"{ContentFlow.Experience}/edit");
        var fields = ContentFlow.RoleFields();
        fields["Form.Summary"] = new string('x', 600 * 1024);

        var response = await AdminApp.PostFormAsync(owner, $"{ContentFlow.Experience}/edit", token, fields);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, await ContentFlow.CountAsync(app));
    }

    [Fact]
    public async Task EveryContentPage_CapsItsPostBody_NotJustTheEditForms()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var (seeded, stamp) = await SeedAsync(app);
        var before = await ContentFlow.SnapshotAsync(app);
        var big = new Dictionary<string, string> { ["direction"] = "down", ["Stamp"] = new string('x', 100 * 1024) };

        foreach (var (path, tokenFrom) in new[]
                 {
                     ($"{ContentFlow.Experience}?handler=Move&id={seeded.ExperienceId}", ContentFlow.Experience),
                     ($"{ContentFlow.Skills}?handler=Move&id={seeded.SkillId}", ContentFlow.Skills),
                     ($"{ContentFlow.Projects}?handler=Move&id={seeded.ProjectId}", ContentFlow.Projects),
                     ($"{ContentFlow.Certifications}?handler=Move&id={seeded.CertificationId}", ContentFlow.Certifications),
                     ($"{ContentFlow.Testimonials}?handler=Move&id={seeded.TestimonialId}", ContentFlow.Testimonials),
                     ($"{ContentFlow.Talks}/delete/{seeded.TalkId}", $"{ContentFlow.Talks}/delete/{seeded.TalkId}"),
                     ($"{ContentFlow.Blog}/delete/{seeded.BlogId}", $"{ContentFlow.Blog}/delete/{seeded.BlogId}"),
                     ($"{ContentFlow.Testimonials}/delete/{seeded.TestimonialId}", $"{ContentFlow.Testimonials}/delete/{seeded.TestimonialId}"),
                     ($"{ContentFlow.Experience}/delete/{seeded.ExperienceId}", $"{ContentFlow.Experience}/delete/{seeded.ExperienceId}"),
                     ($"{ContentFlow.Projects}/delete/{seeded.ProjectId}", $"{ContentFlow.Projects}/delete/{seeded.ProjectId}"),
                 })
        {
            var response = await ContentFlow.PostAsync(owner, path, big, tokenFrom);
            Assert.True(response.StatusCode == HttpStatusCode.RequestEntityTooLarge, $"POST {path} returned {(int)response.StatusCode}");
        }
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task EveryProfileEditor_CapsItsPostBody_RowButtonsIncluded()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var (_, stamp) = await SeedAsync(app);
        var before = await ContentFlow.SnapshotAsync(app);
        var big = new Dictionary<string, string> { ["Form.Stamp"] = stamp, ["Form.Headline"] = new string('x', 300 * 1024) };

        foreach (var path in new[]
                 {
                     ProfileFlow.Identity, ProfileFlow.Hero, ProfileFlow.Metrics, ProfileFlow.About,
                     ProfileFlow.Metrics + "?handler=Add", ProfileFlow.Metrics + "?handler=Move&index=1&direction=up", ProfileFlow.Metrics + "?handler=Remove&index=0",
                     ProfileFlow.About + "?handler=Add", ProfileFlow.About + "?handler=Move&index=1&direction=up", ProfileFlow.About + "?handler=Remove&index=0",
                     SectionsFlow.Path, SectionsFlow.Path + "?handler=Move&index=1&direction=up",
                 })
        {
            var response = await ContentFlow.PostAsync(owner, path, big, path.Split('?')[0]);
            Assert.True(response.StatusCode == HttpStatusCode.RequestEntityTooLarge, $"POST {path} returned {(int)response.StatusCode}");
        }
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task EveryProfilePost_WithTheRealStamp_ReallyWouldHaveSucceeded_SoTheRefusalsAboveAreTheGuardsThemselves()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var (seeded, _) = await SeedAsync(app);

        // Each save with its real stamp is accepted and writes (the stamp moves), so a refusal in the replays above can only come from the owner check, the token or the stale session.
        foreach (var path in new[] { ProfileFlow.Hero, ProfileFlow.Metrics, ProfileFlow.About, ProfileFlow.Identity, SectionsFlow.Path })
        {
            var stamp = (await ProfileFlow.ProfileAsync(app)).ConcurrencyStamp!;
            var fields = Posts(seeded, stamp).First(p => p.Path == path).Fields;
            var response = await ContentFlow.PostAsync(owner, path, fields);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(path, response.Headers.Location!.OriginalString);
            Assert.NotEqual(stamp, (await ProfileFlow.ProfileAsync(app)).ConcurrencyStamp);
        }
    }
}
