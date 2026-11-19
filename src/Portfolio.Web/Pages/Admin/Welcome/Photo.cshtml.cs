using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Configuration;
using Portfolio.Web.Security;
using Portfolio.Web.Services;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Pages.Admin.Welcome;

// Applies the upload cap before the antiforgery check reads the body (see BodyLimitAttribute).
[BodyLimit]
public class WelcomePhotoModel : WelcomePageModel
{
    private readonly UploadStore _store;

    public WelcomePhotoModel(OnboardingService onboarding, ITemplateCatalog catalog, UploadStore store) : base(onboarding, catalog)
    {
        _store = store;
    }

    public override int CurrentStep => 3;

    public override string? ContinueLabel => "Continue";

    [BindProperty]
    public IFormFile? Photo { get; set; }

    public string MaxSize => UploadForm.Label(_store.MaxBytes);

    public bool HasDraftPhoto => Draft?.PhotoDraft is not null && _store.TryResolveDraft(Draft.PhotoDraft, out _, out _);

    /// <summary>Changes whenever the draft photo does, so the browser never shows a replaced picture from its cache.</summary>
    public long PhotoVersion => Draft?.UpdatedAt.UtcTicks ?? 0;

    /// <summary>What the monogram fallback shows, from the name entered so far.</summary>
    public string Initials => new SiteOptions { OwnerName = Draft?.OwnerName ?? "" }.Initials;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        return Page();
    }

    /// <summary>Continue, or one of the indicator's buttons. A file that was chosen but not uploaded is uploaded first (a file that is not a valid image keeps the owner here with the reason);
    /// with no file chosen the owner simply moves on. The photo is optional.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (Photo is { Length: > 0 })
        {
            if (await TryUploadAsync(ct) is { } error) return Reject(error);
            Flash("Photo added to your draft. It is not public until you publish.");
        }
        return Leave();
    }

    /// <summary>The Upload button: adds the chosen photo and stays on this step.</summary>
    public async Task<IActionResult> OnPostUploadAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (Photo is null || Photo.Length == 0)
            return Reject("Choose an image to upload.");
        if (await TryUploadAsync(ct) is { } error)
            return Reject(error);

        Flash("Photo added to your draft. It is not public until you publish.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        await Onboarding.RemoveDraftPhotoAsync(ct);
        Flash("Photo removed from your draft. Your site will use a monogram of your initials.");
        return RedirectToPage();
    }

    /// <summary>Stores the chosen photo in the hidden draft area. Null when it was stored; otherwise why it was not (too large, or not an image the processor accepts).</summary>
    private async Task<string?> TryUploadAsync(CancellationToken ct)
    {
        if (Photo!.Length > _store.MaxBytes) return $"That file is larger than {MaxSize}.";
        try
        {
            await Onboarding.SaveDraftPhotoAsync(await UploadForm.ReadAsync(Photo, _store.MaxBytes, MaxSize, ct), ct);
            return null;
        }
        catch (ImageRejectedException ex)
        {
            return ex.Message;
        }
    }

    private IActionResult Reject(string message)
    {
        ModelState.AddModelError(nameof(Photo), message);
        return Page();
    }
}
