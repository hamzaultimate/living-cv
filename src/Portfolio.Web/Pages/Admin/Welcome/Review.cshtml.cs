using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Configuration;
using Portfolio.Web.Security;
using Portfolio.Web.Services;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Pages.Admin.Welcome;

// A few short choices; anything much larger is not from this page.
[BodyLimit(16 * 1024)]
public class WelcomeReviewModel : WelcomePageModel
{
    private readonly TemplateResolver _resolver;
    private readonly StarterContent _starter;
    private readonly Portfolio.Data.PortfolioDbContext _db;
    private readonly UploadStore _store;

    public WelcomeReviewModel(OnboardingService onboarding, ITemplateCatalog catalog, TemplateResolver resolver, StarterContent starter,
        Portfolio.Data.PortfolioDbContext db, UploadStore store) : base(onboarding, catalog)
    {
        _resolver = resolver;
        _starter = starter;
        _db = db;
        _store = store;
    }

    public override int CurrentStep => 4;

    [BindProperty]
    public ReviewForm Form { get; set; } = new();

    /// <summary>Which page the preview opens on. Untrusted: it only ever selects one of <see cref="PreviewPages"/>.</summary>
    [BindProperty(SupportsGet = true, Name = "tab")]
    public string? Tab { get; set; }

    [BindProperty(SupportsGet = true, Name = "size")]
    public string? Size { get; set; }

    public bool Mobile => string.Equals(Size, "mobile", StringComparison.Ordinal);

    /// <summary>The site as it would be published (identity, template, accent, photo), without the sample rows.</summary>
    public SiteOptions? DraftSite { get; private set; }

    public IReadOnlyList<(string Path, string Label)> PreviewPages { get; private set; } = [];
    public string PreviewPath { get; private set; } = "/";
    public IReadOnlyList<StarterTable> Sample { get; private set; } = [];
    public string SearchTitle { get; private set; } = "";
    public string SearchDescription { get; private set; } = "";
    public bool HasDraftPhoto => Draft?.PhotoDraft is not null && _store.TryResolveDraft(Draft.PhotoDraft, out _, out _);
    public long PhotoVersion => Draft?.UpdatedAt.UtcTicks ?? 0;

    public string PreviewUrl =>
        $"{PreviewPath}?{RequestTemplate.PreviewParameter}={Uri.EscapeDataString(Template.Key)}&{RequestTemplate.DraftParameter}=1";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (RequireStep(2) is { } back) return back;

        Form = new ReviewForm
        {
            // The address is the one thing the owner cannot be expected to know is wrong: a new install still carries the placeholder from the
            // configuration, so a first run starts from the address this request came to.
            SiteUrl = Draft!.SiteUrl ?? (FirstRun ? $"{Request.Scheme}://{Request.Host}" : State.Profile.Url),
            Accent = Draft.AccentKey,
            Content = Draft.Content,
        };
        await PrepareAsync(ct);
        return Page();
    }

    /// <summary>Saves the choices and shows the preview again.</summary>
    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (RequireStep(2) is { } back) return back;
        if (!ModelState.IsValid) { await PrepareAsync(ct); return Page(); }
        if (!await Onboarding.SaveChoicesAsync(Form, ct))
        {
            ModelState.AddModelError(string.Empty, "That choice is not available for this template.");
            await PrepareAsync(ct);
            return Page();
        }

        Flash("Choices saved. The preview shows your draft; nothing is public yet.");
        return RedirectToPage();
    }

    /// <summary>Saves the choices, then publishes the draft in one step (see <see cref="OnboardingService.PublishAsync"/>).</summary>
    public async Task<IActionResult> OnPostPublishAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (Draft is null && !FirstRun)
        {
            // A second click or a replayed request after a publish: the draft is gone and the wizard is done. Nothing to apply.
            Flash("This setup was already published, so nothing changed.");
            return Redirect("/admin");
        }
        if (RequireStep(2) is { } back) return back;
        if (!ModelState.IsValid) { await PrepareAsync(ct); return Page(); }
        if (!await Onboarding.SaveChoicesAsync(Form, ct))
        {
            ModelState.AddModelError(string.Empty, "That choice is not available for this template.");
            await PrepareAsync(ct);
            return Page();
        }

        var result = await Onboarding.PublishAsync(ct);
        switch (result.Outcome)
        {
            case PublishOutcome.Published:
                var added = result.Sample?.Where(t => t.WouldAdd > 0).Select(t => $"{t.WouldAdd} {t.Name.ToLowerInvariant()}").ToList();
                Flash("Your site is live." + (added is { Count: > 0 } ? $" Sample content added: {string.Join(", ", added)}." : ""));
                return Redirect("/admin");
            case PublishOutcome.Incomplete:
                FlashError("Something from an earlier step is missing or no longer valid. Please check it and try again.");
                return Redirect(Steps[Math.Clamp(result.Step, 1, 4) - 1].Path);
            default:
                // A second click, a replayed request, or another tab got there first: nothing was applied by this request.
                Flash("This setup was already published, so nothing changed.");
                return Redirect("/admin");
        }
    }

    private async Task PrepareAsync(CancellationToken ct)
    {
        DraftSite = await Onboarding.DraftPreviewAsync(ct);
        var site = DraftSite ?? SiteProfileMapper.ToOptions(State.Profile);
        var view = _resolver.Resolve(site, Template.Key);
        PreviewPages = [("/", "Home"), .. view.NavLinks(mobile: true).Select(l => (l.Path, l.Path == "/cv" ? "CV" : TemplateView.Cap(l.Label)))];
        PreviewPath = PreviewPages.Select(p => p.Path).FirstOrDefault(p => p == Tab) ?? "/";
        SearchTitle = TemplateSeo.Apply(Template.Seo.Title, site);
        SearchDescription = TemplateSeo.ApplyDescription(Template.Seo.Description, site);
        Sample = StarterContent.Offers(Template) ? await _starter.DescribeAsync(_db, Template, ct) : [];
    }
}
