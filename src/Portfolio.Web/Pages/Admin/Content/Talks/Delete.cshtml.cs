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
            string.IsNullOrWhiteSpace(t.Event) ? null : $"{View.Label("event")}: {t.Event}",
            t.Date is { } d ? $"{View.Label("date")}: {d:yyyy-MM-dd}" : null,
            string.IsNullOrWhiteSpace(t.YoutubeId) ? null : $"{View.Label("youtubeId")}: {t.YoutubeId}",
        }.Where(x => x is not null).Select(x => x!).ToList());
}
