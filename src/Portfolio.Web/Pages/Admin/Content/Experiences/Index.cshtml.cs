using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Experiences;

public class IndexModel : ContentListModel<Experience>
{
    public IndexModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Experience;

    protected override IQueryable<Experience> Ordered() => Db.Experiences.OrderBy(e => e.SortOrder).ThenBy(e => e.Id);

    protected override ListRow ToRow(Experience e, bool canUp, bool canDown) =>
        new(e.Id, e.Title, e.Company, $"{e.Start} – {(e.Current ? "present" : e.End)}", canUp, canDown);

    protected override string Describe(Experience e) => $"{e.Title} at {e.Company}";
}
