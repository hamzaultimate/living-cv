using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Transfer;

/// <summary>
/// Every cap of the export and import in one place. Text lengths are not repeated here: an imported value is held to the same limit as the editor
/// that writes the same field (see <see cref="ImportRules"/>). These bound the file itself, so a hostile or mistaken file cannot make the
/// server read, hold or write an unreasonable amount.
/// </summary>
public static class TransferLimits
{
    /// <summary>The version of the file format this build writes and reads. A file with another version is refused whole.</summary>
    public const int SchemaVersion = 1;

    /// <summary>The marker every file carries, so a JSON file that is something else is told apart from an export.</summary>
    public const string Format = "living-cv-export";

    /// <summary>Largest file accepted: enforced by the body limit of the page, again by the read, and by the file's declared length.</summary>
    public const int MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>The body limit of the import post: the file plus the multipart boundaries and the other form fields.</summary>
    public const long MaxRequestBytes = MaxFileBytes + BodyLimitAttribute.MultipartOverhead;

    /// <summary>Deepest nesting of JSON the parser accepts (the real format needs 5).</summary>
    public const int MaxJsonDepth = 32;

    public const int MaxExperience = 100;
    public const int MaxSkills = 500;
    public const int MaxProjects = 200;
    public const int MaxCertifications = 200;
    public const int MaxTalks = 300;
    public const int MaxTestimonials = 200;
    public const int MaxPosts = 300;

    /// <summary>Longest list of image files the manifest may carry. The manifest is informational on import: only its size is checked.</summary>
    public const int MaxUploadEntries = 5000;

    /// <summary>Most settings the profile's section list may carry (the template's own sections are fewer; the rest are inert).</summary>
    public const int MaxSectionSettings = 40;

    /// <summary>How many warnings and per-row notes a preview lists before it says how many more there are.</summary>
    public const int MaxListedNotes = 200;

    /// <summary>How long a preview can be confirmed.</summary>
    public static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(10);

    /// <summary>How many previews are held at once, across sessions. A newer one pushes the oldest out.</summary>
    public const int MaxHeldPreviews = 4;

    /// <summary>The most rows the file may carry for <paramref name="section"/> (<see cref="SectionRegistry"/>).</summary>
    public static int MaxRows(string section) => section switch
    {
        SectionRegistry.Experience => MaxExperience,
        SectionRegistry.Skills => MaxSkills,
        SectionRegistry.Projects => MaxProjects,
        SectionRegistry.Certifications => MaxCertifications,
        SectionRegistry.Talks => MaxTalks,
        SectionRegistry.Testimonials => MaxTestimonials,
        SectionRegistry.Blog => MaxPosts,
        _ => 0,
    };
}
