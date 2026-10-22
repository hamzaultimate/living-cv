using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Data.Seeding;
using Portfolio.Web.Configuration;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Services;

/// <summary>The profile row and the wizard's saved answers. <see cref="FirstRun"/> is true until the wizard has been published or skipped.</summary>
public sealed record OnboardingState(SiteProfile Profile, OnboardingDraft? Draft)
{
    public bool FirstRun => Profile.OnboardingCompletedAt is null;
}

public enum PublishOutcome
{
    /// <summary>The draft was applied and the site now shows it.</summary>
    Published,

    /// <summary>There is no draft: it was already published (a second click or a replayed request) or never started. Nothing changed.</summary>
    NothingToPublish,

    /// <summary>An earlier step is missing or no longer valid; <see cref="PublishResult.Step"/> says which to go back to.</summary>
    Incomplete,

    /// <summary>Another publish won the race. Nothing from this one was applied.</summary>
    Conflict,
}

public sealed record PublishResult(PublishOutcome Outcome, int Step = 0, IReadOnlyList<StarterTable>? Sample = null);

/// <summary>
/// The onboarding wizard's server side. Answers are saved to a draft on the profile row after every step and never touch the public
/// profile; publishing applies them in one transaction, then hands the saved profile to the provider and evicts the "Generated"
/// output cache like any other profile write (<see cref="ProfileEditor.AfterCommitAsync"/>).
/// </summary>
public class OnboardingService
{
    /// <summary>Where the owner-only wizard serves the draft photo from; the draft preview points the photo at it.</summary>
    public const string DraftPhotoPath = "/admin/welcome/draft-photo";

    public const int LastStep = 4;

    private static readonly TimeSpan StaleDraftPhotoAge = TimeSpan.FromDays(1);

    private readonly PortfolioDbContext _db;
    private readonly ProfileEditor _editor;
    private readonly ITemplateCatalog _catalog;
    private readonly UploadStore _store;
    private readonly ImageProcessor _processor;
    private readonly StarterContent _starter;
    private readonly TimeProvider _time;
    private readonly ILogger<OnboardingService> _logger;
    private readonly string _seedDir;

    public OnboardingService(PortfolioDbContext db, ProfileEditor editor, ITemplateCatalog catalog, UploadStore store, ImageProcessor processor,
        StarterContent starter, TimeProvider time, ILogger<OnboardingService> logger)
    {
        _db = db;
        _editor = editor;
        _catalog = catalog;
        _store = store;
        _processor = processor;
        _starter = starter;
        _time = time;
        _logger = logger;
        _seedDir = Path.Combine(AppContext.BaseDirectory, "Seed");
    }

    public async Task<OnboardingState> GetStateAsync(CancellationToken ct = default)
    {
        var profile = await _editor.LoadAsync(ct);
        return new OnboardingState(profile, OnboardingDraft.Parse(profile.OnboardingDraft));
    }

    // ---------------------------------------------------------------- steps

    /// <summary>Step 1. The template must come from the catalog (the same allow-list as the admin switcher). Wording the owner has not changed
    /// from the previous template's suggestion is dropped, so the new template's suggestion takes its place.</summary>
    public async Task SaveTemplateAsync(TemplateDefinition template, CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);
        var draft = state.Draft ?? new OnboardingDraft();
        if (_catalog.TryGet(draft.TemplateKey, out var previous) && previous.Key != template.Key)
        {
            if (string.IsNullOrWhiteSpace(draft.JobTitle) || draft.JobTitle == previous.HeroSuggestions.Title) draft.JobTitle = null;
            if (string.IsNullOrWhiteSpace(draft.Summary) || draft.Summary == previous.HeroSuggestions.Summary) draft.Summary = null;
            // An accent belongs to one template's list.
            if (!template.Theme.Accents.Any(a => a.Key == draft.AccentKey)) draft.AccentKey = null;
        }
        draft.TemplateKey = template.Key;
        draft.Step = Math.Max(draft.Step, 1);
        await SaveDraftAsync(state, draft, ct);
    }

    /// <summary>Step 2. <paramref name="form"/> must already have passed validation; it is cleaned of control characters here.</summary>
    public async Task SaveBasicsAsync(BasicsForm form, CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);
        var draft = state.Draft ?? new OnboardingDraft();
        var clean = form.Cleaned();
        draft.OwnerName = clean.OwnerName;
        draft.JobTitle = clean.JobTitle;
        draft.Email = clean.Email;
        draft.Location = clean.Location;
        draft.Summary = clean.Summary;
        draft.Step = Math.Max(draft.Step, 2);
        await SaveDraftAsync(state, draft, ct);
    }

    /// <summary>Step 3. The image goes through the same processor as the photo page (type sniffing, size limits, re-encoding) and is stored in the
    /// hidden draft area; the public profile is not touched. The photo it replaces is deleted, and abandoned drafts from earlier visits are cleaned up.</summary>
    public async Task SaveDraftPhotoAsync(byte[] data, CancellationToken ct = default)
    {
        var processed = await _processor.ProcessAsync(data, ct);
        var name = await _store.SaveDraftAsync(processed, ct);
        var state = await GetStateAsync(ct);
        var draft = state.Draft ?? new OnboardingDraft();
        var previous = draft.PhotoDraft;
        draft.PhotoDraft = name;
        try
        {
            await SaveDraftAsync(state, draft, ct);
        }
        catch
        {
            _store.TryDeleteDraft(name);
            throw;
        }
        _store.TryDeleteDraft(previous);
        _store.PurgeDrafts(StaleDraftPhotoAge, keep: name);
    }

    public async Task RemoveDraftPhotoAsync(CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);
        if (state.Draft?.PhotoDraft is not { } name) return;
        state.Draft.PhotoDraft = null;
        await SaveDraftAsync(state, state.Draft, ct);
        _store.TryDeleteDraft(name);
    }

    /// <summary>Step 4 choices. An accent must be one the draft's template offers, content one of the known kinds; anything else is refused.</summary>
    public async Task<bool> SaveChoicesAsync(ReviewForm form, CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);
        if (state.Draft is not { } draft || !_catalog.TryGet(draft.TemplateKey, out var template)) return false;
        var accent = string.IsNullOrWhiteSpace(form.Accent) ? null : form.Accent.Trim();
        if (accent is not null && !template.Theme.Accents.Any(a => string.Equals(a.Key, accent, StringComparison.Ordinal))) return false;
        if (!StarterChoice.IsKnown(form.Content) || (form.Content == StarterChoice.Sample && !StarterContent.Offers(template))) return false;

        draft.AccentKey = accent;
        draft.Content = form.Content;
        draft.SiteUrl = ProfileForm.Line(form.SiteUrl);
        await SaveDraftAsync(state, draft, ct);
        return true;
    }

    /// <summary>Throws the draft away (and its photo). The public profile and every content row stay as they are.</summary>
    public async Task DiscardAsync(CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);
        if (state.Draft is null && string.IsNullOrEmpty(state.Profile.OnboardingDraft)) return;
        var photo = state.Draft?.PhotoDraft;
        state.Profile.OnboardingDraft = null;
        await TrySaveAsync(ct);
        _store.TryDeleteDraft(photo);
    }

    // ---------------------------------------------------------------- publishing

    /// <summary>
    /// Applies the draft to the site in one transaction: the profile (template, identity, accent, photo), the draft cleared and the wizard marked done
    /// first, then the sample rows if asked for (into empty tables only). A second call finds no draft and changes nothing; two overlapping calls
    /// are serialised by the profile row, and the loser rolls back. Afterwards the provider and the "Generated" cache follow, as for any profile write.
    /// </summary>
    public async Task<PublishResult> PublishAsync(CancellationToken ct = default)
    {
        string? promoted = null;
        string? usedDraftPhoto = null;
        string? replacedPhoto = null;
        SiteProfile? published = null;
        IReadOnlyList<StarterTable>? sample = null;
        PublishResult? early = null;

        var strategy = _db.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                // A retry starts clean: nothing from a failed attempt may linger.
                if (promoted is not null) { _store.TryDelete(promoted); promoted = null; }
                _db.ChangeTracker.Clear();
                early = null;
                published = null;
                sample = null;

                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                var profile = await _db.SiteProfiles.FirstOrDefaultAsync(p => p.Id == SiteProfile.SingletonId, ct);
                var draft = OnboardingDraft.Parse(profile?.OnboardingDraft);
                if (profile is null || draft is null) { early = new PublishResult(PublishOutcome.NothingToPublish); return; }

                if (!_catalog.TryGet(draft.TemplateKey, out var template)) { early = new PublishResult(PublishOutcome.Incomplete, 1); return; }
                if (!BasicsForm.IsComplete(draft)) { early = new PublishResult(PublishOutcome.Incomplete, 2); return; }
                if (!IsValidAddress(draft.SiteUrl)) { early = new PublishResult(PublishOutcome.Incomplete, 4); return; }

                var firstRun = profile.OnboardingCompletedAt is null;
                if (draft.PhotoDraft is not null)
                {
                    promoted = _store.PromoteDraft(draft.PhotoDraft);
                    usedDraftPhoto = draft.PhotoDraft;
                }
                replacedPhoto = profile.PhotoUrl;
                Apply(profile, draft, template, firstRun, promoted);
                profile.OnboardingDraft = null;
                profile.OnboardingCompletedAt = _time.GetUtcNow();
                // Only a first run records an outcome: a re-run on a site that existed before the wizard must not change how that site seeds at start-up.
                if (firstRun) profile.OnboardingOutcome = "published";
                SiteProfileMapper.FitToColumns(profile);
                await _db.SaveChangesAsync(ct);

                // The profile row is already updated in this transaction, so an overlapping publish is waiting on it (or has failed its
                // concurrency check) before it can add any rows.
                if (draft.Content == StarterChoice.Sample && StarterContent.Offers(template))
                    sample = await _starter.ApplyAsync(_db, template, ct);

                await tx.CommitAsync(ct);
                published = profile;
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            _store.TryDelete(promoted);
            _db.ChangeTracker.Clear();
            return new PublishResult(PublishOutcome.Conflict);
        }
        catch
        {
            _store.TryDelete(promoted);
            throw;
        }

        if (early is not null)
        {
            _store.TryDelete(promoted);
            return early;
        }

        // Committed. From here the cleanup runs to completion even if the request is cancelled.
        await _editor.AfterCommitAsync(published!);
        _store.TryDeleteDraft(usedDraftPhoto);
        // The photo that was replaced is deleted only when it was one of our uploads (TryDelete ignores anything else, like the bundled avatar).
        if (promoted is not null && replacedPhoto != promoted) _store.TryDelete(replacedPhoto);
        _store.PurgeDrafts(StaleDraftPhotoAge);
        return new PublishResult(PublishOutcome.Published, 0, sample);
    }

    /// <summary>
    /// Leaves the wizard without publishing. On a first run this keeps the site as it was before the wizard existed: the placeholder identity from
    /// the Site config stays and the demo persona is loaded into any empty table (what start-up did before the wizard deferred it). On a
    /// re-run it only discards the draft. Either way no existing row is changed or removed.
    /// </summary>
    public async Task SkipAsync(CancellationToken ct = default)
    {
        string? photo = null;
        SiteProfile? skipped = null;
        var strategy = _db.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                skipped = null;
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                var profile = await _editor.LoadAsync(ct);
                photo = OnboardingDraft.Parse(profile.OnboardingDraft)?.PhotoDraft;
                var firstRun = profile.OnboardingCompletedAt is null;
                profile.OnboardingDraft = null;
                if (firstRun)
                {
                    profile.OnboardingCompletedAt = _time.GetUtcNow();
                    profile.OnboardingOutcome = "skipped";
                    await _db.SaveChangesAsync(ct);
                    await PortfolioSeeder.SeedAsync(_db, _seedDir, SeedMode.IfEmpty, _logger, ct);
                    skipped = profile;
                }
                else await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            return; // published or skipped in another tab; nothing more to do
        }

        if (skipped is not null) await _editor.AfterCommitAsync(skipped);
        _store.TryDeleteDraft(photo);
    }

    // ---------------------------------------------------------------- preview

    /// <summary>The site as it would look once published (identity, template, accent and draft photo), for the owner's draft preview. Null without a draft.
    /// Sample rows are not part of it: they are added at publish.</summary>
    public async Task<SiteOptions?> DraftPreviewAsync(CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);
        if (state.Draft is not { } draft || !_catalog.TryGet(draft.TemplateKey, out var template)) return null;

        var copy = SiteProfileMapper.ToEntity(SiteProfileMapper.ToOptions(state.Profile));
        Apply(copy, draft, template, state.FirstRun, draft.PhotoDraft is null ? null : DraftPhotoPath);
        return SiteProfileMapper.ToOptions(copy);
    }

    /// <summary>
    /// Writes the draft onto a profile. Only what the wizard asks for is touched: template, name, short name, title, role line, email, location,
    /// summary, site address, accent and (when there is one) the photo. On a first run the placeholder persona that came from the Site config
    /// (hero, metrics, About text, links, phone) is replaced by a plain hero built from the owner's own answers and empty lists, so no stranger's text
    /// survives under the owner's name; on a re-run those stay as the owner edited them. Rows in other tables are never touched here.
    /// </summary>
    internal static void Apply(SiteProfile p, OnboardingDraft d, TemplateDefinition template, bool firstRun, string? photoUrl)
    {
        p.TemplateKey = template.Key;
        if (!string.IsNullOrWhiteSpace(d.OwnerName))
        {
            // The short name (header wordmark) is the owner's own wording on a re-run; it follows the name only when the name changes.
            if (firstRun || p.OwnerName != d.OwnerName) p.BrandName = ShortName(d.OwnerName);
            p.OwnerName = d.OwnerName;
        }
        if (!string.IsNullOrWhiteSpace(d.JobTitle))
        {
            // The role line (for example "Senior Engineer · .NET") is the owner's own wording on a re-run; it follows the title only when the title changes.
            if (firstRun || p.JobTitle != d.JobTitle) p.Role = d.JobTitle;
            p.JobTitle = d.JobTitle;
        }
        if (!string.IsNullOrWhiteSpace(d.Email)) p.Email = d.Email;
        if (!string.IsNullOrWhiteSpace(d.Summary)) p.Summary = d.Summary;
        if (d.Step >= 2) p.Location = d.Location;
        if (!string.IsNullOrWhiteSpace(d.SiteUrl)) p.Url = d.SiteUrl;
        p.AccentColor = template.Theme.Accents.Any(a => a.Key == d.AccentKey) ? d.AccentKey : null;

        if (firstRun)
        {
            p.Hero = new ProfileHero { Eyebrow = p.JobTitle, Headline = p.OwnerName, HeadlineAccent = null, Lede = p.Summary };
            p.Metrics = [];
            p.About = new ProfileAbout { Headline = null, Paragraphs = string.IsNullOrWhiteSpace(p.Summary) ? [] : [p.Summary] };
            p.KnowsAbout = [];
            p.Phone = p.WhatsApp = p.LinkedIn = p.GitHub = p.YouTube = null;
            p.SecondaryEmails = [];
            p.PhotoUrlLight = null;
            // The placeholder avatar goes; a photo the owner already uploaded (before finishing the wizard) stays.
            if (photoUrl is null && !IsUpload(p.PhotoUrl)) p.PhotoUrl = null;
        }
        if (photoUrl is not null)
        {
            // The pages show PhotoUrlLight in the light theme and hide the dark photo there, so one photo has to be set for both.
            p.PhotoUrl = photoUrl;
            p.PhotoUrlLight = photoUrl;
        }
    }

    private static bool IsUpload(string? url) => url is not null && url.StartsWith(UploadStore.RequestPath + "/", StringComparison.Ordinal);

    // ---------------------------------------------------------------- helpers

    /// <summary>The first word of the name, for the header wordmark (the column holds at most 80 characters).</summary>
    internal static string ShortName(string ownerName)
    {
        var first = ownerName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? ownerName;
        return first.Length > SiteProfile.MaxLength.BrandName ? first[..SiteProfile.MaxLength.BrandName] : first;
    }

    internal static bool IsValidAddress(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.Length <= SiteProfile.MaxLength.Url && new HttpUrlAttribute().IsValid(url);

    private async Task SaveDraftAsync(OnboardingState state, OnboardingDraft draft, CancellationToken ct)
    {
        draft.UpdatedAt = _time.GetUtcNow();
        state.Profile.OnboardingDraft = draft.ToJson();
        await TrySaveAsync(ct);
    }

    /// <summary>Saves a draft change. A concurrent publish changes the concurrency token; the answer is then simply not saved (the wizard is done).</summary>
    private async Task TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            _logger.LogInformation("A wizard answer was not saved because the wizard was published in the meantime.");
        }
    }
}
