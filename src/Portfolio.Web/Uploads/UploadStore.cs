using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Portfolio.Web.Uploads;

/// <summary>
/// Stores processed uploads in one folder and maps them to public <c>/uploads/…</c> URLs. Names are random and carry
/// an extension chosen by the server, so a client can influence neither where a file lands nor what it is called.
/// Any URL handed back in for deletion is parsed strictly and its resolved path is confirmed to sit inside the folder.
/// </summary>
public sealed partial class UploadStore
{
    public const string RequestPath = "/uploads";

    [GeneratedRegex("^/uploads/([0-9a-f]{32}\\.(?:jpg|png|webp))\\z", RegexOptions.CultureInvariant)]
    private static partial Regex PublicUrlPattern();

    /// <summary>Folder inside the uploads folder for photos that are not published yet. Nothing under it has a public URL: the provider that
    /// serves <c>/uploads</c> (<see cref="UploadFileProvider"/>) only serves files that sit directly in the uploads folder.</summary>
    public const string DraftFolder = ".drafts";

    [GeneratedRegex("^[0-9a-f]{32}\\.(?:jpg|png|webp)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex DraftNamePattern();

    private readonly ILogger<UploadStore> _logger;
    private readonly string _rootWithSeparator;

    public UploadStore(IOptions<UploadOptions> options, IWebHostEnvironment env, ILogger<UploadStore> logger)
    {
        _logger = logger;
        var configured = options.Value.Path;
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "App_Data", "uploads")
            : Path.Combine(env.ContentRootPath, configured.Trim()); // an absolute path wins over the content root
        Root = Path.GetFullPath(root);
        _rootWithSeparator = Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        MaxBytes = options.Value.MaxBytes is > 0 ? Math.Min(options.Value.MaxBytes, 50L * 1024 * 1024) : UploadOptions.DefaultMaxBytes;
    }

    public string Root { get; }

    public long MaxBytes { get; }

    /// <summary>Creates the folder if needed. False (and a logged error) when it cannot be created or written.</summary>
    public bool EnsureRoot()
    {
        try
        {
            Directory.CreateDirectory(Root);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Uploads folder {Path} is unavailable; photo uploads are disabled until it is writable.", Root);
            return false;
        }
    }

    /// <summary>Writes the image under a new random name and returns its public URL path.</summary>
    public async Task<string> SaveAsync(ProcessedImage image, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Root);
        var name = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)) + image.Extension;
        var finalPath = Path.Combine(Root, name);
        // Written under a dot-prefixed name (never served) and renamed when complete.
        var tempPath = Path.Combine(Root, "." + name + ".tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await stream.WriteAsync(image.Bytes, ct);
            File.Move(tempPath, finalPath);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
        return $"{RequestPath}/{name}";
    }

    /// <summary>Writes an image into the hidden draft area under a new random name and returns that name (not a URL). A draft is only
    /// readable through <see cref="TryResolveDraft"/>, which the owner-only wizard serves, until <see cref="PromoteDraft"/> publishes a copy.</summary>
    public async Task<string> SaveDraftAsync(ProcessedImage image, CancellationToken ct = default)
    {
        var dir = Path.Combine(Root, DraftFolder);
        Directory.CreateDirectory(dir);
        var name = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)) + image.Extension;
        var tempPath = Path.Combine(dir, "." + name + ".tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await stream.WriteAsync(image.Bytes, ct);
            File.Move(tempPath, Path.Combine(dir, name));
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
        return name;
    }

    /// <summary>The draft file for a stored draft name. Only a name this class generated (32 hex digits and a known image extension) resolves,
    /// and the resolved path must sit inside the draft area.</summary>
    public bool TryResolveDraft(string? name, out string fullPath, out string contentType)
    {
        fullPath = "";
        contentType = "";
        if (name is null || !DraftNamePattern().IsMatch(name)) return false;

        var dir = Path.GetFullPath(Path.Combine(Root, DraftFolder)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(dir, name));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(dir, comparison) || !File.Exists(candidate)) return false;

        fullPath = candidate;
        contentType = Path.GetExtension(name) switch { ".jpg" => "image/jpeg", ".png" => "image/png", _ => "image/webp" };
        return true;
    }

    /// <summary>Copies a draft to a new public file and returns its <c>/uploads/…</c> URL, or null when the draft does not exist. The draft itself
    /// stays until <see cref="TryDeleteDraft"/>, so a publish that fails afterwards loses nothing.</summary>
    public string? PromoteDraft(string? name)
    {
        if (!TryResolveDraft(name, out var source, out _)) return null;
        var publicName = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)) + Path.GetExtension(name);
        var tempPath = Path.Combine(Root, "." + publicName + ".tmp");
        try
        {
            File.Copy(source, tempPath);
            File.Move(tempPath, Path.Combine(Root, publicName));
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
        return $"{RequestPath}/{publicName}";
    }

    public bool TryDeleteDraft(string? name) => TryResolveDraft(name, out var path, out _) && TryDeleteFile(path);

    /// <summary>Removes draft files older than <paramref name="olderThan"/> (other than <paramref name="keep"/>), which are left behind when a
    /// wizard is abandoned or a request dies between saving a file and recording its name.</summary>
    public int PurgeDrafts(TimeSpan olderThan, string? keep = null)
    {
        var dir = Path.Combine(Root, DraftFolder);
        if (!Directory.Exists(dir)) return 0;
        var removed = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(file);
                if (name == keep) continue;
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > olderThan && TryDeleteFile(file)) removed++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not clean the draft photo folder.");
        }
        return removed;
    }

    /// <summary>Deletes the file behind a public upload URL. Anything that is not one of our generated URLs, or does not
    /// resolve inside the folder, is ignored (for example the bundled /img/avatar.svg).</summary>
    public bool TryDelete(string? publicUrl)
    {
        if (!TryResolve(publicUrl, out var path)) return false;
        return TryDeleteFile(path);
    }

    public bool TryResolve(string? publicUrl, out string fullPath)
    {
        fullPath = "";
        if (publicUrl is null) return false;
        var match = PublicUrlPattern().Match(publicUrl);
        if (!match.Success) return false;

        var candidate = Path.GetFullPath(Path.Combine(Root, match.Groups[1].Value));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(_rootWithSeparator, comparison)) return false;

        fullPath = candidate;
        return true;
    }

    private bool TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete {File}.", Path.GetFileName(path));
            return false;
        }
    }
}
