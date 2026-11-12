using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Blog;

public class IndexModel : ContentListModel<BlogPost>
{
    public IndexModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Blog;

    public int Published { get; private set; }
    public int Drafts { get; private set; }

    // The public page orders by date, newest first (drafts are not on it); the admin list shows every post in that order.
    protected override IQueryable<BlogPost> Ordered() => Db.BlogPosts.OrderByDescending(b => b.Date).ThenBy(b => b.Id);

    protected override bool Reorderable => false;

    protected override ListRow ToRow(BlogPost b, bool canUp, bool canDown) =>
        new(b.Id, b.Title, $"/blog/{b.Slug}", $"{(b.Draft ? "Draft" : "Published")} · {b.Date:yyyy-MM-dd}", canUp, canDown);

    protected override string Describe(BlogPost b) => b.Title;

    public override async Task OnGetAsync(CancellationToken ct)
    {
        await base.OnGetAsync(ct);
        Drafts = await Db.BlogPosts.CountAsync(b => b.Draft, ct);
        Published = Count - Drafts;
    }
}
