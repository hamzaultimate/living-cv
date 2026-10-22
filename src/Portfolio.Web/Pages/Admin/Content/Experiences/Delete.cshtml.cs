using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Experiences;

public class DeleteModel : ContentDeleteModel<Experience>
{
    public DeleteModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Experience;
    protected override DbSet<Experience> Set => Db.Experiences;

    protected override DeleteInfo Describe(Experience e) =>
        new($"{e.Title} at {e.Company}", [$"{e.Start} – {(e.Current ? "present" : e.End)}", $"{e.Highlights.Count} highlight(s) and {e.Tech.Count} tool(s) listed with it."]);
}
