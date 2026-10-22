namespace Portfolio.Data.Entities;

/// <summary>A talk or video (YouTube embeds land here later).</summary>
public class Talk : IOrderedContent
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public TalkType Type { get; set; } = TalkType.Video;
    public string? YoutubeId { get; set; }
    public string? Event { get; set; }
    public DateOnly? Date { get; set; }
    public string? Url { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Changes on every admin write; the concurrency token (see <see cref="IOrderedContent"/>).</summary>
    public string? ConcurrencyStamp { get; set; }
}
