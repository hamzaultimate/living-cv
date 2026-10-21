using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace Portfolio.Web.Tests.Support;

/// <summary>Builds small valid images, and deliberately hostile variants of them, for the upload tests.</summary>
public static class TestImages
{
    /// <summary>An image whose left half is red and right half is blue.</summary>
    public static byte[] Split(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            using var red = new SKPaint { Color = SKColors.Red };
            using var blue = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(0, 0, width / 2f, height, red);
            canvas.DrawRect(width / 2f, 0, width - width / 2f, height, blue);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 95);
        return data.ToArray();
    }

    public static SKBitmap Decode(byte[] bytes) => SKBitmap.Decode(bytes) ?? throw new InvalidOperationException("Not an image");

    /// <summary>Random-looking pixels, so the encoded file is large.</summary>
    public static byte[] Noise(int width, int height)
    {
        var random = new Random(7);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var pixels = new byte[width * height * 4];
        random.NextBytes(pixels);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Inserts raw JPEG marker segments straight after the start-of-image marker.</summary>
    public static byte[] WithJpegSegments(byte[] jpeg, params byte[][] segments) =>
        [.. jpeg[..2], .. segments.SelectMany(s => s), .. jpeg[2..]];

    /// <summary>An APP1 segment holding EXIF with only the Orientation tag set.</summary>
    public static byte[] ExifOrientation(int orientation)
    {
        byte[] tiff =
        [
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,   // little endian TIFF header, first IFD at 8
            0x01, 0x00,                                       // one entry
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, (byte)orientation, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,                           // no next IFD
        ];
        byte[] body = [.. "Exif\0\0"u8.ToArray(), .. tiff];
        return Segment(0xE1, body);
    }

    public static byte[] JpegComment(string text) => Segment(0xFE, Encoding.ASCII.GetBytes(text));

    private static byte[] Segment(byte marker, byte[] body)
    {
        var length = body.Length + 2;
        return [0xFF, marker, (byte)(length >> 8), (byte)length, .. body];
    }

    /// <summary>Adds a tEXt chunk after IHDR.</summary>
    public static byte[] WithPngText(byte[] png, string key, string value)
    {
        var ihdrEnd = 8 + 4 + 4 + 13 + 4;
        return [.. png[..ihdrEnd], .. PngChunk("tEXt", Encoding.ASCII.GetBytes(key + "\0" + value)), .. png[ihdrEnd..]];
    }

    /// <summary>
    /// A valid 8-bit grayscale PNG of the given size filled with zeros. It compresses to a few dozen KB even when it
    /// would decode to hundreds of megabytes, which is what a decompression bomb looks like.
    /// </summary>
    public static byte[] SolidGrayPng(int width, int height)
    {
        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, width);
        WriteBigEndian(ihdr, 4, height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 0; // grayscale

        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[width + 1];
            for (var y = 0; y < height; y++) z.Write(row);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        png.Write(PngChunk("IHDR", ihdr));
        png.Write(PngChunk("IDAT", idat.ToArray()));
        png.Write(PngChunk("IEND", []));
        return png.ToArray();
    }

    private static byte[] PngChunk(string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var chunk = new byte[12 + data.Length];
        WriteBigEndian(chunk, 0, data.Length);
        typeBytes.CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        WriteBigEndian(chunk, 8 + data.Length, (int)Crc32(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
