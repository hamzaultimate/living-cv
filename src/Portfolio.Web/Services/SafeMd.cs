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
    private static MarkdownPipelineBuilder Configure(bool identifiers)
    {
        var builder = new MarkdownPipelineBuilder().DisableHtml().UsePipeTables();
        // Automatic heading identifiers are made unique by trying names in turn, which grows with the square of the number of equal headings.
        // The counting pipeline leaves them out, so counting is cheap however many headings there are.
        if (identifiers) builder.UseAutoIdentifiers();
        return builder.UseAutoLinks().UseEmphasisExtras().UseListExtras().UseSmartyPants().UseReferralLinks("noopener", "nofollow", "ugc");
    }

    private static readonly MarkdownPipeline Pipeline = Configure(identifiers: true).Build();
    private static readonly MarkdownPipeline CountingPipeline = Configure(identifiers: false).Build();

    /// <summary>The most headings a text may have, wherever they sit (top level, in a quote, in a list item).</summary>
    public const int MaxHeadings = 200;

    /// <summary>
    /// The most <c>[</c> characters a text may have (links, images, references). The library's link parser is quadratic in the number of unclosed openers
    /// (measured at about 60 ms for 2,000 and 3 s for 8,000 of <c>[a](</c>, more with the parse used for counting); 1,000 keeps the worst case near 100 ms per parse and is far
    /// above any real post (the shipped ones have none; an article with a hundred links is already long).
    /// </summary>
    public const int MaxBrackets = 1000;

    /// <summary>Why a text cannot be rendered: too many headings or brackets, nested more deeply than the Markdown library allows, or the library refused it for another reason.</summary>
    public enum Problem { None, TooManyHeadings, TooDeeplyNested, CannotRender, TooManyBrackets }

    /// <summary>Diagnostics for tests: the counting parse must not use the quadratic identifier extension, the real one must.</summary>
    public static bool CountingUsesIdentifiers => CountingPipeline.Extensions.Any(e => e is Markdig.Extensions.AutoIdentifiers.AutoIdentifierExtension);
    public static bool RenderingUsesIdentifiers => Pipeline.Extensions.Any(e => e is Markdig.Extensions.AutoIdentifiers.AutoIdentifierExtension);

    // Rendering is a pure function of the text, so the result is kept by the text's hash: no invalidation is ever needed (an edit is a new text), the preview and the public page
    // share it, and an expensive text is rendered once however often it is requested. Bounded by size: one unit is about 1,024 characters of HTML (about 2 KB), so the limit
    // of 20,000 units is a ceiling of roughly 40 MB.
    private static readonly MemoryCache Cache = new(new MemoryCacheOptions { SizeLimit = 20_000 });

    /// <summary>Renders <paramref name="markdown"/>. Never throws: a text that cannot be rendered is shown as its own encoded text in a preformatted block.</summary>
    public static string ToHtml(string? markdown)
    {
        TryRender(markdown, out var html);
        return html;
    }

    /// <summary>
    /// Like <see cref="ToHtml"/>, and says whether the text could be rendered as Markdown. When it could not, <paramref name="html"/> is the safe fallback and the result says why.
    /// <paramref name="remember"/> false (an unsaved preview) still uses a result that is already kept but adds none, so previews cannot push published posts out of the cache.
    /// </summary>
    public static Problem TryRender(string? markdown, out string html, bool remember = true)
    {
        var text = markdown ?? string.Empty;
        if (text.Length == 0) { html = string.Empty; return Problem.None; }
        var key = KeyOf(text);
        if (Cache.TryGetValue(key, out (Problem Problem, string Html) hit)) { html = hit.Html; return hit.Problem; }

        var problem = Render(text, out html);
        if (remember) Cache.Set(key, (problem, html), new MemoryCacheEntryOptions { Size = 1 + html.Length * 2 / 1024 });
        return problem;
    }

    // The hash covers the UTF-16 code units, not a UTF-8 encoding: a lone surrogate would encode as U+FFFD and collide with a text that really contains it.
    private static string KeyOf(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(text.AsSpan())));

    /// <summary>True when the rendering of exactly this text is kept (a diagnostic for tests).</summary>
    public static bool IsKept(string? markdown) => !string.IsNullOrEmpty(markdown) && Cache.TryGetValue(KeyOf(markdown), out _);

    private static Problem Render(string text, out string html)
    {
        // Cheapest check first, before any parse: the bound on link openers is what keeps the parse below from becoming slow.
        if (text.AsSpan().Count('[') > MaxBrackets) { html = Fallback(text); return Problem.TooManyBrackets; }
        try
        {
            // Count first with the cheap pipeline; this parse can also hit the library's depth limit, which is mapped like a failure in the real one.
            var counting = Markdown.Parse(text, CountingPipeline);
            if (counting.Descendants<HeadingBlock>().Count() > MaxHeadings) { html = Fallback(text); return Problem.TooManyHeadings; }
            html = RenderMarkdown(text);
            return Problem.None;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            html = Fallback(text);
            // The library refuses text nested beyond its depth limit (a hundred list levels, a hundred quote marks, a long run of emphasis marks) by throwing.
            return Classify(ex);
        }
    }

    /// <summary>Only the library's depth limit (an ArgumentException that says the text is too deeply nested) is called nesting; any other failure is neutral.</summary>
    public static Problem Classify(Exception ex) =>
        ex is ArgumentException && ex.Message.Contains("nested", StringComparison.OrdinalIgnoreCase) ? Problem.TooDeeplyNested : Problem.CannotRender;

    private static string Fallback(string text) => "<pre>" + System.Net.WebUtility.HtmlEncode(text) + "</pre>\n";

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
