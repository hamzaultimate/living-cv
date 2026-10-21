using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Pages.Admin;

[AllowAnonymous]
[BodyLimit(16 * 1024)]
public class LoginModel : PageModel
{
    private readonly OwnerAccountService _accounts;
    private readonly ILogger<LoginModel> _logger;
    private readonly TimeProvider _time;
    private readonly KnownDevices _devices;

    public LoginModel(OwnerAccountService accounts, KnownDevices devices, ILogger<LoginModel> logger, TimeProvider time)
    {
        _accounts = accounts;
        _devices = devices;
        _logger = logger;
        _time = time;
    }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty, Display(Name = "User name")]
    public string UserName { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? LocalRedirect(AdminAuth.SafeReturnUrl(Url, ReturnUrl)) : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        // Oversized input is refused before any hashing; the message is the same as for any other failure.
        var plausible = UserName.Length is > 0 and <= OwnerAccount.MaxLength.UserName * 2
            && Password.Length is > 0 and <= PasswordPolicy.MaxLength;
        var context = new SignInContext(SignInThrottle.KeyFor(HttpContext.Connection.RemoteIpAddress), _devices.Read(HttpContext));
        var result = plausible ? await _accounts.SignInAsync(UserName, Password, context, ct) : new OwnerSignInResult(SignInOutcome.Failed);

        if (result.Outcome != SignInOutcome.Succeeded || result.Account is null)
        {
            _logger.LogWarning("Owner sign-in failed ({Outcome}).", result.Outcome);
            ModelState.AddModelError(string.Empty, "Sign-in failed. Check your details, or try again later.");
            Password = "";
            return Page();
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AdminAuth.CreatePrincipal(result.Account, _time.GetUtcNow()),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
        _devices.Append(HttpContext, result.Account);
        return LocalRedirect(AdminAuth.SafeReturnUrl(Url, ReturnUrl));
    }
}
