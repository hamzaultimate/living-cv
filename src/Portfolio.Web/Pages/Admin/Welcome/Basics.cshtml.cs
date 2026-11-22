using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Welcome;

// A few short text fields; anything much larger is not from this page.
[BodyLimit(32 * 1024)]
public class WelcomeBasicsModel : WelcomePageModel
{
    public WelcomeBasicsModel(OnboardingService onboarding, ITemplateCatalog catalog) : base(onboarding, catalog)
    {
    }

    public override int CurrentStep => 2;

    public override string? ContinueLabel => "Continue";

    [BindProperty]
    public BasicsForm Form { get; set; } = new();

    public TemplateHeroSuggestions Suggestions => Template.HeroSuggestions;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);

        // What the owner typed wins; then, for a site that is already set up, what it says now; the template's suggestion fills what is still empty.
        // (A blank field is stored as null, the same as one never typed, so a field the owner cleared is filled again here. SaveTemplateAsync relies on that to swap suggestions.)
        var profile = State.Profile;
        var existing = !FirstRun;
        var draft = Draft ?? new OnboardingDraft();
        Form = new BasicsForm
        {
            OwnerName = draft.OwnerName ?? (existing ? profile.OwnerName : null),
            JobTitle = draft.JobTitle ?? (existing && !string.IsNullOrWhiteSpace(profile.JobTitle) ? profile.JobTitle : Suggestions.Title),
            Email = draft.Email ?? (existing ? profile.Email : null),
            Location = draft.Location ?? (existing ? profile.Location : null),
            Summary = draft.Summary ?? (existing && !string.IsNullOrWhiteSpace(profile.Summary) ? profile.Summary : Suggestions.Summary),
        };
        return Page();
    }

    /// <summary>Saves whatever was typed, complete or not, then goes where <see cref="WelcomePageModel.Goto"/> says (the photo step by default). Only a hard rule (length, plain text) keeps the owner here.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (!ModelState.IsValid) return Page();

        await Onboarding.SaveBasicsAsync(Form, ct);
        return Leave();
    }
}
