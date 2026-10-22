using System.Globalization;
using System.Text.RegularExpressions;

namespace Portfolio.Web.Services;

/// <summary>
/// Input rules shared by the content editors. Every value arrives from a browser form and is untrusted: these checks decide what is
/// allowed to reach the database, whatever the page later does to escape it.
/// </summary>
public static partial class ContentRules
{
    // A tag, comment, declaration or processing instruction start. A bare "<" in "latency <50ms" is ordinary text and is allowed.
    [GeneratedRegex(@"<[A-Za-z/!?]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TagStart();

    /// <summary>True when the text contains something a browser could take for markup.</summary>
    public static bool HasMarkup(string? value) => value is not null && TagStart().IsMatch(value);

    /// <summary>True for control characters other than tab and, when <paramref name="multiline"/>, line breaks.</summary>
    public static bool HasControlCharacters(string? value, bool multiline = false) =>
        value is not null && value.Any(c => char.IsControl(c) && c != '\t' && !(multiline && c is '\n' or '\r'));

    /// <summary>Trimmed text, or null when nothing is left.</summary>
    public static string? Trim(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    /// <summary>The non-empty trimmed lines of a text area.</summary>
    public static List<string> Lines(string? text) =>
        (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    /// <summary>Text with normalised line breaks, for a multi-line value (a paragraph with blank lines kept).</summary>
    public static string? Paragraph(string? value)
    {
        var t = value?.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    /// <summary>An absolute http or https address with a host and no embedded credentials. Everything else (javascript:, data:, relative, empty) is false.</summary>
    public static bool IsHttpUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !HasControlCharacters(value)
        && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && !string.IsNullOrEmpty(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo);

    /// <summary>A site-relative address such as <c>/img/badge.svg</c>: one leading slash, no scheme, no backslash, no dot segments.</summary>
    public static bool IsLocalPath(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal)
        && !value.Contains('\\') && !value.Contains(':') && !value.Contains("..", StringComparison.Ordinal)
        && !HasControlCharacters(value) && !value.Any(char.IsWhiteSpace);

    /// <summary>True when <paramref name="value"/> may be saved in an address field: a web address, or exactly the site-relative value that was
    /// already stored (content seeded with the site points at its own files that way), so editing an older entry does not force a change.</summary>
    public static bool IsAcceptableUrl(string? value, string? stored) =>
        IsHttpUrl(value) || (value is not null && stored is not null && value == stored && (IsLocalPath(stored) || IsHttpUrl(stored)));
}

/// <summary>
/// A date the way people write it on a CV: a year, a month and year, or a full date. The editors keep the text as typed (the pages print it) and
/// use this only to check order and the future. A period covers days, so a year ends on 31 December.
/// </summary>
public readonly record struct PartialDate(DateOnly First, DateOnly Last)
{
    private static readonly string[] MonthYear = ["MMM yyyy", "MMMM yyyy", "yyyy-MM", "MM/yyyy", "M/yyyy"];

    public static bool TryParse(string? text, out PartialDate date)
    {
        date = default;
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t) || t.Length > 30) return false;

        if (t.Length == 4 && t.All(char.IsAsciiDigit))
        {
            var year = int.Parse(t, CultureInfo.InvariantCulture);
            if (year < 1950 || year > 2100) return false;
            date = new PartialDate(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
            return true;
        }
        if (DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day.Year is >= 1950 and <= 2100)
        {
            date = new PartialDate(day, day);
            return true;
        }
        foreach (var format in MonthYear)
            if (DateOnly.TryParseExact(t, format, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var month) && month.Year is >= 1950 and <= 2100)
            {
                var first = new DateOnly(month.Year, month.Month, 1);
                date = new PartialDate(first, first.AddMonths(1).AddDays(-1));
                return true;
            }
        return false;
    }
}
