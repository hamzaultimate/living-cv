using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content;

/// <summary>The content hub: one card per editor, in the template's wording, with how many entries each holds.</summary>
public class IndexModel : AdminPageModel
{
    public sealed record Card(string Section, string Label, string ItemLabel, int Count, bool Enabled);

    private readonly PortfolioDbContext _db;
    private readonly RequestTemplate _template;

    public IndexModel(PortfolioDbContext db, RequestTemplate template)
    {
        _db = db;
        _template = template;
    }

    public IReadOnlyList<Card> Cards { get; private set; } = [];
    public string? Guidance { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var template = await _template.GetAsync();
        var counts = new Dictionary<string, int>
        {
            [SectionRegistry.Experience] = await _db.Experiences.CountAsync(ct),
            [SectionRegistry.Skills] = await _db.Skills.CountAsync(ct),
            [SectionRegistry.Projects] = await _db.Projects.CountAsync(ct),
            [SectionRegistry.Certifications] = await _db.Certifications.CountAsync(ct),
        };
        Cards = EditorFields.Sections.Select(s =>
        {
            var view = new EditorView(template, s);
            return new Card(s, view.SectionLabel, view.ItemLabel, counts[s], view.SectionEnabled);
        }).ToList();
        Guidance = new EditorView(template, SectionRegistry.Experience).Guidance;
    }
}
