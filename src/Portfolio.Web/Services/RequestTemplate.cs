using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using Portfolio.Web.Security;

namespace Portfolio.Web.Services;

/// <summary>The template in effect for the current request: the saved one, or the owner's unsaved preview. Resolved once per request.</summary>
public class RequestTemplate
{
    /// <summary>Query parameter that previews a template; honoured only for the signed-in owner (see <see cref="TemplatePreview"/>).</summary>
    public const string PreviewParameter = "preview";

    internal const string PreviewItem = "lcv:template-preview";

    private readonly ISiteProfileProvider _profile;
    private readonly TemplateResolver _resolver;
    private readonly IHttpContextAccessor _http;
    private Task<TemplateView>? _view;

    public RequestTemplate(ISiteProfileProvider profile, TemplateResolver resolver, IHttpContextAccessor http)
    {
        _profile = profile;
        _resolver = resolver;
        _http = http;
    }

    public Task<TemplateView> GetAsync() => _view ??= LoadAsync();

    private async Task<TemplateView> LoadAsync()
    {
        var http = _http.HttpContext;
        var site = await _profile.GetAsync(http?.RequestAborted ?? default);
        return _resolver.Resolve(site, http?.Items[PreviewItem] as string);
    }
}

/// <summary>Marks a page as the public face of a section: it answers 404 while the template has that section disabled.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SectionPageAttribute : Attribute
{
    public SectionPageAttribute(string section) => Section = section;

    public string Section { get; }
}

/// <summary>Turns a request for a disabled section's page into a 404. Pages opt in with <c>@attribute [SectionPage("...")]</c>.</summary>
public class SectionGateFilter : IAsyncPageFilter
{
    private readonly RequestTemplate _template;

    public SectionGateFilter(RequestTemplate template) => _template = template;

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var gate = context.ActionDescriptor.EndpointMetadata.OfType<SectionPageAttribute>().FirstOrDefault();
        if (gate is not null && !(await _template.GetAsync()).IsEnabled(gate.Section))
        {
            context.Result = new NotFoundResult();
            return;
        }
        await next();
    }
}

public static class TemplatePreview
{
    /// <summary>
    /// Lets the signed-in owner view any public page as another template with <c>?preview=&lt;key&gt;</c>, without saving.
    /// Anyone else (anonymous, a stale session, an unknown key) gets the normal page and nothing reveals that a preview
    /// exists. A preview response is never indexed, cached or stored, and may be framed by the admin area.
    /// </summary>
    public static IApplicationBuilder UseTemplatePreview(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            if (HttpMethods.IsGet(ctx.Request.Method)
                && ctx.Request.Query.TryGetValue(RequestTemplate.PreviewParameter, out var values) && values.Count == 1
                && ctx.GetEndpoint()?.Metadata.GetMetadata<PageActionDescriptor>() is { } page
                && !page.RelativePath.StartsWith("/Pages/Admin/", StringComparison.OrdinalIgnoreCase)
                && page.RelativePath != "/Pages/Setup.cshtml"
                && ctx.RequestServices.GetRequiredService<ITemplateCatalog>().TryGet(values[0], out var template)
                && await AdminAuth.IsOwnerSessionAsync(ctx))
            {
                ctx.Items[RequestTemplate.PreviewItem] = template.Key;
                if (ctx.Features.Get<IOutputCacheFeature>() is { } cache) cache.Context.AllowCacheStorage = false;
                ctx.Response.OnStarting(() =>
                {
                    ctx.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                    ctx.Response.Headers.Pragma = "no-cache";
                    ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
                    ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
                    return Task.CompletedTask;
                });
            }
            await next();
        });
}
