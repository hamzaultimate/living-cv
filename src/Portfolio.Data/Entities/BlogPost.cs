namespace Portfolio.Data.Entities;

/// <summary>A blog post. Body is authored/seeded as Markdown and rendered at display time.</summary>
public class BlogPost : IStampedContent
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public DateOnly Date { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool Draft { get; set; }
    public string? Cover { get; set; }
    public string BodyMarkdown { get; set; } = string.Empty;

    /// <summary>Describes <see cref="Cover"/> for people who cannot see it. Null for a cover that was seeded without a description: the title is used then.</summary>
    public string? CoverAlt { get; set; }

    /// <summary>Changes on every admin write; the concurrency token (see <see cref="IStampedContent"/>).</summary>
    public string? ConcurrencyStamp { get; set; }

    /// <summary>Column maximum lengths, shared by the model configuration and the editor.</summary>
    public static class MaxLength
    {
        public const int Title = 200;
        public const int Slug = 120;
        public const int CoverAlt = 300;
    }
}
