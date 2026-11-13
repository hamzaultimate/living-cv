using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Security;
using Portfolio.Web.Services;
using Portfolio.Web.Transfer;

namespace Portfolio.Web.Pages.Admin.Transfer;

/// <summary>
/// Applies a held preview. The post carries only the preview's token (and the antiforgery token): what is written is what the preview showed, nothing the browser sends. The token
/// works once, from the session that made the preview, for ten minutes. A GET of this address changes nothing: it sends the visitor back to the import page.
/// </summary>
// A token and the antiforgery token; anything much larger is not from this page.
[BodyLimit(16 * 1024)]
public class ConfirmModel : AdminPageModel
{
    private readonly ImportService _import;
    private readonly RequestTemplate _template;

    public ConfirmModel(ImportService import, RequestTemplate template)
    {
        _import = import;
        _template = template;
    }

    public EditorView View { get; private set; } = null!;

    [BindProperty]
    public string? Token { get; set; }

    public ImportResult? Result { get; private set; }

    /// <summary>Why nothing was written, when the token did not open a preview.</summary>
    public string? Refusal { get; private set; }

    public IActionResult OnGet() => RedirectToPage("/Admin/Transfer/Index");

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        View = new EditorView(await _template.GetAsync(), ProfileParts.Transfer);
        var plan = _import.Take(Token, ImportSession.KeyOf(User));
        if (plan is null)
        {
            Refusal = "That preview has expired, was already applied, or was made in another session, so nothing was written. Preview the file again.";
            return Page();
        }
        Result = await _import.ApplyAsync(plan, ct);
        return Page();
    }

    /// <summary>Drops a preview without applying it.</summary>
    public IActionResult OnPostDiscard()
    {
        var found = _import.Discard(Token, ImportSession.KeyOf(User));
        Flash(found ? "The preview was discarded. Nothing was written." : "That preview was already gone. Nothing was written.");
        return RedirectToPage("/Admin/Transfer/Index");
    }
}
