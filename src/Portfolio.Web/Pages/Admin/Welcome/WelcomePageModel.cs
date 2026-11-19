using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Welcome;

/// <summary>
/// Shared by the four wizard steps: the saved draft, the template the wizard shows, the status of each step for the indicator, where a save goes next
/// (<see cref="Leave"/>), and the two actions every step offers (leave without publishing, start over). Every handler here is a POST; a GET only ever reads.
/// </summary>
public abstract class WelcomePageModel : AdminPageModel
{
    protected WelcomePageModel(OnboardingService onboarding, ITemplateCatalog catalog)
    {
        Onboarding = onboarding;
        Catalog = catalog;
    }

    protected OnboardingService Onboarding { get; }
    protected ITemplateCatalog Catalog { get; }

    public abstract int CurrentStep { get; }

    /// <summary>The step the owner asked to go to: an indicator button, or the card's Continue. Untrusted: it is only ever clamped to one of the four steps (<see cref="WizardProgress.ClampStep"/>).</summary>
    [BindProperty(Name = "goto")]
    public string? Goto { get; set; }

    /// <summary>The label of the card's Continue button; null when the step has its own (the template step's Use button, the review step's Publish).</summary>
    public virtual string? ContinueLabel => null;

    public OnboardingState State { get; private set; } = null!;

    /// <summary>The template the wizard shows: the draft's, else the saved site's, else the default on a first run (<see cref="OnboardingService.TemplateFor"/>).</summary>
    public TemplateDefinition Template { get; private set; } = null!;

    /// <summary>Each step's status, computed from the draft alone.</summary>
    public IReadOnlyList<StepStatus> Progress { get; private set; } = [];

    public bool FirstRun => State.FirstRun;
    public OnboardingDraft? Draft => State.Draft;
    public string StepLabel => WizardProgress.Steps[CurrentStep - 1].Label;

    /// <summary>The step after this one; the last step is its own next.</summary>
    public int NextStep => Math.Min(CurrentStep + 1, OnboardingService.LastStep);

    protected async Task LoadAsync(CancellationToken ct)
    {
        State = await Onboarding.GetStateAsync(ct);
        Template = Onboarding.TemplateFor(State);
        Progress = Onboarding.ProgressOf(State.Draft);
    }

    /// <summary>Where a save goes next: the step <see cref="Goto"/> asked for, else the step after this one. A local path from a fixed list, never the posted value.</summary>
    protected IActionResult Leave() => Redirect(WizardProgress.Steps[WizardProgress.ClampStep(Goto, NextStep) - 1].Path);

    /// <summary>Leaves the wizard without publishing (see <see cref="OnboardingService.SkipAsync"/>).</summary>
    public async Task<IActionResult> OnPostSkipAsync(CancellationToken ct)
    {
        switch (await Onboarding.SkipAsync(ct))
        {
            case SkipOutcome.Skipped:
                Flash("Setup skipped. The site keeps its placeholder profile and demo content; run the wizard again from the dashboard whenever you like. You can add your own content under Content at any time.");
                break;
            case SkipOutcome.DraftDiscarded:
                Flash("Nothing was published. Your draft was discarded and the site is unchanged.");
                break;
            case SkipOutcome.AlreadyFinished:
                Flash("Setup was already finished in another tab, so nothing changed.");
                break;
            default:
                FlashError("Your profile was changed while setup was being skipped, so nothing was recorded. Setup is still open: choose Skip setup again, or continue it.");
                break;
        }
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
