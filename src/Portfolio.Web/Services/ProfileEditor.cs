using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;

namespace Portfolio.Web.Services;

/// <summary>
/// Writes the site profile from the admin area. After the row is saved the cached profile is dropped and the
/// "Generated" output cache (CV PDF, share image, sitemap) is evicted, so edits show up everywhere at once.
/// </summary>
public class ProfileEditor
{
    private readonly PortfolioDbContext _db;
    private readonly ISiteProfileProvider _provider;
    private readonly IOutputCacheStore _outputCache;

    public ProfileEditor(PortfolioDbContext db, ISiteProfileProvider provider, IOutputCacheStore outputCache)
    {
        _db = db;
        _provider = provider;
        _outputCache = outputCache;
    }

    public async Task<SiteProfile> LoadAsync(CancellationToken ct = default) => await LoadOrCreateAsync(ct);

    public static string NewStamp() => Guid.NewGuid().ToString("N");

    /// <summary>Saves the identity form. <paramref name="seenStamp"/> is the stamp the form was loaded with: if the profile was written since, nothing is saved.</summary>
    public Task<SaveOutcome> SaveAsync(ProfileForm form, string? seenStamp, CancellationToken ct = default) =>
        UpdateAsync(form.ApplyTo, seenStamp, ct);

    /// <summary>
    /// Applies <paramref name="apply"/> to the stored profile and saves it under a new stamp, but only if the profile still carries <paramref name="seenStamp"/>, the stamp
    /// the editor's form was loaded with (a missing stamp matches only a row that has never been stamped). Anything that wrote the profile in between makes the outcome a
    /// <see cref="SaveOutcome.Conflict"/>, with nothing written. After a save the provider and the "Generated" cache follow (<see cref="AfterCommitAsync"/>).
    /// </summary>
    public async Task<SaveOutcome> UpdateAsync(Action<SiteProfile> apply, string? seenStamp, CancellationToken ct = default)
    {
        var profile = await LoadOrCreateAsync(ct);
        apply(profile);
        var entry = _db.Entry(profile);
        // A row that is only now being created has nothing to be stale against.
        if (entry.State != EntityState.Added) entry.Property(p => p.ConcurrencyStamp).OriginalValue = string.IsNullOrWhiteSpace(seenStamp) ? null : seenStamp;
        profile.ConcurrencyStamp = NewStamp();
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || (ex is DbUpdateException && SiteProfileProvider.IsDuplicateKey(ex)))
        {
            // Either the row changed since the form was loaded, or the row did not exist yet and something else created it first: nothing was written.
            _db.ChangeTracker.Clear();
            return SaveOutcome.Conflict;
        }
        await AfterCommitAsync(profile);
        return SaveOutcome.Saved;
    }

    /// <summary>
    /// A change that does not come from a form (a new photo, a new template): it has no earlier copy to be stale against, so it is applied to whatever the profile holds
    /// now. It still replaces the stamp, so a form opened before it is refused, and it starts again from the stored row if another write lands first. After three
    /// losses in a row it gives up with <see cref="SaveOutcome.Conflict"/> and writes nothing, so the caller can say so and clean up.
    /// </summary>
    private async Task<SaveOutcome> UpdateLatestAsync(Action<SiteProfile> apply, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var profile = await LoadOrCreateAsync(ct);
            apply(profile);
            profile.ConcurrencyStamp = NewStamp();
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException || (ex is DbUpdateException && SiteProfileProvider.IsDuplicateKey(ex)))
            {
                _db.ChangeTracker.Clear();
                if (attempt < 3) continue;
                return SaveOutcome.Conflict;
            }
            await AfterCommitAsync(profile);
            return SaveOutcome.Saved;
        }
    }

    /// <summary>Points the profile at a new photo (or clears it). The outcome says whether it was written (<see cref="SaveOutcome.Conflict"/> means nothing was) and <c>Previous</c> is the photo it replaced.</summary>
    public async Task<(SaveOutcome Outcome, string? Previous)> SetPhotoAsync(string? photoUrl, CancellationToken ct = default)
    {
        string? previous = null;
        var outcome = await UpdateLatestAsync(profile =>
        {
            previous = profile.PhotoUrl;
            // The light-theme photo that is just a copy of the photo being replaced (the wizard sets both) follows it, so it never points at the
            // deleted file; a separate light photo the owner chose is left alone.
            if (previous is not null && profile.PhotoUrlLight == previous) profile.PhotoUrlLight = photoUrl;
            profile.PhotoUrl = photoUrl;
        }, ct);
        return (outcome, previous);
    }

    /// <summary>Makes <paramref name="template"/> the site's template. Takes a catalog entry rather than a key, so only a template that
    /// loaded can be chosen. Only the template choice changes: every content row stays, and sections the template does not use are
    /// hidden by the pages, not removed.</summary>
    public Task<SaveOutcome> SetTemplateAsync(TemplateDefinition template, CancellationToken ct = default) =>
        UpdateLatestAsync(profile => profile.TemplateKey = template.Key, ct);

    /// <summary>Makes a change to the content rows (experience, skills, projects, certifications) show up everywhere at once: the provider's version
    /// moves on, which the "Generated" cache varies on, and the cache is evicted, so /sitemap.xml, /cv.pdf and /og.png follow without a restart.
    /// Call it after the rows are committed.</summary>
    public async Task AfterContentCommitAsync()
    {
        var saved = await _db.SiteProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == SiteProfile.SingletonId, CancellationToken.None);
        if (saved is not null) await AfterCommitAsync(saved);
        else
        {
            // No profile row yet: nothing to prime, but cached output must still go.
            _provider.Invalidate();
            await _outputCache.EvictByTagAsync(OutputCacheTags.Generated, CancellationToken.None);
        }
    }

    private async Task<SiteProfile> LoadOrCreateAsync(CancellationToken ct)
    {
        var profile = await _db.SiteProfiles.FirstOrDefaultAsync(p => p.Id == SiteProfile.SingletonId, ct);
        if (profile is not null) return profile;

        // The provider creates the row on first read; this covers a database that has never served a page.
        profile = SiteProfileMapper.ToEntity(await _provider.GetAsync(ct));
        SiteProfileMapper.FitToColumns(profile);
        _db.SiteProfiles.Add(profile);
        return profile;
    }

    /// <summary>Makes a profile that has just been committed (by this editor or by the onboarding wizard's transaction) take effect everywhere:
    /// the provider is handed the saved values and the "Generated" output cache is evicted.</summary>
    public async Task AfterCommitAsync(SiteProfile profile)
    {
        // The row is committed, so the cleanup below must run to completion even if the request is cancelled now:
        // stopping halfway would leave the old profile or old generated files being served.
        // Order matters: hand the provider the saved profile first (this also changes its version, which the
        // "Generated" cache varies on, so a render that read the old profile can never be served again), then evict.
        _provider.Prime(SiteProfileMapper.ToOptions(profile));
        await _outputCache.EvictByTagAsync(OutputCacheTags.Generated, CancellationToken.None);
    }
}
