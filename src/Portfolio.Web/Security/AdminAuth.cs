using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Portfolio.Data.Entities;

namespace Portfolio.Web.Security;

/// <summary>Registration and pipeline wiring for owner sign-in and the /admin area.</summary>
public static class AdminAuth
{
    public const string StampClaim = "lcv:stamp";
    public const string LoginPath = "/admin/login";
    public const string AdminPath = "/admin";

    public static IServiceCollection AddOwnerAuth(this IServiceCollection services, IWebHostEnvironment env)
    {
        // Outside Development the cookies are Secure (HTTPS only) and use the __Host- prefix, which browsers
        // only accept with Secure, Path=/ and no Domain, so a sibling subdomain cannot set or shadow them.
        var secure = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        var prefix = env.IsDevelopment() ? "" : "__Host-";

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<OwnerPasswordHasher>();
        services.AddSingleton<SetupKeyGate>();
        services.AddScoped<OwnerAccountService>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = prefix + "lcv-admin";
                o.Cookie.Path = "/";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = secure;
                o.LoginPath = LoginPath;
                o.AccessDeniedPath = LoginPath;
                o.LogoutPath = AdminPath + "/logout";
                o.ReturnUrlParameter = "returnUrl";
                o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                o.SlidingExpiration = true;
                o.Events.OnValidatePrincipal = async ctx =>
                {
                    // Every request re-checks the stamp, so resetting the password ends all existing sessions.
                    var accounts = ctx.HttpContext.RequestServices.GetRequiredService<OwnerAccountService>();
                    if (!await accounts.IsStampCurrentAsync(ctx.Principal?.FindFirstValue(StampClaim), ctx.HttpContext.RequestAborted))
                    {
                        ctx.RejectPrincipal();
                        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            });

        services.AddAuthorization();

        services.AddAntiforgery(o =>
        {
            o.Cookie.Name = prefix + "lcv-af";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = secure;
        });
        services.Configure<CookieTempDataProviderOptions>(o =>
        {
            o.Cookie.Name = prefix + "lcv-flash";
            o.Cookie.SecurePolicy = secure;
            o.Cookie.SameSite = SameSiteMode.Strict;
        });
        return services;
    }

    public static ClaimsPrincipal CreatePrincipal(OwnerAccount account)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Name, account.UserName),
            new Claim(StampClaim, account.SecurityStamp),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Runs before output caching: marks /admin and /setup responses as private and non-indexable.
    /// </summary>
    public static IApplicationBuilder UsePrivateAreaHeaders(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            if (IsPrivateArea(ctx.Request.Path))
            {
                ctx.Response.OnStarting(() =>
                {
                    ctx.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                    ctx.Response.Headers.Pragma = "no-cache";
                    ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
                    return Task.CompletedTask;
                });
            }
            await next();
        });

    /// <summary>
    /// Deny by default: after routing, anything under /admin that is not explicitly [AllowAnonymous] needs a signed-in
    /// owner, including paths that match no page. This backs up the folder convention, so a page added
    /// under /Admin (or a misrouted request) can never be served anonymously.
    /// </summary>
    public static IApplicationBuilder UseAdminDenyByDefault(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments(AdminPath, StringComparison.OrdinalIgnoreCase)
                && ctx.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null
                && ctx.User.Identity?.IsAuthenticated != true)
            {
                await ctx.ChallengeAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }
            await next();
        });

    private static bool IsPrivateArea(PathString path) =>
        path.StartsWithSegments(AdminPath, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/setup", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The only place a post-login destination is accepted. Local paths inside /admin only: no absolute or
    /// protocol-relative URLs, no backslashes (browsers treat them as slashes), nothing outside the admin area.
    /// </summary>
    public static string SafeReturnUrl(IUrlHelper url, string? returnUrl)
    {
        const string fallback = AdminPath;
        if (string.IsNullOrWhiteSpace(returnUrl) || returnUrl.Length > 2048) return fallback;
        if (returnUrl.Any(char.IsControl) || returnUrl.Contains('\\')) return fallback;
        if (!url.IsLocalUrl(returnUrl)) return fallback;
        var path = returnUrl.AsSpan();
        var end = path.IndexOfAny('?', '#');
        if (end >= 0) path = path[..end];
        return new PathString(path.ToString()).StartsWithSegments(AdminPath, StringComparison.OrdinalIgnoreCase) ? returnUrl : fallback;
    }
}
