using System.Text.Json;

namespace Portfolio.Web.Services;

/// <summary>Shared absolute-URL and schema.org JSON-LD builders used across pages.</summary>
public static class SeoHelpers
{
    /// <summary>The absolute base URL (no trailing slash) for canonical/OG/sitemap/JSON-LD links.
    /// Taken from the site profile Url so every host the app answers on (IP, hosting-panel name, dev.*, or a
    /// spoofed Host header) advertises the one canonical domain. Falls back to the request's own
    /// scheme+host only when the profile Url isn't a valid http(s) URL.</summary>
    public static async Task<string> AbsoluteBaseAsync(HttpContext ctx)
    {
        var siteUrl = (await ctx.RequestServices.GetRequiredService<ISiteProfileProvider>().GetAsync(ctx.RequestAborted)).Url;
        return TryGetBase(siteUrl, out var b) ? b : $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    }

    public static bool TryGetBase(string? siteUrl, out string baseUrl)
    {
        if (Uri.TryCreate(siteUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            baseUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            return true;
        }
        baseUrl = "";
        return false;
    }

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
