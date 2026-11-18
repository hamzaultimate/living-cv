using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Portfolio.Data.Entities;

namespace Portfolio.Web.Services;

/// <summary>The kinds of starting content the wizard offers.</summary>
public static class StarterChoice
{
    public const string Blank = "blank";
    public const string Sample = "sample";

    public static bool IsKnown(string? value) => value is Blank or Sample;
}

/// <summary>
/// What the owner has entered in the onboarding wizard so far, saved after every step as JSON on the profile row. It is
/// never read by the public site: the profile columns stay as they were until the owner publishes. Every value is untrusted
/// text that was validated on the way in and is validated again before publishing.
/// </summary>
public sealed class OnboardingDraft
{
    /// <summary>A stored draft larger than this is ignored; a valid one is a few kilobytes.</summary>
    public const int MaxJsonLength = 16 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip,
    };

    /// <summary>Furthest step whose answers are saved: 0 none, 1 template, 2 basics.</summary>
    public int Step { get; set; }

    public string? TemplateKey { get; set; }
    public string? OwnerName { get; set; }
    public string? JobTitle { get; set; }
    public string? Email { get; set; }
    public string? Location { get; set; }
    public string? Summary { get; set; }

    /// <summary>File name of the draft photo inside the uploads folder's hidden draft area (see <c>UploadStore</c>); null for none.</summary>
    public string? PhotoDraft { get; set; }

    public string? SiteUrl { get; set; }

    /// <summary>Key from the template's accent list; null keeps the template's own colour.</summary>
    public string? AccentKey { get; set; }

    public string Content { get; set; } = StarterChoice.Blank;

    public DateTimeOffset UpdatedAt { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>A copy that can be changed without touching the original (the review page checks a draft with the address as shown).</summary>
    public OnboardingDraft Clone() => (OnboardingDraft)MemberwiseClone();

    /// <summary>The draft stored on the profile, or null when there is none or it cannot be read (treated as no draft).</summary>
    public static OnboardingDraft? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonLength) return null;
        try
        {
            return JsonSerializer.Deserialize<OnboardingDraft>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Rejects markup characters in a value that is shown as a plain name or title.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NoMarkupAttribute : ValidationAttribute
{
    public NoMarkupAttribute() : base("Use plain text only: the characters < and > are not allowed.") { }

    public override bool IsValid(object? value) => value is not string s || (!s.Contains('<') && !s.Contains('>'));
}

/// <summary>
/// The wizard's Basics step as typed. Only the hard rules are bound here (length limits and no markup characters), so any half-finished step can be saved.
/// Whether the answers are complete is a separate question asked at publishing: <see cref="Missing"/> and <see cref="IsComplete"/>.
/// Lengths come from the profile columns, so a value that passes here always fits.
/// </summary>
public class BasicsForm
{
    public const int SummaryMax = 600;

    [StringLength(SiteProfile.MaxLength.OwnerName), NoMarkup, Display(Name = "Name")]
    public string? OwnerName { get; set; }

    [StringLength(SiteProfile.MaxLength.JobTitle), NoMarkup, Display(Name = "Professional title")]
    public string? JobTitle { get; set; }

    [StringLength(300), Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(SiteProfile.MaxLength.Location), NoMarkup, Display(Name = "Location")]
    public string? Location { get; set; }

    [StringLength(SummaryMax), Display(Name = "Short summary")]
    public string? Summary { get; set; }

    /// <summary>The values as stored: trimmed, with line breaks and other control characters turned into spaces.</summary>
    public BasicsForm Cleaned() => new()
    {
        OwnerName = ProfileForm.Line(OwnerName), JobTitle = ProfileForm.Line(JobTitle), Email = ProfileForm.Line(Email),
        Location = ProfileForm.Line(Location), Summary = ProfileForm.Line(Summary),
    };

    /// <summary>True when every one of the five basics is empty (nothing has been typed on the step yet).</summary>
    public static bool AllEmpty(OnboardingDraft d) =>
        new[] { d.OwnerName, d.JobTitle, d.Email, d.Location, d.Summary }.All(string.IsNullOrWhiteSpace);

    /// <summary>The fields that would stop a publish, by lower-case name and in form order: a required one that is empty, an email that is not an address, or text that breaks a hard rule.
    /// Empty for a complete draft. A draft is data like any other, so this is asked again before publishing.</summary>
    public static IReadOnlyList<string> Missing(OnboardingDraft? d)
    {
        var rules = PublishRules.From(d ?? new OnboardingDraft());
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(rules, new ValidationContext(rules), results, validateAllProperties: true);
        return results.SelectMany(r => r.MemberNames).Distinct().Select(Describe).ToList();
    }

    /// <summary>True when the stored values pass the publish rules.</summary>
    public static bool IsComplete(OnboardingDraft d) => Missing(d).Count == 0;

    private static string Describe(string member) => member switch
    {
        nameof(OwnerName) => "name",
        nameof(JobTitle) => "professional title",
        nameof(Email) => "email",
        nameof(Location) => "location",
        _ => "short summary",
    };

    /// <summary>The completeness rules, applied to what a draft holds. Kept apart from the form so a half-typed step can still be saved.</summary>
    private sealed class PublishRules
    {
        [Required, StringLength(SiteProfile.MaxLength.OwnerName), NoMarkup]
        public string? OwnerName { get; init; }

        [Required, StringLength(SiteProfile.MaxLength.JobTitle), NoMarkup]
        public string? JobTitle { get; init; }

        [Required, StringLength(300), EmailAddress]
        public string? Email { get; init; }

        [StringLength(SiteProfile.MaxLength.Location), NoMarkup]
        public string? Location { get; init; }

        [Required, StringLength(SummaryMax)]
        public string? Summary { get; init; }

        public static PublishRules From(OnboardingDraft d) => new()
        {
            OwnerName = d.OwnerName, JobTitle = d.JobTitle, Email = d.Email, Location = d.Location, Summary = d.Summary,
        };
    }
}

/// <summary>The review step's choices.</summary>
public class ReviewForm
{
    [Required(ErrorMessage = "Enter the address your site will be published at."), StringLength(SiteProfile.MaxLength.Url), HttpUrl, Display(Name = "Site address")]
    public string? SiteUrl { get; set; }

    /// <summary>An accent key, or empty for the template's own colour. Checked against the template's list by the service.</summary>
    [StringLength(SiteProfile.MaxLength.AccentColor)]
    public string? Accent { get; set; }

    [Required, StringLength(10)]
    public string Content { get; set; } = StarterChoice.Blank;
}
