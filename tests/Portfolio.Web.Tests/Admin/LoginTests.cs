using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Portfolio.Web.Security;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class LoginTests
{
    private static async Task<(AdminApp App, HttpClient Client)> OwnerWithoutSessionAsync()
    {
        var app = new AdminApp();
        var client = app.NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await app.RunSetupAsync(client)).StatusCode);
        return (app, client);
    }

    private static string AlertText(string html)
    {
        var start = html.IndexOf("role=\"alert\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "No error shown");
        var end = html.IndexOf("</div>", start, StringComparison.Ordinal);
        return WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html[start..end], @"<[^>]+>|\s+", " ")).Trim();
    }

    [Fact]
    public async Task Login_Succeeds_AndSetsAHardenedSessionCookie()
    {
        var (app, client) = await OwnerWithoutSessionAsync();
        using var _ = app;

        var response = await AdminApp.SignInAsync(client);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin", response.Headers.Location?.OriginalString);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-lcv-admin=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);

        var device = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-lcv-device=", StringComparison.Ordinal));
        Assert.Contains("secure", device, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", device, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", device, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", device, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=", device, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", device, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task Login_Failure_GivesTheSameGenericMessage_ForWrongNameWrongPasswordAndLockout()
    {
        var (app, client) = await OwnerWithoutSessionAsync();
        using var _ = app;

        var wrongPassword = await AdminApp.SignInAsync(client, password: "not the right password");
        var wrongName = await AdminApp.SignInAsync(client, user: "nobody");
        for (var i = 0; i < OwnerAccountService.MaxFailedAttempts; i++)
            await AdminApp.SignInAsync(client, password: "not the right password");
        var locked = await AdminApp.SignInAsync(client);

        var messages = new[] { wrongPassword, wrongName, locked };
        Assert.All(messages, m => Assert.Equal(HttpStatusCode.OK, m.StatusCode));
        var texts = new List<string>();
        foreach (var m in messages) texts.Add(AlertText(await m.Content.ReadAsStringAsync()));
        Assert.Single(texts.Distinct());
        Assert.DoesNotContain("locked", texts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not found", texts[0], StringComparison.OrdinalIgnoreCase);
        Assert.False(wrongPassword.Headers.Contains("Set-Cookie") && wrongPassword.Headers.GetValues("Set-Cookie").Any(c => c.Contains("lcv-admin")));
    }

    [Fact]
    public async Task Login_LocksAfterRepeatedFailures_AndRecoversAfterTheWindow()
    {
        var (app, client) = await OwnerWithoutSessionAsync();
        using var _ = app;

        for (var i = 0; i < OwnerAccountService.MaxFailedAttempts; i++)
            await AdminApp.SignInAsync(client, password: "not the right password");

        Assert.Equal(HttpStatusCode.OK, (await AdminApp.SignInAsync(client)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin")).StatusCode);

        app.Clock.Advance(OwnerAccountService.LockoutDuration + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.Redirect, (await AdminApp.SignInAsync(client)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task Login_CannotBeUnlockedOrEvadedByNameVariants()
    {
        var (app, client) = await OwnerWithoutSessionAsync();
        using var _ = app;
        string[] variants = ["OWNER", " owner ", "Owner", "owner\t", "nobody"];

        foreach (var name in variants)
            await AdminApp.SignInAsync(client, user: name, password: "not the right password");

        Assert.Equal(HttpStatusCode.OK, (await AdminApp.SignInAsync(client, user: "OWNER")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task Login_RejectsAPostWithoutAnAntiforgeryToken()
    {
        var (app, client) = await OwnerWithoutSessionAsync();
        using var _ = app;

        var response = await AdminApp.PostFormAsync(client, "/admin/login", token: null, new Dictionary<string, string>
        {
            ["UserName"] = AdminApp.UserName, ["Password"] = AdminApp.Password,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_RefusesAnOversizedPasswordWithoutLockingOrErroring()
    {
        var (app, client) = await OwnerWithoutSessionAsync();
        using var _ = app;

        var response = await AdminApp.SignInAsync(client, password: new string('x', 5000));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sign-in failed", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/admin/profile", "/admin/profile")]
    [InlineData("/admin/profile?x=1", "/admin/profile?x=1")]
    [InlineData("/Admin/Photo", "/Admin/Photo")]
    [InlineData("//evil.example/admin", "/admin")]
    [InlineData("/\\evil.example", "/admin")]
    [InlineData("/\\/evil.example", "/admin")]
    [InlineData("https://evil.example/admin", "/admin")]
    [InlineData("http://localhost/admin", "/admin")]
    [InlineData("javascript:alert(1)", "/admin")]
    [InlineData("/", "/admin")]
    [InlineData("/about", "/admin")]
    [InlineData("/administrator", "/admin")]
    [InlineData("/admin\r\nSet-Cookie: a=b", "/admin")]
    [InlineData("", "/admin")]
    public async Task Login_RedirectsOnlyToLocalAdminPaths(string returnUrl, string expected)
    {
        var (app, client) = await OwnerWithoutSessionAsync();
        using var _ = app;
        var path = "/admin/login?returnUrl=" + Uri.EscapeDataString(returnUrl);
        var token = await AdminApp.GetTokenAsync(client, path);

        var response = await AdminApp.PostFormAsync(client, path, token, new Dictionary<string, string>
        {
            ["UserName"] = AdminApp.UserName, ["Password"] = AdminApp.Password,
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Anonymous_IsSentToLoginWithTheirDestination_AndReturnedThereAfterSigningIn()
    {
        var (app, client) = await OwnerWithoutSessionAsync();
        using var _ = app;

        var redirect = await client.GetAsync("/admin/profile");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        var location = redirect.Headers.Location!.OriginalString;
        Assert.StartsWith("https://localhost/admin/login?returnUrl=", location);

        var token = await AdminApp.GetTokenAsync(client, location);
        var login = await AdminApp.PostFormAsync(client, location, token, new Dictionary<string, string>
        {
            ["UserName"] = AdminApp.UserName, ["Password"] = AdminApp.Password,
        });
        Assert.Equal("/admin/profile", login.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Logout_IsPostOnly_RequiresAToken_AndEndsTheSession()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();

        var get = await client.GetAsync("/admin/logout");
        Assert.Equal(HttpStatusCode.Redirect, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);

        var noToken = await AdminApp.PostFormAsync(client, "/admin/logout", token: null, []);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);

        var token = await AdminApp.GetTokenAsync(client, "/admin");
        var post = await AdminApp.PostFormAsync(client, "/admin/logout", token, []);
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task Logout_StillSignsOut_WhenTheStampCannotBeRotated()
    {
        using var database = new TestDatabase(createSchema: false);
        var services = new ServiceCollection().AddSingleton<Microsoft.AspNetCore.Authentication.IAuthenticationService, RecordingAuthenticationService>().BuildServiceProvider();
        var accounts = new OwnerAccountService(database.CreateContext(), new OwnerPasswordHasher(), new SignInGate(),
            new SignInThrottle(TimeProvider.System), null!, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<OwnerAccountService>.Instance);
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = services };
        var model = new Portfolio.Web.Pages.Admin.LogoutModel(accounts, Microsoft.Extensions.Logging.Abstractions.NullLogger<Portfolio.Web.Pages.Admin.LogoutModel>.Instance)
        {
            PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NullTempDataProvider()),
        };

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.True(((RecordingAuthenticationService)services.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationService>()).SignedOut);
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(Microsoft.AspNetCore.Http.HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(Microsoft.AspNetCore.Http.HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class RecordingAuthenticationService : Microsoft.AspNetCore.Authentication.IAuthenticationService
    {
        public bool SignedOut { get; private set; }
        public Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> AuthenticateAsync(Microsoft.AspNetCore.Http.HttpContext context, string? scheme) => throw new NotSupportedException();
        public Task ChallengeAsync(Microsoft.AspNetCore.Http.HttpContext context, string? scheme, Microsoft.AspNetCore.Authentication.AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task ForbidAsync(Microsoft.AspNetCore.Http.HttpContext context, string? scheme, Microsoft.AspNetCore.Authentication.AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignInAsync(Microsoft.AspNetCore.Http.HttpContext context, string? scheme, System.Security.Claims.ClaimsPrincipal principal, Microsoft.AspNetCore.Authentication.AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignOutAsync(Microsoft.AspNetCore.Http.HttpContext context, string? scheme, Microsoft.AspNetCore.Authentication.AuthenticationProperties? properties)
        {
            SignedOut = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ResettingThePassword_EndsExistingSessions()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);

        using (var scope = app.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<OwnerAccountService>().ResetPasswordAsync("a brand new long password"));

        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task ASession_EndsAfterTheAbsoluteLimit_EvenIfTheCookieIsStillFresh()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();

        // An active owner keeps sliding the cookie forward...
        for (var elapsed = TimeSpan.Zero; elapsed < AdminAuth.MaxSessionAge - TimeSpan.FromMinutes(25); elapsed += TimeSpan.FromMinutes(20))
        {
            app.Clock.Advance(TimeSpan.FromMinutes(20));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);
        }

        // ...but only up to the cap.
        // (the last refresh was at 11h40, so the cookie itself is still within its 30 minutes at 12h05)
        app.Clock.Advance(TimeSpan.FromMinutes(25));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task PublicPages_ServeNormally_ForASignedInOwner()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/about")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task OnlyAdminRequests_SlideTheSessionCookie()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();
        static bool Renewed(HttpResponseMessage r) =>
            r.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("__Host-lcv-admin=", StringComparison.Ordinal));

        app.Clock.Advance(TimeSpan.FromMinutes(20));
        Assert.False(Renewed(await client.GetAsync("/about")));
        Assert.False(Renewed(await client.GetAsync("/")));
        Assert.True(Renewed(await client.GetAsync("/admin")));
    }

    [Fact]
    public void PrivateCookies_AreScopedToTheWholeSite_SoTheHostPrefixIsAccepted()
    {
        using var app = new AdminApp();

        Assert.Equal("/", app.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.Path);
        Assert.Equal("/", app.Services.GetRequiredService<IOptions<CookieTempDataProviderOptions>>().Value.Cookie.Path);
    }

    private static bool SignedIn(HttpResponseMessage r) => r.StatusCode == HttpStatusCode.Redirect;

    [Fact]
    public async Task AStrangerLockingTheAccount_DoesNotLockTheOwnersTrustedBrowser()
    {
        using var app = new AdminApp();
        var owner = app.NewClient("192.0.2.10");
        await app.RunSetupAsync(owner);
        Assert.True(SignedIn(await AdminApp.SignInAsync(owner)));   // this browser now holds the device cookie
        var logout = await AdminApp.PostFormAsync(owner, "/admin/logout", await AdminApp.GetTokenAsync(owner, "/admin"), []);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);

        var stranger = app.NewClient("198.51.100.77");
        for (var i = 0; i < OwnerAccountService.MaxFailedAttempts; i++)
            await AdminApp.SignInAsync(stranger, password: "not the right password");

        // A browser without the cookie is locked out, even with the right password...
        Assert.False(SignedIn(await AdminApp.SignInAsync(app.NewClient("203.0.113.5"))));
        // ...but the owner's own browser still gets in, and that clears the lockout for everyone.
        Assert.True(SignedIn(await AdminApp.SignInAsync(owner)));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/admin")).StatusCode);
        Assert.True(SignedIn(await AdminApp.SignInAsync(app.NewClient("203.0.113.5"))));
    }

    [Fact]
    public async Task ATrustedBrowser_StillNeedsTheRightPassword()
    {
        using var app = new AdminApp();
        var owner = app.NewClient("192.0.2.10");
        await app.RunSetupAsync(owner);
        Assert.True(SignedIn(await AdminApp.SignInAsync(owner)));
        await AdminApp.PostFormAsync(owner, "/admin/logout", await AdminApp.GetTokenAsync(owner, "/admin"), []);

        Assert.False(SignedIn(await AdminApp.SignInAsync(owner, password: "not the right password")));
        Assert.Equal(HttpStatusCode.Redirect, (await owner.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task ResettingThePassword_RevokesTrustedBrowsers()
    {
        using var app = new AdminApp();
        var owner = app.NewClient("192.0.2.10");
        await app.RunSetupAsync(owner);
        Assert.True(SignedIn(await AdminApp.SignInAsync(owner)));

        const string newPassword = "a brand new long password";
        using (var scope = app.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<OwnerAccountService>().ResetPasswordAsync(newPassword));
        var stranger = app.NewClient("198.51.100.77");
        for (var i = 0; i < OwnerAccountService.MaxFailedAttempts; i++)
            await AdminApp.SignInAsync(stranger, password: "not the right password");

        // The old device cookie no longer counts, so the lockout applies to this browser too.
        Assert.False(SignedIn(await AdminApp.SignInAsync(owner, password: newPassword)));
    }

    [Fact]
    public async Task OneAddress_IsThrottled_AfterTooManyAttempts_AndRecoversAfterTheWindow()
    {
        using var app = new AdminApp();
        await app.RunSetupAsync(app.NewClient());
        var flooder = app.NewClient("198.51.100.77");

        for (var i = 0; i < SignInThrottle.MaxAttempts; i++)
            await AdminApp.SignInAsync(flooder, password: "not the right password");

        Assert.False(SignedIn(await AdminApp.SignInAsync(flooder)));
        // Another address is unaffected by the flood (it only meets the account lockout the first five attempts caused).
        Assert.False(SignedIn(await AdminApp.SignInAsync(app.NewClient("192.0.2.10"), password: "wrong")));

        app.Clock.Advance(SignInThrottle.Window + TimeSpan.FromSeconds(1));
        Assert.True(SignedIn(await AdminApp.SignInAsync(flooder)));
    }

    [Fact]
    public async Task SigningOut_EndsEveryOtherSessionOfTheAccount()
    {
        using var app = new AdminApp();
        var first = await app.SignedInClientAsync();
        var second = app.NewClient();
        Assert.True(SignedIn(await AdminApp.SignInAsync(second)));
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/admin")).StatusCode);

        var token = await AdminApp.GetTokenAsync(first, "/admin");
        Assert.Equal(HttpStatusCode.Redirect, (await AdminApp.PostFormAsync(first, "/admin/logout", token, [])).StatusCode);

        Assert.Equal(HttpStatusCode.Redirect, (await second.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task AdminPages_CarryARestrictiveContentSecurityPolicy()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();

        foreach (var path in new[] { "/admin", "/admin/login", "/admin/profile", "/admin/photo" })
        {
            var csp = string.Join(";", (await client.GetAsync(path)).Headers.GetValues("Content-Security-Policy"));
            Assert.Contains("default-src 'none'", csp);
            Assert.Contains("frame-ancestors 'none'", csp);
            Assert.DoesNotContain("unsafe-inline", csp);
        }
        Assert.DoesNotContain("style=", await client.GetStringAsync("/admin/profile"));
    }

    [Fact]
    public async Task AdminResponses_AreNeverCached_AndNotIndexable()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();

        foreach (var path in new[] { "/admin", "/admin/login", "/admin/profile" })
        {
            var response = await client.GetAsync(path);
            Assert.Contains("no-store", string.Join(",", response.Headers.GetValues("Cache-Control")));
            Assert.Contains("noindex", string.Join(",", response.Headers.GetValues("X-Robots-Tag")));
            if (response.Content.Headers.ContentType?.MediaType == "text/html")
                Assert.Contains("<meta name=\"robots\" content=\"noindex", await response.Content.ReadAsStringAsync());
        }

        var anonymous = await app.NewClient().GetAsync("/admin/anything");
        Assert.Contains("no-store", string.Join(",", anonymous.Headers.GetValues("Cache-Control")));
    }

    [Fact]
    public async Task Robots_DisallowsAdmin_AndSitemapOmitsIt()
    {
        using var app = new AdminApp();
        var client = app.NewClient();

        var robots = await client.GetStringAsync("/robots.txt");
        var sitemap = await client.GetStringAsync("/sitemap.xml");

        Assert.Contains("Disallow: /admin", robots);
        Assert.DoesNotContain("/admin", sitemap);
        Assert.DoesNotContain("/setup", sitemap);
    }
}
