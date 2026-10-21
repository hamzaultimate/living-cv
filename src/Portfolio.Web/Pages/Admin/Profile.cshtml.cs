using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin;

// A profile form is a few kilobytes of text; anything much larger is not from this page.
[BodyLimit(256 * 1024)]
public class ProfileModel : AdminPageModel
{
    private readonly ProfileEditor _editor;

    public ProfileModel(ProfileEditor editor) => _editor = editor;

    [BindProperty]
    public ProfileForm Form { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct) => Form = ProfileForm.From(await _editor.LoadAsync(ct));

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Form.PadRows();
        if (!ModelState.IsValid) return Page();

        await _editor.SaveAsync(Form, ct);
        Flash("Profile saved. The public pages, CV, share image and sitemap now use it.");
        return RedirectToPage();
    }
}
