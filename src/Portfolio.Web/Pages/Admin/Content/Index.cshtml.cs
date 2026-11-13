using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content;

/// <summary>The content hub: one card per editor, in the template's wording, with how many entries each holds.</summary>
public class IndexModel : AdminPageModel
{
    public sealed record Card(string Section, string Label, string ItemLabel, int Count, bool Enabled, string? Detail = null);

    private readonly PortfolioDbContext _db;
    private readonly RequestTemplate _template;

    public IndexModel(PortfolioDbContext db, RequestTemplate template)
    {
        _db = db;
        _template = template;
    }

    public IReadOnlyList<Card> Cards { get; private set; } = [];
    public string? Guidance { get; private set; }

    /// <summary>The export and import page's name in the template's words, for the link under the cards.</summary>
    public string TransferHeading { get; private set; } = "Export and import";

    public async Task OnGetAsync(CancellationToken ct)
    {
        var template = await _template.GetAsync();
        var counts = new Dictionary<string, int>
        {
            [SectionRegistry.Experience] = await _db.Experiences.CountAsync(ct),
            [SectionRegistry.Skills] = await _db.Skills.CountAsync(ct),
            [SectionRegistry.Projects] = await _db.Projects.CountAsync(ct),
            [SectionRegistry.Certifications] = await _db.Certifications.CountAsync(ct),
            [SectionRegistry.Talks] = await _db.Talks.CountAsync(ct),
            [SectionRegistry.Testimonials] = await _db.Testimonials.CountAsync(ct),
            [SectionRegistry.Blog] = await _db.BlogPosts.CountAsync(ct),
        };
        var drafts = await _db.BlogPosts.CountAsync(b => b.Draft, ct);
        Cards = EditorFields.Sections.Select(s =>
        {
            var view = new EditorView(template, s);
            var detail = s == SectionRegistry.Blog && counts[s] > 0 ? $"{counts[s] - drafts} published, {drafts} {(drafts == 1 ? "draft" : "drafts")}" : null;
            return new Card(s, view.SectionLabel, view.ItemLabel, counts[s], view.SectionEnabled, detail);
        }).ToList();
        Guidance = new EditorView(template, SectionRegistry.Experience).Guidance;
        TransferHeading = new EditorView(template, ProfileParts.Transfer).Label("heading");
    }
}
