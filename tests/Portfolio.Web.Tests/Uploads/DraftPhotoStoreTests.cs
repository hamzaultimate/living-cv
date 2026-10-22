using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Web.Uploads;
using Xunit;

namespace Portfolio.Web.Tests.Uploads;

/// <summary>The hidden draft area the onboarding wizard stores an unpublished photo in.</summary>
public sealed class DraftPhotoStoreTests : IDisposable
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

    private readonly string _sandbox = Directory.CreateTempSubdirectory("lcv-draft-").FullName;

    public void Dispose() => Directory.Delete(_sandbox, recursive: true);

    private UploadStore NewStore() =>
        new(Options.Create(new UploadOptions { Path = Path.Combine(_sandbox, "uploads") }), new Env { ContentRootPath = _sandbox }, NullLogger<UploadStore>.Instance);

    private static ProcessedImage Image(string extension = ".webp") => new([1, 2, 3, 4], extension, "image/webp", 1, 1);

    [Fact]
    public async Task ADraft_IsWrittenUnderARandomName_InsideTheHiddenDraftFolder_NotInThePublicFolder()
    {
        var store = NewStore();

        var name = await store.SaveDraftAsync(Image(".png"));

        Assert.Matches("^[0-9a-f]{32}\\.png$", name);
        Assert.True(File.Exists(Path.Combine(store.Root, ".drafts", name)));
        Assert.False(File.Exists(Path.Combine(store.Root, name)));
        Assert.Empty(Directory.EnumerateFiles(store.Root));
    }

    [Fact]
    public async Task TheDraftFolder_IsNotServedByTheStaticFileProviderThatServesUploads()
    {
        var store = NewStore();
        var name = await store.SaveDraftAsync(Image(".jpg"));
        // The provider Program.cs builds for /uploads. (A plain PhysicalFileProvider serves a file inside a dot-prefixed folder: its
        // exclusion filter only looks at the name of the file itself.)
        using var provider = new UploadFileProvider(store.Root);
        using var plain = new PhysicalFileProvider(store.Root);
        Assert.True(plain.GetFileInfo($"/.drafts/{name}").Exists);

        Assert.False(provider.GetFileInfo($"/.drafts/{name}").Exists, "file in .drafts");
        Assert.False(provider.GetFileInfo($"/{name}").Exists);
        Assert.False(provider.GetFileInfo($".drafts/{name}").Exists, "relative");
        Assert.False(provider.GetDirectoryContents("/.drafts").Exists, "dir");
        Assert.False(provider.GetDirectoryContents("/").Exists);
        Assert.False(provider.GetFileInfo($"/./.drafts/{name}").Exists);
        Assert.False(provider.GetFileInfo($"/x/../.drafts/{name}").Exists);
        Assert.False(provider.GetFileInfo($"\\.drafts\\{name}").Exists);
        Assert.False(provider.GetFileInfo("/").Exists);
    }

    [Fact]
    public async Task APublishedUpload_IsStillServed_ByTheFlatProvider()
    {
        var store = NewStore();
        var url = store.PromoteDraft(await store.SaveDraftAsync(Image(".png")))!;
        using var provider = new UploadFileProvider(store.Root);

        Assert.True(provider.GetFileInfo(url["/uploads".Length..]).Exists);
    }

    [Fact]
    public async Task ADraft_ResolvesOnlyByTheNameTheStoreGave_WithTheRightContentType()
    {
        var store = NewStore();
        var name = await store.SaveDraftAsync(Image(".webp"));

        Assert.True(store.TryResolveDraft(name, out var path, out var contentType));
        Assert.Equal(Path.Combine(store.Root, ".drafts", name), path);
        Assert.Equal("image/webp", contentType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../../etc/passwd")]
    [InlineData("..%2f..%2fetc%2fpasswd")]
    [InlineData("..\\..\\secret.png")]
    [InlineData("/etc/passwd")]
    [InlineData("0123456789abcdef0123456789abcdef.svg")]
    [InlineData("0123456789abcdef0123456789abcdef.png/../../x")]
    [InlineData("../0123456789abcdef0123456789abcdef.png")]
    [InlineData(".0123456789abcdef0123456789abcdef.png.tmp")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF.png")]
    [InlineData("0123456789abcdef0123456789abcdef.png\n")]
    [InlineData("0123456789abcdef0123456789abcdef.png\0.jpg")]
    public async Task NoOtherNameResolves_SoTheFilesystemCannotBeReadThroughIt(string? name)
    {
        var store = NewStore();
        await store.SaveDraftAsync(Image());
        File.WriteAllText(Path.Combine(_sandbox, "secret.png"), "secret");

        Assert.False(store.TryResolveDraft(name, out var path, out _));
        Assert.Equal("", path);
        Assert.False(store.TryDeleteDraft(name));
        Assert.Null(store.PromoteDraft(name));
        Assert.True(File.Exists(Path.Combine(_sandbox, "secret.png")));
    }

    [Fact]
    public async Task APublicUploadUrl_IsNotADraftName_AndADraftName_IsNotAPublicUrl()
    {
        var store = NewStore();
        var name = await store.SaveDraftAsync(Image());

        Assert.False(store.TryResolveDraft($"/uploads/{name}", out _, out _));
        Assert.False(store.TryDelete($"/uploads/{name}")); // nothing public by that name, and the draft survives
        Assert.True(store.TryResolveDraft(name, out _, out _));
    }

    [Fact]
    public async Task Promoting_CopiesTheDraftToANewPublicFile_AndLeavesTheDraftInPlace()
    {
        var store = NewStore();
        var name = await store.SaveDraftAsync(Image(".jpg"));

        var url = store.PromoteDraft(name);

        Assert.NotNull(url);
        Assert.Matches("^/uploads/[0-9a-f]{32}\\.jpg$", url);
        Assert.NotEqual($"/uploads/{name}", url);
        Assert.True(store.TryResolve(url, out var publicPath));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(publicPath));
        Assert.True(store.TryResolveDraft(name, out _, out _));
        Assert.Empty(Directory.EnumerateFiles(store.Root, ".*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task APromotedCopy_IsIndependentOfTheDraft_SoDeletingTheDraftKeepsThePublicFile()
    {
        var store = NewStore();
        var name = await store.SaveDraftAsync(Image());
        var url = store.PromoteDraft(name)!;

        Assert.True(store.TryDeleteDraft(name));

        Assert.False(store.TryResolveDraft(name, out _, out _));
        Assert.True(store.TryResolve(url, out var path));
        Assert.True(File.Exists(path));
        Assert.False(store.TryDeleteDraft(name)); // deleting twice is harmless
    }

    [Fact]
    public async Task PurgingDrafts_RemovesOnlyOldOnesAndNeverTheOneToKeep()
    {
        var store = NewStore();
        var old = await store.SaveDraftAsync(Image());
        var keep = await store.SaveDraftAsync(Image());
        var fresh = await store.SaveDraftAsync(Image());
        var oldFile = Path.Combine(store.Root, ".drafts", old);
        var keepFile = Path.Combine(store.Root, ".drafts", keep);
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-3));
        File.SetLastWriteTimeUtc(keepFile, DateTime.UtcNow.AddDays(-3));

        var removed = store.PurgeDrafts(TimeSpan.FromDays(1), keep);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(keepFile));
        Assert.True(store.TryResolveDraft(fresh, out _, out _));
    }

    [Fact]
    public void PurgingWithoutADraftFolder_IsANoOp() => Assert.Equal(0, NewStore().PurgeDrafts(TimeSpan.Zero));

    [Fact]
    public void PromotingAMissingDraft_ReturnsNull_AndWritesNothing()
    {
        var store = NewStore();

        Assert.Null(store.PromoteDraft("0123456789abcdef0123456789abcdef.png"));
        Assert.False(Directory.Exists(store.Root) && Directory.EnumerateFileSystemEntries(store.Root).Any());
    }
}
