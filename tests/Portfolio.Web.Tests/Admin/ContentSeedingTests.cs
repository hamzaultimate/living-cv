using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Seeding;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>Deleting content from the admin must never bring the demo content back at the next start-up.</summary>
public class ContentSeedingTests
{
    private static readonly string SeedDir = Path.Combine(AppContext.BaseDirectory, "Seed");

    private static async Task<bool> RestartAsync(AdminApp app, bool keyConfigured = true)
    {
        using var db = app.CreateDbContext();
        return await FirstRun.SeedAtStartupAsync(db, SeedDir, SeedMode.IfEmpty, keyConfigured);
    }

    /// <summary>Removes every row of the four content tables through the confirmation pages, the way an owner would.</summary>
    private static async Task RemoveEverythingAsync(AdminApp app, HttpClient owner)
    {
        using var db = app.CreateDbContext();
        var targets = (await db.Experiences.Select(x => x.Id).ToListAsync()).Select(i => $"{ContentFlow.Experience}/delete/{i}")
            .Concat((await db.Skills.Select(x => x.Id).ToListAsync()).Select(i => $"{ContentFlow.Skills}/delete/{i}"))
            .Concat((await db.Projects.Select(x => x.Id).ToListAsync()).Select(i => $"{ContentFlow.Projects}/delete/{i}"))
            .Concat((await db.Certifications.Select(x => x.Id).ToListAsync()).Select(i => $"{ContentFlow.Certifications}/delete/{i}"))
            .ToList();
        foreach (var path in targets)
        {
            var remove = await ContentFlow.PostAsync(owner, path, new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, path) });
            Assert.Equal(System.Net.HttpStatusCode.Redirect, remove.StatusCode);
        }
        Assert.Equal(0, await ContentFlow.CountAsync(app));
    }

    [Theory]
    [InlineData("software")]
    [InlineData("healthcare")]
    public async Task ABlankSite_StaysEmpty_AfterTheOwnerCreatesAndDeletesEverything_AndRestarts(string template)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, template);
        await ContentFlow.SeedRowsAsync(app);
        await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields());
        await RemoveEverythingAsync(app, owner);

        Assert.False(await RestartAsync(app));
        Assert.False(await RestartAsync(app, keyConfigured: false)); // the setup key was removed, as the guide advises
        Assert.False(await RestartAsync(app));

        Assert.Equal(0, await ContentFlow.CountAsync(app));
        var counts = await DemoSite.RowCountsAsync(app);
        Assert.All(counts.Where(c => c.Key != "profiles"), c => Assert.Equal(0, c.Value));
        Assert.Contains("class=\"mt-10 font-mono text-sm text-muted\"", await app.NewClient().GetStringAsync("/experience"));
    }

    [Fact]
    public async Task ASiteThatStartedWithSampleContent_StaysEmpty_OnceTheOwnerRemovesTheSamples()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        await WizardFlow.SaveBasicsAsync(owner);
        await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields(content: StarterChoice.Sample));
        Assert.True(await ContentFlow.CountAsync(app) > 0);

        await RemoveEverythingAsync(app, owner);
        await RestartAsync(app);
        await RestartAsync(app, keyConfigured: false);

        Assert.Equal(0, await ContentFlow.CountAsync(app));
    }

    [Fact]
    public async Task ASkippedWizard_ThatLoadedTheDemo_StaysEmpty_OnceTheOwnerRemovesIt()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PostAsync(owner, WizardFlow.Template + "?handler=Skip", new Dictionary<string, string>(), WizardFlow.Template);
        Assert.True(await ContentFlow.CountAsync(app) > 0);

        await RemoveEverythingAsync(app, owner);
        await RestartAsync(app);

        Assert.Equal(0, await ContentFlow.CountAsync(app));
    }

    [Fact]
    public async Task ASiteThatExistedBeforeTheWizard_KeepsTheOldStartUpBehaviour_AnEmptiedTableIsSeededAgain()
    {
        // Documented limit: a site the wizard never finished (outcome empty) seeds an empty table at start-up, as it always did.
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
        {
            (await db.SiteProfiles.SingleAsync()).OnboardingOutcome = null; // what the migration leaves on an existing site
            await db.SaveChangesAsync();
        }
        Assert.Equal(0, await ContentFlow.CountAsync(app));

        Assert.True(await RestartAsync(app, keyConfigured: false));

        Assert.True(await ContentFlow.CountAsync(app) > 0);
    }

    [Fact]
    public async Task OneEmptiedTable_IsNotRefilled_WhileTheOthersKeepTheirRows()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PostAsync(owner, WizardFlow.Template + "?handler=Skip", new Dictionary<string, string>(), WizardFlow.Template);
        using (var db = app.CreateDbContext())
        {
            var ids = await db.Skills.Select(x => x.Id).ToListAsync();
            foreach (var id in ids)
            {
                var path = $"{ContentFlow.Skills}/delete/{id}";
                await ContentFlow.PostAsync(owner, path, new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, path) });
            }
        }
        var before = await DemoSite.RowCountsAsync(app);
        Assert.Equal(0, before["skills"]);

        await RestartAsync(app);

        Assert.Equal(before, await DemoSite.RowCountsAsync(app));
    }
}
