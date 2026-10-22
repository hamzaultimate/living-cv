using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Portfolio.Data.Entities;

namespace Portfolio.Web.Services;

/// <summary>Accepts an empty value or an absolute http(s) URL; anything else (javascript:, data:, relative) is refused because these values become links.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HttpUrlAttribute : ValidationAttribute
{
    public HttpUrlAttribute() : base("Enter a full web address starting with http:// or https://.") { }

    public override bool IsValid(object? value) =>
        value is null or "" || (value is string s
            && Uri.TryCreate(s.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && !string.IsNullOrEmpty(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo));
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class PatternOrEmptyAttribute : ValidationAttribute
{
    private readonly Regex _pattern;

    public PatternOrEmptyAttribute(string pattern, string message) : base(message) =>
        _pattern = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public override bool IsValid(object? value) =>
        value is null or "" || (value is string s && _pattern.IsMatch(s.Trim()));
}

/// <summary>Everything the profile editor changes. Template, theme and section settings are not editable here.</summary>
public class ProfileForm : IValidatableObject
{
    public const int MetricRows = 8;
    public const int ValuePropRows = 6;
    public const int MaxParagraphs = 10;
    public const int MaxListItems = 30;

    // Limits for values stored in unbounded columns or JSON.
    public const int SummaryMax = 2000;
    public const int HeroMax = 300;
    public const int LedeMax = 800;
    public const int ParagraphMax = 2000;
    public const int ItemMax = 120;
    public const int ContactMax = 300;

    [Required, StringLength(SiteProfile.MaxLength.OwnerName)] public string OwnerName { get; set; } = "";
    [Required, StringLength(SiteProfile.MaxLength.BrandName)] public string BrandName { get; set; } = "";
    [StringLength(SiteProfile.MaxLength.Role)] public string? Role { get; set; }
    [StringLength(SiteProfile.MaxLength.JobTitle)] public string? JobTitle { get; set; }
    [StringLength(SiteProfile.MaxLength.Location)] public string? Location { get; set; }

    [Required, StringLength(SiteProfile.MaxLength.Url), HttpUrl] public string Url { get; set; } = "";

    [StringLength(SiteProfile.MaxLength.GoogleAnalyticsId),
     PatternOrEmpty("^G-[A-Za-z0-9]{4,20}$", "Use a GA4 measurement ID such as G-XXXXXXXXXX, or leave it empty.")]
    public string? GoogleAnalyticsId { get; set; }

    [StringLength(SummaryMax)] public string? Summary { get; set; }
    [StringLength(MaxListItems * (ItemMax + 2))] public string? KnowsAboutText { get; set; }

    [StringLength(HeroMax)] public string? HeroEyebrow { get; set; }
    [StringLength(HeroMax)] public string? HeroHeadline { get; set; }
    [StringLength(HeroMax)] public string? HeroHeadlineAccent { get; set; }
    [StringLength(LedeMax)] public string? HeroLede { get; set; }

    public List<MetricRow> Metrics { get; set; } = new();

    [StringLength(HeroMax)] public string? AboutHeadline { get; set; }
    [StringLength(MaxParagraphs * (ParagraphMax + 4))] public string? AboutParagraphsText { get; set; }
    public List<ValuePropRow> ValueProps { get; set; } = new();
    [StringLength(HeroMax)] public string? AboutCurrentlyNote { get; set; }
    [StringLength(MaxListItems * (ItemMax + 2))] public string? AboutFocusText { get; set; }

    [StringLength(40), PatternOrEmpty(@"^[0-9+()\-. ]{3,40}$", "Use digits, spaces and + ( ) - . only.")] public string? Phone { get; set; }
    [StringLength(20), PatternOrEmpty(@"^\+?[0-9]{6,20}$", "Digits only, country code first (for example 15550100).")] public string? WhatsApp { get; set; }
    [StringLength(ContactMax), EmailAddress] public string? Email { get; set; }
    [StringLength(5 * (ContactMax + 2))] public string? SecondaryEmailsText { get; set; }
    [StringLength(ContactMax), HttpUrl] public string? LinkedIn { get; set; }
    [StringLength(ContactMax), HttpUrl] public string? GitHub { get; set; }
    [StringLength(ContactMax), HttpUrl] public string? YouTube { get; set; }

    public class MetricRow
    {
        [StringLength(20)] public string? Value { get; set; }
        [StringLength(20)] public string? Unit { get; set; }
        [StringLength(80)] public string? Label { get; set; }
        public bool IsBlank => string.IsNullOrWhiteSpace(Value) && string.IsNullOrWhiteSpace(Unit) && string.IsNullOrWhiteSpace(Label);
    }

    public class ValuePropRow
    {
        [StringLength(120)] public string? Title { get; set; }
        [StringLength(500)] public string? Body { get; set; }
        public bool IsBlank => string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Body);
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Metrics.Count > MetricRows)
            yield return new ValidationResult($"At most {MetricRows} metrics.", [nameof(Metrics)]);
        if (ValueProps.Count > ValuePropRows)
            yield return new ValidationResult($"At most {ValuePropRows} value cards.", [nameof(ValueProps)]);
        for (var i = 0; i < Metrics.Count; i++)
            if (!Metrics[i].IsBlank && string.IsNullOrWhiteSpace(Metrics[i].Value))
                yield return new ValidationResult("A metric needs a value.", [$"{nameof(Metrics)}[{i}].{nameof(MetricRow.Value)}"]);
        for (var i = 0; i < ValueProps.Count; i++)
            if (!ValueProps[i].IsBlank && (string.IsNullOrWhiteSpace(ValueProps[i].Title) || string.IsNullOrWhiteSpace(ValueProps[i].Body)))
                yield return new ValidationResult("A value card needs a title and a body.", [$"{nameof(ValueProps)}[{i}].{nameof(ValuePropRow.Title)}"]);

        foreach (var (name, text) in new[] { (nameof(KnowsAboutText), KnowsAboutText), (nameof(AboutFocusText), AboutFocusText) })
        {
            var lines = Lines(text);
            if (lines.Count > MaxListItems) yield return new ValidationResult($"At most {MaxListItems} lines.", [name]);
            else if (lines.Any(l => l.Length > ItemMax)) yield return new ValidationResult($"Each line can be at most {ItemMax} characters.", [name]);
        }

        var paragraphs = Paragraphs(AboutParagraphsText);
        if (paragraphs.Count > MaxParagraphs)
            yield return new ValidationResult($"At most {MaxParagraphs} paragraphs.", [nameof(AboutParagraphsText)]);
        else if (paragraphs.Any(p => p.Length > ParagraphMax))
            yield return new ValidationResult($"Each paragraph can be at most {ParagraphMax} characters.", [nameof(AboutParagraphsText)]);

        var secondary = Lines(SecondaryEmailsText);
        if (secondary.Count > 5 || secondary.Any(e => e.Length > ContactMax || !new EmailAddressAttribute().IsValid(e)))
            yield return new ValidationResult("Up to 5 valid email addresses, one per line.", [nameof(SecondaryEmailsText)]);
    }

    /// <summary>Fills the empty rows the form shows so the owner can add entries without extra buttons.</summary>
    public void PadRows()
    {
        while (Metrics.Count < MetricRows) Metrics.Add(new MetricRow());
        while (ValueProps.Count < ValuePropRows) ValueProps.Add(new ValuePropRow());
    }

    public static ProfileForm From(SiteProfile p)
    {
        var form = new ProfileForm
        {
            OwnerName = p.OwnerName, BrandName = p.BrandName, Role = p.Role, JobTitle = p.JobTitle, Location = p.Location, Url = p.Url,
            GoogleAnalyticsId = p.GoogleAnalyticsId, Summary = p.Summary,
            KnowsAboutText = string.Join("\n", p.KnowsAbout),
            HeroEyebrow = p.Hero.Eyebrow, HeroHeadline = p.Hero.Headline, HeroHeadlineAccent = p.Hero.HeadlineAccent, HeroLede = p.Hero.Lede,
            Metrics = p.Metrics.Select(m => new MetricRow { Value = m.Value, Unit = m.Unit, Label = m.Label }).ToList(),
            AboutHeadline = p.About.Headline,
            AboutParagraphsText = string.Join("\n\n", p.About.Paragraphs),
            ValueProps = p.About.ValueProps.Select(v => new ValuePropRow { Title = v.Title, Body = v.Body }).ToList(),
            AboutCurrentlyNote = p.About.CurrentlyNote,
            AboutFocusText = string.Join("\n", p.About.Focus),
            Phone = p.Phone, WhatsApp = p.WhatsApp, Email = p.Email,
            SecondaryEmailsText = string.Join("\n", p.SecondaryEmails),
            LinkedIn = p.LinkedIn, GitHub = p.GitHub, YouTube = p.YouTube,
        };
        form.PadRows();
        return form;
    }

    /// <summary>Copies the validated form onto the stored profile. Photo, template, theme and section settings are left alone.</summary>
    public void ApplyTo(SiteProfile p)
    {
        p.OwnerName = Line(OwnerName)!;
        p.BrandName = Line(BrandName)!;
        p.Role = Line(Role) ?? "";
        p.JobTitle = Line(JobTitle) ?? "";
        p.Location = Line(Location);
        p.Url = Line(Url)!;
        p.GoogleAnalyticsId = Line(GoogleAnalyticsId)?.ToUpperInvariant();
        p.Summary = Line(Summary) ?? "";
        p.KnowsAbout = Lines(KnowsAboutText);
        p.Hero = new ProfileHero { Eyebrow = Line(HeroEyebrow), Headline = Line(HeroHeadline), HeadlineAccent = Line(HeroHeadlineAccent), Lede = Line(HeroLede) };
        p.Metrics = Metrics.Where(m => !m.IsBlank)
            .Select(m => new ProfileMetric { Value = Line(m.Value)!, Unit = Line(m.Unit), Label = Line(m.Label) }).ToList();
        p.About = new ProfileAbout
        {
            Headline = Line(AboutHeadline),
            Paragraphs = Paragraphs(AboutParagraphsText),
            ValueProps = ValueProps.Where(v => !v.IsBlank).Select(v => new ProfileValueProp { Title = Line(v.Title)!, Body = Line(v.Body)! }).ToList(),
            CurrentlyNote = Line(AboutCurrentlyNote),
            Focus = Lines(AboutFocusText),
        };
        p.Phone = Line(Phone);
        p.WhatsApp = Line(WhatsApp);
        p.Email = Line(Email);
        p.SecondaryEmails = Lines(SecondaryEmailsText);
        p.LinkedIn = Line(LinkedIn);
        p.GitHub = Line(GitHub);
        p.YouTube = Line(YouTube);
    }

    /// <summary>A single-line value: trimmed, with line breaks and other control characters turned into spaces. Empty becomes null.</summary>
    internal static string? Line(string? value)
    {
        if (value is null) return null;
        var cleaned = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    internal static List<string> Lines(string? text) =>
        (text ?? "").Split('\n').Select(Line).Where(l => l is not null).Select(l => l!).ToList();

    private static List<string> Paragraphs(string? text) =>
        Regex.Split((text ?? "").Replace("\r\n", "\n"), @"\n\s*\n")
            .Select(p => Regex.Replace(p.Trim(), @"[\u0000-\u0009\u000B-\u001F\u007F]", " ").Trim())
            .Where(p => p.Length > 0).ToList();
}
