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
using Portfolio.Data;

namespace Portfolio.Web.Tests.Support;

/// <summary>A TimeProvider the test moves by hand.</summary>
public sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
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

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly string? _setupKey;

    public FakeClock Clock { get; } = new();
    public string UploadsPath { get; } = Directory.CreateTempSubdirectory("lcv-uploads-").FullName;

    public AdminApp() : this(SetupKey, null)
    {
    }

    public static AdminApp Create(string? setupKey, IReadOnlyDictionary<string, string?>? extraConfig = null) => new(setupKey, extraConfig);

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
            services.AddDbContext<PortfolioDbContext>(o => o.UseSqlite(_connection));
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        _connection.Dispose();
        try { Directory.Delete(UploadsPath, recursive: true); } catch (IOException) { }
    }
}
