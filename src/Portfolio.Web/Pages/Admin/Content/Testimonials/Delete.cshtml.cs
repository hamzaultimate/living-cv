using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Testimonials;

public class DeleteModel : ContentDeleteModel<Testimonial>
{
    public DeleteModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Testimonials;
    protected override DbSet<Testimonial> Set => Db.Testimonials;

    protected override DeleteInfo Describe(Testimonial t) =>
        new(t.Author, [$"{View.Label("quote")}: {IndexModel.Excerpt(t.Quote)}"]);
}
