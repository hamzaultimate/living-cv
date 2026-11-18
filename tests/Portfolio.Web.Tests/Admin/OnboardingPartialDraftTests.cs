using Microsoft.Extensions.DependencyInjection;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The service saves whatever has been typed, in any order, and never needs an earlier step first. Completeness is only asked at publishing.</summary>
public class OnboardingPartialDraftTests
{
    private static async Task<T> WithServiceAsync<T>(AdminApp app, Func<OnboardingService, Task<T>> act)
    {
        using var scope = app.Services.CreateScope();
        return await act(scope.ServiceProvider.GetRequiredService<OnboardingService>());
    }

    [Fact]
    public async Task SaveBasics_StoresPartialInput_WithoutATemplate()
    {
        using var app = new AdminApp();
        await ContentFlow.BlankSiteAsync(app); // a published software site: the profile row exists and the wizard is a re-run

        await WithServiceAsync(app, async s => { await s.SaveBasicsAsync(new BasicsForm { OwnerName = "  Jordan  " }); return 0; });

        var draft = await WizardFlow.DraftOrNullAsync(app);
        Assert.Equal("Jordan", draft!.OwnerName);
        Assert.Null(draft.Email);
        Assert.Null(draft.JobTitle);
        Assert.Null(draft.TemplateKey);
        Assert.Equal(StepState.NeedsAttention, WizardProgress.Compute(draft, app.Services.GetRequiredService<ITemplateCatalog>())[1].State);
    }

    [Fact]
    public async Task SaveChoices_CreatesTheDraft_AndStoresAnAddressThatIsNotYetValid()
    {
        using var app = new AdminApp();
        await ContentFlow.BlankSiteAsync(app); // a published software site: the profile row exists and the wizard is a re-run

        var saved = await WithServiceAsync(app, s => s.SaveChoicesAsync(new ReviewForm { SiteUrl = "jordan.example", Accent = "", Content = StarterChoice.Blank }));

        Assert.True(saved);
        var draft = await WizardFlow.DraftOrNullAsync(app);
        Assert.Equal("jordan.example", draft!.SiteUrl);
        Assert.Null(draft.TemplateKey);
    }

    [Fact]
    public async Task SaveChoices_StillRefusesAnAccentTheTemplateDoesNotOffer()
    {
        using var app = new AdminApp();
        await ContentFlow.BlankSiteAsync(app); // a published software site: the profile row exists and the wizard is a re-run

        var saved = await WithServiceAsync(app, s => s.SaveChoicesAsync(new ReviewForm { SiteUrl = "https://jordan.example", Accent = "no-such-accent", Content = StarterChoice.Blank }));

        Assert.False(saved);
        Assert.Null(await WizardFlow.DraftOrNullAsync(app));
    }

    [Fact]
    public async Task TemplateFor_IsTheDraftsTemplate_ElseTheSitesOwn_AndIgnoresAnUnknownKey()
    {
        using var app = new AdminApp();
        await ContentFlow.BlankSiteAsync(app); // a published software site: the profile row exists and the wizard is a re-run
        var catalog = app.Services.GetRequiredService<ITemplateCatalog>();

        var site = await WithServiceAsync(app, async s => s.TemplateFor(await s.GetStateAsync()));
        Assert.Equal(TemplateCatalog.DefaultKey, site.Key);

        await WithServiceAsync(app, async s => await s.SaveTemplateAsync(catalog.Get("healthcare")));
        var chosen = await WithServiceAsync(app, async s => s.TemplateFor(await s.GetStateAsync()));
        Assert.Equal("healthcare", chosen.Key);

        var unknown = await WithServiceAsync(app, async s =>
        {
            var state = await s.GetStateAsync();
            return s.TemplateFor(new OnboardingState(state.Profile, new OnboardingDraft { TemplateKey = "no-such-template" }));
        });
        Assert.Equal(TemplateCatalog.DefaultKey, unknown.Key);
    }

    [Fact]
    public async Task KeepTemplate_CreatesADraftOnTheCurrentTemplate_AndLeavesAChosenOneAlone()
    {
        using var app = new AdminApp();
        await ContentFlow.BlankSiteAsync(app); // a published software site: the profile row exists and the wizard is a re-run
        var catalog = app.Services.GetRequiredService<ITemplateCatalog>();

        Assert.Null(await WithServiceAsync(app, s => s.KeepTemplateAsync()));
        Assert.Equal(TemplateCatalog.DefaultKey, (await WizardFlow.DraftOrNullAsync(app))!.TemplateKey);

        await WithServiceAsync(app, async s => await s.SaveTemplateAsync(catalog.Get("healthcare")));
        Assert.Null(await WithServiceAsync(app, s => s.KeepTemplateAsync()));
        Assert.Equal("healthcare", (await WizardFlow.DraftOrNullAsync(app))!.TemplateKey);
    }

    [Fact]
    public async Task ThePreview_AppliesEachFieldThatHasAValue_AndKeepsTheSavedValueForTheRest()
    {
        using var app = new AdminApp();
        await ContentFlow.BlankSiteAsync(app); // a published software site: the profile row exists and the wizard is a re-run
        var saved = await WizardFlow.ProfileAsync(app);
        await WithServiceAsync(app, async s => { await s.SaveBasicsAsync(new BasicsForm { OwnerName = "Part Person" }); return 0; });

        var preview = await WithServiceAsync(app, s => s.DraftPreviewAsync());

        Assert.NotNull(preview); // a draft without a template previews on the template the site is on
        Assert.Equal("Part Person", preview!.OwnerName);
        Assert.Equal(saved.JobTitle, preview.JobTitle);
    }

    [Fact]
    public async Task AnEmptyLocation_ClearsTheSavedOne_OnlyOnceTheBasicsAreComplete()
    {
        using var app = new AdminApp();
        await ContentFlow.BlankSiteAsync(app); // a published software site: the profile row exists and the wizard is a re-run
        Assert.Equal("Leeds, UK", (await WizardFlow.ProfileAsync(app)).Location); // BlankSiteAsync published it

        await WithServiceAsync(app, async s => { await s.SaveBasicsAsync(new BasicsForm { OwnerName = "Part Person" }); return 0; });
        var partial = await WithServiceAsync(app, s => s.DraftPreviewAsync());
        Assert.Equal("Leeds, UK", partial!.Location);

        await WithServiceAsync(app, async s => { await s.SaveBasicsAsync(new BasicsForm { OwnerName = "Jordan", JobTitle = "Nurse", Email = "j@owner.example", Summary = "Hello." }); return 0; });
        var complete = await WithServiceAsync(app, s => s.DraftPreviewAsync());
        Assert.True(string.IsNullOrEmpty(complete!.Location));

        await WithServiceAsync(app, async s => { await s.SaveBasicsAsync(new BasicsForm { OwnerName = "Jordan", Location = "Bristol" }); return 0; });
        var typed = await WithServiceAsync(app, s => s.DraftPreviewAsync());
        Assert.Equal("Bristol", typed!.Location);
    }

    [Fact]
    public async Task PublishingAnIncompleteDraft_IsStillRefused_ByTheService()
    {
        using var app = new AdminApp();
        await ContentFlow.BlankSiteAsync(app); // a published software site: the profile row exists and the wizard is a re-run
        var catalog = app.Services.GetRequiredService<ITemplateCatalog>();
        await WithServiceAsync(app, async s => await s.SaveTemplateAsync(catalog.Get("software")));
        await WithServiceAsync(app, async s => { await s.SaveBasicsAsync(new BasicsForm { OwnerName = "Jordan", Email = "nope" }); return 0; });
        await WithServiceAsync(app, s => s.SaveChoicesAsync(new ReviewForm { SiteUrl = "https://jordan.example", Accent = "", Content = StarterChoice.Blank }));
        var before = WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app));

        var result = await WithServiceAsync(app, s => s.PublishAsync());

        Assert.Equal(PublishOutcome.Incomplete, result.Outcome);
        Assert.Equal(2, result.Step);
        Assert.Equal(before, WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app)));
        Assert.NotNull(await WizardFlow.DraftOrNullAsync(app)); // the draft is kept
    }
}
