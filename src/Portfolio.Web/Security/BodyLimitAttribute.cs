using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Security;

/// <summary>
/// Caps the request body of a page before anything reads it. Razor Pages validate the antiforgery token in an
/// authorization filter, which reads the whole form; a limit applied later (for example <c>[RequestSizeLimit]</c>)
/// would only take effect after that. This runs first. A declared Content-Length over the cap is refused with 413
/// outright; otherwise the server's body limit and the form reader's limits are set so a body that lies about or omits
/// its length is cut off while it is being read, not after it has been buffered.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class BodyLimitAttribute : Attribute, IAuthorizationFilter, IOrderedFilter
{
    /// <summary>Slack for multipart boundaries and the other form fields around an upload.</summary>
    public const long MultipartOverhead = 64 * 1024;

    private readonly long? _bytes;

    /// <summary>A fixed cap, for ordinary text forms.</summary>
    public BodyLimitAttribute(long bytes) => _bytes = bytes;

    /// <summary>The configured upload cap (<c>Uploads:MaxBytes</c>) plus multipart overhead.</summary>
    public BodyLimitAttribute() { }

    public int Order => int.MinValue;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var limit = _bytes ?? http.RequestServices.GetRequiredService<UploadStore>().MaxBytes + MultipartOverhead;

        if (http.Request.ContentLength > limit)
        {
            context.Result = new StatusCodeResult(StatusCodes.Status413PayloadTooLarge);
            return;
        }

        var feature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = limit;

        http.Features.Set<IFormFeature>(new FormFeature(http.Request, new FormOptions
        {
            BufferBodyLengthLimit = limit,
            MultipartBodyLengthLimit = limit,
        }));
    }
}
