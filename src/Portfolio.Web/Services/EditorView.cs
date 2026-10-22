
namespace Portfolio.Web.Services;

/// <summary>
/// What a content editor shows for the template in effect: the section's name, what one entry is called, each field's label and hint,
/// which optional fields are hidden, and the guidance note for sensitive professions. Everything comes from the manifest
/// (<see cref="TemplateEditor"/>, the sections and the copy) with the neutral wording of <see cref="EditorFields"/> as the fallback, so no
/// page decides anything by template key.
/// </summary>
public sealed class EditorView
{
    private readonly TemplateEditorSection _settings;
    private readonly IReadOnlyList<EditorFieldInfo> _fields;
    private readonly Dictionary<string, string> _copyDefaults;

    public EditorView(TemplateView template, string section)
    {
        Section = section;
        Template = template;
        var resolved = template.Section(section);
        _settings = template.Definition.Editor.For(section) ?? new TemplateEditorSection();
        _fields = EditorFields.For(section);
        SectionLabel = TemplateView.Cap(resolved.Label);
        SectionEnabled = resolved.Enabled;
        var item = !string.IsNullOrWhiteSpace(_settings.ItemLabel) ? _settings.ItemLabel!.Trim()
            : !string.IsNullOrWhiteSpace(resolved.ItemLabel) ? resolved.ItemLabel!.Trim()
            : DefaultItemLabel(section);
        ItemLabel = TemplateView.Cap(item);
        Guidance = template.Definition.Sensitive && !string.IsNullOrWhiteSpace(template.Definition.Guidance) ? template.Definition.Guidance : null;
        SkillGroups = template.Definition.SkillGroups;

        // The public pages already name these parts of a project in the template's words; the editor reuses them.
        var p = template.Copy.Projects;
        _copyDefaults = section == SectionRegistry.Projects
            ? new Dictionary<string, string>
            {
                ["problem"] = p.ProblemHeading, ["approach"] = p.ApproachHeading, ["tech"] = p.StackHeading,
                ["links"] = p.LinksHeading, ["screenshots"] = p.ScreenshotsHeading,
            }
            : new Dictionary<string, string>();
    }

    private static string DefaultItemLabel(string section) => section switch
    {
        SectionRegistry.Experience => "Role",
        SectionRegistry.Skills => "Skill",
        SectionRegistry.Projects => "Entry",
        SectionRegistry.Certifications => "Credential",
        _ => "Entry",
    };

    public string Section { get; }
    public TemplateView Template { get; }

    /// <summary>The section's name in the template's wording, capitalised for a heading ("Licences &amp; registrations").</summary>
    public string SectionLabel { get; }

    /// <summary>What one entry is called, capitalised ("Initiative").</summary>
    public string ItemLabel { get; }

    public string ItemLabelLower => ItemLabel.ToLowerInvariant();

    /// <summary>False when the template does not use this section: the entries are kept but the public site does not show them.</summary>
    public bool SectionEnabled { get; }

    /// <summary>The template's note about not publishing patient or client data, for sensitive templates only.</summary>
    public string? Guidance { get; }

    public IReadOnlyList<string> SkillGroups { get; }

    public string Label(string key)
    {
        if (_settings.Fields.TryGetValue(key, out var f) && !string.IsNullOrWhiteSpace(f?.Label)) return f.Label!.Trim();
        if (_copyDefaults.TryGetValue(key, out var fromCopy) && !string.IsNullOrWhiteSpace(fromCopy)) return fromCopy;
        return _fields.FirstOrDefault(x => x.Key == key)?.Label ?? key;
    }

    public string? Hint(string key)
    {
        if (_settings.Fields.TryGetValue(key, out var f) && !string.IsNullOrWhiteSpace(f?.Hint)) return f.Hint!.Trim();
        if (IsSkillGroup(key) && SkillGroups.Count > 0) return "Suggested for this template: " + string.Join(", ", SkillGroups) + ". You can use your own.";
        return _fields.FirstOrDefault(x => x.Key == key)?.Hint;
    }

    private bool IsSkillGroup(string key) => Section == SectionRegistry.Skills && key == "group";

    /// <summary>True for a field the entry cannot exist without.</summary>
    public bool Required(string key) => _fields.FirstOrDefault(x => x.Key == key)?.Required ?? false;

    /// <summary>False for an optional field the template hides. A required field is always visible, whatever a manifest says.</summary>
    public bool Visible(string key)
    {
        var info = _fields.FirstOrDefault(x => x.Key == key);
        if (info is null) return false;
        return info.Required || !(_settings.Fields.TryGetValue(key, out var f) && f is { Hidden: true });
    }

    /// <summary>The template's word for a certification status.</summary>
    public string StatusLabel(Portfolio.Data.Entities.CertificationStatus status) => CertificationBadges.Label(status, Template.Copy.Certifications);
}
