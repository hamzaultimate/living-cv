using System.Net;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>Criterion 9: signed-in admin pages may load first-party script, the login page may not, and nothing is ever inline.</summary>
public class AdminCspTests
{
    [Fact]
    public async Task TheLoginPage_AllowsNoScript()
    {
        using var app = new AdminApp();
        var client = app.NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await app.RunSetupAsync(client)).StatusCode);

        var response = await client.GetAsync("/admin/login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csp = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
        Assert.DoesNotContain("script-src", csp);
        Assert.Contains("default-src 'none'", csp);
    }

    [Theory]
    [InlineData("/admin/login", false)]
    [InlineData("/Admin/Login", false)]
    [InlineData("/ADMIN/LOGIN", false)]
    [InlineData("/admin/login/", false)]
    [InlineData("/admin/login/anything", false)]
    [InlineData("/admin/loginx", true)]   // not the login page, so a prefix match would be wrong
    [InlineData("/admin/logout", true)]
    public async Task OnlyTheLoginPageAndWhatLiesBelowIt_AllowNoScript(string path, bool allowsScript)
    {
        using var app = new AdminApp();
        var client = app.NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await app.RunSetupAsync(client)).StatusCode);

        var response = await client.GetAsync(path);

        var csp = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Equal(allowsScript, csp.Contains("script-src 'self'"));
        Assert.Contains("default-src 'none'", csp);
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/profile")]
    [InlineData("/admin/photo")]
    [InlineData("/admin/content")]
    [InlineData("/admin/sections")]
    [InlineData("/admin/template")]
    [InlineData("/admin/transfer")]
    [InlineData("/admin/welcome")]
    public async Task EverySignedInAdminPage_AllowsScriptFromItsOwnOrigin_AndNoInlineScript(string path)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var response = await owner.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csp = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
    }
}
