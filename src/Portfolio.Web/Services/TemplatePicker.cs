namespace Portfolio.Web.Services;

/// <summary>One name in the picker's names box. <see cref="Tag"/> is "In use" or "Selected" for the template the host marks, else null.</summary>
public sealed record PickerEntry(string Key, string Name, string Url, bool IsSelected, string? Tag);

public sealed record PickerGroup(string Profession, IReadOnlyList<PickerEntry> Entries);

/// <summary>A preview tab. <see cref="Path"/> is always one of the pages the host passed in, never a caller's string.</summary>
public sealed record PickerPage(string Path, string Label, string Url, bool IsCurrent);

/// <summary>What the details strip shows, all from the manifest.</summary>
public sealed record PickerDetails(IReadOnlyList<string> Pages, string? ItemLabel, bool HasSample, string? Guidance);

/// <summary>A callout under the preview: a bold heading, a sentence and an optional link.</summary>
public sealed record PickerNote(string Heading, string Text, string? LinkUrl = null, string? LinkText = null);

/// <summary>The one button a host offers for the selected template. It is disabled, with the reason beside it, when <see cref="DisabledReason"/> is set.</summary>
public sealed record PickerAction(string Page, string Key, string Label, string? DisabledReason = null);

public sealed record TemplatePickerInput(
    string BasePath,
    TemplateDefinition Selected,
    IReadOnlyList<(string Path, string Label)> Pages,
    string? Tab,
    string? Size,
    string? MarkedKey,
    string MarkLabel);

public sealed record TemplatePickerModel(
    TemplateDefinition Selected,
    string? SelectedTag,
    IReadOnlyList<PickerGroup> Groups,
    IReadOnlyList<PickerPage> Pages,
    string PreviewPath,
    string PreviewUrl,
    bool Mobile,
    string DesktopUrl,
    string MobileUrl,
    string? ThumbUrl,
    PickerDetails Details)
{
    /// <summary>Set by the host with <c>with { Action = ... }</c>; null means no button (the template is already in use).</summary>
    public PickerAction? Action { get; init; }

    public IReadOnlyList<PickerNote> Notes { get; init; } = [];
}

/// <summary>
/// Builds the model behind the shared template picker. <c>show</c>, <c>tab</c> and <c>size</c> come from the query string and are untrusted:
/// they only ever pick a catalog template, one of the preview pages the host supplied, or one of two widths.
/// </summary>
public static class TemplatePickerBuilder
{
    private const string Desktop = "desktop";
    private const string Mobile = "mobile";

    /// <summary>The template to show: the catalog entry named by <paramref name="show"/>, else <paramref name="fallbackKey"/> (the default template when that is unknown too).</summary>
    public static TemplateDefinition Select(ITemplateCatalog catalog, string? show, string fallbackKey) =>
        catalog.TryGet(show, out var chosen) ? chosen : catalog.Get(fallbackKey);

    /// <summary>The preview tabs for a resolved template: Home, then the menu's pages in the order a visitor sees them.</summary>
    public static IReadOnlyList<(string Path, string Label)> PagesOf(TemplateView view) =>
        [("/", "Home"), .. view.NavLinks(mobile: true).Select(l => (l.Path, l.Path == "/cv" ? "CV" : TemplateView.Cap(l.Label)))];

    public static TemplatePickerModel Build(ITemplateCatalog catalog, TemplatePickerInput input)
    {
        var selected = input.Selected;
        var mobile = string.Equals(input.Size, Mobile, StringComparison.Ordinal);
        var size = mobile ? Mobile : Desktop;
        var previewPath = input.Pages.Select(p => p.Path).FirstOrDefault(p => p == input.Tab) ?? "/";
        var show = Uri.EscapeDataString(selected.Key);

        string Link(string path, string width) => $"{input.BasePath}?show={show}&tab={Uri.EscapeDataString(path)}&size={width}";

        var groups = catalog.All
            .GroupBy(t => t.Profession, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PickerGroup(g.Key, g.Select(t => new PickerEntry(
                    t.Key,
                    t.Name,
                    $"{input.BasePath}?show={Uri.EscapeDataString(t.Key)}&size={size}",
                    t.Key == selected.Key,
                    t.Key == input.MarkedKey ? input.MarkLabel : null)).ToList()))
            .ToList();

        var pages = input.Pages.Select(p => new PickerPage(p.Path, p.Label, $"{input.BasePath}?show={show}&tab={Uri.EscapeDataString(p.Path)}&size={size}", p.Path == previewPath)).ToList();

        var details = new PickerDetails(
            selected.Sections.Where(s => s.Enabled && s.Key != SectionRegistry.Contact).Select(s => s.Label).Append("contact").ToList(),
            selected.Sections.FirstOrDefault(s => s.Key == SectionRegistry.Projects && s.Enabled)?.ItemLabel,
            StarterContent.Offers(selected),
            selected.Guidance);

        return new TemplatePickerModel(
            selected,
            selected.Key == input.MarkedKey ? input.MarkLabel : null,
            groups,
            pages,
            previewPath,
            $"{previewPath}?{RequestTemplate.PreviewParameter}={show}",
            mobile,
            Link(previewPath, Desktop),
            Link(previewPath, Mobile),
            catalog.GetPreviewImagePath(selected.Key) is not null ? $"/admin/welcome/thumb/{show}" : null,
            details);
    }
}
