namespace Portfolio.Data.Entities;

/// <summary>A role in the reverse-chronological work history.</summary>
public class Experience
{
    public int Id { get; set; }
    public required string Company { get; set; }
    public required string Title { get; set; }
    public required string Start { get; set; }   // free text, e.g. "2023" or "Mar 2023"
    public string? End { get; set; }             // null when current
    public bool Current { get; set; }
    public required string Summary { get; set; }
    public List<string> Highlights { get; set; } = new();
    public List<string> Tech { get; set; } = new();
    public int SortOrder { get; set; }
}
