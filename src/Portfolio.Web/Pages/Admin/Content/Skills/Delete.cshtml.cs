using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Skills;

public class DeleteModel : ContentDeleteModel<Skill>
{
    public DeleteModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Skills;
    protected override DbSet<Skill> Set => Db.Skills;

    protected override DeleteInfo Describe(Skill s) => new(s.Name, [$"It is listed in the group {s.Group}."]);
}
