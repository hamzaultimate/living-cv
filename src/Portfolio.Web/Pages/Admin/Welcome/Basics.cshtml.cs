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

    [BindProperty]
    public BasicsForm Form { get; set; } = new();

    public TemplateHeroSuggestions Suggestions => Template.HeroSuggestions;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (RequireStep(1) is { } back) return back;

        // What the owner typed wins; then, for a site that is already set up, what it says now; the template's suggestion fills what is still empty.
        var profile = State.Profile;
        var existing = !FirstRun;
        Form = new BasicsForm
        {
            OwnerName = Draft!.OwnerName ?? (existing ? profile.OwnerName : null),
            JobTitle = Draft.JobTitle ?? (existing && !string.IsNullOrWhiteSpace(profile.JobTitle) ? profile.JobTitle : Suggestions.Title),
            Email = Draft.Email ?? (existing ? profile.Email : null),
            Location = Draft.Location ?? (existing ? profile.Location : null),
            Summary = Draft.Summary ?? (existing && !string.IsNullOrWhiteSpace(profile.Summary) ? profile.Summary : Suggestions.Summary),
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (RequireStep(1) is { } back) return back;
        if (!ModelState.IsValid) return Page();

        await Onboarding.SaveBasicsAsync(Form, ct);
        return RedirectToPage("/Admin/Welcome/Photo");
    }
}
