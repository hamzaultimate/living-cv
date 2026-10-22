using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace Portfolio.Web.Services;

public static class NotFoundPage
{
    /// <summary>The page that renders the styled not-found response for a disabled section.</summary>
    public const string Path = "/not-found";

    internal const string GatedItem = "lcv:section-gated";

    /// <summary>
    /// Shows <see cref="Path"/> for the empty 404 that <see cref="SectionGateFilter"/> gives a page of a section the template does
    /// not have. Every other response is left exactly as it was: an unknown address, /setup and /admin keep their blank 404, and
    /// other status codes, non-GET requests and responses that carry a body (a missing project) are never turned into a page.
    /// </summary>
    public static IApplicationBuilder UseNotFoundPage(this IApplicationBuilder app) =>
        app.UseStatusCodePages(async context =>
        {
            var http = context.HttpContext;
            if (http.Response.StatusCode != StatusCodes.Status404NotFound
                || !http.Items.ContainsKey(GatedItem)
                || !(HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)))
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
