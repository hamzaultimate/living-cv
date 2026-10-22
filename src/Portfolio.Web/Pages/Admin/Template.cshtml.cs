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
        PreviewPages = [("/", "Home"), .. view.NavLinks(mobile: true).Select(l => (l.Path, Cap(l.Label)))];
        PreviewPath = PreviewPages.Select(p => p.Path).FirstOrDefault(p => p == Tab) ?? "/";
        HiddenContent = await HiddenContentAsync(view, ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!_catalog.TryGet(Key, out var template))
        {
            FlashError("That template is not available.");
            return RedirectToPage();
        }

        await _editor.SetTemplateAsync(template, ct);
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
