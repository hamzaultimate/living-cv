using Portfolio.Web.Configuration;

namespace Portfolio.Web.Services;

/// <summary>
/// The profile provider every page reads. For an owner's draft preview request it returns the wizard's unpublished
/// identity (set on the request by <see cref="TemplatePreview"/> after the session was checked); for every other request,
/// and for everything that is not a page (sitemap, CV, share image), it is the real provider and nothing else.
/// </summary>
public sealed class DraftPreviewProfileProvider : ISiteProfileProvider
{
    internal const string DraftItem = "lcv:draft-preview";

    private readonly ISiteProfileProvider _inner;
    private readonly IHttpContextAccessor _http;

    public DraftPreviewProfileProvider(ISiteProfileProvider inner, IHttpContextAccessor http)
    {
        _inner = inner;
        _http = http;
    }

    public Task<SiteOptions> GetAsync(CancellationToken ct = default) =>
        _http.HttpContext?.Items[DraftItem] is SiteOptions draft ? Task.FromResult(draft) : _inner.GetAsync(ct);

    public void Invalidate() => _inner.Invalidate();

    public void Prime(SiteOptions saved) => _inner.Prime(saved);

    public int Version => _inner.Version;
}
