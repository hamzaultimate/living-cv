using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Talks;

public class DeleteModel : ContentDeleteModel<Talk>
{
    public DeleteModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Talks;
    protected override DbSet<Talk> Set => Db.Talks;

    protected override DeleteInfo Describe(Talk t) =>
        new(t.Title, new[]
        {
            View.Visible("event") && !string.IsNullOrWhiteSpace(t.Event) ? $"{View.Label("event")}: {t.Event}" : null,
            View.Visible("date") && t.Date is { } d ? $"{View.Label("date")}: {d:yyyy-MM-dd}" : null,
            View.Visible("youtubeId") && !string.IsNullOrWhiteSpace(t.YoutubeId) ? $"{View.Label("youtubeId")}: {t.YoutubeId}" : null,
        }.Where(x => x is not null).Select(x => x!).ToList());
}
