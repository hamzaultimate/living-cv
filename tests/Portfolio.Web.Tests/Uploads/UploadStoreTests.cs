using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Web.Uploads;
using Xunit;

namespace Portfolio.Web.Tests.Uploads;

public sealed class UploadStoreTests : IDisposable
{
    private sealed class Env : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "t";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private readonly string _sandbox = Directory.CreateTempSubdirectory("lcv-store-").FullName;

    public void Dispose() => Directory.Delete(_sandbox, recursive: true);

    private UploadStore NewStore(string? path = null, long maxBytes = UploadOptions.DefaultMaxBytes) =>
        new(Options.Create(new UploadOptions { Path = path, MaxBytes = maxBytes }), new Env { ContentRootPath = _sandbox }, NullLogger<UploadStore>.Instance);

    private static ProcessedImage Image(string extension = ".webp") => new([1, 2, 3, 4], extension, "image/webp", 1, 1);

    [Fact]
    public void TheDefaultFolder_IsOutsideWwwroot_UnderAppData()
    {
        var store = NewStore();

        Assert.Equal(Path.Combine(_sandbox, "App_Data", "uploads"), store.Root);
        Assert.DoesNotContain("wwwroot", store.Root);
    }

    [Fact]
    public void ARelativePath_ResolvesAgainstTheContentRoot_AndAnAbsolutePathIsUsedAsIs()
    {
        Assert.Equal(Path.GetFullPath(Path.Combine(_sandbox, "..", "shared-uploads")), NewStore("../shared-uploads").Root);
        var absolute = Path.Combine(_sandbox, "elsewhere");
        Assert.Equal(absolute, NewStore(absolute).Root);
    }

    [Theory]
    [InlineData(0L, UploadOptions.DefaultMaxBytes)]
    [InlineData(-5L, UploadOptions.DefaultMaxBytes)]
    [InlineData(1_000_000L, 1_000_000L)]
    [InlineData(long.MaxValue, 50L * 1024 * 1024)]
    public void TheSizeCap_DefaultsAndIsBounded(long configured, long expected) =>
        Assert.Equal(expected, NewStore(maxBytes: configured).MaxBytes);

    [Fact]
    public async Task Saved_FilesGetRandomGeneratedNames_WithTheServerChosenExtension()
    {
        var store = NewStore();

        var a = await store.SaveAsync(Image(".webp"));
        var b = await store.SaveAsync(Image(".webp"));
        var c = await store.SaveAsync(Image(".jpg"));

        Assert.NotEqual(a, b);
        Assert.All(new[] { a, b }, u => Assert.Matches(new Regex(@"^/uploads/[0-9a-f]{32}\.webp$"), u));
        Assert.Matches(new Regex(@"^/uploads/[0-9a-f]{32}\.jpg$"), c);
        Assert.True(File.Exists(Path.Combine(store.Root, a["/uploads/".Length..])));
        Assert.Empty(Directory.GetFiles(store.Root, ".*.tmp"));
    }

    [Fact]
    public async Task Delete_RemovesOnlyOurOwnFiles()
    {
        var store = NewStore();
        var url = await store.SaveAsync(Image());
        var path = Path.Combine(store.Root, url["/uploads/".Length..]);

        Assert.True(store.TryDelete(url));
        Assert.False(File.Exists(path));
        Assert.False(store.TryDelete(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/img/avatar.svg")]
    [InlineData("/uploads/../outside.webp")]
    [InlineData("/uploads/..%2foutside.webp")]
    [InlineData("/uploads/%2e%2e/outside.webp")]
    [InlineData("/uploads/..\\outside.webp")]
    [InlineData("/uploads/sub/0123456789abcdef0123456789abcdef.webp")]
    [InlineData("/uploads/0123456789ABCDEF0123456789ABCDEF.webp")]
    [InlineData("/uploads/0123456789abcdef0123456789abcdef.svg")]
    [InlineData("/uploads/0123456789abcdef0123456789abcdef.webp.exe")]
    [InlineData("/uploads/0123456789abcdef0123456789abcdef.webp\n")]
    [InlineData("https://evil.example/uploads/0123456789abcdef0123456789abcdef.webp")]
    [InlineData("//uploads/0123456789abcdef0123456789abcdef.webp")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    public void Delete_IgnoresAnythingThatIsNotAGeneratedUploadUrl(string? url)
    {
        var store = NewStore();
        Directory.CreateDirectory(store.Root);
        var outside = Path.Combine(Path.GetDirectoryName(store.Root)!, "outside.webp");
        File.WriteAllText(outside, "keep me");
        var lookalike = Path.Combine(store.Root, "0123456789abcdef0123456789abcdef.webp");
        File.WriteAllText(lookalike, "x");

        var result = store.TryDelete(url);

        Assert.False(result && url != "/uploads/0123456789abcdef0123456789abcdef.webp");
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void Resolve_NeverLeavesTheUploadsFolder()
    {
        var store = NewStore();

        Assert.True(store.TryResolve("/uploads/0123456789abcdef0123456789abcdef.png", out var path));
        Assert.StartsWith(store.Root + Path.DirectorySeparatorChar, path);
        Assert.False(store.TryResolve("/uploads/../../etc/passwd", out _));
    }
}
