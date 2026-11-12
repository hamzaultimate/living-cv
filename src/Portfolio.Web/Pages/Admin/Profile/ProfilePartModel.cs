using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Portfolio.Web.Pages.Admin.Content;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Profile;

/// <summary>The buttons of one row of a list on a profile editor (Move up, Move down, Remove): where the row is and what it is called for a screen reader.</summary>
public sealed record RowButtonsVm(int Index, int Count, string Name, string? FocusId, bool CanRemove = true);

/// <summary>
/// Shared by the editors of the profile's own blocks (hero, metrics, About). They read and write the profile row through <see cref="ProfileEditor"/>: a save names the
/// stamp the form was loaded with and is refused if the profile was written since. The words (page name, field labels, hints, messages) come from the template's
/// <c>editor</c> block through <see cref="EditorView"/>; nothing here names a profession. A GET only reads.
/// </summary>
public abstract class ProfilePartModel : AdminPageModel
{
    protected ProfilePartModel(ProfileEditor profile, RequestTemplate template)
    {
        Writer = profile;
        Template = template;
    }

    protected ProfileEditor Writer { get; }
    protected RequestTemplate Template { get; }

    /// <summary>The key of the block (<see cref="ProfileParts"/>), which is also the last segment of the address.</summary>
    public abstract string Part { get; }

    public EditorView View { get; protected set; } = null!;

    /// <summary>The page's address, and where Cancel goes back to.</summary>
    public virtual string Route => "/admin/profile/" + Part;

    /// <summary>Said after a row was added, moved or removed. The change is only on the page until Save: nothing was written.</summary>
    public string? Status { get; protected set; }

    /// <summary>The id of the control that takes keyboard focus when the page is drawn after a row button, so the person keeps their place.</summary>
    public string? FocusId { get; protected set; }

    /// <summary>True when the page shows input that was never saved (a refused save, or a row changed in place), so the unsaved changes warning starts on.</summary>
    public bool StartsDirty => !ModelState.IsValid || Status is not null;

    /// <summary>After a redirect the message takes keyboard focus, so it is announced.</summary>
    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context) => ViewData["FocusFlash"] = true;

    protected async Task LoadViewAsync() => View = new EditorView(await Template.GetAsync(), Part);

    public string? Autofocus(string id) => FocusId == id ? "autofocus" : null;

    public string ConflictMessage => $"{View.Heading} was changed somewhere else (another tab or session) since you opened it, so nothing was saved. Reload the page to see the latest version.";

    public string SavedMessage => $"{View.Heading} saved. Your public pages now use it.";

    /// <summary>The control for field <paramref name="key"/>; it draws nothing when the template hides the field (a hidden field is neither drawn nor read).</summary>
    public FieldVm Field(string key, string property, string? value, FieldKind kind = FieldKind.Text, int maxLength = 200, int rows = 4, bool? required = null, bool withHint = true) =>
        FieldVm.For(View, ModelState, key, property, value, kind, maxLength, rows, required: required, withHint: withHint);

    /// <summary>The page again, with the input as the person typed it and a message that says nothing was written.</summary>
    protected IActionResult Conflict()
    {
        ModelState.AddModelError(string.Empty, ConflictMessage);
        ViewData["Conflict"] = true;
        return Page();
    }

    /// <summary>The page again after a row button: the posted rows as they are now, a message for the live region, and focus on <paramref name="focusId"/>.</summary>
    protected IActionResult RowChanged(string status, string? focusId)
    {
        ModelState.Clear();
        Status = status;
        FocusId = focusId;
        ViewData["FocusId"] = focusId; // read by the shared field partial
        return Page();
    }
}
