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

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddScoped<PortfolioContentService>();
builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection("Site"));
builder.Services.Configure<RouteOptions>(o => o.LowercaseUrls = true);

// QuestPDF community licence (free for this use).
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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Baseline security headers.
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

// Living-CV PDF — generated from the same DB content as the /cv page, so it never drifts.
app.MapGet("/cv.pdf", async (PortfolioContentService repo, IOptions<SiteOptions> siteOpt) =>
{
    var site = siteOpt.Value;
    var roles = await repo.GetExperiencesAsync();
    var skills = await repo.GetSkillsAsync();
    var order = new[] { "Backend", "Frontend", "Architecture", "Cloud/DevOps", "Data", "AI", "Embedded/IoT" };
    var groups = skills.GroupBy(s => s.Group)
        .OrderBy(g => { var i = Array.IndexOf(order, g.Key); return i < 0 ? int.MaxValue : i; })
        .ToList();
    var projects = await repo.GetFeaturedProjectsAsync();
    var certs = await repo.GetCertificationsAsync();
    var contacts = new[] { site.HasEmail ? site.Email : null, site.HasPhone ? site.Phone : null, site.HasLinkedIn ? site.LinkedIn : null, site.HasGitHub ? site.GitHub : null }
        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();

    var data = new CvData(
        site.OwnerName,
        site.Role,
        site.Summary,
        contacts, roles, groups, projects, certs, site.Url);

    var bytes = new CvDocument(data).GeneratePdf();
    var fileName = $"{site.OwnerName.ToLowerInvariant().Replace(' ', '-')}-cv.pdf";
    return Results.File(bytes, "application/pdf", fileName);
});

// Open Graph share image (name + role), generated on the brand background.
app.MapGet("/og.png", (IOptions<SiteOptions> siteOpt) =>
{
    var s = siteOpt.Value;
    var domainLabel = (s.Url ?? "").Replace("https://", "").Replace("http://", "").TrimEnd('/').ToUpperInvariant();
    var tagline = string.Join(" ", new[] { s.Hero.Headline, s.Hero.HeadlineAccent }.Where(x => !string.IsNullOrWhiteSpace(x)));
    return Results.File(OgImage.Generate(s.OwnerName, s.Role, domainLabel, tagline), "image/png");
});

// SEO: sitemap over all static routes + project/blog slugs from the DB.
app.MapGet("/sitemap.xml", async (HttpContext ctx, PortfolioContentService repo) =>
{
    var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    var urls = new List<string> { "/", "/about", "/experience", "/projects", "/skills", "/certifications", "/blog", "/talks", "/cv", "/contact" };
    urls.AddRange((await repo.GetProjectsAsync()).Select(p => $"/projects/{p.Slug}"));
    urls.AddRange((await repo.GetBlogPostsAsync()).Select(b => $"/blog/{b.Slug}"));

    var sb = new StringBuilder();
    sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
    sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
    foreach (var u in urls)
        sb.AppendLine($"  <url><loc>{baseUrl}{u}</loc></url>");
    sb.AppendLine("</urlset>");
    return Results.Content(sb.ToString(), "application/xml");
});

// robots.txt — allow prod + point to the sitemap; disallow the dev subdomain from indexing.
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

// Writes a small JSON body for /health/db so connection issues are diagnosable from the
// browser during setup (status + description + error per check). Safe: messages are things
// like "Login failed for user 'app'" — no secrets. Consider trimming once DB is verified.
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

// Applies EF Core migrations and runs the idempotent seeder. Never brings the site
// down: any failure is logged and the app continues serving.
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
