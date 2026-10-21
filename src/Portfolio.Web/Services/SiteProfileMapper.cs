using Portfolio.Data.Entities;
using Portfolio.Web.Configuration;

namespace Portfolio.Web.Services;

/// <summary>Maps between the database profile and the <see cref="SiteOptions"/> shape the pages consume.</summary>
public static class SiteProfileMapper
{
    public static SiteOptions ToOptions(SiteProfile p) => new()
    {
        OwnerName = p.OwnerName,
        BrandName = p.BrandName,
        Role = p.Role,
        JobTitle = p.JobTitle,
        Url = p.Url,
        GoogleAnalyticsId = p.GoogleAnalyticsId,
        Summary = p.Summary,
        KnowsAbout = p.KnowsAbout.ToList(),
        PhotoUrl = p.PhotoUrl,
        PhotoUrlLight = p.PhotoUrlLight,
        Hero = new HeroOptions
        {
            Eyebrow = p.Hero.Eyebrow,
            Headline = p.Hero.Headline,
            HeadlineAccent = p.Hero.HeadlineAccent,
            Lede = p.Hero.Lede,
        },
        Metrics = p.Metrics.Select(m => new MetricOption { Value = m.Value, Unit = m.Unit, Label = m.Label }).ToList(),
        About = new AboutOptions
        {
            Headline = p.About.Headline,
            Paragraphs = p.About.Paragraphs.ToList(),
            ValueProps = p.About.ValueProps.Select(v => new ValuePropOption { Title = v.Title, Body = v.Body }).ToList(),
            CurrentlyNote = p.About.CurrentlyNote,
            Focus = p.About.Focus.ToList(),
        },
        Phone = p.Phone,
        WhatsApp = p.WhatsApp,
        Email = p.Email,
        SecondaryEmails = p.SecondaryEmails.ToList(),
        LinkedIn = p.LinkedIn,
        GitHub = p.GitHub,
        YouTube = p.YouTube,
        TemplateKey = p.TemplateKey,
        ThemeKey = p.ThemeKey,
        AccentColor = p.AccentColor,
        Sections = p.Sections.Select(s => new SectionOption { Key = s.Key, Label = s.Label, Enabled = s.Enabled, SortOrder = s.SortOrder }).ToList(),
    };

    public static SiteProfile ToEntity(SiteOptions o)
    {
        var p = new SiteProfile();
        CopyTo(o, p);
        return p;
    }

    /// <summary>Overwrites every profile field from <paramref name="o"/>.</summary>
    public static void CopyTo(SiteOptions o, SiteProfile p)
    {
        p.OwnerName = o.OwnerName;
        p.BrandName = o.BrandName;
        p.Role = o.Role;
        p.JobTitle = o.JobTitle;
        p.Url = o.Url;
        p.GoogleAnalyticsId = o.GoogleAnalyticsId;
        p.Summary = o.Summary;
        p.KnowsAbout = o.KnowsAbout.ToList();
        p.PhotoUrl = o.PhotoUrl;
        p.PhotoUrlLight = o.PhotoUrlLight;
        p.Hero = new ProfileHero
        {
            Eyebrow = o.Hero.Eyebrow,
            Headline = o.Hero.Headline,
            HeadlineAccent = o.Hero.HeadlineAccent,
            Lede = o.Hero.Lede,
        };
        p.Metrics = o.Metrics.Select(m => new ProfileMetric { Value = m.Value, Unit = m.Unit, Label = m.Label }).ToList();
        p.About = new ProfileAbout
        {
            Headline = o.About.Headline,
            Paragraphs = o.About.Paragraphs.ToList(),
            ValueProps = o.About.ValueProps.Select(v => new ProfileValueProp { Title = v.Title, Body = v.Body }).ToList(),
            CurrentlyNote = o.About.CurrentlyNote,
            Focus = o.About.Focus.ToList(),
        };
        p.Phone = o.Phone;
        p.WhatsApp = o.WhatsApp;
        p.Email = o.Email;
        p.SecondaryEmails = o.SecondaryEmails.ToList();
        p.LinkedIn = o.LinkedIn;
        p.GitHub = o.GitHub;
        p.YouTube = o.YouTube;
        p.TemplateKey = o.TemplateKey;
        p.ThemeKey = o.ThemeKey;
        p.AccentColor = o.AccentColor;
        p.Sections = o.Sections.Select(s => new SectionSetting { Key = s.Key, Label = s.Label, Enabled = s.Enabled, SortOrder = s.SortOrder }).ToList();
    }
}
