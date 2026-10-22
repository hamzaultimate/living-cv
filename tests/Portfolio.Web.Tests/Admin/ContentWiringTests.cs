using System.Net;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The dashboard, the navigation and the wizard point at the editors, and nothing still says content editing is coming later.</summary>
public class ContentWiringTests
{
    [Fact]
    public async Task TheFinishSettingUpPrompt_AndTheReviewStep_PointAtTheEditors()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner);

        var dashboard = await owner.GetStringAsync("/admin");
        var review = await owner.GetStringAsync(WizardFlow.Review);

        Assert.Contains("Finish setting up your site", dashboard);
        Assert.Contains("<a href=\"/admin/content\">Content</a>", dashboard);
        Assert.Contains("add your own under Content in the admin menu", review);
        Assert.DoesNotContain("later release", review);
        Assert.DoesNotContain("arrives", review);
    }

    [Fact]
    public async Task NoAdminPage_SaysThatEditingContentIsComingLater()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        var pages = new List<string> { "/admin", "/admin/profile", "/admin/photo", "/admin/template", "/admin/content" };
        pages.AddRange(WizardFlow.Pages);
        pages.AddRange(ContentFlow.Sections);

        foreach (var path in pages)
        {
            var response = await owner.GetAsync(path);
            if (response.StatusCode != HttpStatusCode.OK) continue; // the wizard redirects until its steps are saved
            var html = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("arrives in a later", html);
            Assert.DoesNotContain("later release", html);
            Assert.DoesNotContain("coming soon", html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TheSkipMessage_MentionsTheEditors()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        await WizardFlow.PostAsync(owner, WizardFlow.Template + "?handler=Skip", new Dictionary<string, string>(), WizardFlow.Template);

        Assert.Contains("You can add your own content under Content at any time.", await owner.GetStringAsync("/admin"));
    }

    [Fact]
    public async Task TheHub_ListsFourEditors_WithCountsAndAddLinks()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.SeedRowsAsync(app);
        await ContentFlow.PostAsync(owner, ContentFlow.Skills + "/edit", ContentFlow.Fields(("Name", "Second"), ("Group", "Garden")));

        var hub = await owner.GetStringAsync("/admin/content");

        foreach (var section in new[] { "experience", "skills", "projects", "certifications" })
        {
            Assert.Contains($"href=\"/admin/content/{section}\"", hub);
            Assert.Contains($"href=\"/admin/content/{section}/edit\"", hub);
        }
        Assert.Contains("2 entries", hub);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(hub, "1 entry").Count);
    }
}
