using System.Text;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Portfolio.Data;
using Portfolio.Data.Seeding;
using Portfolio.Web.Configuration;
using Portfolio.Web.Health;
using Portfolio.Web.Security;
using Portfolio.Web.Services;
using Portfolio.Web.Uploads;
using QuestPDF.Fluent;

var builder = WebApplication.CreateBuilder(args);

// Register the app services, configuration, and content pipeline.
builder.Services.AddRazorPages(o =>
{
    // Everything under /Admin needs the signed-in owner; only the login page opts out (see Login.cshtml.cs).
    o.Conventions.AuthorizeFolder("/Admin");
})
// A page marked [SectionPage] answers 404 while the template has its section disabled.
.AddMvcOptions(o => o.Filters.Add<SectionGateFilter>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<RequestTemplate>();
builder.Services.AddSingleton<TemplateResolver>();
builder.Services.AddOwnerAuth(builder.Environment);
builder.Services.AddScoped<ProfileEditor>();
builder.Services.Configure<UploadOptions>(builder.Configuration.GetSection("Uploads"));
builder.Services.AddSingleton<UploadStore>();
builder.Services.AddSingleton<ImageProcessor>();
builder.Services.AddScoped<PortfolioContentService>();
builder.Services.AddSingleton(new CvAtsReportProvider(Path.Combine(AppContext.BaseDirectory, "Seed")));
builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection("Site"));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ISiteProfileProvider, SiteProfileProvider>();
builder.Services.AddSingleton<ITemplateCatalog>(sp =>
    new TemplateCatalog(Path.Combine(AppContext.BaseDirectory, "Templates"), sp.GetRequiredService<ILogger<TemplateCatalog>>()));
builder.Services.Configure<RouteOptions>(o => o.LowercaseUrls = true);

// The PDF CV, OG image, and sitemap are generated on demand and are CPU/DB-heavy, but their
// inputs only change on restart (config + reseed). Cache them server-side so a burst of
// requests renders once (output caching also coalesces concurrent misses) instead of once each.
// Query keys are ignored so ?r=<random> can't force a fresh render per request, and caching is
// off in Development so edits show up immediately.
builder.Services.AddOutputCache(o =>
    o.AddPolicy("Generated", p =>
    {
        // Tagged so a profile save can evict every generated asset at once (see ProfileEditor). Also keyed on the
        // profile version: a render that started before a save read the old profile and is stored under the old key,
        // so the eviction cannot be undone by a request that was already in flight.
        p.Expire(TimeSpan.FromHours(1)).SetVaryByQuery(Array.Empty<string>()).Tag(OutputCacheTags.Generated)
            .VaryByValue(ctx => new KeyValuePair<string, string>("profile", ctx.RequestServices.GetRequiredService<ISiteProfileProvider>().Version.ToString()));
        if (builder.Environment.IsDevelopment()) p.NoCache();
    }));

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var connectionString = builder.Configuration.GetConnectionString("Default");
builder.Services.AddDbContext<PortfolioDbContext>(options =>
{
    // When no connection string is configured (e.g. before DB creds are set), the
    // context is left provider-less; the site still runs and /health/db reports it.
    if (!string.IsNullOrWhiteSpace(connectionString))
        options.UseSqlServer(connectionString, sql =>
        {
            sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(4), errorNumbersToAdd: null);
            sql.CommandTimeout(20);
        });
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "db" });

var app = builder.Build();

// Load the shipped templates now: a broken default manifest should stop the start (and the deploy's health check), not
// answer every page with a 500 on the first request. A broken optional template is skipped and logged by the catalog.
// Intentionally before the operator commands below: a broken default manifest should stop those too.
_ = app.Services.GetRequiredService<ITemplateCatalog>();

// Operator command: runs against the database and exits before the web host starts serving.
if (OwnerCli.IsCommand(args))
{
    Environment.ExitCode = await OwnerCli.RunAsync(app.Services, args, Console.In, Console.Out, Console.Error);
    return;
}

if (app.Environment.IsDevelopment())
{
    app.Logger.LogInformation("Starting Living CV in Development mode. Visit /health and /health/db to verify setup.");
}

// Configure the request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// A page of a section the template does not have shows the styled not-found page; every other 404 is left as it was.
app.UseNotFoundPage();

// SEO: force a single canonical host. www.<domain> and <domain> otherwise both serve
// 200 with their own self-referencing canonical tag, splitting ranking/link signals
// between two hostnames Google sees as duplicates. Redirect www -> apex to match the
// canonical/OG/sitemap URLs, which are already apex-based (the profile Url). The canonical
// host follows the profile, so editing the Url in /admin takes effect without a restart.
app.Use(async (ctx, next) =>
{
    var host = ctx.Request.Host.Host;
    if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
    {
        var siteUrl = (await ctx.RequestServices.GetRequiredService<ISiteProfileProvider>().GetAsync(ctx.RequestAborted)).Url;
        if (Uri.TryCreate(siteUrl, UriKind.Absolute, out var siteUri) && host.Equals($"www.{siteUri.Host}", StringComparison.OrdinalIgnoreCase))
        {
            var target = $"{ctx.Request.Scheme}://{siteUri.Host}{ctx.Request.PathBase}{ctx.Request.Path}{ctx.Request.QueryString}";
            ctx.Response.Redirect(target, permanent: true);
            return;
        }
    }
    await next();
});

app.UseHttpsRedirection();

// Apply baseline security headers.
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

// Uploaded photos live outside wwwroot and are served from /uploads. File names are random and never reused, so
// they can be cached for a year; only the three raster types we write are served, and never sniffed as anything else.
var uploads = app.Services.GetRequiredService<UploadStore>();
if (uploads.EnsureRoot())
{
    app.UseStaticFiles(new StaticFileOptions
    {
        RequestPath = UploadStore.RequestPath,
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploads.Root),
        ServeUnknownFileTypes = false,
        ContentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider(new Dictionary<string, string>
        {
            [".jpg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
        }),
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        },
    });
}

// Admin and setup responses are marked private and non-indexable before anything can cache them.
app.UsePrivateAreaHeaders();

app.UseRouting();

app.UseOutputCache();

app.UseAuthentication();
app.UseTemplatePreview();
app.UseAdminDenyByDefault();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// Liveness (no DB dependency — used by the deploy pipeline) and DB readiness. The DB check's
// description/error name the server, database, and raw exception text, so they're only
// returned in Development; elsewhere the endpoint reports bare status and the details go to
// the app log (the health check service logs failing checks).
var detailedHealth = app.Environment.IsDevelopment();
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = c => !c.Tags.Contains("db") });
app.MapHealthChecks("/health/db", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("db"),
    ResponseWriter = (ctx, report) => WriteHealthJson(ctx, report, detailedHealth),
});

// Generate the PDF CV from the same content source used by the site pages.
app.MapGet("/cv.pdf", async (HttpContext ctx, PortfolioContentService repo, ISiteProfileProvider profile) =>
{
    var site = await profile.GetAsync();
    var data = await repo.BuildCvDataAsync(site);
    SkipCacheIfFallback(ctx, repo);

    var bytes = new CvDocument(data).GeneratePdf();
    var fileName = $"{site.OwnerName.ToLowerInvariant().Replace(' ', '-')}-cv.pdf";
    return Results.File(bytes, "application/pdf", fileName);
}).CacheOutput("Generated");

// Generate the Open Graph image from the current site identity.
app.MapGet("/og.png", async (ISiteProfileProvider profile) =>
{
    var s = await profile.GetAsync();
    var domainLabel = (s.Url ?? "").Replace("https://", "").Replace("http://", "").TrimEnd('/').ToUpperInvariant();
    var tagline = string.Join(" ", new[] { s.Hero.Headline, s.Hero.HeadlineAccent }.Where(x => !string.IsNullOrWhiteSpace(x)));
    return Results.File(OgImage.Generate(s.OwnerName, s.Role, domainLabel, tagline), "image/png");
}).CacheOutput("Generated");

// Expose sitemap and robots routes for search engines. Blog posts carry a real publish
// date, so they get <lastmod> to help crawlers prioritise re-fetching fresh content;
// static/project routes have no reliable last-changed date, so it's omitted rather than guessed.
// URLs use the site profile Url (via AbsoluteBaseAsync), never the request Host, so the sitemap always lists the
// canonical domain.
app.MapGet("/sitemap.xml", async (HttpContext ctx, PortfolioContentService repo, ISiteProfileProvider profile, TemplateResolver templates) =>
{
    var baseUrl = await SeoHelpers.AbsoluteBaseAsync(ctx);
    // The saved template decides which pages exist: a disabled section has no page, so it has no sitemap entry either.
    // Always the saved template, never an owner preview, because this response is cached for everyone.
    var template = templates.Resolve(await profile.GetAsync());
    var urls = new List<(string Path, string? LastMod)> { ("/", null) };
    urls.AddRange(template.NavLinks(mobile: true).Select(l => (l.Path, (string?)null)));
    if (template.IsEnabled(SectionRegistry.Projects))
        urls.AddRange((await repo.GetProjectsAsync()).Select(p => ($"/projects/{p.Slug}", (string?)null)));
    if (template.IsEnabled(SectionRegistry.Blog))
        urls.AddRange((await repo.GetBlogPostsAsync()).Select(b => ($"/blog/{b.Slug}", (string?)b.Date.ToString("yyyy-MM-dd"))));
    SkipCacheIfFallback(ctx, repo);

    var sb = new StringBuilder();
    sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
    sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
    foreach (var (path, lastMod) in urls)
    {
        sb.Append($"  <url><loc>{baseUrl}{path}</loc>");
        if (lastMod is not null)
            sb.Append($"<lastmod>{lastMod}</lastmod>");
        sb.AppendLine("</url>");
    }
    sb.AppendLine("</urlset>");
    return Results.Content(sb.ToString(), "application/xml");
}).CacheOutput("Generated");

// Allow indexing in production and keep development subdomains out of search results.
app.MapGet("/robots.txt", async (HttpContext ctx) =>
{
    var baseUrl = await SeoHelpers.AbsoluteBaseAsync(ctx);
    var isDev = ctx.Request.Host.Host.StartsWith("dev.", StringComparison.OrdinalIgnoreCase);
    var body = isDev
        ? "User-agent: *\nDisallow: /\n"
        : $"User-agent: *\nAllow: /\nDisallow: /admin\n\nSitemap: {baseUrl}/sitemap.xml\n";
    return Results.Text(body, "text/plain");
});

await MigrateAndSeedAsync(app);

app.Run();

// A query that hit its empty fallback (transient DB failure) would otherwise be cached as a
// 200 with empty content for the full hour, so keep that response out of the output cache.
static void SkipCacheIfFallback(HttpContext ctx, PortfolioContentService repo)
{
    if (repo.HadFallback && ctx.Features.Get<IOutputCacheFeature>() is { } cache)
        cache.Context.AllowCacheStorage = false;
}

// Write a simple JSON payload for the database health endpoint. Descriptions and errors
// (server/db names, exception text) are included only when `detailed` is set.
static Task WriteHealthJson(HttpContext ctx, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report, bool detailed)
{
    ctx.Response.ContentType = "application/json";
    var payload = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = detailed ? e.Value.Description : null,
            error = detailed ? e.Value.Exception?.Message : null,
        }),
    };
    return ctx.Response.WriteAsJsonAsync(payload);
}

// Apply EF Core migrations and seed content without blocking startup.
static async Task MigrateAndSeedAsync(WebApplication app)
{
    var seedMode = app.Configuration.GetSeedMode(app.Logger);
    app.Logger.LogInformation("Seed mode: {SeedMode}", seedMode);

    var cs = app.Configuration.GetConnectionString("Default");
    if (string.IsNullOrWhiteSpace(cs))
    {
        app.Logger.LogWarning("ConnectionStrings:Default not set — skipping migrate/seed. DB-backed features disabled until configured.");
        return;
    }

    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        await db.Database.MigrateAsync(cts.Token);
        var seedDir = Path.Combine(AppContext.BaseDirectory, "Seed");
        await PortfolioSeeder.SeedAsync(db, seedDir, seedMode, app.Logger, cts.Token);
        await scope.ServiceProvider.GetRequiredService<ISiteProfileProvider>().GetAsync(cts.Token);
        app.Logger.LogInformation("Database migrated and seeded.");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Database migrate/seed failed — site still starts; DB features may be unavailable.");
    }
}

public partial class Program;
