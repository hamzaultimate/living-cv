using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Pages.Admin.Content.Blog;

public class DeleteModel : ContentDeleteModel<BlogPost>
{
    private readonly UploadStore _uploads;

    public DeleteModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template, UploadStore uploads) : base(db, content, template) => _uploads = uploads;

    public override string Section => SectionRegistry.Blog;
    protected override DbSet<BlogPost> Set => Db.BlogPosts;

    protected override DeleteInfo Describe(BlogPost b)
    {
        var lines = new List<string>
        {
            b.Draft ? "It is a draft, so it is not shown on your site now." : $"Its page, /blog/{b.Slug}, stops working and answers \"not found\"; it leaves the list and the sitemap.",
            $"{View.Label("date")}: {b.Date:yyyy-MM-dd}",
        };
        if (!string.IsNullOrWhiteSpace(b.Cover))
            lines.Add(_uploads.TryResolve(b.Cover, out _) ? "The cover image you uploaded for it will be deleted from storage unless something else uses it." : "Its cover image comes with the site and is not touched.");
        return new DeleteInfo(b.Title, lines);
    }

    protected override Task CleanUpAsync(BlogPost removed, CancellationToken ct) => Writer.DeleteUploadsAsync([removed.Cover], null);
}
