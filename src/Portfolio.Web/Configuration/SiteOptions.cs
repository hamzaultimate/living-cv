namespace Portfolio.Web.Configuration;

/// <summary>
/// The entire public identity of the site, bound from the "Site" config section. This is the
/// single place to personalise a fork: name, role, hero copy, metrics, the About narrative, and
/// contact links all live here so the Razor pages and CV/OG generators stay content-free.
/// All values are public info that is safe to commit.
/// </summary>
public class SiteOptions
{
    // ── Identity ────────────────────────────────────────────────────────────
    /// <summary>Full name, used in titles, the CV, OG image, and schema.org.</summary>
    public string OwnerName { get; set; } = "Your Name";

    /// <summary>Short wordmark shown in the header (a "." accent is appended in the layout).</summary>
    public string BrandName { get; set; } = "Portfolio";

    /// <summary>Role line, e.g. "Senior Software Engineer · .NET / Cloud".</summary>
    public string Role { get; set; } = "";

    /// <summary>Bare job title for schema.org, e.g. "Senior Software Engineer".</summary>
    public string JobTitle { get; set; } = "";

    /// <summary>Canonical public URL of the site, e.g. "https://example.dev".</summary>
    public string Url { get; set; } = "";

    /// <summary>One-paragraph professional summary — used on the CV and as the default meta description.</summary>
    public string Summary { get; set; } = "";

    /// <summary>Topics for the schema.org Person "knowsAbout" list.</summary>
    public List<string> KnowsAbout { get; set; } = new();

    // ── Home page ───────────────────────────────────────────────────────────
    public HeroOptions Hero { get; set; } = new();
    public List<MetricOption> Metrics { get; set; } = new();

    // ── About page ──────────────────────────────────────────────────────────
    public AboutOptions About { get; set; } = new();

    // ── Contact + profile links ─────────────────────────────────────────────
    public string? Phone { get; set; }
    public string? WhatsApp { get; set; } // digits only, country code first, e.g. 15550100
    public string? Email { get; set; }
    public List<string> SecondaryEmails { get; set; } = new();
    public string? LinkedIn { get; set; }
    public string? GitHub { get; set; }
    public string? YouTube { get; set; }

    public bool HasPhone => !string.IsNullOrWhiteSpace(Phone);
    public bool HasWhatsApp => !string.IsNullOrWhiteSpace(WhatsApp);
    public bool HasEmail => !string.IsNullOrWhiteSpace(Email);
    public bool HasLinkedIn => !string.IsNullOrWhiteSpace(LinkedIn);
    public bool HasGitHub => !string.IsNullOrWhiteSpace(GitHub);
    public bool HasYouTube => !string.IsNullOrWhiteSpace(YouTube);

    public string EmailHref => HasEmail ? $"mailto:{Email}" : "#";
    public string PhoneHref => HasPhone ? $"tel:{Phone!.Replace(" ", string.Empty)}" : "#";
    public string WhatsAppHref => HasWhatsApp ? $"https://wa.me/{WhatsApp!.Replace("+", string.Empty).Replace(" ", string.Empty)}" : "#";

    /// <summary>All emails (primary first) for listings.</summary>
    public IEnumerable<string> AllEmails =>
        (HasEmail ? new[] { Email! } : Array.Empty<string>()).Concat(SecondaryEmails.Where(e => !string.IsNullOrWhiteSpace(e)));
}

/// <summary>Hero section copy on the home page.</summary>
public class HeroOptions
{
    public string? Eyebrow { get; set; }
    public string? Headline { get; set; }
    /// <summary>The trailing, accent-coloured part of the headline.</summary>
    public string? HeadlineAccent { get; set; }
    public string? Lede { get; set; }
}

/// <summary>A single headline metric on the home page (e.g. value "~20k", unit "rps", label "throughput").</summary>
public class MetricOption
{
    public string Value { get; set; } = "";
    public string? Unit { get; set; }
    public string? Label { get; set; }
}

/// <summary>Narrative content for the About page.</summary>
public class AboutOptions
{
    public string? Headline { get; set; }
    public List<string> Paragraphs { get; set; } = new();
    public List<ValuePropOption> ValueProps { get; set; } = new();
    public string? CurrentlyNote { get; set; }
    public List<string> Focus { get; set; } = new();
}

/// <summary>A value-proposition card on the About page (icon is chosen by position in the page).</summary>
public class ValuePropOption
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}
