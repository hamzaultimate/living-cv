using System.Reflection;
using System.Text.RegularExpressions;
using Portfolio.Data.Entities;
using Portfolio.Web.Configuration;

namespace Portfolio.Web.Services;

/// <summary>
/// Checks a template manifest against the authoring rules. The catalog skips a template with any error; tests run the same
/// checks over every template found on disk, so a new profession is correct by construction.
/// </summary>
public static partial class TemplateValidator
{
    public const int MaxTitleLength = 65;
    public const int MaxDescriptionLength = TemplateSeo.MaxDescriptionLength;

    /// <summary>Professions where published content can identify third parties; their templates must carry guidance.</summary>
    public static readonly IReadOnlyList<string> SensitiveKeys = ["healthcare", "legal"];

    // Sample values used to measure the SEO patterns: a typical full name, the template's own profession as the title, and a
    // summary of typical length.
    private const string SampleName = "Jordan Alexander";
    private static readonly string SampleSummary = new('s', 100);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KebabCase();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColour();

    public static List<string> Validate(TemplateDefinition t, string? folderName = null)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(t.Key)) errors.Add("key is required.");
        else
        {
            if (!KebabCase().IsMatch(t.Key)) errors.Add($"key '{t.Key}' must be lowercase kebab-case.");
            if (t.Key.Length > 60) errors.Add("key must be at most 60 characters.");
            if (folderName is not null && !string.Equals(t.Key, folderName, StringComparison.OrdinalIgnoreCase))
                errors.Add($"key '{t.Key}' does not match folder '{folderName}'.");
        }

        Require(errors, "name", t.Name, 60);
        Require(errors, "profession", t.Profession, 80);
        Require(errors, "description", t.Description, 200);

        if (!Archetypes.Known.Contains(t.Archetype, StringComparer.Ordinal))
            errors.Add($"archetype '{t.Archetype}' is unknown (expected one of {string.Join(", ", Archetypes.Known)}).");
        else if (!Archetypes.Implemented.Contains(t.Archetype, StringComparer.Ordinal))
            errors.Add($"archetype '{t.Archetype}' has no layout yet (available: {string.Join(", ", Archetypes.Implemented)}).");

        if (!string.IsNullOrWhiteSpace(t.Key) && (t.Sensitive || SensitiveKeys.Contains(t.Key, StringComparer.OrdinalIgnoreCase)))
        {
            if (!t.Sensitive) errors.Add($"'{t.Key}' is a sensitive profession: set \"sensitive\": true.");
            if (string.IsNullOrWhiteSpace(t.Guidance)) errors.Add("a sensitive template needs a guidance note about not publishing patient or client data.");
        }

        if (!string.IsNullOrWhiteSpace(t.Theme.Default) && !KebabCase().IsMatch(t.Theme.Default))
            errors.Add($"theme.default '{t.Theme.Default}' must be lowercase kebab-case.");
        if (!string.IsNullOrWhiteSpace(t.Theme.ThemeColor) && !HexColour().IsMatch(t.Theme.ThemeColor))
            errors.Add($"theme.themeColor '{t.Theme.ThemeColor}' must look like #0f172a.");

        ValidateSections(t, errors);

        var groups = t.SkillGroups.Select(g => g?.Trim() ?? "").ToList();
        if (groups.Any(g => g.Length == 0)) errors.Add("skillGroups must not contain blank names.");
        if (groups.Where(g => g.Length > 0).GroupBy(g => g, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            errors.Add("skillGroups must not repeat a group.");

        ValidateSeo(t, errors);
        ValidateCopy(t, errors);

        if (t.Starter is not null)
        {
            var file = t.Starter.File;
            if (t.Starter.Source is not null)
            {
                if (file is not null) errors.Add("starter must name either a file or a source, not both.");
                if (!t.Starter.IsSeed()) errors.Add($"starter.source must be \"{TemplateStarterRef.SeedSource}\".");
            }
            else if (string.IsNullOrWhiteSpace(file) || file != Path.GetFileName(file) || !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                errors.Add("starter.file must be a .json file name inside the template folder.");
        }

        ValidateHeroSuggestions(t, errors);
        ValidateAccents(t, errors);
        ValidateEditor(t, errors);

        return errors;
    }

    /// <summary>Checks a loaded starter file: it must declare itself fictional and carry the identity the pages need.</summary>
    public static List<string> ValidateStarter(TemplateStarter starter)
    {
        var errors = new List<string>();
        if (!starter.Fictional) errors.Add("starter content must set \"fictional\": true.");
        if (string.IsNullOrWhiteSpace(starter.Profile.OwnerName)) errors.Add("starter profile needs an ownerName.");
        if (string.IsNullOrWhiteSpace(starter.Profile.JobTitle)) errors.Add("starter profile needs a jobTitle.");
        return errors;
    }

    public const int MaxAccents = 8;
    public const int MaxSuggestedTitle = 120;
    public const int MaxSuggestedSummary = 400;

    private static void ValidateHeroSuggestions(TemplateDefinition t, List<string> errors)
    {
        var s = t.HeroSuggestions;
        Require(errors, "heroSuggestions.title", s.Title, MaxSuggestedTitle);
        Require(errors, "heroSuggestions.summary", s.Summary, MaxSuggestedSummary);
        if (HasMarkup(s.Title)) errors.Add("heroSuggestions.title must be plain text (no < or >).");
        if (HasMarkup(s.Summary)) errors.Add("heroSuggestions.summary must be plain text (no < or >).");
    }

    /// <summary>The accents are the allow-list for a value that ends up in a stylesheet, so everything about them is checked strictly.</summary>
    private static void ValidateAccents(TemplateDefinition t, List<string> errors)
    {
        var accents = t.Theme.Accents;
        if (accents.Count > MaxAccents) errors.Add($"theme.accents lists {accents.Count} accents; the limit is {MaxAccents}.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in accents)
        {
            if (string.IsNullOrWhiteSpace(a.Key) || !KebabCase().IsMatch(a.Key) || a.Key.Length > SiteProfile.MaxLength.AccentColor)
                errors.Add($"theme.accents key '{a.Key}' must be lowercase kebab-case and at most {SiteProfile.MaxLength.AccentColor} characters.");
            else if (!seen.Add(a.Key)) errors.Add($"theme.accents lists '{a.Key}' twice.");
            Require(errors, $"theme.accents['{a.Key}'].label", a.Label, 30);
            if (HasMarkup(a.Label)) errors.Add($"theme.accents['{a.Key}'].label must be plain text.");
            foreach (var (name, colour) in new[] { ("dark.accent", a.Dark.Accent), ("dark.strong", a.Dark.Strong), ("light.accent", a.Light.Accent), ("light.strong", a.Light.Strong) })
                if (!HexColour().IsMatch(colour ?? "")) errors.Add($"theme.accents['{a.Key}'].{name} '{colour}' must look like #0f172a.");
        }
    }

    public const int MaxEditorLabel = 60;
    public const int MaxEditorHint = 200;
    public const int MaxEditorItemLabel = 40;

    /// <summary>The editor block only reshapes wording and visibility, so it is checked against the closed field lists: an unknown field, markup in
    /// a label, or an attempt to hide a field the entry cannot exist without would otherwise be silently ignored or break the form.</summary>
    private static void ValidateEditor(TemplateDefinition t, List<string> errors)
    {
        foreach (var section in EditorFields.Sections)
        {
            var settings = t.Editor.For(section)!;
            var known = EditorFields.For(section);
            if (settings.ItemLabel is not null)
            {
                if (string.IsNullOrWhiteSpace(settings.ItemLabel) || settings.ItemLabel.Length > MaxEditorItemLabel)
                    errors.Add($"editor.{section}.itemLabel must be 1 to {MaxEditorItemLabel} characters.");
                if (HasMarkup(settings.ItemLabel)) errors.Add($"editor.{section}.itemLabel must be plain text (no < or >).");
            }
            foreach (var (key, field) in settings.Fields)
            {
                var info = known.FirstOrDefault(f => f.Key == key);
                var path = $"editor.{section}.fields.{key}";
                if (info is null)
                {
                    errors.Add($"{path} is not a field of the {section} editor (expected one of {string.Join(", ", known.Select(f => f.Key))}).");
                    continue;
                }
                if (field is null) { errors.Add($"{path} must be an object."); continue; }
                if (field.Label is not null && (string.IsNullOrWhiteSpace(field.Label) || field.Label.Length > MaxEditorLabel))
                    errors.Add($"{path}.label must be 1 to {MaxEditorLabel} characters.");
                if (field.Hint is not null && (string.IsNullOrWhiteSpace(field.Hint) || field.Hint.Length > MaxEditorHint))
                    errors.Add($"{path}.hint must be 1 to {MaxEditorHint} characters.");
                if (HasMarkup(field.Label) || HasMarkup(field.Hint)) errors.Add($"{path} label and hint must be plain text (no < or >).");
                if (field.Hidden && info.Required) errors.Add($"{path} cannot be hidden: an entry cannot exist without it.");
            }
        }
    }

    private static void ValidateSections(TemplateDefinition t, List<string> errors)
    {
        if (t.Sections.Count == 0) errors.Add("sections must list at least one section.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in t.Sections)
        {
            if (!SectionRegistry.IsKnown(s.Key)) { errors.Add($"section key '{s.Key}' is not one of: {string.Join(", ", SectionRegistry.Keys)}."); continue; }
            if (!seen.Add(s.Key)) errors.Add($"section '{s.Key}' is listed twice.");
            if (string.IsNullOrWhiteSpace(s.Label)) errors.Add($"section '{s.Key}' needs a label.");
            if (HasMarkup(s.Label) || HasMarkup(s.ShortLabel) || HasMarkup(s.ItemLabel)) errors.Add($"section '{s.Key}' labels must be plain text.");
            if (s.Key is SectionRegistry.Projects or SectionRegistry.Blog && string.IsNullOrWhiteSpace(s.ItemLabel))
                errors.Add($"section '{s.Key}' needs an itemLabel (what one entry is called).");
        }
    }

    private static void ValidateSeo(TemplateDefinition t, List<string> errors)
    {
        var site = new SiteOptions { OwnerName = SampleName, JobTitle = t.Profession, Summary = SampleSummary };
        CheckPattern(errors, "seo.title", t.Seo.Title, site, MaxTitleLength);
        CheckPattern(errors, "seo.description", t.Seo.Description, site, MaxDescriptionLength);
        CheckPattern(errors, "seo.aboutDescription", t.Seo.AboutDescription, site, MaxDescriptionLength);
    }

    private static void CheckPattern(List<string> errors, string field, string pattern, SiteOptions site, int max)
    {
        if (string.IsNullOrWhiteSpace(pattern)) { errors.Add($"{field} is required."); return; }
        var length = TemplateSeo.Apply(pattern, site).Length;
        if (length > max) errors.Add($"{field} expands to {length} characters; the limit is {max}.");
    }

    /// <summary>Every copy string must be present (unless marked optional) and plain text; every Description is a meta description.</summary>
    private static void ValidateCopy(TemplateDefinition t, List<string> errors)
    {
        var site = new SiteOptions { OwnerName = SampleName, JobTitle = t.Profession, Summary = SampleSummary };
        foreach (var group in typeof(TemplateCopy).GetProperties())
        {
            var groupValue = group.GetValue(t.Copy);
            foreach (var prop in group.PropertyType.GetProperties().Where(p => p.PropertyType == typeof(string)))
            {
                var path = $"copy.{Camel(group.Name)}.{Camel(prop.Name)}";
                var value = (string?)prop.GetValue(groupValue);
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (prop.GetCustomAttribute<OptionalCopyAttribute>() is null) errors.Add($"{path} is required.");
                    continue;
                }
                if (HasMarkup(value)) errors.Add($"{path} must be plain text (no < or >).");
                if (prop.Name == "Description")
                    CheckPattern(errors, path, value, site, MaxDescriptionLength);
                if (path == "copy.projects.imageAlt" && !value.Contains("{title}", StringComparison.Ordinal))
                    errors.Add($"{path} must contain {{title}}, or every image would share one alt text.");
            }
        }
    }

    private static void Require(List<string> errors, string field, string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add($"{field} is required.");
        else if (value.Length > max) errors.Add($"{field} must be at most {max} characters.");
    }

    private static bool HasMarkup(string? value) => value is not null && (value.Contains('<') || value.Contains('>'));

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
