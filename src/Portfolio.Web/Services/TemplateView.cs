using Microsoft.AspNetCore.Html;
using Portfolio.Web.Configuration;

namespace Portfolio.Web.Services;

/// <summary>A template section after the owner's overrides are applied: what the navigation, sitemap and routes use.</summary>
public sealed record ResolvedSection(string Key, string Label, string ShortLabel, string? ItemLabel, bool Enabled)
{
    public string? Path => SectionRegistry.PathFor(Key);
}

/// <summary>
/// The template in effect for one render: the definition picked by the profile (or by an owner preview), its sections after
/// the owner's overrides, and helpers pages use to read copy. Pages read everything profession-specific from here.
/// </summary>
public sealed class TemplateView
{
    private readonly Dictionary<string, ResolvedSection> _sections;

    public TemplateView(SiteOptions site, TemplateDefinition definition, IReadOnlyList<ResolvedSection> sections, bool isPreview)
    {
        Site = site;
        Definition = definition;
        Sections = sections;
        IsPreview = isPreview;
        _sections = sections.ToDictionary(s => s.Key, StringComparer.Ordinal);
    }

    public SiteOptions Site { get; }
    public TemplateDefinition Definition { get; }

    /// <summary>The template's sections in navigation order, enabled or not.</summary>
    public IReadOnlyList<ResolvedSection> Sections { get; }

    /// <summary>True when an owner is previewing a template that is not saved yet.</summary>
    public bool IsPreview { get; }

    public string Key => Definition.Key;
    public string Archetype => Definition.Archetype;
    public TemplateCopy Copy => Definition.Copy;

    /// <summary>Value for the page's <c>data-template</c> attribute, or null for the base look.</summary>
    public string? ThemeName => string.IsNullOrWhiteSpace(Definition.Theme.Default) ? null : Definition.Theme.Default;

    public string ThemeColor => string.IsNullOrWhiteSpace(Definition.Theme.ThemeColor) ? "#0f172a" : Definition.Theme.ThemeColor;

    /// <summary>The section, or a disabled placeholder when the template does not have it.</summary>
    public ResolvedSection Section(string key) =>
        _sections.TryGetValue(key, out var s) ? s : new ResolvedSection(key, key, key, null, false);

    public bool IsEnabled(string key) => Section(key).Enabled;

    /// <summary>Navigation entries (path, label) in section order: enabled sections that own a page, then the CV; the contact page
    /// comes last and only in the mobile menu, where the desktop header has its own button.</summary>
    public IReadOnlyList<(string Path, string Label)> NavLinks(bool mobile)
    {
        var links = Sections
            .Where(s => s.Enabled && s.Path is not null && s.Key != SectionRegistry.Contact)
            .Select(s => (s.Path!, mobile ? s.Label : s.ShortLabel))
            .ToList();
        links.Add(("/cv", "cv"));
        if (mobile && IsEnabled(SectionRegistry.Contact)) links.Add(("/contact", Section(SectionRegistry.Contact).Label));
        return links;
    }

    /// <summary>Plain template text as markup. Only &amp; &lt; &gt; and double quotes are escaped, which is all text and
    /// double-quoted attributes need; apostrophes and typographic characters stay readable instead of becoming entities.</summary>
    public static IHtmlContent Text(string? value) =>
        new HtmlString(string.IsNullOrEmpty(value) ? "" : value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;"));

    /// <summary>The first letter upper-cased, for headings built from a lower-case navigation label.</summary>
    public static string Cap(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}

/// <summary>Builds <see cref="TemplateView"/>s from the profile and the catalog. Stateless and shared.</summary>
public class TemplateResolver
{
    private readonly ITemplateCatalog _catalog;

    public TemplateResolver(ITemplateCatalog catalog) => _catalog = catalog;

    /// <summary>The view for <paramref name="site"/>'s saved template, or for <paramref name="previewKey"/> when it names a catalog template.
    /// An unknown saved key falls back to the default template.</summary>
    public TemplateView Resolve(SiteOptions site, string? previewKey = null)
    {
        var preview = previewKey is not null && _catalog.TryGet(previewKey, out _);
        var definition = preview ? _catalog.Get(previewKey) : _catalog.Get(site.TemplateKey);

        // The owner's per-section settings sit on top of the template: a custom label, or switching a section off.
        // They cannot enable a section the template does not have, so a stale setting never revives hidden content.
        var overrides = site.Sections.GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        var sections = definition.Sections.Select(s =>
        {
            overrides.TryGetValue(s.Key, out var custom);
            var label = string.IsNullOrWhiteSpace(custom?.Label) ? s.Label : custom!.Label!.Trim();
            var shortLabel = string.IsNullOrWhiteSpace(custom?.Label) ? s.ShortLabel ?? s.Label : label;
            return new ResolvedSection(s.Key, label, shortLabel, s.ItemLabel, s.Enabled && custom?.Enabled != false);
        }).ToList();

        return new TemplateView(site, definition, sections, preview);
    }
}
