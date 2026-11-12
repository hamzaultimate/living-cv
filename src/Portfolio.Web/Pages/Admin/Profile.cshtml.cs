using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin;

// A profile form is a few kilobytes of text; anything much larger is not from this page.
[BodyLimit(256 * 1024)]
public class ProfileModel : AdminPageModel
{
    /// <summary>The identity page names what was refused as "the profile": a product word, not one of the template's.</summary>
    public const string ConflictMessage = "The profile was changed somewhere else (another tab or session) since you opened it, so nothing was saved. Reload the page to see the latest version.";

    private readonly ProfileEditor _editor;
    private readonly RequestTemplate _template;

    public ProfileModel(ProfileEditor editor, RequestTemplate template)
    {
        _editor = editor;
        _template = template;
    }

    [BindProperty]
    public ProfileForm Form { get; set; } = new();

    /// <summary>Where the rest of the profile is edited, in the template's words.</summary>
    public IReadOnlyList<(string Url, string Heading)> Parts { get; private set; } = [];

    /// <summary>The message after a save redirect takes keyboard focus, so it is announced.</summary>
    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context) => ViewData["FocusFlash"] = true;

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadPartsAsync();
        Form = ProfileForm.From(await _editor.LoadAsync(ct));
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadPartsAsync();
        if (!ModelState.IsValid) return Page();

        if (await _editor.SaveAsync(Form, Form.Stamp, ct) != SaveOutcome.Saved)
        {
            ModelState.AddModelError(string.Empty, ConflictMessage);
            ViewData["Conflict"] = true;
            return Page();
        }
        Flash("Profile saved. The public pages, CV, share image and sitemap now use it.");
        return RedirectToPage();
    }

    private async Task LoadPartsAsync()
    {
        Parts = EditorView.ProfileLinks(await _template.GetAsync());
    }
}
