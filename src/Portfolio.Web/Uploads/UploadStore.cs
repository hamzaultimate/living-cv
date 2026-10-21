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
