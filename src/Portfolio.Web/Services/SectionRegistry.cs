namespace Portfolio.Web.Services;

/// <summary>
/// The closed list of sections a template can relabel, reorder or disable, and the public route each one owns.
/// A template cannot invent a section: it can only choose from these.
/// </summary>
public static class SectionRegistry
{
    public const string About = "about";
    public const string Experience = "experience";
    public const string Projects = "projects";
    public const string Skills = "skills";
    public const string Certifications = "certifications";
    public const string Blog = "blog";
    public const string Talks = "talks";
    public const string Testimonials = "testimonials";
    public const string Contact = "contact";

    public static readonly IReadOnlyList<string> Keys =
        [About, Experience, Projects, Skills, Certifications, Blog, Talks, Testimonials, Contact];

    public static bool IsKnown(string? key) => key is not null && Keys.Contains(key, StringComparer.Ordinal);

    /// <summary>The listing route of a section, or null when it only appears as a block on the home page.</summary>
    public static string? PathFor(string key) => key switch
    {
        About => "/about",
        Experience => "/experience",
        Projects => "/projects",
        Skills => "/skills",
        Certifications => "/certifications",
        Blog => "/blog",
        Talks => "/talks",
        Contact => "/contact",
        _ => null,
    };
}
