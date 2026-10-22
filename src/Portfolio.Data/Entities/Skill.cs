namespace Portfolio.Data.Entities;

/// <summary>A single skill, grouped by category (Backend, Cloud/DevOps, AI, ...).</summary>
public class Skill : IOrderedContent
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Group { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Changes on every admin write; the concurrency token (see <see cref="IOrderedContent"/>).</summary>
    public string? ConcurrencyStamp { get; set; }
}
