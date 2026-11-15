using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Security;
using Portfolio.Web.Transfer;

namespace Portfolio.Web.Pages.Admin.Transfer;

/// <summary>
/// Sends every piece of content as one JSON file. It is a GET because it changes nothing: it reads the database and writes nothing, so it needs no
/// antiforgery token, and a request from another site cannot make it do anything harmful. It also cannot read it: the answer is not readable
/// cross-origin (no CORS headers are sent, and the session cookie is <c>SameSite=Strict</c>, so such a request is anonymous and is turned away). Like every page under
/// /admin it needs the owner's session. The answer is a download that must not be stored (<c>no-store</c>), shown by no browser (<c>attachment</c>) or
/// sniffed into another type (<c>nosniff</c>).
/// </summary>
// The only thing a request to this page can carry is a refusal: a post has no body this page reads.
[BodyLimit(1024)]
public class ExportModel : AdminPageModel
{
    private readonly ExportService _export;
    private readonly TimeProvider _clock;

    public ExportModel(ExportService export, TimeProvider clock)
    {
        _export = export;
        _clock = clock;
    }

    /// <summary>The export is a read; a post to its address is refused and does nothing.</summary>
    public IActionResult OnPost()
    {
        Response.Headers.Allow = "GET";
        return StatusCode(StatusCodes.Status405MethodNotAllowed);
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var file = await _export.BuildAsync(ct);
        var bytes = ExportJson.Serialize(file);

        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(bytes, "application/json", $"site-export-{_clock.GetUtcNow():yyyy-MM-dd}.json");
    }
}
