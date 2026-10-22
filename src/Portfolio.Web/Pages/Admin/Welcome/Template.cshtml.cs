using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Welcome;

/// <summary>What the template card shows: the manifest's facts, nothing hardcoded per profession.</summary>
public sealed record TemplateCard(TemplateDefinition Template, bool HasPreview, IReadOnlyList<string> Pages, string? ItemLabel, bool HasSample);

// A template choice is one short key; anything much larger is not from this page.
[BodyLimit(16 * 1024)]
public class WelcomeTemplateModel : WelcomePageModel
{
    public WelcomeTemplateModel(OnboardingService onboarding, ITemplateCatalog catalog) : base(onboarding, catalog)
    {
    }

    public override int CurrentStep => 1;

    /// <summary>The template chosen on the form being posted. Untrusted: it only ever selects a catalog entry (<see cref="ITemplateCatalog.TryGet"/>).</summary>
    [BindProperty]
    public string? Key { get; set; }

    public string SelectedKey { get; private set; } = TemplateCatalog.DefaultKey;

    /// <summary>The catalog's templates grouped by profession, each with what it includes.</summary>
    public IReadOnlyList<IGrouping<string, TemplateCard>> Groups { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        SelectedKey = Template.Key;
        Groups = Cards();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (!Catalog.TryGet(Key, out var chosen))
        {
            ModelState.AddModelError(nameof(Key), "Choose one of the templates.");
            SelectedKey = Template.Key;
            Groups = Cards();
            return Page();
        }

        await Onboarding.SaveTemplateAsync(chosen, ct);
        return RedirectToPage("/Admin/Welcome/Basics");
    }

    private IReadOnlyList<IGrouping<string, TemplateCard>> Cards() =>
        Catalog.All.Select(t => new TemplateCard(
                t,
                Catalog.GetPreviewImagePath(t.Key) is not null,
                t.Sections.Where(s => s.Enabled && s.Key != SectionRegistry.Contact).Select(s => s.Label).ToList(),
                t.Sections.FirstOrDefault(s => s.Key == SectionRegistry.Projects && s.Enabled)?.ItemLabel,
                StarterContent.Offers(t)))
            .GroupBy(c => c.Template.Profession, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
