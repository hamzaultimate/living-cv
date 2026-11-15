using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Portfolio.Data.Entities;

namespace Portfolio.Web.Transfer;

// The shape of the export file (schema version 1). Property order is the order in the file. Every field is written, null included, so a file says what
// each field holds and an import of it restores a field that was cleared since. No concurrency stamp, list stamp or database id is ever written.

public sealed record ExportFile(
    int SchemaVersion, string Format, DateTimeOffset ExportedAt, ExportProfile Profile, ExportContent Content, IReadOnlyList<ExportUpload> Uploads);

public sealed record ExportProfile(
    string OwnerName, string BrandName, string Role, string JobTitle, string Url, string? GoogleAnalyticsId, string Summary, string? Location, IReadOnlyList<string> KnowsAbout,
    string? PhotoUrl, string? PhotoUrlLight, string? Phone, string? WhatsApp, string? Email, IReadOnlyList<string> SecondaryEmails, string? LinkedIn, string? GitHub, string? YouTube,
    string TemplateKey, string? AccentColor, ExportHero Hero, IReadOnlyList<ExportMetric> Metrics, ExportAbout About, IReadOnlyList<ExportSection> Sections);

public sealed record ExportHero(string? Eyebrow, string? Headline, string? HeadlineAccent, string? Lede);

public sealed record ExportMetric(string Value, string? Unit, string? Label);

public sealed record ExportAbout(string? Headline, IReadOnlyList<string> Paragraphs, IReadOnlyList<ExportCard> ValueProps, string? CurrentlyNote, IReadOnlyList<string> Focus);

public sealed record ExportCard(string Title, string Body);

/// <summary>A stored section setting, including those for keys the current template does not list.</summary>
public sealed record ExportSection(string Key, string? Label, bool Enabled, bool? Shown, int SortOrder);

public sealed record ExportContent(
    IReadOnlyList<ExportRole> Experience, IReadOnlyList<ExportSkill> Skills, IReadOnlyList<ExportProject> Projects, IReadOnlyList<ExportCertification> Certifications,
    IReadOnlyList<ExportTalk> Talks, IReadOnlyList<ExportTestimonial> Testimonials, IReadOnlyList<ExportPost> Blog);

public sealed record ExportRole(string Company, string Title, string Start, string? End, bool Current, string Summary, IReadOnlyList<string> Highlights, IReadOnlyList<string> Tech);

public sealed record ExportSkill(string Name, string Group);

public sealed record ExportLink(string Label, string Url);

public sealed record ExportImage(string Url, string? Caption);

public sealed record ExportProof(string Label, string Url, ProofKind Kind);

public sealed record ExportProject(
    string Title, string Slug, string? Role, string? Org, string? Period, string Summary, string? Category, string? Problem, string? Approach,
    IReadOnlyList<string> Impact, IReadOnlyList<string> Tech, IReadOnlyList<ExportLink> Links, string? HeroImage, string? HeroImageAlt,
    IReadOnlyList<ExportImage> Screenshots, IReadOnlyList<ExportProof> Proofs, bool Featured);

public sealed record ExportCertification(
    string Name, string Issuer, CertificationStatus Status, string? Date, string? Category, string? CredentialUrl, string? CertificateUrl, string? BadgeImageUrl,
    IReadOnlyList<ExportProof> Proofs);

public sealed record ExportTalk(string Title, TalkType Type, string? Event, DateOnly? Date, string? YoutubeId, string? Url);

public sealed record ExportTestimonial(string Quote, string Author, string? AuthorTitle, string? SourceUrl, string? Relation, bool Featured);

public sealed record ExportPost(
    string Title, string Slug, string? Description, DateOnly Date, IReadOnlyList<string> Tags, bool Draft, string? Cover, string? CoverAlt, string Body);

/// <summary>An uploaded image file the data refers to. The files themselves are not in the export.</summary>
public sealed record ExportUpload(string Path, bool Exists, IReadOnlyList<ExportUse> UsedBy);

/// <summary>Where an uploaded file is used: the table ("profile", "projects", "blog"), the entry (its address name, empty for the profile) and the field.</summary>
public sealed record ExportUse(string Table, string? Entry, string Field);

/// <summary>Reads and writes the export file's JSON.</summary>
public static class ExportJson
{
    /// <summary>
    /// Indented, camelCase, enums as their names (<c>Earned</c>, <c>InProgress</c>, as the seed files write them), and not escaping characters that JSON does not need escaped (so Markdown, quotes and non-Latin text stay readable).
    /// The relaxed escaping is safe because the file is only ever sent as a download with <c>nosniff</c> and is never embedded in a page.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public static byte[] Serialize(ExportFile file) => JsonSerializer.SerializeToUtf8Bytes(file, Options);
}
