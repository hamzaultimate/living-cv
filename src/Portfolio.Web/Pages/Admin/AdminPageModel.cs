using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Portfolio.Web.Pages.Admin;

/// <summary>Shared by the admin pages: flash messages shown once by the admin layout.</summary>
public abstract class AdminPageModel : PageModel
{
    public const string FlashKey = "Flash";
    public const string FlashErrorKey = "FlashError";

    protected void Flash(string message) => TempData[FlashKey] = message;

    protected void FlashError(string message) => TempData[FlashErrorKey] = message;
}
