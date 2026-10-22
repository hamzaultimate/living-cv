using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Services;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Pages.Admin.Welcome;

/// <summary>Serves the owner's unpublished photo to the wizard and to the draft preview. Like every /admin page it needs the owner's session;
/// the file name comes from the stored draft, never from the request, so nothing in the URL selects a file.</summary>
public class WelcomeDraftPhotoModel : AdminPageModel
{
    private readonly OnboardingService _onboarding;
    private readonly UploadStore _store;

    public WelcomeDraftPhotoModel(OnboardingService onboarding, UploadStore store)
    {
        _onboarding = onboarding;
        _store = store;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var state = await _onboarding.GetStateAsync(ct);
        return _store.TryResolveDraft(state.Draft?.PhotoDraft, out var path, out var contentType)
            ? PhysicalFile(path, contentType)
            : NotFound();
    }
}
