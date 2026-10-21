using System.Net;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task PublicPages_DoNotNeedTheDatabaseToAcceptAnOwnersCookie()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/about")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
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
