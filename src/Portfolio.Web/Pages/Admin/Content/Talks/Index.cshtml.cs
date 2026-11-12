using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Talks;

public class IndexModel : ContentListModel<Talk>
{
    public IndexModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Talks;

    // The public page orders by date, newest first, so the admin list does too (ties by position, then id, as the public query does).
    protected override IQueryable<Talk> Ordered() => Db.Talks.OrderByDescending(t => t.Date).ThenBy(t => t.SortOrder).ThenBy(t => t.Id);

    protected override bool Reorderable => false;

    protected override ListRow ToRow(Talk t, bool canUp, bool canDown) =>
        new(t.Id, t.Title, t.Event, t.Date?.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture), canUp, canDown);

    protected override string Describe(Talk t) => t.Title;
}
