using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Data.Seeding;
using Portfolio.Web.Configuration;

namespace Portfolio.Web.Services;

/// <summary>The only way pages and services read the site's public identity.</summary>
public interface ISiteProfileProvider
{
    Task<SiteOptions> GetAsync(CancellationToken ct = default);

    /// <summary>Drops the cached profile so the next read reloads it from the database.</summary>
    void Invalidate();
}

/// <summary>
/// Reads the profile row from the database through a memory cache. The "Site" config section
/// supplies the defaults: it populates the row on first run and is used as-is when the database
/// is not configured or unreachable, so the site still renders without a database.
/// With <c>Seed:Mode=Authoritative</c> the row is overwritten from config once per process.
/// </summary>
public class SiteProfileProvider : ISiteProfileProvider
{
    private const string CacheKey = "site-profile";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FallbackCacheDuration = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly IOptions<SiteOptions> _defaults;
    private readonly IConfiguration _config;
    private readonly ILogger<SiteProfileProvider> _logger;
    private readonly SemaphoreSlim _load = new(1, 1);
    private bool _synced;

    public SiteProfileProvider(IServiceScopeFactory scopeFactory, IMemoryCache cache, IOptions<SiteOptions> defaults,
        IConfiguration config, ILogger<SiteProfileProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _defaults = defaults;
        _config = config;
        _logger = logger;
    }

    private bool DatabaseConfigured => !string.IsNullOrWhiteSpace(_config.GetConnectionString("Default"));

    public async Task<SiteOptions> GetAsync(CancellationToken ct = default)
    {
        if (!DatabaseConfigured) return _defaults.Value;
        if (_cache.TryGetValue(CacheKey, out SiteOptions? cached) && cached is not null) return cached;

        await _load.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(CacheKey, out cached) && cached is not null) return cached;

            SiteOptions profile;
            TimeSpan duration;
            try
            {
                profile = await LoadOrCreateAsync(ct);
                duration = CacheDuration;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Site profile query failed; using configured defaults.");
                profile = _defaults.Value;
                duration = FallbackCacheDuration;
            }

            _cache.Set(CacheKey, profile, duration);
            return profile;
        }
        finally
        {
            _load.Release();
        }
    }

    public void Invalidate() => _cache.Remove(CacheKey);

    private async Task<SiteOptions> LoadOrCreateAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var row = await db.SiteProfiles.FirstOrDefaultAsync(p => p.Id == SiteProfile.SingletonId, ct);
        if (row is null)
        {
            row = SiteProfileMapper.ToEntity(_defaults.Value);
            db.SiteProfiles.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("Site profile created from the Site config section.");
            }
            catch (DbUpdateException)
            {
                // Another instance inserted the row first; read theirs.
                db.ChangeTracker.Clear();
                row = await db.SiteProfiles.FirstAsync(p => p.Id == SiteProfile.SingletonId, ct);
            }
        }
        else if (!_synced && _config.GetSeedMode() == SeedMode.Authoritative)
        {
            SiteProfileMapper.CopyTo(_defaults.Value, row);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Site profile overwritten from the Site config section (Seed:Mode=Authoritative).");
        }

        _synced = true;
        return SiteProfileMapper.ToOptions(row);
    }
}
