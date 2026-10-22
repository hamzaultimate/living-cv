using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Skills;

public class SkillForm
{
    public string? Name { get; set; }
    public string? Group { get; set; }
    public string? Stamp { get; set; }
}

[BodyLimit(64 * 1024)]
public class EditModel : ContentPageModel
{
    public EditModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Skills;

    public int? Id { get; private set; }
    public bool IsNew => Id is null;

    /// <summary>Groups offered as suggestions: the template's own, then any already in use.</summary>
    public IReadOnlyList<string> GroupOptions { get; private set; } = [];

    [BindProperty]
    public SkillForm Form { get; set; } = new();

    /// <summary>Set by the "Save and add another" button.</summary>
    [BindProperty]
    public bool AddAnother { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id, string? group, CancellationToken ct)
    {
        await LoadViewAsync();
        await LoadGroupsAsync(ct);
        if (id is null)
        {
            // A new skill can start in the group of the one just added (the "add another" flow).
            Form.Group = ContentRules.Trim(group) is { Length: <= 60 } g && !ContentRules.HasMarkup(g) ? g : null;
            return Page();
        }

        var s = await Db.Skills.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return ListWithError("That skill no longer exists.");
        Id = s.Id;
        Form = new SkillForm { Name = s.Name, Group = s.Group, Stamp = s.ConcurrencyStamp };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, CancellationToken ct)
    {
        await LoadViewAsync();
        Id = id;
        var c = new FormCheck(ModelState, View);
        var name = c.Text(nameof(Form.Name), "name", Form.Name, 100, required: true);
        var group = c.Text(nameof(Form.Group), "group", Form.Group, 60, required: true);

        // The seeder identifies a skill by its name, so two skills with one name would break Seed:Mode=Authoritative.
        if (name is not null && c.IsValid(nameof(Form.Name))
            && await Db.Skills.AnyAsync(x => x.Id != id && x.Name.ToLower() == name.ToLower(), ct))
            c.Error(nameof(Form.Name), $"You already have a skill called \"{name}\". Edit that one instead.");

        if (!ModelState.IsValid)
        {
            await LoadGroupsAsync(ct);
            return Page();
        }

        Skill row;
        if (id is null)
        {
            var next = ContentEditor.NextOrder(await Db.Skills.Where(x => x.Group == group).Select(x => x.SortOrder).ToListAsync(ct));
            row = new Skill { Name = name!, Group = group!, SortOrder = next, ConcurrencyStamp = ContentEditor.NewStamp() };
            Db.Skills.Add(row);
        }
        else
        {
            var found = await Db.Skills.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (found is null) return ListWithError("That skill was removed in another tab, so your changes were not saved.");
            row = found;
            Writer.Stamp(row, Form.Stamp);
            if (row.Group != group)
            {
                // Moving to another group puts it at the end of that group.
                row.SortOrder = ContentEditor.NextOrder(await Db.Skills.Where(x => x.Group == group).Select(x => x.SortOrder).ToListAsync(ct));
            }
            row.Name = name!;
            row.Group = group!;
        }

        var outcome = await Writer.CommitAsync(ct);
        if (outcome != SaveOutcome.Saved)
        {
            await LoadGroupsAsync(ct);
            AddConflict();
            return Page();
        }

        if (AddAnother && id is null)
        {
            Flash($"Added {name}. Add the next one below.");
            return Redirect($"{ListUrl}/edit?group={Uri.EscapeDataString(group!)}");
        }
        Flash($"Saved {name}.");
        return Redirect($"{ListUrl}#row-{row.Id}");
    }

    private async Task LoadGroupsAsync(CancellationToken ct)
    {
        var used = await Db.Skills.Select(s => s.Group).Distinct().ToListAsync(ct);
        GroupOptions = View.SkillGroups.Concat(used.OrderBy(g => g, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
