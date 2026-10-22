namespace Portfolio.Data.Entities;

/// <summary>A project case study (problem → approach → impact).</summary>
public class Project : IOrderedContent
{
    /// <summary>Column maximum lengths, shared by the model configuration and the admin form.</summary>
    public static class MaxLength
    {
        public const int Title = 200;
        public const int Slug = 120;
        public const int HeroImage = 400;
        public const int HeroImageAlt = 300;
        public const int Category = 80;
    }

    public int Id { get; set; }
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public string? Role { get; set; }
    public string? Org { get; set; }
    public string? Period { get; set; }
    public required string Summary { get; set; }

    /// <summary>Domain/industry used for the Work-page filter (e.g. "Distributed Systems", "IoT / Embedded").</summary>
    public string? Category { get; set; }

    public string? Problem { get; set; }
    public string? Approach { get; set; }

    /// <summary>Card/hero thumbnail (e.g. "/img/projects/sharp.png"). Optional — cards fall back to text-only.</summary>
    public string? HeroImage { get; set; }

    /// <summary>Alt text for <see cref="HeroImage"/>, written by the admin. Null for older rows; pages then use the project title.</summary>
    public string? HeroImageAlt { get; set; }

    /// <summary>Gallery screenshots shown on the case-study page and opened in a lightbox. Optional.</summary>
    public List<ProjectImage> Screenshots { get; set; } = new();

    /// <summary>Impact metrics as short strings (e.g. "1.8M users served in &lt;50s").</summary>
    public List<string> Impact { get; set; } = new();
    public List<string> Tech { get; set; } = new();
    public List<Link> Links { get; set; } = new();
    public List<Proof> Proofs { get; set; } = new();

    public bool Featured { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Changes on every admin write; the concurrency token (see <see cref="IOrderedContent"/>).</summary>
    public string? ConcurrencyStamp { get; set; }
}
