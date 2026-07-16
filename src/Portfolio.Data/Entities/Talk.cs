namespace Portfolio.Data.Entities;

/// <summary>A talk or video (YouTube embeds land here later).</summary>
public class Talk
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public TalkType Type { get; set; } = TalkType.Video;
    public string? YoutubeId { get; set; }
    public string? Event { get; set; }
    public DateOnly? Date { get; set; }
    public string? Url { get; set; }
    public int SortOrder { get; set; }
}
