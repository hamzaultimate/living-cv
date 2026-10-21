using Microsoft.Data.SqlClient;
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
/// Reads are stale-while-revalidate: once a profile has loaded, an expired entry is served
/// immediately while a single background load refreshes it. Only the very first read waits for
/// the database, and only briefly before the config defaults are served.
/// The cache is per instance (register as a singleton); <see cref="Invalidate"/> does not reach
/// other processes, which pick up changes when their own entry expires.
/// </summary>
public class SiteProfileProvider : ISiteProfileProvider
{
    private const string CacheKey = "site-profile";
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultFirstLoadTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FallbackCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly IOptions<SiteOptions> _defaults;
    private readonly IConfiguration _config;
    private readonly ILogger<SiteProfileProvider> _logger;
    private readonly TimeSpan _cacheDuration;
    private readonly TimeSpan _firstLoadTimeout;

    private readonly object _gate = new();
    private SiteOptions? _lastGood;
    private Task<SiteOptions>? _inflight;
    private int _generation;
    private bool _synced;

    public SiteProfileProvider(IServiceScopeFactory scopeFactory, IMemoryCache cache, IOptions<SiteOptions> defaults,
        IConfiguration config, ILogger<SiteProfileProvider> logger,
        TimeSpan? cacheDuration = null, TimeSpan? firstLoadTimeout = null)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _defaults = defaults;
        _config = config;
        _logger = logger;
        _cacheDuration = cacheDuration ?? DefaultCacheDuration;
        _firstLoadTimeout = firstLoadTimeout ?? DefaultFirstLoadTimeout;
    }

    private bool DatabaseConfigured => !string.IsNullOrWhiteSpace(_config.GetConnectionString("Default"));

    public async Task<SiteOptions> GetAsync(CancellationToken ct = default)
    {
        if (!DatabaseConfigured) return _defaults.Value;

        Task<SiteOptions> load;
        lock (_gate)
        {
            if (_cache.TryGetValue(CacheKey, out SiteOptions? cached) && cached is not null) return cached;
            load = _inflight ??= Task.Run(LoadAsync, CancellationToken.None);
            if (_lastGood is not null) return _lastGood;
        }

        // Nothing cached yet: wait briefly for the first load, then serve the defaults without caching them.
        try
        {
            return await load.WaitAsync(_firstLoadTimeout, ct);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Site profile load took longer than {Timeout}; serving configured defaults.", _firstLoadTimeout);
            return _defaults.Value;
        }
    }

    /// <summary>Drops the cached profile (including the stale copy) so the next read reloads it.
    /// A load already in flight is abandoned: its result is not cached.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _generation++;
            _lastGood = null;
            _inflight = null;
            _cache.Remove(CacheKey);
        }
    }

    private async Task<SiteOptions> LoadAsync()
    {
        int generation;
        lock (_gate) generation = _generation;

        SiteOptions profile;
        TimeSpan duration;
        var loaded = false;
        try
        {
            using var cts = new CancellationTokenSource(LoadTimeout);
            profile = await LoadOrCreateAsync(cts.Token);
            duration = _cacheDuration;
            loaded = true;
        }
        catch (Exception ex)
        {
            SiteOptions? stale;
            lock (_gate) stale = _lastGood;
            _logger.LogWarning(ex, "Site profile query failed; using {Source}.", stale is null ? "configured defaults" : "the last loaded profile");
            profile = stale ?? _defaults.Value;
            duration = FallbackCacheDuration;
        }

        lock (_gate)
        {
            if (generation == _generation)
            {
                if (loaded) _lastGood = profile;
                _cache.Set(CacheKey, profile, duration);
                _inflight = null;
            }
        }
        return profile;
    }

    private async Task<SiteOptions> LoadOrCreateAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var row = await db.SiteProfiles.FirstOrDefaultAsync(p => p.Id == SiteProfile.SingletonId, ct);
        if (row is null)
        {
            row = SiteProfileMapper.ToEntity(_defaults.Value);
            SiteProfileMapper.FitToColumns(row);
            db.SiteProfiles.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("Site profile created from the Site config section.");
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex))
            {
                // Another instance inserted the row first; read theirs.
                db.ChangeTracker.Clear();
                row = await db.SiteProfiles.FirstOrDefaultAsync(p => p.Id == SiteProfile.SingletonId, ct);
                if (row is null) throw;
            }
        }
        else if (!_synced && _config.GetSeedMode(_logger) == SeedMode.Authoritative)
        {
            SiteProfileMapper.CopyTo(_defaults.Value, row);
            SiteProfileMapper.FitToColumns(row);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Site profile overwritten from the Site config section (Seed:Mode=Authoritative).");
        }

        _synced = true;
        return SiteProfileMapper.ToOptions(row);
    }

    /// <summary>True for a primary-key / unique-index violation: SQL Server 2627 and 2601, or the
    /// equivalent SQLite extended codes (used by the test database).</summary>
    internal static bool IsDuplicateKey(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is SqlException sql && sql.Number is 2627 or 2601) return true;
            if (e.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException"
                && e.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(e) is 1555 or 2067) return true;
        }
        return false;
    }
}
