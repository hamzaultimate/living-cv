using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Portfolio.Data.Entities;

namespace Portfolio.Web.Services;

/// <summary>
/// Finds the page-level partials an archetype provides under <c>Pages/Shared/Layouts/&lt;archetype&gt;</c>. The home page always has one;
/// the work pages are optional, so an archetype that does not provide them keeps the shared implementation and its output is untouched.
/// </summary>
public static class LayoutPartials
{
    public const string Home = "_Home";
    public const string Projects = "_Projects";
    public const string ProjectDetail = "_ProjectDetail";

    /// <summary>True for an address a plain link can carry: a site-relative path or an http(s) address. Anything else (a script scheme, a protocol-relative address) is never put in a link.</summary>
    public static bool IsLinkable(string? url) =>
        !string.IsNullOrWhiteSpace(url) && ((url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal))
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

    /// <summary>The view name of the archetype's partial, or null when the archetype has none. The archetype comes from a validated manifest
    /// (one of <see cref="Archetypes.Known"/>), and anything else is refused before it is used as part of a view name.</summary>
    public static string? Find(ICompositeViewEngine views, ViewContext context, string archetype, string partial)
    {
        if (!Archetypes.Implemented.Contains(archetype, StringComparer.Ordinal)) return null;
        var name = $"Layouts/{archetype}/{partial}";
        return views.FindView(context, name, isMainPage: false).Success ? name : null;
    }
}

/// <summary>What the work listing hands to an archetype's <c>_Projects</c> partial: the projects in public order and the breadcrumb markup the page built.</summary>
public sealed record ProjectsPage(IReadOnlyList<Project> Projects, string BreadcrumbJsonLd);

/// <summary>What the case-study page hands to an archetype's <c>_ProjectDetail</c> partial: the project and the structured data the page built.</summary>
public sealed record ProjectPage(Project Project, string BreadcrumbJsonLd, string ProjectJsonLd);

/// <summary>One card of the gallery work grid: the project, whether to mark it as featured, and the heading level that fits where the grid sits (2 under a page title, 3 under a section heading).</summary>
public sealed record WorkCard(Project Project, bool ShowFeatured = false, int HeadingLevel = 3);

/// <summary>A project's id, address and title, for links between case studies.</summary>
public sealed record ProjectLink(int Id, string Slug, string Title);
