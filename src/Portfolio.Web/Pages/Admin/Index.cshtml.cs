using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin;

public class IndexModel : AdminPageModel
{
    private readonly OnboardingService _onboarding;
    private readonly ILogger<IndexModel> _logger;
    private readonly RequestTemplate _template;

    public IndexModel(OnboardingService onboarding, ILogger<IndexModel> logger, RequestTemplate template)
    {
        _onboarding = onboarding;
        _logger = logger;
        _template = template;
    }

    /// <summary>The names of the editable sections in the template's words ("experience, skills, work and credentials"), for the Content tile.</summary>
    public string ContentLabels { get; private set; } = "content";

    /// <summary>The profile's own editors (home page introduction, metrics, About) with their names in the template's words.</summary>
    public IReadOnlyList<(string Url, string Heading)> Parts { get; private set; } = [];

    /// <summary>What one section is called in the template's words, for the sections tile.</summary>
    public string SectionsItem { get; private set; } = "Section";

    /// <summary>The sections page's name in the template's words.</summary>
    public string SectionsHeading { get; private set; } = "Sections";

    /// <summary>True until the onboarding wizard has been published or skipped. Only a prompt: nothing is forced and no admin page is blocked.</summary>
    public bool SetupPending { get; private set; }

    /// <summary>Where "Continue setup" goes: the first step that is not saved yet.</summary>
    public string ResumePath { get; private set; } = "/admin/welcome";

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            var template = await _template.GetAsync();
            Parts = EditorView.ProfileLinks(template);
            var sections = new EditorView(template, ProfileParts.Sections);
            SectionsHeading = sections.Heading;
            SectionsItem = sections.ItemLabel;
            var labels = EditorFields.Sections.Select(s => template.Section(s).Label).ToList();
            ContentLabels = labels.Count > 1 ? string.Join(", ", labels.Take(labels.Count - 1)) + " and " + labels[^1] : labels.FirstOrDefault() ?? "content";
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
