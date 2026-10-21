using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Security;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Pages.Admin;

[BodyLimit(16 * 1024)]
public class LogoutModel : AdminPageModel
{
    private readonly OwnerAccountService _accounts;

    public LogoutModel(OwnerAccountService accounts) => _accounts = accounts;

    // Signing out changes state, so it is POST-only (and antiforgery-protected); a GET just goes home.
    public IActionResult OnGet() => RedirectToPage("/Admin/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        // Clearing the browser's cookie does not revoke a copy of it, so also change the stamp: every other
        // session cookie for the account stops working. Trusted-device cookies are unaffected (see KnownDevices).
        await _accounts.RotateStampAsync(CancellationToken.None);
        Flash("You have been signed out.");
        return RedirectToPage("/Admin/Login");
    }
}
