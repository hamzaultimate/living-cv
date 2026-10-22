using Microsoft.Extensions.DependencyInjection;
using Portfolio.Web.Security;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class ResetOwnerCommandTests
{
    private const string NewPassword = "a brand new long password";

    private static async Task<(int Code, string Out, string Err)> RunAsync(AdminApp app, string[] args, string stdin = "",
        Func<string, string?>? secret = null, string? envPassword = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await OwnerCli.RunAsync(app.Services, args, new StringReader(stdin), output, error, secret,
            name => name == OwnerCli.PasswordEnvironmentVariable ? envPassword : null);
        return (code, output.ToString(), error.ToString());
    }

    private static async Task<SignInOutcome> TrySignInAsync(AdminApp app, string password)
    {
        using var scope = app.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<OwnerAccountService>().SignInAsync(AdminApp.UserName, password)).Outcome;
    }

    private static async Task CreateOwnerAsync(AdminApp app)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OwnerAccountService>().CreateAsync(AdminApp.UserName, AdminApp.Password);
    }

    [Fact]
    public void OnlyTheResetCommandIsRecognised()
    {
        Assert.True(OwnerCli.IsCommand(["reset-owner"]));
        Assert.True(OwnerCli.IsCommand(["RESET-OWNER", "--password-stdin"]));
        Assert.False(OwnerCli.IsCommand([]));
        Assert.False(OwnerCli.IsCommand(["--urls", "http://localhost:5000"]));
    }

    [Fact]
    public async Task WithoutAnOwner_ItExplainsAndChangesNothing()
    {
        using var app = new AdminApp();

        var (code, _, err) = await RunAsync(app, ["reset-owner", "--password-stdin"], NewPassword + "\n");

        Assert.Equal(1, code);
        Assert.Contains("/setup", err);
    }

    [Fact]
    public async Task FromStdin_ChangesThePassword_AndClearsLockout()
    {
        using var app = new AdminApp();
        await CreateOwnerAsync(app);
        for (var i = 0; i < OwnerAccountService.MaxFailedAttempts; i++) await TrySignInAsync(app, "wrong password");
        Assert.Equal(SignInOutcome.LockedOut, await TrySignInAsync(app, AdminApp.Password));

        var (code, output, err) = await RunAsync(app, ["reset-owner", "--password-stdin"], NewPassword + "\n");

        Assert.Equal(0, code);
        Assert.DoesNotContain(NewPassword, output + err);
        Assert.Contains($"user name: {AdminApp.UserName}", output);
        Assert.Equal(SignInOutcome.Failed, await TrySignInAsync(app, AdminApp.Password));
        Assert.Equal(SignInOutcome.Succeeded, await TrySignInAsync(app, NewPassword));
    }

    [Fact]
    public async Task FromTheEnvironment_ChangesThePassword()
    {
        using var app = new AdminApp();
        await CreateOwnerAsync(app);

        var (code, _, _) = await RunAsync(app, ["reset-owner"], envPassword: NewPassword);

        Assert.Equal(0, code);
        Assert.Equal(SignInOutcome.Succeeded, await TrySignInAsync(app, NewPassword));
    }

    [Fact]
    public async Task FromThePrompt_NeedsTheSamePasswordTwice()
    {
        using var app = new AdminApp();
        await CreateOwnerAsync(app);
        var answers = new Queue<string>(["first password value", "second password value"]);

        var (code, _, err) = await RunAsync(app, ["reset-owner"], secret: _ => answers.Dequeue());

        Assert.Equal(1, code);
        Assert.Contains("do not match", err);
        Assert.Equal(SignInOutcome.Succeeded, await TrySignInAsync(app, AdminApp.Password));
    }

    [Fact]
    public async Task ATooShortPassword_IsRejectedAndNothingChanges()
    {
        using var app = new AdminApp();
        await CreateOwnerAsync(app);

        var (code, _, err) = await RunAsync(app, ["reset-owner", "--password-stdin"], "short\n");

        Assert.Equal(1, code);
        Assert.Contains("at least 12", err);
        Assert.Equal(SignInOutcome.Succeeded, await TrySignInAsync(app, AdminApp.Password));
    }
}
