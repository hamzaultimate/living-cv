using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Portfolio.Web.Uploads;

/// <summary>
/// The file provider behind <c>/uploads</c>. Published uploads are flat files directly in the uploads folder, so anything in a subfolder
/// (the hidden draft area, or a path trying to reach one) is simply not found, and nothing can be listed. This does not rely on the
/// physical provider's own exclusion of dot-prefixed names, which hides a dotted file or folder itself but not a file inside one.
/// </summary>
public sealed class UploadFileProvider : IFileProvider, IDisposable
{
    private readonly PhysicalFileProvider _inner;

    public UploadFileProvider(string root) => _inner = new PhysicalFileProvider(root);

    public IFileInfo GetFileInfo(string subpath)
    {
        var path = subpath.TrimStart('/');
        return path.Length == 0 || path.AsSpan().IndexOfAny('/', '\\') >= 0 || path.StartsWith('.')
            ? new NotFoundFileInfo(subpath)
            : _inner.GetFileInfo(subpath);
    }

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    public void Dispose() => _inner.Dispose();
}
