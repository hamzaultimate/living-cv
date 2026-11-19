using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin;

public class IndexModel : AdminPageModel
{
    private readonly OnboardingService _onboarding;
    private readonly ILogger<IndexModel> _logger;
    private readonly RequestTemplate _template;
    private readonly AdminContentCounts _counts;

    public IndexModel(OnboardingService onboarding, ILogger<IndexModel> logger, RequestTemplate template, AdminContentCounts counts)
    {
        _onboarding = onboarding;
        _logger = logger;
        _template = template;
        _counts = counts;
    }

    /// <summary>The status row, the "Finish your site" checklist and the counts. A block that could not be read is null and left out.</summary>
    public DashboardModel Dashboard { get; private set; } = DashboardModel.Empty;

    /// <summary>True until the onboarding wizard has been published or skipped. Only a prompt: nothing is forced and no admin page is blocked.</summary>
    public bool SetupPending { get; private set; }

    /// <summary>Where "Continue setup" goes: the first step that is not saved yet.</summary>
    public string ResumePath { get; private set; } = "/admin/welcome";

    public async Task OnGetAsync(CancellationToken ct)
    {
        // The dashboard must open even when the database is struggling: each read is its own try, and a block whose data is missing is simply left out.
        TemplateView? template = null;
        OnboardingState? state = null;
        try
        {
            template = await _template.GetAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the template for the dashboard.");
        }
        try
        {
            state = await _onboarding.GetStateAsync(ct);
            SetupPending = state.FirstRun;
            if (state.Draft is { } draft)
                ResumePath = Welcome.WelcomePageModel.Steps[Math.Clamp(draft.Step + 1, 1, 4) - 1].Path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the setup state for the dashboard.");
        }
        Dashboard = AdminDashboard.Build(template, state, ResumePath, await _counts.GetAsync(ct)); // the counts log their own failure
    }
}
