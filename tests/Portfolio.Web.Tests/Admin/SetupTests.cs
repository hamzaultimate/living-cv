using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Web.Security;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class SetupTests
{
    private static async Task<int> OwnerCountAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return await db.OwnerAccounts.CountAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short")]
    public async Task Setup_IsDisabled_WhenTheKeyIsMissingOrTooShort(string? key)
    {
        using var app = AdminApp.Create(key);
        var client = app.NewClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/setup")).StatusCode);

        var token = await AdminApp.GetTokenAsync(client, "/admin/login");
        var post = await AdminApp.PostFormAsync(client, "/setup", token, new Dictionary<string, string>
        {
            ["SetupKey"] = key ?? "", ["UserName"] = "owner", ["Password"] = AdminApp.Password, ["ConfirmPassword"] = AdminApp.Password,
        });
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(0, await OwnerCountAsync(app));
    }

    [Fact]
    public async Task Setup_WithTheWrongKey_CreatesNothing_AndNeverEchoesKeys()
    {
        using var app = new AdminApp();
        var client = app.NewClient();

        var response = await app.RunSetupAsync(client, key: "this-key-is-wrong-0000000");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("setup key is not correct", html);
        Assert.DoesNotContain("this-key-is-wrong-0000000", html);
        Assert.DoesNotContain(AdminApp.SetupKey, html);
        Assert.DoesNotContain(AdminApp.Password, html);
        Assert.Equal(0, await OwnerCountAsync(app));
    }

    [Fact]
    public async Task Setup_WithTheRightKey_CreatesTheOwner_ThenClosesForGood()
    {
        using var app = new AdminApp();
        var client = app.NewClient();

        var response = await app.RunSetupAsync(client);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // The first run continues into the setup wizard once the owner has signed in.
        Assert.Equal("/admin/login?ReturnUrl=%2Fadmin%2Fwelcome", response.Headers.Location?.OriginalString);
        Assert.Equal(1, await OwnerCountAsync(app));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/setup")).StatusCode);
        var token = await AdminApp.GetTokenAsync(client, "/admin/login");
        var again = await AdminApp.PostFormAsync(client, "/setup", token, new Dictionary<string, string>
        {
            ["SetupKey"] = AdminApp.SetupKey, ["UserName"] = "second", ["Password"] = "another long password 1", ["ConfirmPassword"] = "another long password 1",
        });
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(1, await OwnerCountAsync(app));
    }

    [Fact]
    public async Task Setup_RejectsAPostWithoutAnAntiforgeryToken()
    {
        using var app = new AdminApp();
        var client = app.NewClient();

        var response = await AdminApp.PostFormAsync(client, "/setup", token: null, new Dictionary<string, string>
        {
            ["SetupKey"] = AdminApp.SetupKey, ["UserName"] = "owner", ["Password"] = AdminApp.Password, ["ConfirmPassword"] = AdminApp.Password,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await OwnerCountAsync(app));
    }

    [Theory]
    [InlineData("owner", "short", "short", "at least 12")]
    [InlineData("owner", "correct horse battery staple", "different horse battery staple", "do not match")]
    [InlineData("ow", "correct horse battery staple", "correct horse battery staple", "3 to 64")]
    [InlineData("has space", "correct horse battery staple", "correct horse battery staple", "3 to 64")]
    [InlineData("owner-account-1", "owner-account-1", "owner-account-1", "same as the user name")]
    public async Task Setup_ValidatesTheUserNameAndPassword(string user, string password, string confirm, string expected)
    {
        using var app = new AdminApp();
        var client = app.NewClient();
        var token = await AdminApp.GetTokenAsync(client, "/setup");

        var response = await AdminApp.PostFormAsync(client, "/setup", token, new Dictionary<string, string>
        {
            ["SetupKey"] = AdminApp.SetupKey, ["UserName"] = user, ["Password"] = password, ["ConfirmPassword"] = confirm,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await OwnerCountAsync(app));
    }

    [Fact]
    public async Task Setup_ThrottlesKeyGuessing_EvenForTheCorrectKey_UntilTheWindowPasses()
    {
        using var app = new AdminApp();
        var client = app.NewClient();

        for (var i = 0; i < SetupKeyGate.MaxFailures; i++)
            await app.RunSetupAsync(client, key: "wrong-key-wrong-key-" + i);

        var blocked = await app.RunSetupAsync(client);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
        Assert.Contains("Too many", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(0, await OwnerCountAsync(app));

        app.Clock.Advance(SetupKeyGate.ThrottleDuration + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.Redirect, (await app.RunSetupAsync(client)).StatusCode);
        Assert.Equal(1, await OwnerCountAsync(app));
    }

    [Fact]
    public async Task Setup_ClosesWhenAnAccountAlreadyExists_EvenBeforeAnyRequestSeesIt()
    {
        using var app = new AdminApp();
        using (var scope = app.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<OwnerAccountService>();
            await service.CreateAsync("existing", "a long enough password");
        }

        Assert.Equal(HttpStatusCode.NotFound, (await app.NewClient().GetAsync("/setup")).StatusCode);
    }

    [Fact]
    public void SetupKeyGate_ComparesTheKeyExactly()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Setup:Key"] = AdminApp.SetupKey }).Build();
        var gate = new SetupKeyGate(config, new FakeClock(), Microsoft.Extensions.Logging.Abstractions.NullLogger<SetupKeyGate>.Instance);

        Assert.Equal(SetupKeyResult.Wrong, gate.Check(AdminApp.SetupKey + " "));
        Assert.Equal(SetupKeyResult.Wrong, gate.Check(AdminApp.SetupKey.ToUpperInvariant()));
        Assert.Equal(SetupKeyResult.Wrong, gate.Check(null));
        Assert.Equal(SetupKeyResult.Correct, gate.Check(AdminApp.SetupKey));
    }
}
