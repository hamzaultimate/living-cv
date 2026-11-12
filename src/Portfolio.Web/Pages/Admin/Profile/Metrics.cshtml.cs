using Microsoft.AspNetCore.Mvc;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Profile;

public class MetricRowForm
{
    /// <summary>Where the row sits in the stored list (set when the form is drawn), so a field the template hides keeps the value its row already has. Null for a row added on the page.</summary>
    public int? Source { get; set; }

    public string? Value { get; set; }
    public string? Unit { get; set; }
    public string? Label { get; set; }
}

public class MetricsForm
{
    public List<MetricRowForm> Rows { get; set; } = new();

    /// <summary>The stamp of the profile when the form was loaded.</summary>
    public string? Stamp { get; set; }
}

// A few short rows; anything much larger is not from this page.
[BodyLimit(64 * 1024)]
public class MetricsModel : ProfilePartModel
{
    public MetricsModel(ProfileEditor profile, RequestTemplate template) : base(profile, template) { }

    public override string Part => ProfileParts.Metrics;

    [BindProperty]
    public MetricsForm Form { get; set; } = new();

    public bool CanAdd => Form.Rows.Count < ProfileParts.MetricRows;

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadViewAsync();
        var p = await Writer.LoadAsync(ct);
        Form = new MetricsForm
        {
            Rows = p.Metrics.Select((m, i) => new MetricRowForm { Source = i, Value = m.Value, Unit = m.Unit, Label = m.Label }).ToList(),
            Stamp = p.ConcurrencyStamp,
        };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadViewAsync();
        var c = new FormCheck(ModelState, View);
        var showUnit = View.Visible("unit");
        var showLabel = View.Visible("label");
        // A row the person left empty is dropped. Only what the template shows counts: a hidden column is never typed into.
        var rows = Form.Rows.Select((r, i) => (Row: r, Number: i + 1))
            .Where(x => !(string.IsNullOrWhiteSpace(x.Row.Value) && (!showUnit || string.IsNullOrWhiteSpace(x.Row.Unit)) && (!showLabel || string.IsNullOrWhiteSpace(x.Row.Label))))
            .ToList();
        if (rows.Count > ProfileParts.MetricRows) ModelState.AddModelError(string.Empty, $"At most {ProfileParts.MetricRows} rows in {View.Heading}.");

        var kept = new List<(int? Source, string Value, string? Unit, string? Label)>();
        foreach (var (row, number) in rows.Take(ProfileParts.MetricRows))
        {
            var name = $"{View.ItemLabel} {number}";
            var i = number - 1;
            var value = c.Text($"Rows[{i}].{nameof(row.Value)}", "value", row.Value, ProfileParts.MetricValueMax, required: true, context: name);
            var unit = showUnit ? c.Text($"Rows[{i}].{nameof(row.Unit)}", "unit", row.Unit, ProfileParts.MetricUnitMax, context: name) : null;
            var label = showLabel ? c.Text($"Rows[{i}].{nameof(row.Label)}", "label", row.Label, ProfileParts.MetricLabelMax, context: name) : null;
            kept.Add((row.Source, value ?? "", unit, label));
        }
        if (!ModelState.IsValid) return Page();

        var outcome = await Writer.UpdateAsync(p =>
        {
            var stored = p.Metrics.ToList();
            var taken = new HashSet<int>();
            p.Metrics = kept.Select(k =>
            {
                // A hidden column keeps what the stored row has. The source names that row; one that is out of range, or already used by another row, names nothing.
                var old = k.Source is { } s && s >= 0 && s < stored.Count && taken.Add(s) ? stored[s] : null;
                return new ProfileMetric { Value = k.Value, Unit = showUnit ? k.Unit : old?.Unit, Label = showLabel ? k.Label : old?.Label };
            }).ToList();
        }, Form.Stamp, ct);
        if (outcome != SaveOutcome.Saved) return Conflict();

        Flash(SavedMessage);
        return RedirectToPage();
    }

    // The row buttons change the rows on the page only; nothing is written until Save. They are posts so that the typed values travel with them without script.

    public async Task<IActionResult> OnPostAddAsync()
    {
        await LoadViewAsync();
        if (!CanAdd) return RowChanged($"You can have at most {ProfileParts.MetricRows} rows in {View.Heading}.", null);
        Form.Rows.Add(new MetricRowForm());
        var i = Form.Rows.Count - 1;
        return RowChanged($"{View.ItemLabel} {i + 1} added. It is not saved until you save.", $"Form_Rows_{i}__Value");
    }

    public async Task<IActionResult> OnPostRemoveAsync(int index)
    {
        await LoadViewAsync();
        if (index < 0 || index >= Form.Rows.Count) return RowChanged("That row is not on the page any more.", "add-row");
        Form.Rows.RemoveAt(index);
        return RowChanged($"{View.ItemLabel} {index + 1} removed. It is not saved until you save.", CanAdd ? "add-row" : null);
    }

    public async Task<IActionResult> OnPostMoveAsync(int index, string? direction)
    {
        await LoadViewAsync();
        var target = index + (direction == "up" ? -1 : direction == "down" ? 1 : 0);
        if (index < 0 || index >= Form.Rows.Count || target < 0 || target >= Form.Rows.Count || target == index) return RowChanged("That row cannot move any further.", null);
        (Form.Rows[index], Form.Rows[target]) = (Form.Rows[target], Form.Rows[index]);
        // Focus stays on the row that moved: its button for the same direction, or the other one when it has reached an end.
        var keepGoing = direction == "up" ? target > 0 : target < Form.Rows.Count - 1;
        return RowChanged($"{View.ItemLabel} {index + 1} moved {direction} to position {target + 1}. It is not saved until you save.",
            $"row-{target}-{(keepGoing ? direction : direction == "up" ? "down" : "up")}");
    }
}
