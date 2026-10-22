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

    public async Task SaveAsync(ProfileForm form, CancellationToken ct = default)
    {
        var profile = await LoadOrCreateAsync(ct);
        form.ApplyTo(profile);
        await CommitAsync(profile, ct);
    }

    /// <summary>Points the profile at a new photo (or clears it) and returns the previous value.</summary>
    public async Task<string?> SetPhotoAsync(string? photoUrl, CancellationToken ct = default)
    {
        var profile = await LoadOrCreateAsync(ct);
        var previous = profile.PhotoUrl;
        profile.PhotoUrl = photoUrl;
        await CommitAsync(profile, ct);
        return previous;
    }

    /// <summary>Makes <paramref name="template"/> the site's template. Takes a catalog entry rather than a key, so only a template that
    /// loaded can be chosen. Only the template choice changes: every content row stays, and sections the template does not use are
    /// hidden by the pages, not removed.</summary>
    public async Task SetTemplateAsync(TemplateDefinition template, CancellationToken ct = default)
    {
        var profile = await LoadOrCreateAsync(ct);
        profile.TemplateKey = template.Key;
        await CommitAsync(profile, ct);
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

    private async Task CommitAsync(SiteProfile profile, CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
        await AfterCommitAsync(profile);
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
