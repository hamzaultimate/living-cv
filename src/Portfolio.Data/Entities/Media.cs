namespace Portfolio.Data.Entities;

/// <summary>Helpers for classifying project media (image vs. video) by file extension.</summary>
public static class Media
{
    private static readonly string[] VideoExt = { ".mp4", ".webm", ".ogg", ".ogv", ".mov", ".m4v" };

    /// <summary>True when the url points at a video file we can play inline.</summary>
    public static bool IsVideo(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var path = url.Split('?', '#')[0];
        var dot = path.LastIndexOf('.');
        if (dot < 0) return false;
        var ext = path[dot..].ToLowerInvariant();
        return Array.IndexOf(VideoExt, ext) >= 0;
    }
}
