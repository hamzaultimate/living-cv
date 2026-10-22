using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Security;

namespace Portfolio.Web.Pages.Admin;

[BodyLimit(16 * 1024)]
public class LogoutModel : AdminPageModel
{
    private readonly OwnerAccountService _accounts;

    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(OwnerAccountService accounts, ILogger<LogoutModel> logger)
    {
        _accounts = accounts;
        _logger = logger;
    }

    // Signing out changes state, so it is POST-only (and antiforgery-protected); a GET just goes home.
    public IActionResult OnGet() => RedirectToPage("/Admin/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        // Clearing the browser's cookie does not revoke a copy of it, so also change the stamp: every other
        // session cookie for the account stops working. Trusted-device cookies are unaffected (see KnownDevices).
        // If the database is unavailable the sign-out still goes out: the cookie is already cleared above.
        try
        {
            await _accounts.RotateStampAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not rotate the security stamp on sign-out; other sessions stay valid until it is changed.");
        }
        Flash("You have been signed out.");
        return RedirectToPage("/Admin/Login");
    }
}
