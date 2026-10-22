using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content;

public enum FieldKind { Text, Url, Textarea, Select, Checkbox }

/// <summary>Everything the shared field partial needs to draw one form control: wording from the template, the value to show and its errors.</summary>
public sealed class FieldVm
{
    /// <summary>False for a field the template hides: the partial draws nothing.</summary>
    public bool Visible { get; init; } = true;

    public required string Key { get; init; }
    public required string Property { get; init; }
    public required string Label { get; init; }
    public string? Hint { get; init; }
    public string? Value { get; init; }
    public FieldKind Kind { get; init; } = FieldKind.Text;
    public int MaxLength { get; init; } = 200;
    public bool Required { get; init; }
    public int Rows { get; init; } = 4;
    public bool IsChecked { get; init; }
    public string? ListId { get; init; }
    public string? Autocomplete { get; init; }
    public IReadOnlyList<(string Value, string Text)> Options { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];

    public string Name => "Form." + Property;
    public string Id => "Form_" + Property;
    public string DescribedBy => string.Join(' ', new[] { Hint is null ? null : Id + "-hint", Errors.Count > 0 ? Id + "-error" : null }.Where(x => x is not null));
}

/// <summary>One line of a list screen.</summary>
public sealed record ListRow(int Id, string Title, string? Detail, string? Meta, bool CanMoveUp, bool CanMoveDown, (string Text, string Url)? Extra = null);

/// <summary>One table on a list screen: the whole section, or one skill group.</summary>
public sealed record ListGroup(string? Heading, IReadOnlyList<ListRow> Rows);

/// <summary>What the confirmation page shows before an entry is removed.</summary>
public sealed record DeleteInfo(string Title, IReadOnlyList<string> Lines);

/// <summary>What the shared list partial reads.</summary>
public interface IContentList
{
    EditorView View { get; }
    string ListUrl { get; }
    int Count { get; }
    IReadOnlyList<ListGroup> Groups { get; }
}

/// <summary>What the shared confirmation partial reads.</summary>
public interface IContentDelete
{
    EditorView View { get; }
    string ListUrl { get; }
    int Id { get; }
    DeleteInfo Info { get; }
    string? Stamp { get; }
}

/// <summary>
/// Shared by every content editor page: the editor wording for the template in effect, the messages for a stale form, and the way a page
/// builds a field. Handlers that change data are POST only; a GET only reads.
/// </summary>
public abstract class ContentPageModel : AdminPageModel
{
    public const string ConflictMessage = "This entry was changed or removed somewhere else (another tab or session) since you opened it, so nothing was saved. Reload it to see the latest version.";

    protected ContentPageModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template)
    {
        Db = db;
        Writer = content;
        Template = template;
    }

    protected PortfolioDbContext Db { get; }
    protected ContentEditor Writer { get; }
    protected RequestTemplate Template { get; }

    /// <summary>The section key, which is also the last-but-one segment of every address of the editor (<c>/admin/content/{section}</c>).</summary>
    public abstract string Section { get; }

    public EditorView View { get; private set; } = null!;

    public string ListUrl => "/admin/content/" + Section;

    /// <summary>Content pages move keyboard focus to the message shown after a redirect (a save, a removal, a refusal), so it is announced.</summary>
    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context) => ViewData["FocusFlash"] = true;

    protected async Task LoadViewAsync() => View = new EditorView(await Template.GetAsync(), Section);

    /// <summary>The control for field <paramref name="key"/>; it draws nothing when the template hides the field (a hidden field is neither drawn nor read).</summary>
    public FieldVm Field(string key, string property, string? value, FieldKind kind = FieldKind.Text, int maxLength = 200, int rows = 4,
        bool isChecked = false, string? listId = null, string? autocomplete = null, IReadOnlyList<(string Value, string Text)>? options = null, bool? required = null)
    {
        if (!View.Visible(key)) return new FieldVm { Key = key, Property = property, Label = key, Visible = false };
        ModelState.TryGetValue("Form." + property, out var entry);
        return new FieldVm
        {
            Key = key, Property = property, Label = View.Label(key), Hint = View.Hint(key), Value = value, Kind = kind, MaxLength = maxLength,
            Required = required ?? View.Required(key), Rows = rows, IsChecked = isChecked, ListId = listId, Autocomplete = autocomplete, Options = options ?? [],
            Errors = entry?.Errors.Select(e => e.ErrorMessage).ToList() ?? [],
        };
    }

    protected IActionResult ListWithError(string message)
    {
        FlashError(message);
        return Redirect(ListUrl);
    }

    /// <summary>The page the owner sees again after a refused save: the same form with their input and a message that says nothing was written.</summary>
    protected void AddConflict()
    {
        ModelState.AddModelError(string.Empty, ConflictMessage);
        ViewData["Conflict"] = true;
    }
}

/// <summary>A list screen: the entries of one section in their public order, with Move up and Move down buttons.</summary>
// The only post is a button press; anything much larger is not from this page.
[BodyLimit(16 * 1024)]
public abstract class ContentListModel<T> : ContentPageModel, IContentList where T : class, IOrderedContent
{
    protected ContentListModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    protected abstract IQueryable<T> Ordered();
    protected abstract ListRow ToRow(T row, bool canUp, bool canDown);
    protected virtual string? GroupOf(T row) => null;
    protected abstract string Describe(T row);

    public int Count { get; private set; }
    public IReadOnlyList<ListGroup> Groups { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadViewAsync();
        await BuildAsync(ct);
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, string? direction, CancellationToken ct)
    {
        await LoadViewAsync();
        var delta = direction switch { "up" => -1, "down" => 1, _ => 0 };
        if (delta == 0) return ListWithError("Choose up or down.");

        var rows = await Ordered().ToListAsync(ct);
        var row = rows.FirstOrDefault(r => r.Id == id);
        if (row is null) return ListWithError($"That {View.ItemLabelLower} no longer exists.");

        var scope = rows.Where(r => GroupOf(r) == GroupOf(row)).ToList();
        var outcome = await Writer.MoveAsync(scope, id, delta, ct);
        if (outcome != SaveOutcome.Saved) return ListWithError("The list was changed somewhere else, so it was not reordered. It is shown again as it is now.");

        Flash($"Moved {Describe(row)} {(delta < 0 ? "up" : "down")}.");
        return Redirect($"{ListUrl}#row-{id}");
    }

    private async Task BuildAsync(CancellationToken ct)
    {
        var rows = await Ordered().AsNoTracking().ToListAsync(ct);
        Count = rows.Count;
        var groups = new List<ListGroup>();
        foreach (var set in rows.GroupBy(GroupOf))
        {
            var list = set.OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToList();
            groups.Add(new ListGroup(set.Key, list.Select((r, i) => ToRow(r, i > 0, i < list.Count - 1)).ToList()));
        }
        Groups = OrderGroups(groups);
    }

    protected virtual IReadOnlyList<ListGroup> OrderGroups(List<ListGroup> groups) => groups;
}

/// <summary>A confirmation page: GET shows what will be removed, POST removes it.</summary>
[BodyLimit(16 * 1024)]
public abstract class ContentDeleteModel<T> : ContentPageModel, IContentDelete where T : class, IOrderedContent
{
    protected ContentDeleteModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    protected abstract DbSet<T> Set { get; }
    protected abstract DeleteInfo Describe(T row);

    /// <summary>Called after the row is gone, to remove files that belonged only to it.</summary>
    protected virtual Task CleanUpAsync(T removed, CancellationToken ct) => Task.CompletedTask;

    public DeleteInfo Info { get; private set; } = null!;
    public int Id { get; private set; }

    [BindProperty]
    public string? Stamp { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        await LoadViewAsync();
        var row = await Set.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return ListWithError($"That {View.ItemLabelLower} no longer exists.");
        Id = id;
        Stamp = row.ConcurrencyStamp;
        Info = Describe(row);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct)
    {
        await LoadViewAsync();
        var row = await Set.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return ListWithError($"That {View.ItemLabelLower} was already removed.");

        var info = Describe(row);
        Writer.Stamp(row, Stamp);
        Set.Remove(row);
        var outcome = await Writer.CommitAsync(ct);
        if (outcome != SaveOutcome.Saved)
        {
            FlashError(ConflictMessage);
            return Redirect(ListUrl);
        }

        await CleanUpAsync(row, ct);
        Flash($"Removed {info.Title}.");
        return Redirect(ListUrl);
    }
}
