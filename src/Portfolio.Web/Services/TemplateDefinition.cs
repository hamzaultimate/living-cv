using System.Text.Json.Serialization;
using Portfolio.Web.Configuration;

namespace Portfolio.Web.Services;

/// <summary>
/// A profession template loaded from <c>Templates/&lt;key&gt;/template.json</c>: the profession-specific section list,
/// copy, ordering and theme that shared pages and services read instead of hardcoding. Unknown properties in a manifest
/// are rejected (see <see cref="TemplateCatalog"/>), so a typo cannot silently do nothing.
/// </summary>
public class TemplateDefinition
{
    /// <summary>Lets editors validate the manifest against <c>Templates/template.schema.json</c>; not read by the app.</summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    /// <summary>Lowercase kebab-case, equal to the folder name, immutable once released.</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Profession { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Layout archetype: one of <see cref="Archetypes.Implemented"/>.</summary>
    public string Archetype { get; set; } = Archetypes.Showcase;

    /// <summary>True for professions where published content can harm third parties (patients, clients); requires <see cref="Guidance"/>.</summary>
    public bool Sensitive { get; set; }

    /// <summary>A note shown to the owner when picking this template, e.g. never to publish patient-identifiable information.</summary>
    public string? Guidance { get; set; }

    public TemplateTheme Theme { get; set; } = new();

    /// <summary>Default order of the public navigation and sections. A section left out is not part of the template (hidden).</summary>
    public List<TemplateSection> Sections { get; set; } = new();

    /// <summary>Canonical ordering for skill groups (CV and Skills page); groups not listed sort last.</summary>
    public List<string> SkillGroups { get; set; } = new();

    public TemplateSeo Seo { get; set; } = new();

    public TemplateCopy Copy { get; set; } = new();

    /// <summary>Optional sample content, a path relative to the template folder.</summary>
    public TemplateStarterRef? Starter { get; set; }
}

/// <summary>The closed list of layout archetypes.</summary>
public static class Archetypes
{
    public const string Showcase = "showcase";
    public const string Credentials = "credentials";

    /// <summary>Archetypes the plan names (showcase, gallery, results, credentials).</summary>
    public static readonly IReadOnlyList<string> Known = [Showcase, "gallery", "results", Credentials];

    /// <summary>Archetypes that have layout partials under <c>Pages/Shared/Layouts/&lt;archetype&gt;</c>. A manifest naming any other is skipped.</summary>
    public static readonly IReadOnlyList<string> Implemented = [Showcase, Credentials];
}

public class TemplateTheme
{
    /// <summary>Value of the <c>data-template</c> attribute on the page, matching <c>[data-template="..."]</c> blocks in <c>Styles/themes</c>.
    /// Empty means the base look defined on <c>:root</c>, with no attribute.</summary>
    public string? Default { get; set; }

    /// <summary>Browser UI colour (meta theme-color). Empty keeps the base value.</summary>
    public string? ThemeColor { get; set; }
}

/// <summary>One entry of the template's section list. Order is the default navigation and home order.</summary>
public class TemplateSection
{
    /// <summary>One of <see cref="SectionRegistry.Keys"/>.</summary>
    public string Key { get; set; } = "";

    /// <summary>Navigation label (and the word used in "back to ..." links).</summary>
    public string Label { get; set; } = "";

    /// <summary>Shorter label for the desktop navigation; defaults to <see cref="Label"/>.</summary>
    public string? ShortLabel { get; set; }

    /// <summary>What one entry is called, e.g. "Case study" or "Initiative".</summary>
    public string? ItemLabel { get; set; }

    public bool Enabled { get; set; } = true;
}

/// <summary>SEO copy patterns. Placeholders: {name}, {title}, {summary}.</summary>
public class TemplateSeo
{
    /// <summary>Home page title (the full literal title, not suffixed).</summary>
    public string Title { get; set; } = "{name} — {title}";
    public string Description { get; set; } = "{summary}";
    public string AboutDescription { get; set; } = "{summary}";

    /// <summary>When true the home page lists the enabled certifications as credentials in its schema.org Person markup.</summary>
    public bool CredentialsJsonLd { get; set; }

    public static string Apply(string pattern, SiteOptions site) =>
        pattern
            .Replace("{name}", site.OwnerName)
            .Replace("{title}", site.JobTitle)
            .Replace("{summary}", site.Summary);

    /// <summary>Longest meta description search engines show before cutting it themselves.</summary>
    public const int MaxDescriptionLength = 160;

    /// <summary><see cref="Apply"/> for a meta description: a long name or title can push the expanded pattern past
    /// <see cref="MaxDescriptionLength"/>, so it is cut at a word boundary and ends with an ellipsis instead of mid-word.</summary>
    public static string ApplyDescription(string pattern, SiteOptions site)
    {
        var text = Apply(pattern, site).Trim();
        if (text.Length <= MaxDescriptionLength) return text;
        var cut = text[..(MaxDescriptionLength - 1)];
        var space = cut.LastIndexOf(' ');
        if (space > MaxDescriptionLength / 2) cut = cut[..space];
        return cut.TrimEnd(' ', ',', ';', ':', '-', '—', '.') + "…";
    }
}

public class TemplateStarterRef
{
    public string File { get; set; } = "";
}

/// <summary>Marks a copy string that may be left empty in a manifest.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OptionalCopyAttribute : Attribute;

/// <summary>
/// Every piece of profession-specific wording the shared pages show. There are no defaults on purpose: a template that
/// forgets a string fails validation instead of silently showing another profession's words. Strings are plain text;
/// the pages escape them (see <see cref="TemplateView.Text"/>). Description patterns accept {name}, {title}, {summary}.
/// </summary>
public class TemplateCopy
{
    public LayoutCopy Layout { get; set; } = new();
    public HomeCopy Home { get; set; } = new();
    public ProjectsCopy Projects { get; set; } = new();
    public ExperienceCopy Experience { get; set; } = new();
    public SkillsCopy Skills { get; set; } = new();
    public CertificationsCopy Certifications { get; set; } = new();
    public BlogCopy Blog { get; set; } = new();
    public TalksCopy Talks { get; set; } = new();
    public ContactCopy Contact { get; set; } = new();
    public CvCopy Cv { get; set; } = new();
}

public class LayoutCopy
{
    /// <summary>Small print in the footer; empty hides it.</summary>
    [OptionalCopy] public string FooterNote { get; set; } = "";
}

public class HomeCopy
{
    public string WorkEyebrow { get; set; } = "";
    public string WorkHeading { get; set; } = "";
    public string WorkEmptyTitle { get; set; } = "";
    public string WorkEmptyBody { get; set; } = "";
    public string TestimonialsEyebrow { get; set; } = "";
    public string TestimonialsHeading { get; set; } = "";
    public string ContactHeading { get; set; } = "";
    public string ExperienceEyebrow { get; set; } = "";
    public string ExperienceHeading { get; set; } = "";
    public string CredentialsEyebrow { get; set; } = "";
    public string CredentialsHeading { get; set; } = "";
    public string SkillsHeading { get; set; } = "";
}

public class ProjectsCopy
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Eyebrow { get; set; } = "";
    public string Heading { get; set; } = "";
    public string Lead { get; set; } = "";
    public string EmptyTitle { get; set; } = "";
    public string EmptyBody { get; set; } = "";
    public string FilterLabel { get; set; } = "";
    public string FeaturedBadge { get; set; } = "";
    public string ProblemHeading { get; set; } = "";
    public string ApproachHeading { get; set; } = "";
    public string NoWriteup { get; set; } = "";
    public string StackHeading { get; set; } = "";
    public string LinksHeading { get; set; } = "";
    public string ProofHeading { get; set; } = "";
    public string ScreenshotsHeading { get; set; } = "";

    /// <summary>Alt text of a project image that has no caption of its own; <c>{title}</c> is the project title.</summary>
    public string ImageAlt { get; set; } = "";

    /// <summary>Accessible name of the image gallery on a project page.</summary>
    public string GalleryLabel { get; set; } = "";

    /// <summary>Accessible name of the button that opens an image full size; a caption is appended after a colon.</summary>
    public string OpenImageLabel { get; set; } = "";

    /// <summary><see cref="ImageAlt"/> for one project.</summary>
    public string AltFor(string title) => ImageAlt.Replace("{title}", title);
}

public class ExperienceCopy
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Eyebrow { get; set; } = "";
    public string Heading { get; set; } = "";
    public string Lead { get; set; } = "";
    public string Empty { get; set; } = "";
}

public class SkillsCopy
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Eyebrow { get; set; } = "";
    public string Heading { get; set; } = "";
    public string Lead { get; set; } = "";
    public string Empty { get; set; } = "";
}

public class CertificationsCopy
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Eyebrow { get; set; } = "";
    public string Heading { get; set; } = "";
    public string Lead { get; set; } = "";
    public string EmptyTitle { get; set; } = "";
    public string EmptyBody { get; set; } = "";
    public string FilterLabel { get; set; } = "";
    public string StatusEarned { get; set; } = "";
    public string StatusInProgress { get; set; } = "";
    public string StatusPlanned { get; set; } = "";
    public string ViewCertificate { get; set; } = "";
    public string ViewCredential { get; set; } = "";
}

public class BlogCopy
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Eyebrow { get; set; } = "";
    public string Heading { get; set; } = "";
    public string Lead { get; set; } = "";
    public string Empty { get; set; } = "";
}

public class TalksCopy
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Eyebrow { get; set; } = "";
    public string Heading { get; set; } = "";
    public string Empty { get; set; } = "";
    public string Watch { get; set; } = "";
}

public class ContactCopy
{
    public string Description { get; set; } = "";
    public string Heading { get; set; } = "";
    public string Lead { get; set; } = "";
}

public class CvCopy
{
    public string Description { get; set; } = "";
    public string ExperienceHeading { get; set; } = "";
    public string SkillsHeading { get; set; } = "";
    public string ProjectsHeading { get; set; } = "";
    public string CertificationsHeading { get; set; } = "";
}
