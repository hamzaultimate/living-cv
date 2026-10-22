using Markdig;
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

    public static string ToHtml(string? markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);
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
