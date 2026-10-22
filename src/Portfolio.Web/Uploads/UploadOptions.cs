namespace Portfolio.Web.Uploads;

/// <summary>Bound from the "Uploads" config section.</summary>
public class UploadOptions
{
    public const long DefaultMaxBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Folder that holds uploaded files. Relative paths are resolved against the content root. Empty means
    /// <c>App_Data/uploads</c> under the content root: outside wwwroot, never served by the static asset
    /// handler, and not part of what a normal publish replaces. For hosts that clear the site folder on deploy,
    /// point this at a folder outside it.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>Largest accepted upload, in bytes.</summary>
    public long MaxBytes { get; set; } = DefaultMaxBytes;
}
