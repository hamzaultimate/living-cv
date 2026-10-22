using System.Reflection;
using System.Text.RegularExpressions;
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
            if (string.IsNullOrWhiteSpace(file) || file != Path.GetFileName(file) || !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                errors.Add("starter.file must be a .json file name inside the template folder.");
        }

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
