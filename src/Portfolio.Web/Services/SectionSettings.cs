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
/// from what the template does by default or an earlier explicit choice exists (it is kept, not re-derived), and the positions are written (all of them) only when the order differs from the template's. Settings for keys the template does not list
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
        var item = view.ItemLabel;
        var keys = rows.Select(r => r.Key).ToList();
        // The list must be exactly the template's sections, each once: a forged, missing, repeated or foreign key is refused whole, so it can neither add a section nor drop one.
        if (keys.Count != definition.Sections.Count || keys.Distinct(StringComparer.Ordinal).Count() != keys.Count || keys.Any(k => Find(definition, k) is null))
        {
            check.Error(string.Empty, "The list on this page does not match your template any more, so nothing was saved. Reload the page and try again.");
            return false;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var template = Find(definition, rows[i].Key)!;
            var name = TemplateView.Cap(template.Label);
            var property = $"Rows[{i}].{nameof(SectionRowForm.Label)}";
            var text = labelShown ? check.Text(property, "label", rows[i].Label, ProfileParts.SectionLabelMax, context: name) : null;
            // Zero-width and direction-control characters (Unicode category Cf) are invisible: a name made of them looks empty, and bidi overrides can make a menu entry read
            // as another. Kept local to the sections page so no other editor's behaviour changes.
            if (text is not null && check.IsValid(property) && text.Any(c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format))
                check.Error(property, $"{name}: \"{view.Label("label")}\" contains characters that are not allowed.");
            typed.Add(text);
        }

        // Two sections with one name in the menu would be indistinguishable. Compared case-insensitively over the names the owner would end up with, switched off or not.
        // A typed name must also differ from what the menu shows for every other section (its short name on a wide screen, or its own typed name) and from the fixed "CV" entry.
        var effective = rows.Select((r, i) => labelShown && !string.IsNullOrWhiteSpace(typed[i]) ? typed[i]! : Find(definition, r.Key)!.Label).ToList();
        var shown = rows.Select((r, i) => labelShown && !string.IsNullOrWhiteSpace(typed[i]) ? typed[i]! : Find(definition, r.Key)!.ShortLabel ?? Find(definition, r.Key)!.Label).ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var property = $"Rows[{i}].{nameof(SectionRowForm.Label)}";
            if (!check.IsValid(property)) continue;
            bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            var typedName = labelShown && !string.IsNullOrWhiteSpace(typed[i]);
            if (effective.Take(i).Any(e => Same(e, effective[i])) || (typedName && Enumerable.Range(0, rows.Count).Any(j => j != i && (Same(shown[j], effective[i]) || Same(effective[j], effective[i])))))
                check.Error(property, $"Each \"{item}\" needs its own name: \"{effective[i]}\" is already used by another menu entry.");
            else if (typedName && Same(effective[i], "cv"))
                check.Error(property, $"Each \"{item}\" needs its own name: \"{effective[i]}\" is the name of the CV link, which is always in the menu.");
        }

        // A site with no section that has its own page would have an empty menu.
        var anyPage = rows.Any((r, i) => (Find(definition, r.Key)!.Required || r.Enabled) && SectionRegistry.PathFor(r.Key!) is not null);
        if (!anyPage) check.Error(string.Empty, $"Keep at least one \"{item}\" switched on that appears in the menu.");

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
            // A choice is stored when it differs from the template's default, and an earlier explicit choice (Shown, or the older Enabled: false) is never dropped by a later
            // save, even when it now equals the default: otherwise a switch of template would bring back a section the owner hid.
            var touched = old is { Shown: not null } or { Enabled: false };
            bool? shown = !template.Required && (rows[i].Enabled != template.Enabled || touched) ? rows[i].Enabled : null;
            var order = orderChanged ? i + 1 : 0;
            if (label is null && shown is null && order == 0) continue;
            result.Add(new SectionSetting { Key = template.Key, Label = label, Enabled = shown != false, Shown = shown, SortOrder = order });
        }
        // Copies, not the stored objects: the profile tracks those, and a list may not hold an object it already owns in another place.
        result.AddRange(stored.Where(s => !listed.Contains(s.Key)).Select(s => new SectionSetting { Key = s.Key, Label = s.Label, Enabled = s.Enabled, Shown = s.Shown, SortOrder = s.SortOrder }));
        return result;
    }

    /// <summary>What a switch to another template does to the menu: whether it may go ahead, and which sections were kept on so the menu is not left empty.</summary>
    public sealed record SwitchPlan(bool Allowed, IReadOnlyList<string> Kept, TemplateView Target);

    /// <summary>True when at least one section that has its own page (so it appears in the menu) is on.</summary>
    public static bool HasMenuSection(TemplateView view) => view.Sections.Any(s => s.Enabled && s.Path is not null);

    /// <summary>
    /// Decides a switch of <paramref name="profile"/> to <paramref name="target"/> against the row as it is. The new template's defaults with the owner's stored choices on top must leave a section
    /// in the menu. When they would not (typically a site that shows only a section the new template has off by default), the menu sections that are on now are kept on by storing an explicit
    /// <see cref="SectionSetting.Shown"/> for them, so the visible site keeps its menu; the switch is refused only when nothing is on now either. The profile is changed only when the plan allows it.
    /// </summary>
    public static SwitchPlan PlanSwitch(TemplateResolver resolver, SiteProfile profile, TemplateDefinition target)
    {
        var options = SiteProfileMapper.ToOptions(profile);
        var after = resolver.Resolve(options, target.Key);
        if (HasMenuSection(after)) return new SwitchPlan(true, [], after);

        var now = resolver.Resolve(options);
        var keep = after.Sections.Where(s => !s.Enabled && s.Path is not null && now.IsEnabled(s.Key)).ToList();
        if (keep.Count == 0) return new SwitchPlan(false, [], after);

        var sections = profile.Sections.ToList();
        foreach (var s in keep)
        {
            var old = sections.LastOrDefault(x => string.Equals(x.Key, s.Key, StringComparison.OrdinalIgnoreCase));
            if (old is null) sections.Add(new SectionSetting { Key = s.Key, Enabled = true, Shown = true });
            else { sections.Remove(old); sections.Add(new SectionSetting { Key = old.Key, Label = old.Label, Enabled = true, Shown = true, SortOrder = old.SortOrder }); }
        }
        profile.Sections = sections;
        return new SwitchPlan(true, keep.Select(s => TemplateView.Cap(s.Label)).ToList(), resolver.Resolve(SiteProfileMapper.ToOptions(profile), target.Key));
    }

    /// <summary>The words for a refused switch, in the target template's own wording.</summary>
    public static string NoMenuMessage(string templateName, TemplateView target)
    {
        var view = new EditorView(target, ProfileParts.Sections);
        return $"Switching to {templateName} would leave no \"{view.ItemLabel}\" switched on that appears in the menu, so nothing was changed. Open {view.Heading} and switch one on first.";
    }
}
