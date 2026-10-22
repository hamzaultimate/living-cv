using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Projects;

public class IndexModel : ContentListModel<Project>
{
    public IndexModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Projects;

    protected override IQueryable<Project> Ordered() => Db.Projects.OrderBy(p => p.SortOrder).ThenBy(p => p.Id);

    protected override ListRow ToRow(Project p, bool canUp, bool canDown)
    {
        var meta = new[] { p.Category, p.Featured ? "Shown on the home page" : null, string.IsNullOrWhiteSpace(p.HeroImage) ? null : "Has a cover image",
                           p.Screenshots.Count > 0 ? $"{p.Screenshots.Count} gallery image(s)" : null }.Where(x => !string.IsNullOrWhiteSpace(x));
        return new ListRow(p.Id, p.Title, $"/projects/{p.Slug}", string.Join(" · ", meta), canUp, canDown, View.Visible("screenshots") ? ("Images", $"{ListUrl}/images/{p.Id}") : null);
    }

    protected override string Describe(Project p) => p.Title;
}
