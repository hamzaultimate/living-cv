namespace Portfolio.Data.Entities;

/// <summary>A blog post. Body is authored/seeded as Markdown and rendered at display time.</summary>
public class BlogPost
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
}
