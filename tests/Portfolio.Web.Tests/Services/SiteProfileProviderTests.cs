using Microsoft.EntityFrameworkCore;
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
}
