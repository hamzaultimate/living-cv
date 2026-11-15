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

/// <summary>Everything the identity page changes: who the owner is and how to reach them. The home page introduction, the metrics and the About page have their
/// own editors (<c>/admin/profile/hero</c>, <c>metrics</c>, <c>about</c>) and are never touched from here. Photo, template, theme and section settings are not editable here either.</summary>
public class ProfileForm : IValidatableObject
{
    public const int MaxListItems = 30;

    // Limits for values stored in unbounded columns or JSON.
    public const int SummaryMax = 2000;
    public const int ItemMax = 120;
    public const int ContactMax = 300;

    [Required, StringLength(SiteProfile.MaxLength.OwnerName)] public string OwnerName { get; set; } = "";
    [Required, StringLength(SiteProfile.MaxLength.BrandName)] public string BrandName { get; set; } = "";
    [StringLength(SiteProfile.MaxLength.Role)] public string? Role { get; set; }
    [StringLength(SiteProfile.MaxLength.JobTitle)] public string? JobTitle { get; set; }
    [StringLength(SiteProfile.MaxLength.Location)] public string? Location { get; set; }

    /// <summary>The stamp of the profile when the form was loaded; the save is refused if the profile was written since.</summary>
    [StringLength(SiteProfile.MaxLength.ConcurrencyStamp)] public string? Stamp { get; set; }

    [Required, StringLength(SiteProfile.MaxLength.Url), HttpUrl] public string Url { get; set; } = "";

    [StringLength(SiteProfile.MaxLength.GoogleAnalyticsId),
     PatternOrEmpty("^G-[A-Za-z0-9]{4,20}$", "Use a GA4 measurement ID such as G-XXXXXXXXXX, or leave it empty.")]
    public string? GoogleAnalyticsId { get; set; }

    [StringLength(SummaryMax)] public string? Summary { get; set; }
    [StringLength(MaxListItems * (ItemMax + 2))] public string? KnowsAboutText { get; set; }

    [StringLength(40), PatternOrEmpty(@"^[0-9+()\-. ]{3,40}$", "Use digits, spaces and + ( ) - . only.")] public string? Phone { get; set; }
    [StringLength(20), PatternOrEmpty(@"^\+?[0-9]{6,20}$", "Digits only, country code first (for example 15550100).")] public string? WhatsApp { get; set; }
    [StringLength(ContactMax), EmailAddress] public string? Email { get; set; }
    [StringLength(5 * (ContactMax + 2))] public string? SecondaryEmailsText { get; set; }
    [StringLength(ContactMax), HttpUrl] public string? LinkedIn { get; set; }
    [StringLength(ContactMax), HttpUrl] public string? GitHub { get; set; }
    [StringLength(ContactMax), HttpUrl] public string? YouTube { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        var topics = Lines(KnowsAboutText);
        if (topics.Count > MaxListItems) yield return new ValidationResult($"At most {MaxListItems} lines.", [nameof(KnowsAboutText)]);
        else if (topics.Any(l => l.Length > ItemMax)) yield return new ValidationResult($"Each line can be at most {ItemMax} characters.", [nameof(KnowsAboutText)]);

        var secondary = Lines(SecondaryEmailsText);
        if (secondary.Count > 5 || secondary.Any(e => e.Length > ContactMax || !new EmailAddressAttribute().IsValid(e)))
            yield return new ValidationResult("Up to 5 valid email addresses, one per line.", [nameof(SecondaryEmailsText)]);
    }

    public static ProfileForm From(SiteProfile p)
    {
        return new ProfileForm
        {
            OwnerName = p.OwnerName, BrandName = p.BrandName, Role = p.Role, JobTitle = p.JobTitle, Location = p.Location, Url = p.Url,
            GoogleAnalyticsId = p.GoogleAnalyticsId, Summary = p.Summary,
            KnowsAboutText = string.Join("\n", p.KnowsAbout), Stamp = p.ConcurrencyStamp,
            Phone = p.Phone, WhatsApp = p.WhatsApp, Email = p.Email,
            SecondaryEmailsText = string.Join("\n", p.SecondaryEmails),
            LinkedIn = p.LinkedIn, GitHub = p.GitHub, YouTube = p.YouTube,
        };
    }

    /// <summary>Copies the validated form onto the stored profile. The hero, metrics and About text, photo, template, theme and section settings are left alone.</summary>
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
}
