namespace Portfolio.Data.Entities;

/// <summary>
/// The site's public identity: one row, always <see cref="SingletonId"/>. Seeded from the
/// "Site" config section on first run and owned by the database afterwards.
/// </summary>
public class SiteProfile
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string OwnerName { get; set; } = "Your Name";
    public string BrandName { get; set; } = "Portfolio";
    public string Role { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Url { get; set; } = "";
    public string? GoogleAnalyticsId { get; set; }
    public string Summary { get; set; } = "";
    public List<string> KnowsAbout { get; set; } = new();

    public string? PhotoUrl { get; set; }
    public string? PhotoUrlLight { get; set; }

    public ProfileHero Hero { get; set; } = new();
    public List<ProfileMetric> Metrics { get; set; } = new();
    public ProfileAbout About { get; set; } = new();

    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public string? Email { get; set; }
    public List<string> SecondaryEmails { get; set; } = new();
    public string? LinkedIn { get; set; }
    public string? GitHub { get; set; }
    public string? YouTube { get; set; }

    public string TemplateKey { get; set; } = "software";
    public string? ThemeKey { get; set; }
    public string? AccentColor { get; set; }

    /// <summary>Per-section settings (label, visibility, order). Stored as JSON.</summary>
    public List<SectionSetting> Sections { get; set; } = new();
}

public class ProfileHero
{
    public string? Eyebrow { get; set; }
    public string? Headline { get; set; }
    public string? HeadlineAccent { get; set; }
    public string? Lede { get; set; }
}

public class ProfileMetric
{
    public string Value { get; set; } = "";
    public string? Unit { get; set; }
    public string? Label { get; set; }
}

public class ProfileAbout
{
    public string? Headline { get; set; }
    public List<string> Paragraphs { get; set; } = new();
    public List<ProfileValueProp> ValueProps { get; set; } = new();
    public string? CurrentlyNote { get; set; }
    public List<string> Focus { get; set; } = new();
}

public class ProfileValueProp
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}

/// <summary>A section's owner overrides. Stored as JSON on the profile.</summary>
public class SectionSetting
{
    public required string Key { get; set; }
    public string? Label { get; set; }
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
}
