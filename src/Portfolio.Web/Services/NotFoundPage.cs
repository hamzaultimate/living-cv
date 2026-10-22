using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace Portfolio.Web.Services;

public static class NotFoundPage
{
    /// <summary>The page that renders the styled not-found response.</summary>
    public const string Path = "/not-found";

    /// <summary>
    /// Shows <see cref="Path"/> for an empty 404 on a page request: an unknown address, or a page of a section the template does
    /// not have. Responses that already carry a body (a missing project) are left alone, as are other status codes, non-GET
    /// requests and file-like addresses, so a 413 or a missing image is never turned into a page.
    /// </summary>
    public static IApplicationBuilder UseNotFoundPage(this IApplicationBuilder app) =>
        app.UseStatusCodePages(async context =>
        {
            var http = context.HttpContext;
            if (http.Response.StatusCode != StatusCodes.Status404NotFound
                || !(HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))
                || System.IO.Path.HasExtension(http.Request.Path.Value))
                return;

            var original = (http.Request.Path, http.Request.QueryString, http.Request.PathBase);
            http.Features.Set<IStatusCodeReExecuteFeature>(new StatusCodeReExecuteFeature
            {
                OriginalPathBase = original.PathBase.Value ?? "",
                OriginalPath = original.Path.Value ?? "",
                OriginalQueryString = original.QueryString.HasValue ? original.QueryString.Value : null,
            });
            http.SetEndpoint(null);
            http.Features.Get<IRouteValuesFeature>()?.RouteValues.Clear();
            http.Request.Path = Path;
            http.Request.QueryString = QueryString.Empty;
            try
            {
                await context.Next(http);
            }
            finally
            {
                http.Features.Set<IStatusCodeReExecuteFeature?>(null);
                http.Request.Path = original.Path;
                http.Request.QueryString = original.QueryString;
                http.Request.PathBase = original.PathBase;
            }
        });
}
