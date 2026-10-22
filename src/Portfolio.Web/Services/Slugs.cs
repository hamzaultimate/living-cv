using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Portfolio.Web.Services;

/// <summary>Builds and checks project web address names (the <c>{slug}</c> of <c>/projects/{slug}</c>).</summary>
public static partial class Slugs
{
    public const int MaxLength = 120;

    /// <summary>Longest generated name before a collision suffix is added.</summary>
    private const int GeneratedLength = 100;

    /// <summary>Names that would read as an action or a system address next to <c>/projects/</c> or in a link.</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
    {
        "new", "edit", "delete", "create", "update", "admin", "api", "index", "default", "null", "undefined",
        "feed", "rss", "sitemap", "robots", "cv", "login", "logout", "setup", "health", "uploads", "img", "css", "js",
    };

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Pattern();

    /// <summary>True for lower-case letters and digits separated by single hyphens, within the column length.</summary>
    public static bool IsValid(string? slug) => slug is not null && slug.Length <= MaxLength && Pattern().IsMatch(slug);

    public static bool IsReserved(string slug) => Reserved.Contains(slug);

    /// <summary>A URL-safe name for <paramref name="title"/>: accents folded to plain letters, everything else that is not a letter or digit
    /// becomes a hyphen. Never empty and never reserved.</summary>
    public static string From(string? title)
    {
        var sb = new StringBuilder();
        foreach (var c in (title ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > GeneratedLength) slug = slug[..GeneratedLength].TrimEnd('-');
        if (slug.Length == 0) slug = "project";
        return IsReserved(slug) ? slug + "-1" : slug;
    }

    /// <summary><paramref name="slug"/> if free, otherwise the first of <c>slug-2</c>, <c>slug-3</c>, ... that is.</summary>
    public static string Unique(string slug, Func<string, bool> taken)
    {
        if (!taken(slug)) return slug;
        for (var n = 2; ; n++)
        {
            var candidate = $"{slug}-{n}";
            if (!taken(candidate)) return candidate;
        }
    }
}
