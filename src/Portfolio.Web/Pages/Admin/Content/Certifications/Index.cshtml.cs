using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Certifications;

public class IndexModel : ContentListModel<Certification>
{
    public IndexModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Certifications;

    protected override IQueryable<Certification> Ordered() => Db.Certifications.OrderBy(c => c.SortOrder).ThenBy(c => c.Id);

    protected override ListRow ToRow(Certification c, bool canUp, bool canDown) =>
        new(c.Id, c.Name, c.Issuer, string.Join(" · ", new[] { View.StatusLabel(c.Status), c.Date }.Where(x => !string.IsNullOrWhiteSpace(x))), canUp, canDown);

    protected override string Describe(Certification c) => $"{c.Name} ({c.Issuer})";
}
