using Portfolio.Web.Configuration;

namespace Portfolio.Web.Services;

/// <summary>
/// A profession preset loaded from <c>Templates/&lt;key&gt;/template.json</c>: the profession-specific
/// copy and ordering that shared pages and services read instead of hardcoding.
/// </summary>
public class TemplatePreset
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Profession { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Layout archetype: showcase, gallery, results, or credentials.</summary>
    public string Archetype { get; set; } = "showcase";

    /// <summary>Canonical ordering for CV skill groups; groups not listed sort last.</summary>
    public List<string> SkillGroups { get; set; } = new();

    public TemplateSeo Seo { get; set; } = new();
}

/// <summary>SEO copy patterns. Placeholders: {name}, {title}, {summary}.</summary>
public class TemplateSeo
{
    /// <summary>Home page title (the full literal title, not suffixed).</summary>
    public string Title { get; set; } = "{name} — {title}";
    public string Description { get; set; } = "{summary}";
    public string AboutDescription { get; set; } = "{summary}";

    public static string Apply(string pattern, SiteOptions site) =>
        pattern
            .Replace("{name}", site.OwnerName)
            .Replace("{title}", site.JobTitle)
            .Replace("{summary}", site.Summary);
}
