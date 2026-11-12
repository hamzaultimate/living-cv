using Portfolio.Data.Entities;
using Portfolio.Web.Configuration;

namespace Portfolio.Web.Services;

/// <summary>One line of the sections page: a section the template has, as the owner's settings leave it.</summary>
public sealed class SectionRowForm
{
    /// <summary>The section's key (<see cref="SectionRegistry"/>). Posted back so a row is matched to its section by key, never by position.</summary>
    public string? Key { get; set; }

    /// <summary>The owner's own name for the section; empty means the template's name.</summary>
    public string? Label { get; set; }

    public bool Enabled { get; set; }
}

/// <summary>
/// The rules of the sections page, apart from the page: which rows it shows, whether a posted list is acceptable, and the settings it writes. The owner can rename, show,
/// hide and reorder the sections the template lists; nothing here can add a section the template does not list, and nothing deletes a row of content.
/// Only differences from the template are stored: a label equal to the template's is dropped, <see cref="SectionSetting.Shown"/> is written only when the choice differs
/// from what the template does by default, and the positions are written (all of them) only when the order differs from the template's. Settings for keys the template does not list
/// (left by another template or by hand) are carried over untouched.
/// </summary>
public static class SectionSettings
{
    /// <summary>The rows for the page, in the owner's current order.</summary>
    public static List<SectionRowForm> Rows(TemplateDefinition definition, TemplateView resolved) =>
        resolved.Sections.Select(s =>
        {
            var template = definition.Sections.First(d => d.Key == s.Key);
            return new SectionRowForm { Key = s.Key, Label = s.Label != template.Label ? s.Label : null, Enabled = s.Enabled };
        }).ToList();

    public static TemplateSection? Find(TemplateDefinition definition, string? key) => definition.Sections.FirstOrDefault(s => s.Key == key);

    /// <summary>
    /// Checks <paramref name="rows"/> against the template and records what is wrong on <paramref name="check"/>. <paramref name="labelShown"/> is false when the template leaves the name
    /// out of the form (the posted text is then ignored). Returns true when the list may be saved.
    /// </summary>
    public static bool Validate(FormCheck check, EditorView view, TemplateDefinition definition, IReadOnlyList<SectionRowForm> rows, bool labelShown, out List<string?> labels)
    {
        var typed = new List<string?>();
        labels = typed;
        var item = view.ItemLabelLower;
        var keys = rows.Select(r => r.Key).ToList();
        // The list must be exactly the template's sections, each once: a forged, missing, repeated or foreign key is refused whole, so it can neither add a section nor drop one.
        if (keys.Count != definition.Sections.Count || keys.Distinct(StringComparer.Ordinal).Count() != keys.Count || keys.Any(k => Find(definition, k) is null))
        {
            check.Error(string.Empty, $"The list of {item}s does not match your template any more, so nothing was saved. Reload the page and try again.");
            return false;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var template = Find(definition, rows[i].Key)!;
            var name = TemplateView.Cap(template.Label);
            typed.Add(labelShown ? check.Text($"Rows[{i}].{nameof(SectionRowForm.Label)}", "label", rows[i].Label, ProfileParts.SectionLabelMax, context: name) : null);
        }

        // Two sections with one name in the menu would be indistinguishable. Compared case-insensitively over the names the owner would end up with, switched off or not.
        var effective = rows.Select((r, i) => labelShown && !string.IsNullOrWhiteSpace(typed[i]) ? typed[i]! : Find(definition, r.Key)!.Label).ToList();
        for (var i = 0; i < rows.Count; i++)
            if (check.IsValid($"Rows[{i}].{nameof(SectionRowForm.Label)}") && effective.Take(i).Any(e => string.Equals(e, effective[i], StringComparison.OrdinalIgnoreCase)))
                check.Error($"Rows[{i}].{nameof(SectionRowForm.Label)}", $"Each {item} needs its own name: \"{effective[i]}\" is already used by another.");

        // A site with no section that has its own page would have an empty menu.
        var anyPage = rows.Any((r, i) => (Find(definition, r.Key)!.Required || r.Enabled) && SectionRegistry.PathFor(r.Key!) is not null);
        if (!anyPage) check.Error(string.Empty, $"Keep at least one {item} switched on that appears in the menu.");

        return check.Ok;
    }

    private static bool Any<T>(this IReadOnlyList<T> list, Func<T, int, bool> predicate)
    {
        for (var i = 0; i < list.Count; i++) if (predicate(list[i], i)) return true;
        return false;
    }

    /// <summary>
    /// The settings to store for a validated list. <paramref name="stored"/> are the current settings: entries for keys the template does not list are kept, and when the template leaves
    /// the name out of the form the stored name of each section is kept.
    /// </summary>
    public static List<SectionSetting> Build(TemplateDefinition definition, IReadOnlyList<SectionRowForm> rows, IReadOnlyList<string?> labels, bool labelShown, IReadOnlyList<SectionSetting> stored)
    {
        var listed = definition.Sections.Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orderChanged = !rows.Select(r => r.Key).SequenceEqual(definition.Sections.Select(s => s.Key), StringComparer.Ordinal);
        var result = new List<SectionSetting>();
        for (var i = 0; i < rows.Count; i++)
        {
            var template = Find(definition, rows[i].Key)!;
            var old = stored.LastOrDefault(s => string.Equals(s.Key, template.Key, StringComparison.OrdinalIgnoreCase));
            var typed = labelShown ? labels[i] : old?.Label;
            var label = !string.IsNullOrWhiteSpace(typed) && typed.Trim() != template.Label ? typed.Trim() : null;
            bool? shown = !template.Required && rows[i].Enabled != template.Enabled ? rows[i].Enabled : null;
            var order = orderChanged ? i + 1 : 0;
            if (label is null && shown is null && order == 0) continue;
            result.Add(new SectionSetting { Key = template.Key, Label = label, Enabled = shown != false, Shown = shown, SortOrder = order });
        }
        // Copies, not the stored objects: the profile tracks those, and a list may not hold an object it already owns in another place.
        result.AddRange(stored.Where(s => !listed.Contains(s.Key)).Select(s => new SectionSetting { Key = s.Key, Label = s.Label, Enabled = s.Enabled, Shown = s.Shown, SortOrder = s.SortOrder }));
        return result;
    }
}
