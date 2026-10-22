namespace Portfolio.Web.Uploads;

/// <summary>Reading an uploaded form file, shared by every page that accepts a photo.</summary>
public static class UploadForm
{
    /// <summary>Reads at most <paramref name="maxBytes"/> plus one byte, so an oversized stream is never fully buffered.
    /// Throws <see cref="ImageRejectedException"/> (with a message safe to show) when the file is larger.</summary>
    public static async Task<byte[]> ReadAsync(IFormFile file, long maxBytes, string maxLabel, CancellationToken ct)
    {
        await using var input = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new ImageRejectedException($"That file is larger than {maxLabel}.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>"5 MB" or "300 KB": the size cap as shown to the owner.</summary>
    public static string Label(long maxBytes) =>
        maxBytes >= 1024 * 1024 ? $"{maxBytes / (1024 * 1024)} MB" : $"{Math.Max(1, maxBytes / 1024)} KB";
}
