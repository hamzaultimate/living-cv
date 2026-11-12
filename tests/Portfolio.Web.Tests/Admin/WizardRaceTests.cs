using System.Net;
using Microsoft.EntityFrameworkCore;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The wizard's publish and skip depend on no form, so a profile write that lands in the middle of them is retried, and what is reported is what is true.</summary>
public class WizardRaceTests
{
    /// <summary>Moves the profile's stamp from another connection to the row, as a hero or photo save would, so the save in progress finds it changed.</summary>
    private static async Task MoveStampAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        (await db.SiteProfiles.SingleAsync()).ConcurrencyStamp = ProfileEditor.NewStamp();
        await db.SaveChangesAsync();
    }

    /// <summary>A hook that races the next <paramref name="times"/> profile saves.</summary>
    private static Func<Task> Race(AdminApp app, int times)
    {
        var left = times;
        Func<Task>? hook = null;
        hook = async () =>
        {
            await MoveStampAsync(app);
            if (--left > 0) app.BeforeOrderedSave = hook;
        };
        return hook;
    }

    private static async Task<HttpClient> ReadyToPublishAsync(AdminApp app)
    {
        var owner = await WizardFlow.FirstRunClientAsync(app);
        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await WizardFlow.PickTemplateAsync(owner, "software")));
        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(await WizardFlow.SaveBasicsAsync(owner)));
        return owner;
    }

    [Fact]
    public async Task APublish_ThatLosesOneRaceToAProfileWrite_IsRetried_AndGoesLive()
    {
        using var app = new AdminApp();
        var owner = await ReadyToPublishAsync(app);

        var response = await app.OverlapAsync(() => WizardFlow.PublishAsync(owner), Race(app, 1));

        Assert.Equal("/admin", WizardFlow.Where(response));
        Assert.Contains("Your site is live.", await owner.GetStringAsync("/admin"));
        var profile = await WizardFlow.ProfileAsync(app);
        Assert.NotNull(profile.OnboardingCompletedAt);
        Assert.Equal("published", profile.OnboardingOutcome);
        Assert.Null(profile.OnboardingDraft);
    }

    [Fact]
    public async Task APublish_ThatLosesTwice_SaysTheProfileChanged_KeepsTheDraft_AndNeverClaimsItWasAlreadyPublished()
    {
        using var app = new AdminApp();
        var owner = await ReadyToPublishAsync(app);

        var response = await app.OverlapAsync(() => WizardFlow.PublishAsync(owner), Race(app, 2));

        Assert.Equal(WizardFlow.Review, WizardFlow.Where(response));
        var page = await owner.GetStringAsync(WizardFlow.Review);
        Assert.Contains("Your profile was changed while publishing, so nothing was published.", page);
        Assert.DoesNotContain("already published", page);
        var profile = await WizardFlow.ProfileAsync(app);
        Assert.Null(profile.OnboardingCompletedAt);
        Assert.NotNull(profile.OnboardingDraft); // the answers are kept
        // And pressing Publish again, with nothing racing, works.
        Assert.Equal("/admin", WizardFlow.Where(await WizardFlow.PublishAsync(owner)));
        Assert.NotNull((await WizardFlow.ProfileAsync(app)).OnboardingCompletedAt);
    }

    [Fact]
    public async Task ASkip_ThatLosesOneRaceToAProfileWrite_IsRetried_AndIsRecorded()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await app.NewClient().GetAsync("/"); // the first public read creates the profile row from the Site config

        var response = await app.OverlapAsync(() => WizardFlow.PostAsync(owner, WizardFlow.Template + "?handler=Skip", new Dictionary<string, string>(), WizardFlow.Template), Race(app, 1));

        Assert.Equal("/admin", WizardFlow.Where(response));
        Assert.Contains("Setup skipped.", await owner.GetStringAsync("/admin"));
        var profile = await WizardFlow.ProfileAsync(app);
        Assert.NotNull(profile.OnboardingCompletedAt);
        Assert.Equal("skipped", profile.OnboardingOutcome);
    }

    [Fact]
    public async Task ASkip_ThatLosesTwice_NeverFlashesSuccess_RecordsNothing_AndLeavesSetupOpen()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await app.NewClient().GetAsync("/"); // the first public read creates the profile row from the Site config

        var response = await app.OverlapAsync(() => WizardFlow.PostAsync(owner, WizardFlow.Template + "?handler=Skip", new Dictionary<string, string>(), WizardFlow.Template), Race(app, 2));

        Assert.Equal("/admin", WizardFlow.Where(response));
        var page = await owner.GetStringAsync("/admin");
        Assert.DoesNotContain("Setup skipped", page);
        Assert.Contains("nothing was recorded", page);
        var profile = await WizardFlow.ProfileAsync(app);
        Assert.Null(profile.OnboardingCompletedAt);
        Assert.Null(profile.OnboardingOutcome);
        Assert.Contains("Continue setup", page); // still open: the dashboard still offers it
    }
}
