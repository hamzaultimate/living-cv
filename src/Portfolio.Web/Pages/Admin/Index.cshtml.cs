using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin;

public class IndexModel : AdminPageModel
{
    private readonly OnboardingService _onboarding;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(OnboardingService onboarding, ILogger<IndexModel> logger)
    {
        _onboarding = onboarding;
        _logger = logger;
    }

    /// <summary>True until the onboarding wizard has been published or skipped. Only a prompt: nothing is forced and no admin page is blocked.</summary>
    public bool SetupPending { get; private set; }

    /// <summary>Where "Continue setup" goes: the first step that is not saved yet.</summary>
    public string ResumePath { get; private set; } = "/admin/welcome";

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            var state = await _onboarding.GetStateAsync(ct);
            SetupPending = state.FirstRun;
            if (state.Draft is { } draft)
                ResumePath = Welcome.WelcomePageModel.Steps[Math.Clamp(draft.Step + 1, 1, 4) - 1].Path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The dashboard must open even when the database is struggling; the prompt is simply left out.
            _logger.LogWarning(ex, "Could not read the setup state for the dashboard.");
        }
    }
}
