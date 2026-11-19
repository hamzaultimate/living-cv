using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin;

// A template choice is one short key; anything much larger is not from this page.
[BodyLimit(16 * 1024)]
public class TemplateModel : AdminPageModel
{
    private readonly ProfileEditor _editor;
    private readonly ITemplateCatalog _catalog;
    private readonly TemplateResolver _resolver;
    private readonly PortfolioDbContext _db;

    public TemplateModel(ProfileEditor editor, ITemplateCatalog catalog, TemplateResolver resolver, PortfolioDbContext db)
    {
        _editor = editor;
        _catalog = catalog;
        _resolver = resolver;
        _db = db;
    }

    /// <summary>The template to preview. Untrusted: it only ever selects a catalog entry (see <see cref="TemplatePickerBuilder.Select"/>).</summary>
    [BindProperty(SupportsGet = true, Name = "show")]
    public string? Show { get; set; }

    /// <summary>Which page the preview opens on. Untrusted: it only ever selects one of the preview pages.</summary>
    [BindProperty(SupportsGet = true, Name = "tab")]
    public string? Tab { get; set; }

    /// <summary>"mobile" or anything else (desktop).</summary>
    [BindProperty(SupportsGet = true, Name = "size")]
    public string? Size { get; set; }

    /// <summary>The template chosen on the form being posted.</summary>
    [BindProperty]
    public string? Key { get; set; }

    /// <summary>What the shared picker partial renders, with this page's Switch action and notes added.</summary>
    public TemplatePickerModel Picker { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct)
    {
        var profile = await _editor.LoadAsync(ct);
        var currentKey = _catalog.Get(profile.TemplateKey).Key;
        var selected = TemplatePickerBuilder.Select(_catalog, Show, currentKey);
        var isCurrent = selected.Key == currentKey;

        var view = _resolver.Resolve(SiteProfileMapper.ToOptions(profile), selected.Key);
        IReadOnlyList<string> keeps = [];
        IReadOnlyList<string> reappearing = [];
        var refused = false;
        if (!isCurrent)
        {
            // What a switch would do, decided exactly as the POST decides it but on a detached copy (nothing is written): sections kept on, or a refusal.
            var plan = SectionSettings.PlanSwitch(_resolver, SiteProfileMapper.ToEntity(SiteProfileMapper.ToOptions(profile)), selected);
            keeps = plan.Kept;
            refused = !plan.Allowed;
            view = plan.Target; // the preview shows the menu the switch would leave
            // Sections the switch would bring back: on under the selected template, off under the current one (their content was kept all along).
            var now = _resolver.Resolve(SiteProfileMapper.ToOptions(profile), currentKey);
            reappearing = view.Sections.Where(s => s.Enabled && !now.IsEnabled(s.Key)).Select(s => Cap(s.Label)).ToList();
        }
        var hidden = await HiddenContentAsync(view, ct);

        var notes = new List<PickerNote>();
        var label = $"Switch to {selected.Name}";
        if (!isCurrent)
            notes.Add(new PickerNote("Preview only.", $"Nothing is saved or visible to visitors until you choose \"{label}\"."));
        if (hidden.Count > 0)
            notes.Add(new PickerNote("Hidden but kept:", $"{string.Join(", ", hidden.Select(h => $"{h.Label} ({h.Count})"))}. These stay in the database and reappear if you switch to a template that uses them."));
        if (reappearing.Count > 0)
            notes.Add(new PickerNote("Shown again:", $"{string.Join(", ", reappearing)}. They are on under this template, and their content was kept."));
        if (!isCurrent && refused)
            notes.Add(new PickerNote("Cannot be switched to yet:", "with your saved section choices this template would show no section in the menu, and none of the sections that are on now exist in it. Switch one of its sections on first.", "/admin/sections", "Open Sections"));
        else if (!isCurrent && keeps.Count > 0)
            notes.Add(new PickerNote("Kept on:", $"{string.Join(", ", keeps)}. This template shows them off by default, so they stay on if you switch (they are saved as your choice) and the menu is not left empty."));

        var picker = TemplatePickerBuilder.Build(_catalog, new TemplatePickerInput("/admin/template", selected, TemplatePickerBuilder.PagesOf(view), Tab, Size, currentKey, "In use"));
        Picker = picker with
        {
            Action = isCurrent ? null : new PickerAction("/Admin/Template", selected.Key, label, refused ? "Not available yet: this template would leave your menu without a section. Switch one of its sections on at Sections first." : null),
            Notes = notes,
        };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!_catalog.TryGet(Key, out var template))
        {
            FlashError("That template is not available.");
            return RedirectToPage();
        }

        // The sections page never leaves a site without a menu section; a switch must not either. The rule runs inside the write, on the row as it is then, so a sections save
        // that lands in the middle cannot defeat it. When the new template's defaults would empty the menu, the menu sections that are on now stay on.
        var kept = new List<string>();
        SectionSettings.SwitchPlan? plan = null;
        var outcome = await _editor.SetTemplateAsync(template, profile =>
        {
            plan = SectionSettings.PlanSwitch(_resolver, profile, template);
            kept = plan.Kept.ToList();
            return plan.Allowed;
        }, ct);
        if (outcome == SaveOutcome.Refused)
        {
            FlashError(SectionSettings.NoMenuMessage(template.Name, plan!.Target));
            return RedirectToPage();
        }
        if (outcome != SaveOutcome.Saved)
        {
            FlashError("Your profile was changed at the same moment, so the template was not switched. Please try again.");
            return RedirectToPage();
        }
        if (kept.Count > 0)
        {
            Flash($"Switched to the {template.Name} template. {string.Join(", ", kept)} stay on, although {template.Name} shows them off by default, so your menu is not left empty. Nothing was deleted.");
            return RedirectToPage();
        }
        Flash($"Switched to the {template.Name} template. Nothing was deleted: sections it does not use are hidden, and come back if you switch again.");
        return RedirectToPage();
    }

    private async Task<IReadOnlyList<(string Label, int Count)>> HiddenContentAsync(TemplateView view, CancellationToken ct)
    {
        var counts = new Dictionary<string, int>
        {
            [SectionRegistry.Experience] = await _db.Experiences.CountAsync(ct),
            [SectionRegistry.Projects] = await _db.Projects.CountAsync(ct),
            [SectionRegistry.Skills] = await _db.Skills.CountAsync(ct),
            [SectionRegistry.Certifications] = await _db.Certifications.CountAsync(ct),
            [SectionRegistry.Blog] = await _db.BlogPosts.CountAsync(ct),
            [SectionRegistry.Talks] = await _db.Talks.CountAsync(ct),
            [SectionRegistry.Testimonials] = await _db.Testimonials.CountAsync(ct),
        };
        return counts.Where(c => c.Value > 0 && !view.IsEnabled(c.Key))
            .Select(c => (Label: DisplayName(c.Key), Count: c.Value))
            .ToList();
    }

    private static string DisplayName(string key) => key switch
    {
        SectionRegistry.Experience => "Experience",
        SectionRegistry.Projects => "Projects",
        SectionRegistry.Skills => "Skills",
        SectionRegistry.Certifications => "Certifications",
        SectionRegistry.Blog => "Blog posts",
        SectionRegistry.Talks => "Talks",
        _ => "Testimonials",
    };

    private static string Cap(string value) => TemplateView.Cap(value);
}
