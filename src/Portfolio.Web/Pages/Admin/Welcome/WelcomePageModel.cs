using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Welcome;

/// <summary>
/// Shared by the four wizard steps: the saved draft, the template it names, the step list for the progress indicator, and the two
/// actions every step offers (leave without publishing, start over). Every handler here is a POST; a GET only ever reads.
/// </summary>
public abstract class WelcomePageModel : AdminPageModel
{
    public static readonly IReadOnlyList<(int Number, string Label, string Path)> Steps =
    [
        (1, "Template", "/admin/welcome"),
        (2, "Basics", "/admin/welcome/basics"),
        (3, "Photo", "/admin/welcome/photo"),
        (4, "Review and launch", "/admin/welcome/review"),
    ];

    protected WelcomePageModel(OnboardingService onboarding, ITemplateCatalog catalog)
    {
        Onboarding = onboarding;
        Catalog = catalog;
    }

    protected OnboardingService Onboarding { get; }
    protected ITemplateCatalog Catalog { get; }

    public abstract int CurrentStep { get; }

    public OnboardingState State { get; private set; } = null!;

    /// <summary>The template the draft names (steps 2 to 4), or the saved site's template before one is chosen.</summary>
    public TemplateDefinition Template { get; private set; } = null!;

    public bool FirstRun => State.FirstRun;
    public OnboardingDraft? Draft => State.Draft;
    public string StepLabel => Steps[CurrentStep - 1].Label;

    /// <summary>The furthest step the owner may open: the next unsaved step (the photo is optional, so once the basics are saved the review is open too).</summary>
    public int Reachable => Draft is null ? 1 : Draft.Step >= 2 ? OnboardingService.LastStep : Draft.Step + 1;

    protected async Task LoadAsync(CancellationToken ct)
    {
        State = await Onboarding.GetStateAsync(ct);
        var key = Draft?.TemplateKey ?? (FirstRun ? TemplateCatalog.DefaultKey : State.Profile.TemplateKey);
        Template = Catalog.Get(key);
    }

    /// <summary>Sends the owner back to an earlier step whose answers are missing, so a step is never shown without what it builds on.</summary>
    protected IActionResult? RequireStep(int saved)
    {
        if (Draft is null || !Catalog.TryGet(Draft.TemplateKey, out _) || Draft.Step < saved)
        {
            FlashError(saved <= 1 ? "Choose a template first." : "Fill in your basics first.");
            return RedirectToPage(saved <= 1 ? "/Admin/Welcome/Template" : "/Admin/Welcome/Basics");
        }
        return null;
    }

    /// <summary>Leaves the wizard without publishing (see <see cref="OnboardingService.SkipAsync"/>).</summary>
    public async Task<IActionResult> OnPostSkipAsync(CancellationToken ct)
    {
        var first = (await Onboarding.GetStateAsync(ct)).FirstRun;
        await Onboarding.SkipAsync(ct);
        Flash(first
            ? "Setup skipped. The site keeps its placeholder profile and demo content; run the wizard again from the dashboard whenever you like. You can add your own content under Content at any time."
            : "Nothing was published. Your draft was discarded and the site is unchanged.");
        return Redirect("/admin");
    }

    /// <summary>Throws the draft away and starts again at step 1. The live site and its content are untouched.</summary>
    public async Task<IActionResult> OnPostDiscardAsync(CancellationToken ct)
    {
        await Onboarding.DiscardAsync(ct);
        Flash("Draft discarded. Nothing on the live site changed.");
        return RedirectToPage("/Admin/Welcome/Template");
    }
}
