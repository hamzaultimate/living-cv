using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Configuration;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Welcome;

// A few short choices; anything much larger is not from this page.
[BodyLimit(16 * 1024)]
public class WelcomeReviewModel : WelcomePageModel
{
    /// <summary>The preview tab that shows the search-result card in place of the page.</summary>
    public const string SearchTab = "search";

    private readonly TemplateResolver _resolver;
    private readonly StarterContent _starter;
    private readonly Portfolio.Data.PortfolioDbContext _db;

    public WelcomeReviewModel(OnboardingService onboarding, ITemplateCatalog catalog, TemplateResolver resolver, StarterContent starter,
        Portfolio.Data.PortfolioDbContext db) : base(onboarding, catalog)
    {
        _resolver = resolver;
        _starter = starter;
        _db = db;
    }

    public override int CurrentStep => 4;

    [BindProperty]
    public ReviewForm Form { get; set; } = new();

    /// <summary>Which preview tab is open. Untrusted: it only ever selects one of <see cref="PreviewPages"/>, or <see cref="SearchTab"/>.</summary>
    [BindProperty(SupportsGet = true, Name = "tab")]
    public string? Tab { get; set; }

    [BindProperty(SupportsGet = true, Name = "size")]
    public string? Size { get; set; }

    public bool Mobile => string.Equals(Size, "mobile", StringComparison.Ordinal);

    /// <summary>The site as it would be published (identity, template, accent, photo), without the sample rows.</summary>
    public SiteOptions? DraftSite { get; private set; }

    public IReadOnlyList<(string Path, string Label)> PreviewPages { get; private set; } = [];
    public string PreviewPath { get; private set; } = "/";

    /// <summary>True when the search-result tab is open: the card replaces the preview frame.</summary>
    public bool ShowSearch { get; private set; }

    /// <summary>The value the width links keep in <c>tab</c>: the search tab, or the page being previewed.</summary>
    public string ActiveTab => ShowSearch ? SearchTab : PreviewPath;

    public IReadOnlyList<StarterTable> Sample { get; private set; } = [];
    public string SearchTitle { get; private set; } = "";
    public string SearchDescription { get; private set; } = "";

    /// <summary>The "Ready to publish?" lines, counting the saved draft with the address as it is shown in the form.</summary>
    public PublishChecklist Checklist { get; private set; } = null!;

    public string PreviewUrl =>
        $"{PreviewPath}?{RequestTemplate.PreviewParameter}={Uri.EscapeDataString(Template.Key)}&{RequestTemplate.DraftParameter}=1";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);

        Form = new ReviewForm
        {
            // The address is the one thing the owner cannot be expected to know is wrong: a new install still carries the placeholder from the
            // configuration, so a first run starts from the address this request came to.
            SiteUrl = Draft?.SiteUrl ?? (FirstRun ? $"{Request.Scheme}://{Request.Host}" : State.Profile.Url),
            Accent = Draft?.AccentKey,
            Content = Draft?.Content ?? StarterChoice.Blank,
        };
        await PrepareAsync(ct);
        return Page();
    }

    /// <summary>Continue, or one of the indicator's buttons: saves the choices under the hard rules, then opens the step asked for (this one by default).</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (await TrySaveAsync(ct) is { } again) return again;
        return Leave();
    }

    /// <summary>Saves the choices and shows the preview again.</summary>
    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (await TrySaveAsync(ct) is { } again) return again;

        Flash("Choices saved. The preview shows your draft; nothing is public yet.");
        return RedirectToPage();
    }

    /// <summary>Saves the choices, then publishes the draft in one step (see <see cref="OnboardingService.PublishAsync"/>). The service checks everything again, so the disabled button is only a convenience.</summary>
    public async Task<IActionResult> OnPostPublishAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (Draft is null && !FirstRun)
        {
            // A second click or a replayed request after a publish: the draft is gone and the wizard is done. Nothing to apply.
            Flash("This setup was already published, so nothing changed.");
            return Redirect("/admin");
        }
        if (await TrySaveAsync(ct) is { } again) return again;

        var result = await Onboarding.PublishAsync(ct);
        switch (result.Outcome)
        {
            case PublishOutcome.Published:
                var added = result.Sample?.Where(t => t.WouldAdd > 0).Select(t => $"{t.WouldAdd} {t.Name.ToLowerInvariant()}").ToList();
                Flash("Your site is live." + (added is { Count: > 0 } ? $" Sample content added: {string.Join(", ", added)}." : ""));
                return Redirect("/admin");
            case PublishOutcome.Incomplete:
                // Back here, where the checklist says what is missing and links to the step that fixes it.
                FlashError("Nothing was published: something is missing or no longer valid. The checklist below shows what to fix.");
                return Redirect(WizardProgress.Steps[3].Path);
            case PublishOutcome.NoMenu:
                FlashError(result.Detail ?? "That template would leave your site without a menu section, so nothing was published.");
                return Redirect(WizardProgress.Steps[0].Path);
            case PublishOutcome.Changed:
                // Not "already published": the wizard is still open and the draft is kept, so the only truthful advice is to publish again.
                FlashError("Your profile was changed while publishing, so nothing was published. Your answers are kept: check them and press Publish again.");
                return Redirect(WizardProgress.Steps[3].Path);
            default:
                // A second click, a replayed request, or another tab got there first: nothing was applied by this request.
                Flash("This setup was already published, so nothing changed.");
                return Redirect("/admin");
        }
    }

    /// <summary>Saves the form's choices under the hard rules. Null when saved; otherwise the page to show again with the reason.</summary>
    private async Task<IActionResult?> TrySaveAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) { await PrepareAsync(ct); return Page(); }
        if (!await Onboarding.SaveChoicesAsync(Form, ct))
        {
            ModelState.AddModelError(string.Empty, "That choice is not available for this template.");
            await PrepareAsync(ct);
            return Page();
        }
        return null;
    }

    private async Task PrepareAsync(CancellationToken ct)
    {
        DraftSite = await Onboarding.DraftPreviewAsync(ct);
        var site = DraftSite ?? SiteProfileMapper.ToOptions(State.Profile);
        var view = _resolver.Resolve(site, Template.Key);
        PreviewPages = TemplatePickerBuilder.PagesOf(view);
        ShowSearch = string.Equals(Tab, SearchTab, StringComparison.Ordinal);
        PreviewPath = PreviewPages.Select(p => p.Path).FirstOrDefault(p => p == Tab) ?? "/";
        SearchTitle = TemplateSeo.Apply(Template.Seo.Title, site);
        SearchDescription = TemplateSeo.ApplyDescription(Template.Seo.Description, site);
        Sample = StarterContent.Offers(Template) ? await _starter.DescribeAsync(_db, Template, ct) : [];

        // The address in the form is what the owner can see (on a first run it is prefilled and not yet saved), so the checklist counts it.
        var shown = (Draft ?? new OnboardingDraft()).Clone();
        shown.SiteUrl = ProfileForm.Line(Form.SiteUrl);
        Checklist = Onboarding.ChecklistFor(shown);
    }
}
