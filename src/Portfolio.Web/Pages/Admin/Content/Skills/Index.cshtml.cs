using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Skills;

public class IndexModel : ContentListModel<Skill>
{
    public IndexModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Skills;

    protected override IQueryable<Skill> Ordered() => Db.Skills.OrderBy(s => s.Group).ThenBy(s => s.SortOrder).ThenBy(s => s.Id);

    // Skills are ordered within their group, as on the public page and the CV.
    protected override string? GroupOf(Skill s) => s.Group;

    protected override ListRow ToRow(Skill s, bool canUp, bool canDown) => new(s.Id, s.Name, null, null, canUp, canDown);

    protected override string Describe(Skill s) => $"{s.Name} in {s.Group}";

    // Groups appear in the template's order, like the public Skills page and the CV; groups it does not list follow alphabetically.
    protected override IReadOnlyList<ListGroup> OrderGroups(List<ListGroup> groups)
    {
        var order = View.SkillGroups;
        int Rank(string? g) { var i = order.ToList().FindIndex(x => string.Equals(x, g, StringComparison.Ordinal)); return i < 0 ? int.MaxValue : i; }
        return groups.OrderBy(g => Rank(g.Heading)).ThenBy(g => g.Heading, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
