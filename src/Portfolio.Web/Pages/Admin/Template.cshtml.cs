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

    /// <summary>The template to preview. Untrusted: it only ever selects a catalog entry (see <see cref="ITemplateCatalog.TryGet"/>).</summary>
    [BindProperty(SupportsGet = true, Name = "show")]
    public string? Show { get; set; }

    /// <summary>Which page the preview opens on. Untrusted: it only ever selects one of <see cref="PreviewPages"/>.</summary>
    [BindProperty(SupportsGet = true, Name = "tab")]
    public string? Tab { get; set; }

    [BindProperty(SupportsGet = true, Name = "size")]
    public string? Size { get; set; }

    /// <summary>The template chosen on the form being posted.</summary>
    [BindProperty]
    public string? Key { get; set; }

    public IReadOnlyCollection<TemplateDefinition> Templates => _catalog.All;

    public string CurrentKey { get; private set; } = TemplateCatalog.DefaultKey;
    public TemplateDefinition Selected { get; private set; } = null!;
    public IReadOnlyList<(string Path, string Label)> PreviewPages { get; private set; } = [];
    public string PreviewPath { get; private set; } = "/";
    public bool Mobile => string.Equals(Size, "mobile", StringComparison.Ordinal);

    /// <summary>Sections of the selected template that stay hidden although the site has content for them, with how much content.</summary>
    public IReadOnlyList<(string Label, int Count)> HiddenContent { get; private set; } = [];

    public string PreviewUrl => $"{PreviewPath}?{RequestTemplate.PreviewParameter}={Uri.EscapeDataString(Selected.Key)}";

    public async Task OnGetAsync(CancellationToken ct)
    {
        var profile = await _editor.LoadAsync(ct);
        CurrentKey = _catalog.Get(profile.TemplateKey).Key;
        Selected = _catalog.TryGet(Show, out var chosen) ? chosen : _catalog.Get(CurrentKey);

        var view = _resolver.Resolve(SiteProfileMapper.ToOptions(profile), Selected.Key);
        PreviewPages = [("/", "Home"), .. view.NavLinks(mobile: true).Select(l => (l.Path, l.Path == "/cv" ? "CV" : Cap(l.Label)))];
        PreviewPath = PreviewPages.Select(p => p.Path).FirstOrDefault(p => p == Tab) ?? "/";
        HiddenContent = await HiddenContentAsync(view, ct);
        LeavesNoMenuSection = !HasMenuSection(view);
        if (Selected.Key != CurrentKey)
        {
            // Sections the switch would bring back: on under the selected template, off under the current one (their content was kept all along).
            var now = _resolver.Resolve(SiteProfileMapper.ToOptions(profile), CurrentKey);
            Reappearing = view.Sections.Where(s => s.Enabled && !now.IsEnabled(s.Key)).Select(s => Cap(s.Label)).ToList();
        }
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

    private static bool HasMenuSection(TemplateView view) => SectionSettings.HasMenuSection(view);

    /// <summary>Sections that are off now and would be on after switching to the selected template.</summary>
    public IReadOnlyList<string> Reappearing { get; private set; } = [];

    /// <summary>True when switching to the selected template would leave the site without a section in the menu (the switch is then refused).</summary>
    public bool LeavesNoMenuSection { get; private set; }

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
