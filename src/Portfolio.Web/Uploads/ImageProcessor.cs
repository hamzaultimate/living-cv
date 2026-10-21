using SkiaSharp;

namespace Portfolio.Web.Uploads;

/// <summary>An upload was refused. The message is safe to show to the user.</summary>
public sealed class ImageRejectedException : Exception
{
    public ImageRejectedException(string message) : base(message) { }
}

/// <summary>A re-encoded image ready to store. Contains pixels only, no metadata.</summary>
public sealed record ProcessedImage(byte[] Bytes, string Extension, string ContentType, int Width, int Height);

/// <summary>
/// Turns untrusted upload bytes into a clean image. The type is decided from the file's own bytes (never its name
/// or declared type), only JPEG, PNG and WebP pass, the size is checked from the header before any pixels are
/// decoded, and the result is re-encoded from the decoded pixels, which discards EXIF and any other metadata and
/// anything appended to or hidden inside the original file.
/// </summary>
public sealed class ImageProcessor
{
    /// <summary>Longest edge of a stored image; larger images are scaled down.</summary>
    public const int MaxEdge = 1600;

    /// <summary>Upper bounds on the source, checked before decoding so a tiny file cannot expand into gigabytes.</summary>
    public const int MaxSourceSide = 10_000;
    public const long MaxSourcePixels = 25_000_000;

    private const int JpegQuality = 85;
    private const int WebpQuality = 85;

    // Decoding is memory-heavy; one at a time keeps the worst case bounded.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private enum Kind { Jpeg, Png, Webp }

    public async Task<ProcessedImage> ProcessAsync(byte[] data, CancellationToken ct = default)
    {
        var kind = Sniff(data) ?? throw new ImageRejectedException("Unsupported file. Upload a JPEG, PNG or WebP image.");
        await Gate.WaitAsync(ct);
        try
        {
            return await Task.Run(() => Process(data, kind), ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static Kind? Sniff(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return Kind.Jpeg;
        if (d.Length >= 8 && d[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return Kind.Png;
        if (d.Length >= 12 && d[..4].SequenceEqual("RIFF"u8) && d.Slice(8, 4).SequenceEqual("WEBP"u8)) return Kind.Webp;
        return null;
    }

    private static ProcessedImage Process(byte[] data, Kind kind)
    {
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData) ?? throw new ImageRejectedException("That file could not be read as an image.");

        var expected = kind switch { Kind.Jpeg => SKEncodedImageFormat.Jpeg, Kind.Png => SKEncodedImageFormat.Png, _ => SKEncodedImageFormat.Webp };
        if (codec.EncodedFormat != expected)
            throw new ImageRejectedException("That file could not be read as an image.");

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
            throw new ImageRejectedException("That file could not be read as an image.");
        if (info.Width > MaxSourceSide || info.Height > MaxSourceSide || (long)info.Width * info.Height > MaxSourcePixels)
            throw new ImageRejectedException("The image is too large in pixels. Resize it to under 25 megapixels (for example 5000 by 5000) and try again.");

        // Each step replaces the previous bitmap, so at most two full-size copies exist at once.
        var current = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        try
        {
            if (codec.GetPixels(current.Info, current.GetPixels()) != SKCodecResult.Success)
                throw new ImageRejectedException("That file could not be read as an image.");

            if (ApplyOrigin(current, codec.EncodedOrigin) is { } oriented)
            {
                current.Dispose();
                current = oriented;
            }
            if (Fit(current) is { } sized)
            {
                current.Dispose();
                current = sized;
            }

            using var image = SKImage.FromBitmap(current);
            var (format, extension, contentType, quality) = kind switch
            {
                Kind.Jpeg => (SKEncodedImageFormat.Jpeg, ".jpg", "image/jpeg", JpegQuality),
                Kind.Png => (SKEncodedImageFormat.Png, ".png", "image/png", 100),
                _ => (SKEncodedImageFormat.Webp, ".webp", "image/webp", WebpQuality),
            };
            using var encoded = image.Encode(format, quality) ?? throw new ImageRejectedException("That image could not be processed.");
            return new ProcessedImage(encoded.ToArray(), extension, contentType, current.Width, current.Height);
        }
        finally
        {
            current.Dispose();
        }
    }

    /// <summary>Returns a rotated/flipped copy so the pixels are upright, or null when the image is already upright.</summary>
    private static SKBitmap? ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft || !Enum.IsDefined(origin)) return null;

        float w = source.Width, h = source.Height;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var m = SKMatrix.Identity;
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: m.ScaleX = -1; m.TransX = w; break;
            case SKEncodedOrigin.BottomRight: m.ScaleX = -1; m.TransX = w; m.ScaleY = -1; m.TransY = h; break;
            case SKEncodedOrigin.BottomLeft: m.ScaleY = -1; m.TransY = h; break;
            case SKEncodedOrigin.LeftTop: m.ScaleX = 0; m.ScaleY = 0; m.SkewX = 1; m.SkewY = 1; break;
            case SKEncodedOrigin.RightTop: m.ScaleX = 0; m.ScaleY = 0; m.SkewX = -1; m.TransX = h; m.SkewY = 1; break;
            case SKEncodedOrigin.RightBottom: m.ScaleX = 0; m.ScaleY = 0; m.SkewX = -1; m.TransX = h; m.SkewY = -1; m.TransY = w; break;
            case SKEncodedOrigin.LeftBottom: m.ScaleX = 0; m.ScaleY = 0; m.SkewX = 1; m.SkewY = -1; m.TransY = w; break;
        }

        var result = new SKBitmap(new SKImageInfo(swap ? source.Height : source.Width, swap ? source.Width : source.Height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(result);
        canvas.SetMatrix(m);
        canvas.DrawBitmap(source, 0f, 0f, new SKSamplingOptions(SKFilterMode.Nearest), null);
        return result;
    }

    /// <summary>Returns a scaled-down copy when the longest edge exceeds <see cref="MaxEdge"/>, otherwise null.</summary>
    private static SKBitmap? Fit(SKBitmap source)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= MaxEdge) return null;

        var scale = (double)MaxEdge / longest;
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)),
            source.ColorType, source.AlphaType);
        return source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new ImageRejectedException("That image could not be processed.");
    }
}
