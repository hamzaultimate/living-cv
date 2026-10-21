using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Configuration;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Services;

public class SiteProfileProviderTests
{
    private static SiteOptions Defaults() => new()
    {
        OwnerName = "Alex Rivera",
        BrandName = "Alex",
        Role = "Senior Software Engineer",
        JobTitle = "Senior Engineer",
        Url = "https://example.dev",
        GoogleAnalyticsId = "G-TEST",
        Summary = "Summary",
        KnowsAbout = { ".NET", "Azure" },
        PhotoUrl = "/img/avatar.svg",
        Hero = new HeroOptions { Eyebrow = "Eyebrow", Headline = "Headline", HeadlineAccent = "Accent", Lede = "Lede" },
        Metrics = { new MetricOption { Value = "~20k", Unit = "rps", Label = "peak" } },
        About = new AboutOptions
        {
            Headline = "About",
            Paragraphs = { "P1", "P2" },
            ValueProps = { new ValuePropOption { Title = "V", Body = "B" } },
            CurrentlyNote = "Note",
            Focus = { "Focus" },
        },
        Phone = "+1 555 0100",
        Email = "a@example.dev",
        SecondaryEmails = { "b@example.dev" },
        LinkedIn = "https://linkedin.com/in/alex",
    };

    private static (SiteProfileProvider Provider, IServiceProvider Services) Create(
        TestDatabase? database, SiteOptions? defaults = null, string? seedMode = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = database is null ? "" : "test",
            ["Seed:Mode"] = seedMode,
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        if (database is not null)
            services.AddScoped(_ => database.CreateContext());
        var sp = services.BuildServiceProvider();

        var provider = new SiteProfileProvider(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(defaults ?? Defaults()),
            config,
            NullLogger<SiteProfileProvider>.Instance);
        return (provider, sp);
    }

    [Fact]
    public async Task FirstRun_PopulatesTheProfileRowFromConfig()
    {
        using var database = new TestDatabase();
        var (provider, _) = Create(database);

        var site = await provider.GetAsync();

        Assert.Equal("Alex Rivera", site.OwnerName);
        using var db = database.CreateContext();
        var row = await db.SiteProfiles.SingleAsync();
        Assert.Equal(SiteProfile.SingletonId, row.Id);
        Assert.Equal("Alex Rivera", row.OwnerName);
        Assert.Equal("software", row.TemplateKey);
        Assert.Equal(new[] { ".NET", "Azure" }, row.KnowsAbout);
        Assert.Equal("~20k", Assert.Single(row.Metrics).Value);
        Assert.Equal(new[] { "P1", "P2" }, row.About.Paragraphs);
    }

    [Fact]
    public async Task ProfileReadFromTheDatabase_HasTheSameShapeAndHelpersAsConfig()
    {
        using var database = new TestDatabase();
        var defaults = Defaults();
        var (provider, _) = Create(database, defaults);

        var site = await provider.GetAsync();

        Assert.Equal(defaults.OwnerName, site.OwnerName);
        Assert.Equal(defaults.Hero.Lede, site.Hero.Lede);
        Assert.Equal(defaults.Metrics.Select(m => (m.Value, m.Unit, m.Label)), site.Metrics.Select(m => (m.Value, m.Unit, m.Label)));
        Assert.Equal(defaults.About.Paragraphs, site.About.Paragraphs);
        Assert.Equal(defaults.About.ValueProps.Select(v => (v.Title, v.Body)), site.About.ValueProps.Select(v => (v.Title, v.Body)));
        Assert.Equal(defaults.About.CurrentlyNote, site.About.CurrentlyNote);
        Assert.Equal(defaults.About.Focus, site.About.Focus);
        Assert.True(site.HasEmail);
        Assert.Equal("mailto:a@example.dev", site.EmailHref);
        Assert.Equal(new[] { "a@example.dev", "b@example.dev" }, site.AllEmails);
        Assert.Equal("tel:+15550100", site.PhoneHref);
        Assert.True(site.HasPhoto);
        Assert.False(site.HasPhotoLight);
        Assert.True(site.HasLinkedIn);
        Assert.False(site.HasGitHub);
        Assert.True(site.HasGoogleAnalytics);
    }

    [Fact]
    public async Task ExistingRow_WinsOverConfig_WhenSeedModeIsIfEmpty()
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.SiteProfiles.Add(new SiteProfile { OwnerName = "Edited Name", TemplateKey = "software" });
            await db.SaveChangesAsync();
        }
        var (provider, _) = Create(database);

        var site = await provider.GetAsync();

        Assert.Equal("Edited Name", site.OwnerName);
        using var check = database.CreateContext();
        Assert.Equal("Edited Name", (await check.SiteProfiles.SingleAsync()).OwnerName);
    }

    [Fact]
    public async Task AuthoritativeMode_OverwritesTheRowFromConfig()
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.SiteProfiles.Add(new SiteProfile { OwnerName = "Edited Name" });
            await db.SaveChangesAsync();
        }
        var (provider, _) = Create(database, seedMode: "Authoritative");

        var site = await provider.GetAsync();

        Assert.Equal("Alex Rivera", site.OwnerName);
        using var check = database.CreateContext();
        Assert.Equal("Alex Rivera", (await check.SiteProfiles.SingleAsync()).OwnerName);
    }

    [Fact]
    public async Task Result_IsCachedUntilInvalidated()
    {
        using var database = new TestDatabase();
        var (provider, _) = Create(database);
        var first = await provider.GetAsync();

        using (var db = database.CreateContext())
        {
            (await db.SiteProfiles.SingleAsync()).OwnerName = "Renamed";
            await db.SaveChangesAsync();
        }

        Assert.Same(first, await provider.GetAsync());
        Assert.Equal("Alex Rivera", (await provider.GetAsync()).OwnerName);

        provider.Invalidate();

        Assert.Equal("Renamed", (await provider.GetAsync()).OwnerName);
    }

    [Fact]
    public async Task Prime_ServesTheSavedProfile_NotTheDefaults_EvenWhenTheDatabaseCannotAnswer()
    {
        using var database = new TestDatabase(createSchema: false);
        var (provider, _) = Create(database);
        var saved = Defaults();
        saved.OwnerName = "Saved Name";
        var before = provider.Version;

        provider.Prime(saved);

        Assert.NotEqual(before, provider.Version);
        Assert.Same(saved, await provider.GetAsync());
    }

    [Fact]
    public async Task Invalidate_AndPrime_EachChangeTheVersion()
    {
        using var database = new TestDatabase();
        var (provider, _) = Create(database);
        await provider.GetAsync();
        var first = provider.Version;

        provider.Invalidate();
        var afterInvalidate = provider.Version;
        provider.Prime(Defaults());

        Assert.NotEqual(first, afterInvalidate);
        Assert.NotEqual(afterInvalidate, provider.Version);
    }

    [Fact]
    public async Task NoDatabaseConfigured_ReturnsConfigDefaults()
    {
        var defaults = Defaults();
        var (provider, _) = Create(database: null, defaults);

        var site = await provider.GetAsync();

        Assert.Equal("Alex Rivera", site.OwnerName);
        Assert.Equal(defaults.Url, site.Url);
        provider.Invalidate();
        Assert.Equal("Alex Rivera", (await provider.GetAsync()).OwnerName);
    }

    [Fact]
    public async Task QueryFailure_FallsBackToConfigDefaults()
    {
        using var database = new TestDatabase(createSchema: false);
        var (provider, _) = Create(database);

        var site = await provider.GetAsync();

        Assert.Equal("Alex Rivera", site.OwnerName);
        Assert.Equal("Senior Engineer", site.JobTitle);
    }

    [Fact]
    public async Task QueryFailure_RecoversAfterInvalidateOnceTheDatabaseIsReady()
    {
        using var database = new TestDatabase(createSchema: false);
        var (provider, _) = Create(database);
        await provider.GetAsync();

        using (var db = database.CreateContext())
            await db.Database.EnsureCreatedAsync();
        provider.Invalidate();
        await provider.GetAsync();

        using var check = database.CreateContext();
        Assert.Equal(1, await check.SiteProfiles.CountAsync());
    }

    private static async Task<T> Eventually<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var value = await read();
            if (done(value) || DateTime.UtcNow > deadline) return value;
            await Task.Delay(20);
        }
    }

    private static async Task Rename(TestDatabase database, string name)
    {
        using var db = database.CreateContext();
        (await db.SiteProfiles.SingleAsync()).OwnerName = name;
        await db.SaveChangesAsync();
    }

    private static SiteProfileProvider CreateCustom(
        IServiceCollection services, TimeSpan? cacheDuration = null, TimeSpan? firstLoadTimeout = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "test" }).Build();
        return new SiteProfileProvider(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(Defaults()),
            config,
            NullLogger<SiteProfileProvider>.Instance,
            cacheDuration,
            firstLoadTimeout);
    }

    [Fact]
    public async Task OverLongConfigValues_AreTrimmedAndTruncatedToTheColumnLimits()
    {
        using var database = new TestDatabase();
        var defaults = Defaults();
        defaults.OwnerName = "  " + new string('n', 500) + "  ";
        defaults.BrandName = new string('b', 100);
        defaults.Role = new string('r', 400);
        defaults.Url = "https://example.dev/" + new string('u', 400);
        defaults.GoogleAnalyticsId = new string('g', 80);
        defaults.PhotoUrl = new string('p', 500);
        defaults.TemplateKey = new string('t', 90);
        var (provider, _) = Create(database, defaults);

        var site = await provider.GetAsync();

        using var db = database.CreateContext();
        var row = await db.SiteProfiles.SingleAsync();
        Assert.Equal(SiteProfile.MaxLength.OwnerName, row.OwnerName.Length);
        Assert.Equal(new string('n', 200), row.OwnerName);
        Assert.Equal(SiteProfile.MaxLength.BrandName, row.BrandName.Length);
        Assert.Equal(SiteProfile.MaxLength.Role, row.Role.Length);
        Assert.Equal(SiteProfile.MaxLength.Url, row.Url.Length);
        Assert.Equal(SiteProfile.MaxLength.GoogleAnalyticsId, row.GoogleAnalyticsId!.Length);
        Assert.Equal(SiteProfile.MaxLength.PhotoUrl, row.PhotoUrl!.Length);
        Assert.Equal(SiteProfile.MaxLength.TemplateKey, row.TemplateKey.Length);
        Assert.Equal(row.OwnerName, site.OwnerName);
    }

    [Fact]
    public async Task OverLongConfigValues_AreFittedInAuthoritativeMode()
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.SiteProfiles.Add(new SiteProfile { OwnerName = "Edited" });
            await db.SaveChangesAsync();
        }
        var defaults = Defaults();
        defaults.OwnerName = new string('n', 500);
        var (provider, _) = Create(database, defaults, seedMode: "Authoritative");

        await provider.GetAsync();

        using var check = database.CreateContext();
        Assert.Equal(SiteProfile.MaxLength.OwnerName, (await check.SiteProfiles.SingleAsync()).OwnerName.Length);
    }

    [Fact]
    public async Task InsertRace_ReadsTheRowTheOtherInstanceInserted()
    {
        using var database = new TestDatabase();
        var services = new ServiceCollection();
        services.AddScoped(_ => new PortfolioDbContext(new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseSqlite(database.Connection)
            .AddInterceptors(new InsertFirstInterceptor(database))
            .Options));
        var provider = CreateCustom(services);

        var site = await provider.GetAsync();

        Assert.Equal("Raced", site.OwnerName);
    }

    [Fact]
    public async Task NonDuplicateInsertFailure_IsNotMaskedAsARace_AndFallsBackToDefaults()
    {
        using var database = new TestDatabase();
        var services = new ServiceCollection();
        services.AddScoped(_ => new PortfolioDbContext(new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseSqlite(database.Connection)
            .AddInterceptors(new FailingSaveInterceptor())
            .Options));
        var provider = CreateCustom(services);

        var site = await provider.GetAsync();

        Assert.Equal("Alex Rivera", site.OwnerName);
        using var check = database.CreateContext();
        Assert.Equal(0, await check.SiteProfiles.CountAsync());
    }

    [Fact]
    public async Task ExpiredProfile_IsServedImmediatelyWhileRefreshingInTheBackground()
    {
        using var database = new TestDatabase();
        var gate = new ManualResetEventSlim(true);
        var services = new ServiceCollection();
        services.AddScoped(_ => { gate.Wait(); return database.CreateContext(); });
        var provider = CreateCustom(services, cacheDuration: TimeSpan.FromMilliseconds(50));
        var first = await provider.GetAsync();
        await Rename(database, "Renamed");
        await Task.Delay(120);

        gate.Reset(); // the refresh now blocks on the database
        var stale = await provider.GetAsync().WaitAsync(TimeSpan.FromSeconds(2));
        var staleAgain = await provider.GetAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Same(first, stale);
        Assert.Same(first, staleAgain);
        gate.Set();
        var refreshed = await Eventually(() => provider.GetAsync(), s => s.OwnerName == "Renamed");
        Assert.Equal("Renamed", refreshed.OwnerName);
    }

    [Fact]
    public async Task ExpiredProfile_KeepsServingTheLastGoodOneWhenTheRefreshFails()
    {
        using var database = new TestDatabase();
        var fail = false;
        var services = new ServiceCollection();
        services.AddScoped(_ => fail ? throw new InvalidOperationException("db down") : database.CreateContext());
        var provider = CreateCustom(services, cacheDuration: TimeSpan.FromMilliseconds(50));
        var first = await provider.GetAsync();
        await Rename(database, "Stored");
        fail = true;
        await Task.Delay(120);

        await provider.GetAsync();
        await Task.Delay(100);

        Assert.Same(first, await provider.GetAsync());
    }

    [Fact]
    public async Task FirstLoad_ServesConfigDefaultsAfterTheTimeout_ThenPicksUpTheDatabase()
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.SiteProfiles.Add(new SiteProfile { OwnerName = "From Database" });
            await db.SaveChangesAsync();
        }
        var gate = new ManualResetEventSlim(false);
        var services = new ServiceCollection();
        services.AddScoped(_ => { gate.Wait(); return database.CreateContext(); });
        var provider = CreateCustom(services, firstLoadTimeout: TimeSpan.FromMilliseconds(100));

        var site = await provider.GetAsync().WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal("Alex Rivera", site.OwnerName);
        gate.Set();
        var loaded = await Eventually(() => provider.GetAsync(), s => s.OwnerName == "From Database");
        Assert.Equal("From Database", loaded.OwnerName);
    }

    [Fact]
    public async Task Invalidate_IsNotUndoneByALoadThatWasAlreadyInFlight()
    {
        using var database = new TestDatabase();
        var entered = new SemaphoreSlim(0);
        var gate = new ManualResetEventSlim(false);
        var services = new ServiceCollection();
        services.AddScoped(_ => { entered.Release(); gate.Wait(); return database.CreateContext(); });
        var provider = CreateCustom(services, firstLoadTimeout: TimeSpan.FromSeconds(10));

        var inFlight = provider.GetAsync();
        Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(5)));
        provider.Invalidate();
        gate.Set();
        Assert.Equal("Alex Rivera", (await inFlight).OwnerName);

        await Rename(database, "Renamed");

        // The abandoned load must not have repopulated the cache with the old profile.
        Assert.Equal("Renamed", (await provider.GetAsync()).OwnerName);
    }

    private sealed class InsertFirstInterceptor(TestDatabase database) : SaveChangesInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_done)
            {
                _done = true;
                using var other = database.CreateContext();
                other.SiteProfiles.Add(new SiteProfile { OwnerName = "Raced" });
                await other.SaveChangesAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("string or binary data would be truncated", new InvalidOperationException("truncation"));
    }
}
