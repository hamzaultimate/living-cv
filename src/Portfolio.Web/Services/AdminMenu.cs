namespace Portfolio.Web.Services;

/// <summary>One entry of the admin menu. A parent (Profile, Content) has <see cref="Children"/>; only a leaf is ever the current page.</summary>
public sealed record AdminMenuItem(string Key, string Label, string Url, string Icon, int? Count, bool Hidden, bool IsCurrent, IReadOnlyList<AdminMenuItem> Children)
{
    /// <summary>True for the current page and for a parent whose sub-menu holds it, so the sub-menu is drawn open.</summary>
    public bool ContainsCurrent => IsCurrent || Children.Any(c => c.IsCurrent);
}

public sealed record AdminMenuGroup(string? Label, IReadOnlyList<AdminMenuItem> Items);

public sealed record AdminMenuModel(IReadOnlyList<AdminMenuGroup> Groups)
{
    /// <summary>The items a page can be: a parent's children, or the item itself when it has none.</summary>
    public IEnumerable<AdminMenuItem> Leaves =>
        Groups.SelectMany(g => g.Items).SelectMany(i => i.Children.Count > 0 ? i.Children : new[] { i });

    public AdminMenuItem? Current => Leaves.FirstOrDefault(l => l.IsCurrent);

    public AdminMenuItem? ParentOf(AdminMenuItem leaf) =>
        Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Children.Any(c => c.Key == leaf.Key));
}

/// <summary>One step of the breadcrumb. A null <see cref="Url"/> is the page being shown.</summary>
public sealed record AdminCrumb(string Label, string? Url);

/// <summary>
/// Builds the admin menu from the template in effect and the request path. It returns a model, not markup: the view component draws it.
/// Section names come from the template's wording (<see cref="EditorView"/>); with no template (it could not be read) the neutral defaults of
/// <see cref="EditorFields"/> are used so a page never fails because of its menu.
/// </summary>
public sealed class AdminMenu
{
    /// <summary>The cookie the menu script writes. The server only ever compares it with <see cref="CollapsedValue"/>.</summary>
    public const string CollapsedCookie = "adm-menu";
    public const string CollapsedValue = "collapsed";

    private const string Dashboard = "/admin";
    private const string WizardUrl = "/admin/welcome";

    /// <summary>Wizard pages always draw the rail, whatever the cookie says, so the wizard keeps its room.</summary>
    public static bool IsRailPage(string path) => Covers(WizardUrl, path);

    public static bool IsCollapsed(string path, string? cookie) =>
        IsRailPage(path) || string.Equals(cookie, CollapsedValue, StringComparison.Ordinal);

    public AdminMenuModel Build(TemplateView? template, string path, IReadOnlyDictionary<string, int>? counts)
    {
        var content = new List<AdminMenuItem> { Item("content.overview", "Overview", "/admin/content", "") };
        foreach (var section in EditorFields.Sections)
        {
            var (label, enabled) = SectionName(template, section);
            int? count = counts is not null && counts.TryGetValue(section, out var n) ? n : null;
            content.Add(Item("content." + section, label, "/admin/content/" + section, "", count, hidden: !enabled));
        }

        AdminMenuGroup[] groups =
        [
            new(null, [Item("dashboard", "Dashboard", Dashboard, "dashboard")]),
            new("Your site",
            [
                Item("profile", "Profile", "/admin/profile", "profile", children:
                [
                    Item("profile.basics", "Basics", "/admin/profile", ""),
                    Item("profile.hero", PartLabel(template, ProfileParts.Hero), "/admin/profile/hero", ""),
                    Item("profile.metrics", PartLabel(template, ProfileParts.Metrics), "/admin/profile/metrics", ""),
                    Item("profile.about", PartLabel(template, ProfileParts.About), "/admin/profile/about", ""),
                    Item("profile.photo", "Photo", "/admin/photo", ""),
                ]),
                Item("content", "Content", "/admin/content", "content", children: content),
            ]),
            new("Appearance",
            [
                Item("template", "Template", "/admin/template", "template"),
                Item("sections", PartLabel(template, ProfileParts.Sections), "/admin/sections", "sections"),
            ]),
            new("Tools",
            [
                Item("wizard", "Setup wizard", WizardUrl, "wizard"),
                Item("transfer", PartLabel(template, ProfileParts.Transfer), "/admin/transfer", "transfer"),
            ]),
        ];

        var leaves = groups.SelectMany(g => g.Items).SelectMany(i => i.Children.Count > 0 ? i.Children : new[] { i });
        var current = leaves.Where(l => Covers(l.Url, path)).OrderByDescending(l => l.Url.Length).FirstOrDefault()?.Key;
        return new AdminMenuModel(groups.Select(g => g with { Items = g.Items.Select(i => Mark(i, current)).ToList() }).ToList());
    }

    /// <summary>
    /// Admin / [parent] / [menu item] / [page]. The page is the last crumb a page passes (<c>ViewData["Crumb"]</c>) or its title; it only appears
    /// below a menu item (an edit form under Projects), where the menu item becomes a link back.
    /// </summary>
    public static IReadOnlyList<AdminCrumb> Breadcrumb(AdminMenuModel menu, string path, string? title, string? crumb)
    {
        var last = string.IsNullOrWhiteSpace(crumb) ? title : crumb;
        if (menu.Current is not { } current)
            return string.IsNullOrWhiteSpace(last) ? [new("Admin", "/admin")] : [new("Admin", "/admin"), new(last, null)];
        if (current.Key == "dashboard") return [new("Admin", null)];

        var crumbs = new List<AdminCrumb> { new("Admin", "/admin") };
        if (menu.ParentOf(current) is { } parent) crumbs.Add(new(parent.Label, parent.Url));
        var deeper = !Normalise(path).Equals(current.Url, StringComparison.OrdinalIgnoreCase);
        if (deeper && !string.IsNullOrWhiteSpace(last) && !last.Equals(current.Label, StringComparison.OrdinalIgnoreCase))
        {
            crumbs.Add(new(current.Label, current.Url));
            crumbs.Add(new(last, null));
        }
        else
        {
            crumbs.Add(new(current.Label, null));
        }
        return crumbs;
    }

    private static AdminMenuItem Item(string key, string label, string url, string icon, int? count = null, bool hidden = false, IReadOnlyList<AdminMenuItem>? children = null) =>
        new(key, label, url, icon, count, hidden, false, children ?? []);

    private static AdminMenuItem Mark(AdminMenuItem item, string? current) =>
        item with { IsCurrent = current is not null && item.Children.Count == 0 && item.Key == current, Children = item.Children.Select(c => Mark(c, current)).ToList() };

    private static string Normalise(string path) => path.Length > 1 ? path.TrimEnd('/') : path;

    /// <summary>True when <paramref name="path"/> is the address or lies below it on a segment boundary. The dashboard only matches itself.</summary>
    private static bool Covers(string url, string path)
    {
        path = Normalise(path);
        return path.Equals(url, StringComparison.OrdinalIgnoreCase)
               || (url != Dashboard && path.StartsWith(url + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static string PartLabel(TemplateView? template, string part) =>
        template is null ? EditorFields.For(part).First(f => f.Key == "heading").Label : new EditorView(template, part).Heading;

    private static (string Label, bool Enabled) SectionName(TemplateView? template, string section)
    {
        if (template is null) return (TemplateView.Cap(section), true);
        var view = new EditorView(template, section);
        return (view.SectionLabel, view.SectionEnabled);
    }
}
