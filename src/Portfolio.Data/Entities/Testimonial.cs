namespace Portfolio.Data.Entities;

/// <summary>A testimonial/recommendation — e.g. from LinkedIn, a colleague, or a client.</summary>
public class Testimonial
{
    public int Id { get; set; }
    public required string Quote { get; set; }
    public required string Author { get; set; }

    /// <summary>Author's role/company line, e.g. "Engineering Manager, Acme Corp".</summary>
    public string? AuthorTitle { get; set; }

    /// <summary>Optional link to the source (e.g. the LinkedIn recommendation).</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Optional relationship context, e.g. "Managed the author directly".</summary>
    public string? Relation { get; set; }

    public bool Featured { get; set; }
    public int SortOrder { get; set; }
}
