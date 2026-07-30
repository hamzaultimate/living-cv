using System.Text.Json;

namespace Portfolio.Web.Services;

/// <summary>Shared schema.org JSON-LD builders used across pages.</summary>
public static class SeoHelpers
{
    /// <summary>Builds a BreadcrumbList script body. Pass (name, path) pairs root-first;
    /// the site root should use path "/" and is emitted without a trailing slash to match
    /// the site's canonical URL form.</summary>
    public static string BreadcrumbJsonLd(string absBase, params (string Name, string Path)[] crumbs)
    {
        var items = crumbs.Select((c, i) => new Dictionary<string, object?>
        {
            ["@type"] = "ListItem",
            ["position"] = i + 1,
            ["name"] = c.Name,
            ["item"] = c.Path == "/" ? absBase : $"{absBase}{c.Path}",
        }).ToArray();

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BreadcrumbList",
            ["itemListElement"] = items,
        });
    }
}
