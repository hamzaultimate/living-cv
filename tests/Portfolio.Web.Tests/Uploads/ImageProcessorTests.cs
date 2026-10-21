using System.Text;
using Portfolio.Web.Tests.Support;
using Portfolio.Web.Uploads;
using SkiaSharp;
using Xunit;

namespace Portfolio.Web.Tests.Uploads;

public class ImageProcessorTests
{
    private readonly ImageProcessor _processor = new();

    private static bool Contains(byte[] haystack, string needle) =>
        haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(needle)) >= 0;

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, ".jpg", "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Png, ".png", "image/png")]
    [InlineData(SKEncodedImageFormat.Webp, ".webp", "image/webp")]
    public async Task AcceptedTypes_AreReEncodedInTheirOwnFormat(SKEncodedImageFormat format, string extension, string contentType)
    {
        var result = await _processor.ProcessAsync(TestImages.Split(format, 64, 32));

        Assert.Equal(extension, result.Extension);
        Assert.Equal(contentType, result.ContentType);
        using var codec = SKCodec.Create(new MemoryStream(result.Bytes));
        Assert.Equal(format, codec.EncodedFormat);
        Assert.Equal((64, 32), (result.Width, result.Height));
    }

    public static TheoryData<string, byte[]> NotImages => new()
    {
        { "gif", "GIF89a\x01\0\x01\0\x80\0\0\0\0\0\xff\xff\xff!\xf9\x04\x01\0\0\0\0,\0\0\0\0\x01\0\x01\0\0\x02\x02D\x01\0;"u8.ToArray() },
        { "svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray() },
        { "svg with xml header", "<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray() },
        { "html", "<!doctype html><script>alert(1)</script>"u8.ToArray() },
        { "pdf", "%PDF-1.7\n1 0 obj\n"u8.ToArray() },
        { "zip", [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00] },
        { "bmp", "BM\0\0\0\0\0\0\0\0\x36\0\0\0"u8.ToArray() },
        { "empty", [] },
        { "one byte", [0xFF] },
        { "jpeg magic then text", [0xFF, 0xD8, 0xFF, .. "<script>alert(1)</script>"u8.ToArray()] },
        { "png magic then text", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. "not really a png"u8.ToArray()] },
        { "riff but not webp", "RIFF\x04\0\0\0WAVE"u8.ToArray() },
        { "webp magic then nothing", "RIFF\x04\0\0\0WEBP"u8.ToArray() },
    };

    [Theory]
    [MemberData(nameof(NotImages))]
    public async Task Anything_ThatIsNotAnAllowedRasterImage_IsRejected(string _, byte[] data) =>
        await Assert.ThrowsAsync<ImageRejectedException>(() => _processor.ProcessAsync(data));

    [Fact]
    public async Task ATruncatedImage_IsRejected()
    {
        var png = TestImages.Split(SKEncodedImageFormat.Png, 64, 64);

        await Assert.ThrowsAsync<ImageRejectedException>(() => _processor.ProcessAsync(png[..(png.Length / 2)]));
    }

    [Fact]
    public async Task DataAppendedToAnImage_DoesNotSurviveReEncoding()
    {
        var jpeg = TestImages.Split(SKEncodedImageFormat.Jpeg, 32, 32);
        byte[] polyglot = [.. jpeg, .. "<script>alert(document.cookie)</script><?php system($_GET[0]); ?>"u8.ToArray()];

        var result = await _processor.ProcessAsync(polyglot);

        Assert.False(Contains(result.Bytes, "<script"));
        Assert.False(Contains(result.Bytes, "<?php"));
    }

    [Fact]
    public async Task ExifAndComments_AreStripped_FromJpeg()
    {
        var jpeg = TestImages.WithJpegSegments(TestImages.Split(SKEncodedImageFormat.Jpeg, 32, 32),
            TestImages.ExifOrientation(1), TestImages.JpegComment("SECRET-GPS-51.5N-0.12W"));
        Assert.True(Contains(jpeg, "SECRET-GPS"));
        Assert.True(Contains(jpeg, "Exif"));

        var result = await _processor.ProcessAsync(jpeg);

        Assert.False(Contains(result.Bytes, "SECRET-GPS"));
        Assert.False(Contains(result.Bytes, "Exif"));
    }

    [Fact]
    public async Task TextChunks_AreStripped_FromPng()
    {
        var png = TestImages.WithPngText(TestImages.Split(SKEncodedImageFormat.Png, 32, 32), "Comment", "SECRET-PNG-METADATA");
        Assert.True(Contains(png, "SECRET-PNG-METADATA"));

        var result = await _processor.ProcessAsync(png);

        Assert.False(Contains(result.Bytes, "SECRET-PNG-METADATA"));
        Assert.False(Contains(result.Bytes, "tEXt"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task ExifOrientation_IsAppliedBeforeMetadataIsDropped_ForAllEightOrientations(int orientation)
    {
        const int displayWidth = 40, displayHeight = 20;
        var jpeg = TestImages.QuadrantsStoredForOrientation(orientation, displayWidth, displayHeight);

        var result = await _processor.ProcessAsync(jpeg);

        Assert.Equal((displayWidth, displayHeight), (result.Width, result.Height));
        using var output = TestImages.Decode(result.Bytes);
        Assert.Equal((displayWidth, displayHeight), (output.Width, output.Height));
        foreach (var (x, y) in new[] { (displayWidth / 4, displayHeight / 4), (3 * displayWidth / 4, displayHeight / 4),
                     (displayWidth / 4, 3 * displayHeight / 4), (3 * displayWidth / 4, 3 * displayHeight / 4) })
        {
            var expected = TestImages.QuadrantColor(x, y, displayWidth, displayHeight);
            var actual = output.GetPixel(x, y);
            Assert.True(Math.Abs(actual.Red - expected.Red) < 40 && Math.Abs(actual.Green - expected.Green) < 40 && Math.Abs(actual.Blue - expected.Blue) < 40,
                $"orientation {orientation}: pixel ({x},{y}) is {actual}, expected about {expected}");
        }
        Assert.False(Contains(result.Bytes, "Exif"));
    }

    [Fact]
    public async Task WideGamutPhotos_AreConvertedToSrgb_NotReadAsIfTheyWereSrgb()
    {
        // Display P3 primaries: the same numbers mean a more saturated colour than in sRGB.
        var p3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);
        var source = new SKColor(200, 100, 100);
        // Raw pixel values tagged as Display P3 (no conversion on the way in).
        var raw = new byte[16 * 16 * 4];
        for (var i = 0; i < raw.Length; i += 4) (raw[i], raw[i + 1], raw[i + 2], raw[i + 3]) = (source.Red, source.Green, source.Blue, 255);
        using var tagged = SKImage.FromPixelCopy(new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Opaque, p3), raw);
        byte[] png;
        using (var data = tagged.Encode(SKEncodedImageFormat.Png, 100))
            png = data.ToArray();

        // What the same pixels are in sRGB, as the graphics library itself computes it.
        using var asSrgb = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Opaque, SKColorSpace.CreateSrgb()));
        Assert.True(tagged.ReadPixels(asSrgb.Info, asSrgb.GetPixels(), asSrgb.RowBytes, 0, 0));
        var expected = asSrgb.GetPixel(8, 8);
        Assert.True(Math.Abs(expected.Red - source.Red) > 8, "the test colour must actually change when converted");

        var result = await _processor.ProcessAsync(png);

        using var output = TestImages.Decode(result.Bytes);
        var actual = output.GetPixel(8, 8);
        Assert.InRange(actual.Red, expected.Red - 3, expected.Red + 3);
        Assert.InRange(actual.Green, expected.Green - 3, expected.Green + 3);
        Assert.InRange(actual.Blue, expected.Blue - 3, expected.Blue + 3);
    }

    [Fact]
    public async Task LargeImages_AreScaledDownToTheMaximumEdge()
    {
        var result = await _processor.ProcessAsync(TestImages.Split(SKEncodedImageFormat.Png, 3200, 1600));

        Assert.Equal((ImageProcessor.MaxEdge, ImageProcessor.MaxEdge / 2), (result.Width, result.Height));
    }

    [Fact]
    public async Task SmallImages_AreNotEnlarged()
    {
        var result = await _processor.ProcessAsync(TestImages.Split(SKEncodedImageFormat.Webp, 300, 200));

        Assert.Equal((300, 200), (result.Width, result.Height));
    }

    [Fact]
    public async Task ADecompressionBomb_IsRejectedFromItsHeader_BeforeAnyPixelsAreDecoded()
    {
        // 7000 x 7000 = 49 megapixels (about 196 MB decoded) in a file of well under a megabyte.
        var bomb = TestImages.SolidGrayPng(7000, 7000);
        Assert.True(bomb.Length < 1_000_000, $"fixture is {bomb.Length} bytes");

        var started = DateTime.UtcNow;
        var error = await Assert.ThrowsAsync<ImageRejectedException>(() => _processor.ProcessAsync(bomb));

        Assert.Contains("too large in pixels", error.Message);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AnExtremelyWideImage_IsRejectedEvenWithFewPixels()
    {
        var wide = TestImages.SolidGrayPng(ImageProcessor.MaxSourceSide + 1, 2);

        var error = await Assert.ThrowsAsync<ImageRejectedException>(() => _processor.ProcessAsync(wide));

        Assert.Contains("too large in pixels", error.Message);
    }

    [Fact]
    public async Task AnImageJustUnderTheLimits_IsAccepted()
    {
        var result = await _processor.ProcessAsync(TestImages.SolidGrayPng(ImageProcessor.MaxSourceSide, 2));

        Assert.Equal(ImageProcessor.MaxEdge, result.Width);
    }
}
