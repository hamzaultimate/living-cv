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
    public const string SignedInClaim = "lcv:signed-in";
    public static readonly TimeSpan MaxSessionAge = TimeSpan.FromHours(12);
    public const string LoginPath = "/admin/login";
    public const string AdminPath = "/admin";
    public const string TemplatePath = "/admin/template";
    public const string WelcomePath = "/admin/welcome";
    public const string WelcomeReviewPath = "/admin/welcome/review";
    public const string ContentPath = "/admin/content";

    public static IServiceCollection AddOwnerAuth(this IServiceCollection services, IWebHostEnvironment env)
    {
        // Outside Development the cookies are Secure (HTTPS only) and use the __Host- prefix, which browsers
        // only accept with Secure, Path=/ and no Domain, so a sibling subdomain cannot set or shadow them.
        var secure = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        var prefix = env.IsDevelopment() ? "" : "__Host-";

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<OwnerPasswordHasher>();
        services.AddSingleton<SetupKeyGate>();
        services.AddSingleton<SignInGate>();
        services.AddSingleton<SignInThrottle>();
        services.AddSingleton(sp => new KnownDevices(sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(),
            sp.GetRequiredService<TimeProvider>(), prefix + "lcv-device", alwaysSecure: !env.IsDevelopment()));
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
                // The framework's own sliding renewal would also fire on public pages (a visit to /about would extend an
                // admin session), and a handler cannot cancel it from the validate event. So it is off, and the admin
                // area renews the cookie itself below.
                o.SlidingExpiration = false;
                o.Events.OnValidatePrincipal = async ctx =>
                {
                    // Only the admin area acts on the session, so only admin requests pay for the check.
                    if (!ctx.Request.Path.StartsWithSegments(AdminPath, StringComparison.OrdinalIgnoreCase)) return;
                    if (!await IsSessionValidAsync(ctx.HttpContext, ctx.Principal))
                    {
                        ctx.RejectPrincipal();
                        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        return;
                    }

                    // Slide the window once more than half of it has gone, as the framework does (admin requests only).
                    var now = ctx.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
                    if (ctx.Properties.IssuedUtc is { } issued && now - issued > o.ExpireTimeSpan / 2)
                        ctx.ShouldRenew = true;
                };
            });

        services.AddAuthorization();

        services.AddAntiforgery(o =>
        {
            o.Cookie.Name = prefix + "lcv-af";
            o.Cookie.Path = "/";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = secure;
        });
        services.Configure<CookieTempDataProviderOptions>(o =>
        {
            o.Cookie.Name = prefix + "lcv-flash";
            o.Cookie.Path = "/";
            o.Cookie.SecurePolicy = secure;
            o.Cookie.SameSite = SameSiteMode.Strict;
        });
        return services;
    }

    public static ClaimsPrincipal CreatePrincipal(OwnerAccount account, DateTimeOffset signedInAt)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Name, account.UserName),
            new Claim(StampClaim, account.SecurityStamp),
            new Claim(SignedInClaim, signedInAt.ToUnixTimeSeconds().ToString()),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>True for a signed-in owner whose session is still valid (current stamp, within the age cap). Public pages never
    /// validate the cookie themselves, so anything that grants the owner extra access outside /admin must call this.</summary>
    public static async Task<bool> IsOwnerSessionAsync(HttpContext http) =>
        http.User.Identity?.IsAuthenticated == true && await IsSessionValidAsync(http, http.User);

    /// <summary>
    /// A session is valid while its stamp still matches the account (it changes on password reset) and it is younger
    /// than <see cref="MaxSessionAge"/>: the cookie slides while the owner is active, but never past that cap.
    /// Anything unexpected, such as the database being unreachable, counts as invalid.
    /// </summary>
    private static async Task<bool> IsSessionValidAsync(HttpContext http, ClaimsPrincipal? principal)
    {
        try
        {
            if (!long.TryParse(principal?.FindFirstValue(SignedInClaim), out var signedIn)) return false;
            var age = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(signedIn);
            if (age > MaxSessionAge || age < TimeSpan.FromMinutes(-5)) return false;

            var accounts = http.RequestServices.GetRequiredService<OwnerAccountService>();
            return await accounts.IsStampCurrentAsync(principal?.FindFirstValue(StampClaim), http.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            http.RequestServices.GetRequiredService<ILogger<OwnerAccountService>>().LogWarning(ex, "Could not validate the admin session; treating it as signed out.");
            return false;
        }
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
                    // These pages load only their own stylesheet and photos and run no script of their own, except the content edit forms,
                    // which may load one first-party file (the unsaved changes warning) and nothing inline.
                    // The template page and the wizard's review step also frame the public site (same origin) to preview a template or the draft.
                    var frames = ctx.Request.Path.StartsWithSegments(TemplatePath, StringComparison.OrdinalIgnoreCase)
                                 || ctx.Request.Path.StartsWithSegments(WelcomeReviewPath, StringComparison.OrdinalIgnoreCase) ? " frame-src 'self';" : "";
                    ctx.Response.Headers.ContentSecurityPolicy =
                        $"default-src 'none'; style-src 'self';{(AllowsScript(ctx.Request.Path) ? " script-src 'self';" : "")} img-src 'self'; form-action 'self';{frames} frame-ancestors 'none'; base-uri 'none'";
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

    /// <summary>Only the content edit forms (<c>/admin/content/{section}/edit</c>) load a script: a first-party file that warns about unsaved changes.</summary>
    internal static bool AllowsScript(PathString path) =>
        path.StartsWithSegments(ContentPath, StringComparison.OrdinalIgnoreCase)
        && path.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(3).FirstOrDefault()?.Equals("edit", StringComparison.OrdinalIgnoreCase) == true;

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
