using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Testimonials;

public class IndexModel : ContentListModel<Testimonial>
{
    public IndexModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Testimonials;

    protected override IQueryable<Testimonial> Ordered() => Db.Testimonials.OrderBy(t => t.SortOrder).ThenBy(t => t.Id);

    protected override ListRow ToRow(Testimonial t, bool canUp, bool canDown) =>
        new(t.Id, t.Author, t.AuthorTitle, Excerpt(t.Quote), canUp, canDown);

    protected override string Describe(Testimonial t) => t.Author;

    internal static string Excerpt(string quote)
    {
        var flat = string.Join(' ', quote.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= 80 ? flat : flat[..80].TrimEnd() + "…";
    }
}
