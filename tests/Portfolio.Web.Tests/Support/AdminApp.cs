using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Portfolio.Data;

namespace Portfolio.Web.Tests.Support;

/// <summary>A TimeProvider the test moves by hand.</summary>
public sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// Runs a test's action once, just before the next save that writes ordered content (a row's position or a list stamp) or the profile row, then lets that save go on.
/// The action is another complete request, so a test can make two requests overlap: both have read the list before either has written.
/// </summary>
internal sealed class OrderedSaveHook : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    private readonly AdminApp _app;

    public OrderedSaveHook(AdminApp app) => _app = app;

    public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (_app.BeforeOrderedSave is not null && eventData.Context is { } ctx)
        {
            ctx.ChangeTracker.DetectChanges();
            var writesOrder = ctx.ChangeTracker.Entries().Any(e => e.Entity is Portfolio.Data.Entities.IStampedContent or Portfolio.Data.Entities.SiteProfile && e.State is EntityState.Added or EntityState.Modified);
            var hook = writesOrder ? Interlocked.Exchange(ref _app.BeforeOrderedSaveField, null) : null;
            if (hook is not null) await hook();
        }
        return result;
    }
}

/// <summary>The test server has no network peer; a request can name its client address in this header instead.</summary>
internal sealed class TestClientAddressFilter : IStartupFilter
{
    public const string Header = "X-Test-Client-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((ctx, nxt) =>
        {
            if (ctx.Request.Headers.TryGetValue(Header, out var ip) && System.Net.IPAddress.TryParse(ip.ToString(), out var address))
                ctx.Connection.RemoteIpAddress = address;
            return nxt();
        });
        next(app);
    };
}

/// <summary>
/// The real app on an in-memory SQLite database, run as Production so cookies are Secure and the
/// "Generated" output cache is active. Each instance owns its database, uploads folder and clock.
/// </summary>
public sealed class AdminApp : WebApplicationFactory<Program>
{
    public const string SetupKey = "test-setup-key-0123456789";
    public const string UserName = "owner";
    public const string Password = "correct horse battery staple";

    private SqliteConnection _connection = new("DataSource=:memory:");
    private bool _ownsConnection = true;
    private bool _migrationsApplied;
    private readonly string? _setupKey;

    public FakeClock Clock { get; } = new();

    internal Func<Task>? BeforeOrderedSaveField;

    /// <summary>Runs once, in the middle of the next request that saves ordered content or the profile, right before its save (see <see cref="OrderedSaveHook"/>).</summary>
    public Func<Task>? BeforeOrderedSave { get => BeforeOrderedSaveField; set => BeforeOrderedSaveField = value; }

    /// <summary>Runs <paramref name="outer"/>; right before it saves, <paramref name="inner"/> runs to completion as another request, so the two overlap.</summary>
    public async Task<T> OverlapAsync<T>(Func<Task<T>> outer, Func<Task> inner)
    {
        BeforeOrderedSave = inner;
        var result = await outer();
        Assert.Null(BeforeOrderedSave); // the other request really ran in the middle of this one
        return result;
    }
    public string UploadsPath { get; } = Directory.CreateTempSubdirectory("lcv-uploads-").FullName;

    public AdminApp() : this(SetupKey, null)
    {
    }

    public static AdminApp Create(string? setupKey, IReadOnlyDictionary<string, string?>? extraConfig = null) => new(setupKey, extraConfig);

    /// <summary>
    /// An app that runs the real start-up (migrate, seed, create the profile) against a database whose schema is already in place, as a restart of a
    /// deployed site does. The first call creates its own database; pass <paramref name="restartOf"/> to start again over that app's database.
    /// </summary>
    public static AdminApp StartedWithMigrations(string? setupKey, AdminApp? restartOf = null, IReadOnlyDictionary<string, string?>? extraConfig = null)
    {
        var app = new AdminApp(setupKey, extraConfig) { _migrationsApplied = true };
        if (restartOf is not null)
        {
            app._connection.Dispose();
            app._connection = restartOf._connection;
            app._ownsConnection = false;
        }
        return app;
    }

    private AdminApp(string? setupKey, IReadOnlyDictionary<string, string?>? extraConfig)
    {
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
        _setupKey = setupKey;
        _extraConfig = extraConfig;
        _connection.Open();
    }

    private readonly IReadOnlyDictionary<string, string?>? _extraConfig;
    private string? _templatesDir;

    /// <summary>An app whose template catalog is read from <paramref name="templatesDir"/> instead of the shipped templates.</summary>
    public static AdminApp WithTemplates(string templatesDir, IReadOnlyDictionary<string, string?>? extraConfig = null) =>
        new(SetupKey, extraConfig) { _templatesDir = templatesDir };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "sqlite-test",
                ["Setup:Key"] = _setupKey,
                ["Uploads:Path"] = UploadsPath,
                ["Site:Url"] = "https://owner.example",
            };
            if (_extraConfig is not null)
                foreach (var (k, v) in _extraConfig) values[k] = v;
            config.AddInMemoryCollection(values);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<PortfolioDbContext>));
            services.RemoveAll(typeof(DbContextOptions));
            services.RemoveAll(typeof(PortfolioDbContext));
            foreach (var d in services.Where(d => d.ServiceType.IsGenericType
                         && d.ServiceType.GetGenericArguments().Contains(typeof(PortfolioDbContext))).ToList())
                services.Remove(d);
            services.AddDbContext<PortfolioDbContext>(o =>
            {
                o.UseSqlite(_connection).AddInterceptors(new OrderedSaveHook(this));
                // The migrations are written for SQL Server, so the SQLite model always differs from their snapshot; real start-up must get past that check.
                if (_migrationsApplied) o.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
            });
            if (_templatesDir is not null)
            {
                services.RemoveAll<Portfolio.Web.Services.ITemplateCatalog>();
                services.AddSingleton<Portfolio.Web.Services.ITemplateCatalog>(new Portfolio.Web.Services.TemplateCatalog(_templatesDir));
            }
            services.AddTransient<IStartupFilter, TestClientAddressFilter>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            using var db = new PortfolioDbContext(new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite(_connection).Options);
            db.Database.EnsureCreated();
            if (_migrationsApplied)
            {
                // The schema exists, so tell the migrator every migration is applied: Program's MigrateAsync then does nothing (the migrations
                // are written for SQL Server) and goes on to seed, which is the step these tests are about.
                var history = db.GetService<IHistoryRepository>();
                history.CreateIfNotExists();
                var applied = history.GetAppliedMigrations().Select(m => m.MigrationId).ToHashSet();
                foreach (var m in db.Database.GetMigrations().Where(m => !applied.Contains(m)))
                    db.Database.ExecuteSqlRaw("INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, {1})", m, typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Select(a => a.InformationalVersion.Split('+')[0]).FirstOrDefault() ?? "unknown");
            }
        });
    }

    public PortfolioDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite(_connection).Options);

    /// <summary>A cookie-keeping client that does not follow redirects, so tests can assert them.</summary>
    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    /// <summary>Like <see cref="NewClient()"/>, but every request claims to come from the given address.</summary>
    public HttpClient NewClient(string clientAddress)
    {
        var client = NewClient();
        client.DefaultRequestHeaders.Add(TestClientAddressFilter.Header, clientAddress);
        return client;
    }

    public static readonly Regex TokenPattern =
        new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    public static async Task<string> GetTokenAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var match = TokenPattern.Match(await response.Content.ReadAsStringAsync());
        Assert.True(match.Success, $"No antiforgery token on {path}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, string? token, IEnumerable<KeyValuePair<string, string>> fields)
    {
        var all = fields.ToList();
        if (token is not null) all.Add(new("__RequestVerificationToken", token));
        return await client.PostAsync(path, new FormUrlEncodedContent(all));
    }

    public async Task<HttpResponseMessage> RunSetupAsync(HttpClient client, string? key = SetupKey, string user = UserName, string password = Password)
    {
        var token = await GetTokenAsync(client, "/setup");
        return await PostFormAsync(client, "/setup", token, new Dictionary<string, string>
        {
            ["SetupKey"] = key ?? "", ["UserName"] = user, ["Password"] = password, ["ConfirmPassword"] = password,
        });
    }

    public static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string user = UserName, string password = Password, string loginPath = "/admin/login")
    {
        var token = await GetTokenAsync(client, "/admin/login");
        return await PostFormAsync(client, loginPath, token, new Dictionary<string, string> { ["UserName"] = user, ["Password"] = password });
    }

    /// <summary>Creates the owner through /setup and signs in through /admin/login, like a real first run.</summary>
    public async Task<HttpClient> SignedInClientAsync()
    {
        var client = NewClient();
        var setup = await RunSetupAsync(client);
        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);
        var login = await SignInAsync(client);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    /// <summary>A fresh client signed in as the owner, for an app whose owner already exists (a second browser, or after the first one signed out).</summary>
    public async Task<HttpClient> NewClientSignedInAsync()
    {
        var client = NewClient();
        var login = await SignInAsync(client);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        if (_ownsConnection) _connection.Dispose();
        try { Directory.Delete(UploadsPath, recursive: true); } catch (IOException) { }
    }
}
