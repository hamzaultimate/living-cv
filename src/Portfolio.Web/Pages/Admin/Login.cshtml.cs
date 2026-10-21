using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;

namespace Portfolio.Web.Pages.Admin;

[AllowAnonymous]
[RequestSizeLimit(16 * 1024)]
public class LoginModel : PageModel
{
    private readonly OwnerAccountService _accounts;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(OwnerAccountService accounts, ILogger<LoginModel> logger)
    {
        _accounts = accounts;
        _logger = logger;
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
        var result = plausible ? await _accounts.SignInAsync(UserName, Password, ct) : new OwnerSignInResult(SignInOutcome.Failed);

        if (result.Outcome != SignInOutcome.Succeeded || result.Account is null)
        {
            _logger.LogWarning("Owner sign-in failed ({Outcome}).", result.Outcome);
            ModelState.AddModelError(string.Empty, "Sign-in failed. Check your details, or try again later.");
            Password = "";
            return Page();
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AdminAuth.CreatePrincipal(result.Account),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
        return LocalRedirect(AdminAuth.SafeReturnUrl(Url, ReturnUrl));
    }
}
