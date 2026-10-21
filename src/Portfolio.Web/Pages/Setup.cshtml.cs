using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;

namespace Portfolio.Web.Pages;

/// <summary>
/// One-time creation of the owner account. The page does not exist (404) unless a Setup:Key is configured,
/// a database is available and no account has been created yet; once an account exists it stays closed.
/// </summary>
[AllowAnonymous]
[RequestSizeLimit(16 * 1024)]
public partial class SetupModel : PageModel
{
    private readonly OwnerAccountService _accounts;
    private readonly SetupKeyGate _gate;
    private readonly IConfiguration _config;
    private readonly ILogger<SetupModel> _logger;

    public SetupModel(OwnerAccountService accounts, SetupKeyGate gate, IConfiguration config, ILogger<SetupModel> logger)
    {
        _accounts = accounts;
        _gate = gate;
        _config = config;
        _logger = logger;
    }

    [BindProperty, DataType(DataType.Password), Display(Name = "Setup key")]
    public string SetupKey { get; set; } = "";

    [BindProperty, Display(Name = "User name")]
    public string UserName { get; set; } = "";

    [BindProperty, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [BindProperty, DataType(DataType.Password), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";

    [GeneratedRegex("^[A-Za-z0-9._@+-]{3,64}$")]
    private static partial Regex UserNamePattern();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) =>
        await IsOpenAsync(ct) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!await IsOpenAsync(ct)) return NotFound();

        switch (_gate.Check(SetupKey))
        {
            case SetupKeyResult.Disabled:
                return NotFound();
            case SetupKeyResult.Throttled:
                return Fail("Too many incorrect keys. Try again later.");
            case SetupKeyResult.Wrong:
                _logger.LogWarning("Setup attempted with an incorrect key.");
                return Fail("The setup key is not correct.");
        }

        var userName = UserName.Trim();
        if (!UserNamePattern().IsMatch(userName))
            ModelState.AddModelError(nameof(UserName), "Use 3 to 64 letters, digits or . _ @ + - characters.");
        if (PasswordPolicy.Validate(Password, userName) is { } passwordError)
            ModelState.AddModelError(nameof(Password), passwordError);
        else if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
            ModelState.AddModelError(nameof(ConfirmPassword), "The passwords do not match.");
        if (!ModelState.IsValid) return Page();

        switch (await _accounts.CreateAsync(userName, Password, ct))
        {
            case CreateOwnerOutcome.AlreadyExists:
                // Lost a race with another request (or the account appeared meanwhile): setup is now closed.
                return NotFound();
            default:
                _logger.LogInformation("Owner account created.");
                TempData["Flash"] = "Owner account created. Sign in to continue.";
                return Redirect(AdminAuth.LoginPath);
        }
    }

    private async Task<bool> IsOpenAsync(CancellationToken ct)
    {
        if (!_gate.IsConfigured || string.IsNullOrWhiteSpace(_config.GetConnectionString("Default"))) return false;
        try
        {
            return !await _accounts.AnyAsync(ct);
        }
        catch (Exception ex)
        {
            // Database unreachable or not migrated yet: behave as if there is no setup page.
            _logger.LogWarning(ex, "Could not check whether an owner account exists; /setup is unavailable.");
            return false;
        }
    }

    private IActionResult Fail(string message)
    {
        ModelState.AddModelError(string.Empty, message);
        SetupKey = Password = ConfirmPassword = "";
        return Page();
    }
}
