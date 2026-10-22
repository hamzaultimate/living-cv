using System.Text.RegularExpressions;
using Portfolio.Data.Seeding;

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
}
