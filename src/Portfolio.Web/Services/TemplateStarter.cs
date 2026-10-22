using System.Text.Json;
using System.Text.Json.Serialization;
using Portfolio.Data.Entities;
using Portfolio.Web.Configuration;

namespace Portfolio.Web.Services;

/// <summary>
/// A template's optional sample content: a profile plus rows in the same shape as the seed files. It is always fictional
/// (<see cref="Fictional"/> must be true) and is only ever applied on request, never to an existing site.
/// </summary>
public class TemplateStarter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Must be true: starter content never describes a real person, employer or credential.</summary>
    public bool Fictional { get; set; }

    public SiteOptions Profile { get; set; } = new();
    public List<Experience> Experiences { get; set; } = new();
    public List<Skill> Skills { get; set; } = new();
    public List<Project> Projects { get; set; } = new();
    public List<Certification> Certifications { get; set; } = new();
    public List<Testimonial> Testimonials { get; set; } = new();

    public static TemplateStarter Parse(string json) =>
        JsonSerializer.Deserialize<TemplateStarter>(json, Json) ?? throw new InvalidDataException("Starter file is empty.");
}
