namespace Portfolio.Data.Entities;

/// <summary>A single skill, grouped by category (Backend, Cloud/DevOps, AI, ...).</summary>
public class Skill
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Group { get; set; }
    public int SortOrder { get; set; }
}
