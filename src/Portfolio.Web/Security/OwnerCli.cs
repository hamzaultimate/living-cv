using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;

namespace Portfolio.Web.Security;

/// <summary>
/// <c>dotnet run --project src/Portfolio.Web -- reset-owner</c>: sets a new owner password from the server's own shell.
/// There is deliberately no web or email route for this; whoever can run the command already controls the deployment.
/// The command runs before the web host starts listening and exits when done.
/// </summary>
public static class OwnerCli
{
    public const string ResetCommand = "reset-owner";
    public const string PasswordEnvironmentVariable = "OWNER_NEW_PASSWORD";
    public const string PasswordStdinFlag = "--password-stdin";

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], ResetCommand, StringComparison.OrdinalIgnoreCase);

    /// <param name="readSecret">Reads a line without echoing it; receives the prompt text. Null selects the console.</param>
    /// <returns>Process exit code: 0 success, 1 rejected input or no owner, 2 unexpected failure.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextReader input, TextWriter output, TextWriter error,
        Func<string, string?>? readSecret = null, Func<string, string?>? readEnvironment = null)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;
        try
        {
            string? password = readEnvironment(PasswordEnvironmentVariable);
            if (string.IsNullOrEmpty(password) && args.Contains(PasswordStdinFlag, StringComparer.Ordinal))
                password = input.ReadLine();

            if (string.IsNullOrEmpty(password))
            {
                if (readSecret is null && Console.IsInputRedirected)
                {
                    await error.WriteLineAsync($"No terminal to prompt on. Pass {PasswordStdinFlag} and pipe the password, or set {PasswordEnvironmentVariable}.");
                    return 1;
                }
                readSecret ??= ReadSecretFromConsole;
                password = readSecret("New owner password: ");
                if (!string.Equals(password, readSecret("Repeat new password: "), StringComparison.Ordinal))
                {
                    await error.WriteLineAsync("The passwords do not match. Nothing was changed.");
                    return 1;
                }
            }

            if (PasswordPolicy.Validate(password) is { } problem)
            {
                await error.WriteLineAsync($"{problem} Nothing was changed.");
                return 1;
            }

            using var scope = services.CreateScope();
            var accounts = scope.ServiceProvider.GetRequiredService<OwnerAccountService>();
            if (!await accounts.ResetPasswordAsync(password!))
            {
                await error.WriteLineAsync("No owner account exists yet. Create it at /setup first.");
                return 1;
            }

            await output.WriteLineAsync("Owner password changed. Existing sessions are signed out and any lockout is cleared.");
            return 0;
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"Could not reset the owner password: {ex.GetType().Name}. Check that the database is configured and migrated (start the site once), then retry.");
            return 2;
        }
    }

    private static string? ReadSecretFromConsole(string prompt)
    {
        Console.Error.Write(prompt);
        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (buffer.Length > 0) buffer.Length--; }
            else if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return buffer.ToString();
    }
}
