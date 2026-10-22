using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Seeding;
using Portfolio.Web.Services;

namespace Portfolio.Web.Tests.Support;

/// <summary>Fills a test app's database with the demo persona's content (the shipped seed files) and normalises rendered HTML.</summary>
public static class DemoSite
{
    /// <summary>Public pages compared against the committed software snapshots, as (file name, request path).</summary>
    public static readonly (string File, string Path)[] SnapshotPages =
    [
        ("home.html", "/"),
        ("about.html", "/about"),
        ("experience.html", "/experience"),
        ("projects.html", "/projects"),
        ("project-detail.html", "/projects/{project}"),
        ("project-missing.html", "/projects/no-such-project"),
        ("skills.html", "/skills"),
        ("certifications.html", "/certifications"),
        ("blog.html", "/blog"),
        ("blog-post.html", "/blog/{post}"),
        ("blog-missing.html", "/blog/no-such-post"),
        ("talks.html", "/talks"),
        ("cv.html", "/cv"),
        ("contact.html", "/contact"),
        ("sitemap.xml", "/sitemap.xml"),
    ];

    /// <summary>Seeds every content table from the shipped seed files (a no-op for tables that already have rows).</summary>
    public static async Task SeedAsync(AdminApp app)
    {
        _ = app.Services; // building the host creates the schema
        using var db = app.CreateDbContext();
        await PortfolioSeeder.SeedAsync(db, Path.Combine(AppContext.BaseDirectory, "Seed"), SeedMode.IfEmpty);
    }

    /// <summary>The fictional starter content of a template as the site's profile and content, like picking sample content in a wizard.
    /// The profile keeps the test site's canonical URL. Call before the first request, or the profile cache is refreshed here.</summary>
    public static async Task ApplyStarterAsync(AdminApp app, string templateKey)
    {
        _ = app.Services;
        var file = Path.Combine(AppContext.BaseDirectory, "Templates", templateKey, "starter.json");
        var starter = TemplateStarter.Parse(await File.ReadAllTextAsync(file));
        starter.Profile.Url = "https://owner.example";

        using (var db = app.CreateDbContext())
        {
            db.SiteProfiles.RemoveRange(db.SiteProfiles);
            var row = SiteProfileMapper.ToEntity(starter.Profile);
            SiteProfileMapper.FitToColumns(row);
            db.SiteProfiles.Add(row);
            db.Experiences.AddRange(starter.Experiences);
            db.Skills.AddRange(starter.Skills);
            db.Projects.AddRange(starter.Projects);
            db.Certifications.AddRange(starter.Certifications);
            db.Testimonials.AddRange(starter.Testimonials);
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
    }

    public static (string Project, string Post) FirstSlugs(AdminApp app)
    {
        _ = app.Services;
        using var db = app.CreateDbContext();
        return (db.Projects.OrderBy(p => p.SortOrder).Select(p => p.Slug).First(),
                db.BlogPosts.Where(b => !b.Draft).OrderByDescending(b => b.Date).Select(b => b.Slug).First());
    }

    /// <summary>Removes what differs between two renders of the same page: antiforgery tokens, nonces, asset version hashes and fingerprints and the copyright year.</summary>
    public static string Normalise(string html)
    {
        html = Regex.Replace(html, "(__RequestVerificationToken\" type=\"hidden\" value=\"|nonce=\")[^\"]*", "$1");
        html = Regex.Replace(html, @"\?v=[A-Za-z0-9_\-]+", "?v=HASH");
        html = Regex.Replace(html, @"/(css|js)/(app|site)\.[a-z0-9]{8,}\.(css|js)", "/$1/$2.HASH.$3");
        html = Regex.Replace(html, @"© \d{4}", "© YEAR");
        return html.Replace("\r\n", "\n");
    }

    /// <summary>Words that belong to the software template only. None may appear on a page rendered with the Healthcare template.</summary>
    public static readonly Regex SoftwareWording = new(
        @"\.NET|GitHub|case stud(y|ies)|software|engineer|developer|backend|cloud|devops|microservice|\bAPI\b|\bstack\b|embedded|\bIoT\b|SQL Server|Plesk|silicon|\btalks?\b|distributed|\bsystems?\b|toolkit",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Words that belong to the Healthcare template only. None may appear on a page rendered with the software template.</summary>
    public static readonly Regex HealthcareWording = new(
        @"clinical|patient|healthcare|licen[cs]e|registration|initiative|nursing|\breferences?\b|colleagues say",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The page without its scripts (analytics and structured data are not wording).</summary>
    public static string WithoutScripts(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(html), @"<script\b.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>Signs in as the owner and returns a client plus its token for the template form.</summary>
    public static async Task<HttpResponseMessage> SwitchTemplateAsync(HttpClient owner, string key)
    {
        var token = await AdminApp.GetTokenAsync(owner, "/admin/template");
        return await AdminApp.PostFormAsync(owner, "/admin/template", token, new Dictionary<string, string> { ["Key"] = key });
    }

    /// <summary>Row counts of every content table, to prove a template switch deletes nothing.</summary>
    public static async Task<Dictionary<string, int>> RowCountsAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return new Dictionary<string, int>
        {
            ["profiles"] = await db.SiteProfiles.CountAsync(),
            ["experiences"] = await db.Experiences.CountAsync(),
            ["skills"] = await db.Skills.CountAsync(),
            ["projects"] = await db.Projects.CountAsync(),
            ["certifications"] = await db.Certifications.CountAsync(),
            ["posts"] = await db.BlogPosts.CountAsync(),
            ["talks"] = await db.Talks.CountAsync(),
            ["testimonials"] = await db.Testimonials.CountAsync(),
        };
    }
}
