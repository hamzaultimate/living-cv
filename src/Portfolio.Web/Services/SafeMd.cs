using Markdig;
using Microsoft.Extensions.Caching.Memory;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Portfolio.Web.Services;

/// <summary>
/// Renders Markdown that an editor typed, which is untrusted: raw HTML is switched off (it is shown as text), only the formatting extensions the site
/// needs are on (no generic attributes, media embeds, math or diagrams, which can carry attacker-chosen attributes or elements), and every link and image
/// address is checked after parsing, so entity, whitespace, control character and mixed-case spellings of <c>javascript:</c> and friends are decided on the
/// decoded value. A link that fails the check is rendered as its plain text; an image that fails is rendered as its alt text. The admin preview and the
/// public page call this one function, so the preview shows exactly what will be published.
/// </summary>
public static class SafeMd
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseAutoIdentifiers()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .UseListExtras()
        .UseSmartyPants()
        .UseReferralLinks("noopener", "nofollow", "ugc")
        .Build();

    /// <summary>The most headings a text may have. Automatic heading identifiers are made unique by trying names in turn, which grows with the square of the number of equal headings.</summary>
    public const int MaxHeadings = 200;

    /// <summary>Why a text cannot be rendered: too many headings, or nested more deeply than the Markdown library allows.</summary>
    public enum Problem { None, TooManyHeadings, TooDeeplyNested }

    // Rendering is a pure function of the text, so the result is kept by the text's hash: no invalidation is ever needed (an edit is a new text), the preview and the public page
    // share it, and an expensive text is rendered once however often it is requested. Bounded by the size of the HTML, not by the number of entries.
    private static readonly MemoryCache Cache = new(new MemoryCacheOptions { SizeLimit = 20_000 });
    private static long _renders;

    /// <summary>How many texts have actually been rendered (a cache hit does not count). For tests and diagnostics.</summary>
    public static long Renders => Interlocked.Read(ref _renders);

    /// <summary>Renders <paramref name="markdown"/>. Never throws: a text that cannot be rendered is shown as its own encoded text in a preformatted block.</summary>
    public static string ToHtml(string? markdown)
    {
        TryRender(markdown, out var html);
        return html;
    }

    /// <summary>Like <see cref="ToHtml"/>, and says whether the text could be rendered as Markdown. When it could not, <paramref name="html"/> is the safe fallback and the result says why.</summary>
    public static Problem TryRender(string? markdown, out string html)
    {
        var text = markdown ?? string.Empty;
        if (text.Length == 0) { html = string.Empty; return Problem.None; }
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
        if (Cache.TryGetValue(key, out (Problem Problem, string Html) hit)) { html = hit.Html; return hit.Problem; }

        var problem = Render(text, out html);
        Cache.Set(key, (problem, html), new MemoryCacheEntryOptions { Size = 1 + html.Length / 1024 });
        return problem;
    }

    private static Problem Render(string text, out string html)
    {
        Interlocked.Increment(ref _renders);
        if (CountHeadings(text) > MaxHeadings) { html = Fallback(text); return Problem.TooManyHeadings; }
        try
        {
            html = RenderMarkdown(text);
            return Problem.None;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Markdig refuses text nested beyond its depth limit (a hundred list levels, a hundred quote marks, a long run of emphasis marks) by throwing.
            html = Fallback(text);
            return Problem.TooDeeplyNested;
        }
    }

    private static string Fallback(string text) => "<pre>" + System.Net.WebUtility.HtmlEncode(text) + "</pre>\n";

    /// <summary>Lines that start a heading (<c>#</c> up to three spaces in, or an underline of = or -). An over-count is harmless: it only has to stay above the limit for a text that is really over it.</summary>
    private static int CountHeadings(string text)
    {
        var count = 0;
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            var t = line.TrimStart(' ');
            if (line.Length - t.Length > 3 || t.Length == 0) continue;
            if (t[0] == '#' || ((t[0] == '=' || t[0] == '-') && IsOnly(t.TrimEnd(), t[0]))) count++;
        }
        return count;
    }

    private static bool IsOnly(ReadOnlySpan<char> s, char c)
    {
        foreach (var x in s) if (x != c) return false;
        return s.Length > 0;
    }

    private static string RenderMarkdown(string text)
    {
        var document = Markdown.Parse(text, Pipeline);
        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            if (IsAllowed(link.Url, link.IsImage)) continue;
            // Keep what the reader would have seen (the link text, or the alt text of an image) and drop the address.
            foreach (var child in link.ToList())
            {
                child.Remove();
                link.InsertBefore(child);
            }
            link.Remove();
        }
        // <scheme:...> is a separate node type from a link, with its own renderer: it gets the same check and falls back to its text.
        foreach (var auto in document.Descendants<AutolinkInline>().ToList())
        {
            if (auto.IsEmail || IsAllowed(auto.Url, isImage: false)) continue;
            auto.ReplaceBy(new LiteralInline(auto.Url));
        }
        return document.ToHtml(Pipeline);
    }

    /// <summary>
    /// True for an address that is relative to the site, or starts with http: or https: (and mailto: for a link). Whitespace and control characters
    /// are ignored the way a browser ignores them inside a scheme, so a spelled-out or padded scheme is read as what it really is.
    /// </summary>
    public static bool IsAllowed(string? url, bool isImage)
    {
        if (string.IsNullOrEmpty(url)) return true;
        var s = new string(url.Where(c => !char.IsControl(c) && !char.IsWhiteSpace(c)).ToArray());
        var end = s.IndexOfAny([':', '/', '?', '#']);
        if (end < 0 || s[end] != ':') return true; // no scheme: a path, query or fragment of this site
        var scheme = s[..end].ToLowerInvariant();
        if (scheme.Length == 0 || !char.IsAsciiLetter(scheme[0]) || !scheme.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.')) return false;
        return scheme is "http" or "https" || (!isImage && scheme == "mailto");
    }
}
