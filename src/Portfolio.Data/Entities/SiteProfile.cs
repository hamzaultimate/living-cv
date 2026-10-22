namespace Portfolio.Data.Entities;

/// <summary>
/// The site's public identity: one row, always <see cref="SingletonId"/>. Seeded from the
/// "Site" config section on first run and owned by the database afterwards.
/// </summary>
public class SiteProfile
{
    /// <summary>Column maximum lengths, shared by the model configuration and anything that writes the row.</summary>
    public static class MaxLength
    {
        public const int OwnerName = 200;
        public const int BrandName = 80;
        public const int Role = 300;
        public const int JobTitle = 200;
        public const int Url = 300;
        public const int GoogleAnalyticsId = 40;
        public const int PhotoUrl = 400;
        public const int PhotoUrlLight = 400;
        public const int TemplateKey = 60;
        public const int ThemeKey = 60;
        public const int AccentColor = 40;
        public const int Location = 120;
    }

    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string OwnerName { get; set; } = "Your Name";
    public string BrandName { get; set; } = "Portfolio";
    public string Role { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Url { get; set; } = "";
    public string? GoogleAnalyticsId { get; set; }
    public string Summary { get; set; } = "";

    /// <summary>Where the owner is based, as free text (for example a city). Null when not given.</summary>
    public string? Location { get; set; }
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
    /// <summary>Key of one of the template's accent colours (<c>theme.accents</c> in its manifest), not a CSS value. Null keeps the template's own colour.</summary>
    public string? AccentColor { get; set; }

    /// <summary>Per-section settings (label, visibility, order). Stored as JSON.</summary>
    public List<SectionSetting> Sections { get; set; } = new();

    /// <summary>
    /// When the onboarding wizard was published or skipped. Null means the owner has not finished (or skipped) it yet. Rows that
    /// existed before the wizard was added are given a value by the migration, so deployed sites are never sent into it.
    /// Also the concurrency token for publishing, so two overlapping publishes cannot both apply.
    /// </summary>
    public DateTimeOffset? OnboardingCompletedAt { get; set; }

    /// <summary>The wizard's unpublished answers as JSON (see the web project's OnboardingDraft). Never read by the public site.</summary>
    public string? OnboardingDraft { get; set; }
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
