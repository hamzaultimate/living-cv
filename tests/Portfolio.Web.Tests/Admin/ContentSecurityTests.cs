using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The content editors are owner-only, every change is a POST with an antiforgery token, and a GET never changes anything.</summary>
public class ContentSecurityTests
{
    private static string[] Gets(ContentFlow.Seeded s) =>
    [
        "/admin/content",
        ContentFlow.Experience, ContentFlow.Experience + "/edit", $"{ContentFlow.Experience}/edit/{s.ExperienceId}", $"{ContentFlow.Experience}/delete/{s.ExperienceId}",
        ContentFlow.Skills, ContentFlow.Skills + "/edit", $"{ContentFlow.Skills}/edit/{s.SkillId}", $"{ContentFlow.Skills}/delete/{s.SkillId}",
        ContentFlow.Projects, ContentFlow.Projects + "/edit", $"{ContentFlow.Projects}/edit/{s.ProjectId}", $"{ContentFlow.Projects}/delete/{s.ProjectId}", $"{ContentFlow.Projects}/images/{s.ProjectId}",
        ContentFlow.Certifications, ContentFlow.Certifications + "/edit", $"{ContentFlow.Certifications}/edit/{s.CertificationId}", $"{ContentFlow.Certifications}/delete/{s.CertificationId}",
    ];

    /// <summary>Every state-changing request the editors accept, with fields that would succeed for the signed-in owner.</summary>
    private static (string Path, Dictionary<string, string> Fields)[] Posts(ContentFlow.Seeded s) =>
    [
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
    ];

    [Fact]
    public async Task AnonymousVisitors_AreSentToTheLogin_OnEveryRoute_AndNothingChanges()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var before = await ContentFlow.SnapshotAsync(app);
        var anonymous = app.NewClient();

        foreach (var path in Gets(seeded))
        {
            var get = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, get.StatusCode);
            Assert.Contains("/admin/login", get.Headers.Location!.OriginalString);
        }
        foreach (var (path, fields) in Posts(seeded))
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
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var before = await ContentFlow.SnapshotAsync(app);

        foreach (var (path, fields) in Posts(seeded))
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
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
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
        foreach (var (path, fields) in Posts(seeded))
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
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
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
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);

        foreach (var path in Gets(seeded))
        {
            var response = await owner.GetAsync(path);
            var csp = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
            var isEdit = path.Contains("/edit") || path.Contains("/images/"); // the forms that carry the unsaved changes warning
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
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var before = await ContentFlow.SnapshotAsync(app);
        var big = new Dictionary<string, string> { ["direction"] = "down", ["Stamp"] = new string('x', 100 * 1024) };

        foreach (var (path, tokenFrom) in new[]
                 {
                     ($"{ContentFlow.Experience}?handler=Move&id={seeded.ExperienceId}", ContentFlow.Experience),
                     ($"{ContentFlow.Skills}?handler=Move&id={seeded.SkillId}", ContentFlow.Skills),
                     ($"{ContentFlow.Projects}?handler=Move&id={seeded.ProjectId}", ContentFlow.Projects),
                     ($"{ContentFlow.Certifications}?handler=Move&id={seeded.CertificationId}", ContentFlow.Certifications),
                     ($"{ContentFlow.Experience}/delete/{seeded.ExperienceId}", $"{ContentFlow.Experience}/delete/{seeded.ExperienceId}"),
                     ($"{ContentFlow.Projects}/delete/{seeded.ProjectId}", $"{ContentFlow.Projects}/delete/{seeded.ProjectId}"),
                 })
        {
            var response = await ContentFlow.PostAsync(owner, path, big, tokenFrom);
            Assert.True(response.StatusCode == HttpStatusCode.RequestEntityTooLarge, $"POST {path} returned {(int)response.StatusCode}");
        }
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }
}
