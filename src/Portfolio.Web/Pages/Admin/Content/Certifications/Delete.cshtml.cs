using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Certifications;

public class DeleteModel : ContentDeleteModel<Certification>
{
    public DeleteModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Certifications;
    protected override DbSet<Certification> Set => Db.Certifications;

    protected override DeleteInfo Describe(Certification c) =>
        new(c.Name, [$"{View.Label("issuer")}: {c.Issuer}", $"{View.Label("status")}: {View.StatusLabel(c.Status)}"]);
}
