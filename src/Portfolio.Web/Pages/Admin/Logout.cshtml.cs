using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Pages.Admin;

[BodyLimit(16 * 1024)]
public class LogoutModel : AdminPageModel
{
    // Signing out changes state, so it is POST-only (and antiforgery-protected); a GET just goes home.
    public IActionResult OnGet() => RedirectToPage("/Admin/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        Flash("You have been signed out.");
        return RedirectToPage("/Admin/Login");
    }
}
