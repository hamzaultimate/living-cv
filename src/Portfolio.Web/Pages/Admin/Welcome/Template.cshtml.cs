using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Welcome;

// A template choice is one short key; anything much larger is not from this page.
[BodyLimit(16 * 1024)]
public class WelcomeTemplateModel : WelcomePageModel
{
    private readonly TemplateResolver _resolver;

    public WelcomeTemplateModel(OnboardingService onboarding, ITemplateCatalog catalog, TemplateResolver resolver) : base(onboarding, catalog)
    {
        _resolver = resolver;
    }

    public override int CurrentStep => 1;

    /// <summary>The template chosen on the form being posted. Untrusted: it only ever selects a catalog entry (<see cref="ITemplateCatalog.TryGet"/>).</summary>
    [BindProperty]
    public string? Key { get; set; }

    /// <summary>The template to preview. Untrusted: it only ever selects a catalog entry (see <see cref="TemplatePickerBuilder.Select"/>).</summary>
    [BindProperty(SupportsGet = true, Name = "show")]
    public string? Show { get; set; }

    /// <summary>Which page the preview opens on. Untrusted: it only ever selects one of the preview pages.</summary>
    [BindProperty(SupportsGet = true, Name = "tab")]
    public string? Tab { get; set; }

    /// <summary>"mobile" or anything else (desktop).</summary>
    [BindProperty(SupportsGet = true, Name = "size")]
    public string? Size { get; set; }

    /// <summary>What the shared picker partial renders, with this step's Use or Keep action added.</summary>
    public TemplatePickerModel Picker { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        BuildPicker(Show);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (!Catalog.TryGet(Key, out var chosen))
        {
            ModelState.AddModelError(nameof(Key), "Choose one of the templates.");
            BuildPicker(null);
            return Page();
        }

        if (await Onboarding.SaveTemplateAsync(chosen, ct) is { } refused)
        {
            ModelState.AddModelError(nameof(Key), refused);
            BuildPicker(chosen.Key);
            return Page();
        }
        return RedirectToPage("/Admin/Welcome/Basics");
    }

    /// <summary>Shows <paramref name="show"/> when it names a template, else the template the draft (or, before one exists, the site) is on.</summary>
    private void BuildPicker(string? show)
    {
        var chosenKey = Catalog.TryGet(Draft?.TemplateKey, out var inDraft) ? inDraft.Key : null;
        var selected = TemplatePickerBuilder.Select(Catalog, show, Template.Key);
        var view = _resolver.Resolve(SiteProfileMapper.ToOptions(State.Profile), selected.Key);
        var label = selected.Key == chosenKey ? $"Keep {selected.Name} and continue" : $"Use {selected.Name} and continue";

        var picker = TemplatePickerBuilder.Build(Catalog, new TemplatePickerInput("/admin/welcome", selected, TemplatePickerBuilder.PagesOf(view), Tab, Size, chosenKey, "Selected"));
        Picker = picker with { Action = new PickerAction("/Admin/Welcome/Template", selected.Key, label) };
    }
}
