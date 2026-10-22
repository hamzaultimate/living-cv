using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>
/// Enumerates the real endpoint table, so a page added under /Admin later is covered without touching this file.
/// </summary>
public class AdminAccessTests : IClassFixture<AdminApp>
{
    private readonly AdminApp _app;

    public AdminAccessTests(AdminApp app) => _app = app;

    private static string DescriptorPath(RouteEndpoint e) =>
        e.Metadata.GetMetadata<PageActionDescriptor>()?.ViewEnginePath ?? "";

    private List<RouteEndpoint> AdminEndpoints() =>
        _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => DescriptorPath(e).StartsWith("/Admin/", StringComparison.OrdinalIgnoreCase)
                        || e.RoutePattern.RawText?.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

    [Fact]
    public void EveryAdminPage_RequiresAuthorization_UnlessItIsExplicitlyAnonymous()
    {
        var endpoints = AdminEndpoints();
        Assert.NotEmpty(endpoints);

        var anonymous = endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(DescriptorPath).Distinct().OrderBy(x => x).ToList();
        Assert.Equal(["/Admin/Login"], anonymous);

        foreach (var e in endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null))
            Assert.True(e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(), $"{e.DisplayName} ({e.RoutePattern.RawText}) has no authorization metadata");
    }

    [Fact]
    public void EveryAdminPageModel_IsEitherAnonymousOnPurpose_OrReachableOnlyThroughAnAuthorizedEndpoint()
    {
        // The page models that endpoints actually exist for (pages may sit in subfolders such as /Admin/Welcome).
        var modelsWithEndpoints = AdminEndpoints()
            .Select(e => (e.Metadata.GetMetadata<PageActionDescriptor>() as CompiledPageActionDescriptor)?.ModelTypeInfo?.AsType())
            .Where(x => x is not null)
            .ToHashSet();
        var models = typeof(Program).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(PageModel).IsAssignableFrom(t) && t.Namespace?.StartsWith("Portfolio.Web.Pages.Admin", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(models);
        foreach (var model in models)
        {
            Assert.Contains(model, modelsWithEndpoints);
            var anonymous = model.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Length > 0;
            Assert.Equal(model.Name == "LoginModel", anonymous);
        }
    }

    [Fact]
    public void TheAdminAreaHasExactlyTheExpectedPages_SoANewPageIsAddedOnPurpose()
    {
        // Adding an admin page must be a conscious change here, next to the checks above that prove it is protected.
        var routes = AdminEndpoints().Select(e => e.RoutePattern.RawText!.ToLowerInvariant()).Distinct().OrderBy(x => x).ToList();

        Assert.Equal(
            ["admin", "admin/content",
             "admin/content/certifications", "admin/content/certifications/delete/{id:int}", "admin/content/certifications/edit/{id:int?}",
             "admin/content/experience", "admin/content/experience/delete/{id:int}", "admin/content/experience/edit/{id:int?}",
             "admin/content/projects", "admin/content/projects/delete/{id:int}", "admin/content/projects/edit/{id:int?}", "admin/content/projects/images/{id:int}",
             "admin/content/skills", "admin/content/skills/delete/{id:int}", "admin/content/skills/edit/{id:int?}",
             "admin/login", "admin/logout", "admin/photo", "admin/profile", "admin/template",
             "admin/welcome", "admin/welcome/basics", "admin/welcome/draft-photo", "admin/welcome/photo", "admin/welcome/review", "admin/welcome/thumb/{key}"],
            routes);
    }

    [Fact]
    public async Task TheTemplatePage_RejectsAnonymousPosts_WithoutChangingTheTemplate()
    {
        var client = _app.NewClient();

        var post = await client.PostAsync("/admin/template", new FormUrlEncodedContent(new Dictionary<string, string> { ["Key"] = "healthcare" }));

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Contains("/admin/login", post.Headers.Location!.OriginalString);
        using var db = _app.CreateDbContext();
        Assert.DoesNotContain(db.SiteProfiles.Select(p => p.TemplateKey), k => k == "healthcare");
    }

    [Fact]
    public async Task EveryProtectedAdminRoute_RedirectsAnonymousVisitorsToLogin_ForGetAndPost()
    {
        var client = _app.NewClient();
        var protectedRoutes = AdminEndpoints()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(e => e.RoutePattern.RawText!)
            .Distinct()
            .ToList();
        Assert.NotEmpty(protectedRoutes);

        foreach (var route in protectedRoutes)
        {
            var get = await client.GetAsync(route);
            Assert.True(get.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized, $"GET {route} returned {(int)get.StatusCode}");
            if (get.StatusCode == HttpStatusCode.Redirect)
                Assert.Contains("/admin/login", get.Headers.Location!.OriginalString);

            var post = await client.PostAsync(route, new FormUrlEncodedContent([]));
            Assert.True(post.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized, $"POST {route} returned {(int)post.StatusCode}");
        }
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/")]
    [InlineData("/ADMIN")]
    [InlineData("/Admin/Profile")]
    [InlineData("/admin/does-not-exist")]
    [InlineData("/admin/profile/extra")]
    [InlineData("/admin/photo.png")]
    [InlineData("/admin/logout")]
    [InlineData("/admin/template")]
    [InlineData("/admin/TEMPLATE?show=healthcare")]
    [InlineData("/admin/template?preview=healthcare")]
    public async Task UnknownAndMixedCaseAdminPaths_AreDeniedToo(string path)
    {
        var response = await _app.NewClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/login", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("/", HttpStatusCode.OK)]
    [InlineData("/about", HttpStatusCode.OK)]
    [InlineData("/admin/login", HttpStatusCode.OK)]
    public async Task PublicPages_StayPublic(string path, HttpStatusCode expected) =>
        Assert.Equal(expected, (await _app.NewClient().GetAsync(path)).StatusCode);
}
