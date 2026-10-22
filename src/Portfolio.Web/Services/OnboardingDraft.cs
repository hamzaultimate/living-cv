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

/// <summary>The wizard's Basics step. Lengths come from the profile columns, so a value that validates here always fits.</summary>
public class BasicsForm
{
    public const int SummaryMax = 600;

    [Required(ErrorMessage = "Enter your name."), StringLength(SiteProfile.MaxLength.OwnerName), NoMarkup, Display(Name = "Name")]
    public string? OwnerName { get; set; }

    [Required(ErrorMessage = "Enter your professional title."), StringLength(SiteProfile.MaxLength.JobTitle), NoMarkup, Display(Name = "Professional title")]
    public string? JobTitle { get; set; }

    [Required(ErrorMessage = "Enter an email address."), StringLength(300), EmailAddress(ErrorMessage = "Enter a valid email address."), Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(SiteProfile.MaxLength.Location), NoMarkup, Display(Name = "Location")]
    public string? Location { get; set; }

    [Required(ErrorMessage = "Write a short summary."), StringLength(SummaryMax), Display(Name = "Short summary")]
    public string? Summary { get; set; }

    /// <summary>The values as stored: trimmed, with line breaks and other control characters turned into spaces.</summary>
    public BasicsForm Cleaned() => new()
    {
        OwnerName = ProfileForm.Line(OwnerName), JobTitle = ProfileForm.Line(JobTitle), Email = ProfileForm.Line(Email),
        Location = ProfileForm.Line(Location), Summary = ProfileForm.Line(Summary),
    };

    /// <summary>True when the stored values pass the same rules as the form (used before publishing, since a draft is data like any other).</summary>
    public static bool IsComplete(OnboardingDraft d)
    {
        var form = new BasicsForm { OwnerName = d.OwnerName, JobTitle = d.JobTitle, Email = d.Email, Location = d.Location, Summary = d.Summary };
        return Validator.TryValidateObject(form, new ValidationContext(form), null, validateAllProperties: true);
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
