using System.Text;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Portfolio.Data;
using Portfolio.Data.Seeding;
using Portfolio.Web.Configuration;
using Portfolio.Web.Health;
using Portfolio.Web.Services;
using QuestPDF.Fluent;

var builder = WebApplication.CreateBuilder(args);

// Register the app services, configuration, and content pipeline.
builder.Services.AddRazorPages();
builder.Services.AddScoped<PortfolioContentService>();
builder.Services.AddSingleton(new CvAtsReportProvider(Path.Combine(AppContext.BaseDirectory, "Seed")));
builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection("Site"));
builder.Services.Configure<RouteOptions>(o => o.LowercaseUrls = true);

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

// SEO: force a single canonical host. www.<domain> and <domain> otherwise both serve
// 200 with their own self-referencing canonical tag, splitting ranking/link signals
// between two hostnames Google sees as duplicates. Redirect www -> apex to match the
// canonical/OG/sitemap URLs, which are already apex-based (Site:Url).
var canonicalHost = Uri.TryCreate(app.Configuration["Site:Url"], UriKind.Absolute, out var siteUri) ? siteUri.Host : null;
if (!string.IsNullOrEmpty(canonicalHost))
{
    app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Host.Host.Equals($"www.{canonicalHost}", StringComparison.OrdinalIgnoreCase))
        {
            var target = $"{ctx.Request.Scheme}://{canonicalHost}{ctx.Request.PathBase}{ctx.Request.Path}{ctx.Request.QueryString}";
            ctx.Response.Redirect(target, permanent: true);
            return;
        }
        await next();
    });
}

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

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// Liveness (no DB dependency — used by the deploy pipeline) and DB readiness.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = c => !c.Tags.Contains("db") });
app.MapHealthChecks("/health/db", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("db"),
    ResponseWriter = WriteHealthJson,
});

// Generate the PDF CV from the same content source used by the site pages.
app.MapGet("/cv.pdf", async (PortfolioContentService repo, IOptions<SiteOptions> siteOpt) =>
{
    var site = siteOpt.Value;
    var data = await repo.BuildCvDataAsync(site);

    var bytes = new CvDocument(data).GeneratePdf();
    var fileName = $"{site.OwnerName.ToLowerInvariant().Replace(' ', '-')}-cv.pdf";
    return Results.File(bytes, "application/pdf", fileName);
});

// Generate the Open Graph image from the current site identity.
app.MapGet("/og.png", (IOptions<SiteOptions> siteOpt) =>
{
    var s = siteOpt.Value;
    var domainLabel = (s.Url ?? "").Replace("https://", "").Replace("http://", "").TrimEnd('/').ToUpperInvariant();
    var tagline = string.Join(" ", new[] { s.Hero.Headline, s.Hero.HeadlineAccent }.Where(x => !string.IsNullOrWhiteSpace(x)));
    return Results.File(OgImage.Generate(s.OwnerName, s.Role, domainLabel, tagline), "image/png");
});

// Expose sitemap and robots routes for search engines. Blog posts carry a real publish
// date, so they get <lastmod> to help crawlers prioritise re-fetching fresh content;
// static/project routes have no reliable last-changed date, so it's omitted rather than guessed.
app.MapGet("/sitemap.xml", async (HttpContext ctx, PortfolioContentService repo) =>
{
    var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    var urls = new List<(string Path, string? LastMod)> {
        ("/", null), ("/about", null), ("/experience", null), ("/projects", null), ("/skills", null),
        ("/certifications", null), ("/blog", null), ("/talks", null), ("/cv", null), ("/contact", null),
    };
    urls.AddRange((await repo.GetProjectsAsync()).Select(p => ($"/projects/{p.Slug}", (string?)null)));
    urls.AddRange((await repo.GetBlogPostsAsync()).Select(b => ($"/blog/{b.Slug}", (string?)b.Date.ToString("yyyy-MM-dd"))));

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
});

// Allow indexing in production and keep development subdomains out of search results.
app.MapGet("/robots.txt", (HttpContext ctx) =>
{
    var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    var isDev = ctx.Request.Host.Host.StartsWith("dev.", StringComparison.OrdinalIgnoreCase);
    var body = isDev
        ? "User-agent: *\nDisallow: /\n"
        : $"User-agent: *\nAllow: /\n\nSitemap: {baseUrl}/sitemap.xml\n";
    return Results.Text(body, "text/plain");
});

await MigrateAndSeedAsync(app);

app.Run();

// Write a simple JSON payload for the database health endpoint so setup issues are visible.
static Task WriteHealthJson(HttpContext ctx, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
{
    ctx.Response.ContentType = "application/json";
    var payload = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            error = e.Value.Exception?.Message,
        }),
    };
    return ctx.Response.WriteAsJsonAsync(payload);
}

// Apply EF Core migrations and seed content without blocking startup.
static async Task MigrateAndSeedAsync(WebApplication app)
{
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
        await PortfolioSeeder.SeedAsync(db, seedDir, app.Logger, cts.Token);
        app.Logger.LogInformation("Database migrated and seeded.");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Database migrate/seed failed — site still starts; DB features may be unavailable.");
    }
}
