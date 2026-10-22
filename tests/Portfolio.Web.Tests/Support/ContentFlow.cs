using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Tests.Support;

/// <summary>Drives the content editors over HTTP the way a browser would: GET for the token and the stamp, POST the form.</summary>
public static class ContentFlow
{
    public const string Experience = "/admin/content/experience";
    public const string Skills = "/admin/content/skills";
    public const string Projects = "/admin/content/projects";
    public const string Certifications = "/admin/content/certifications";

    public const string Talks = "/admin/content/talks";
    public const string Testimonials = "/admin/content/testimonials";

    public static readonly string[] Sections = [Experience, Skills, Projects, Certifications, Talks, Testimonials];

    /// <summary>A site the wizard has published blank in the given template, with the owner signed in.</summary>
    public static async Task<HttpClient> BlankSiteAsync(AdminApp app, string template = "software")
    {
        var owner = await WizardFlow.FirstRunClientAsync(app);
        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await WizardFlow.PickTemplateAsync(owner, template)));
        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields("Casey Morgan", "Specialist", "casey@owner.example", "Leeds, UK", "Specialist who keeps the lights on."))));
        var publish = await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields("https://casey.example"));
        Assert.Equal(HttpStatusCode.Redirect, publish.StatusCode);
        return owner;
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, IEnumerable<KeyValuePair<string, string>> fields, string? tokenFrom = null) =>
        WizardFlow.PostAsync(client, path, fields, tokenFrom);

    /// <summary>A multipart post (a form with a file), with the antiforgery token a browser would send.</summary>
    public static async Task<HttpResponseMessage> PostMultipartAsync(HttpClient client, string path, IEnumerable<KeyValuePair<string, string>> fields,
        (string Name, byte[] Bytes, string FileName, string ContentType)? file = null, string? tokenFrom = null)
    {
        var token = await AdminApp.GetTokenAsync(client, tokenFrom ?? path.Split('?')[0]);
        var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        foreach (var (name, value) in fields) content.Add(new StringContent(value), name);
        if (file is { } f)
        {
            var part = new ByteArrayContent(f.Bytes);
            part.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(f.ContentType);
            content.Add(part, f.Name, f.FileName);
        }
        return await client.PostAsync(path, content);
    }

    private static readonly Regex StampPattern = new("name=\"(?:Form\\.)?Stamp\"(?:[^>]*value=\"([^\"]*)\")?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The concurrency stamp a form carries, as a browser would send it back.</summary>
    public static async Task<string> StampAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = StampPattern.Match(html);
        Assert.True(match.Success, $"No stamp on {path}");
        return WebUtility.HtmlDecode(match.Groups[1].Value); // a row written before stamps existed has no value attribute: the browser posts it empty
    }

    private static readonly Regex ListStampPattern = new("name=\"listStamp\"[^>]*value=\"([^\"]*)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The list stamp the Move buttons of a list page carry, as a browser would post it back.</summary>
    public static async Task<string> ListStampAsync(HttpClient client, string section)
    {
        var html = await client.GetStringAsync(section);
        var match = ListStampPattern.Match(html);
        Assert.True(match.Success, $"No list stamp on {section}: the page has no Move button");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>Presses a Move button: loads the list page for its stamp, then posts the move with it.</summary>
    public static async Task<HttpResponseMessage> MoveAsync(HttpClient client, string section, int id, string direction) =>
        await MoveWithStampAsync(client, section, id, direction, await ListStampAsync(client, section));

    /// <summary>Posts a move with a stamp the caller already holds (a page that was opened earlier).</summary>
    public static Task<HttpResponseMessage> MoveWithStampAsync(HttpClient client, string section, int id, string direction, string listStamp) =>
        PostAsync(client, $"{section}?handler=Move&id={id}", new Dictionary<string, string> { ["direction"] = direction, ["listStamp"] = listStamp }, section);

    public static Dictionary<string, string> Fields(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Name.Contains('.') || p.Name == "Stamp" ? p.Name : "Form." + p.Name, p => p.Value);

    public static Dictionary<string, string> RoleFields(string company = "Northwind Health", string title = "Staff Nurse", string start = "2019", string end = "2022",
        string summary = "Cared for patients on a busy ward.", bool current = false) =>
        Fields(("Company", company), ("Title", title), ("Start", start), ("End", end), ("Summary", summary), ("Current", current ? "true" : "false"));

    public static async Task<string> BodyAsync(HttpClient client, string path) => await client.GetStringAsync(path);

    public static async Task<string> TextOfAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync();

    /// <summary>The ids of a table, in id order, so a test can find the row it just created.</summary>
    public static async Task<List<int>> IdsAsync(AdminApp app, Func<Portfolio.Data.PortfolioDbContext, IQueryable<int>> ids)
    {
        using var db = app.CreateDbContext();
        return await ids(db).OrderBy(x => x).ToListAsync();
    }

    public sealed record Seeded(int ExperienceId, int SkillId, int ProjectId, int CertificationId, string ScreenshotUrl = "", int TalkId = 0, int TestimonialId = 0);

    /// <summary>One row in each table, written straight to the database (stamped, like rows the editors write).</summary>
    public static async Task<Seeded> SeedRowsAsync(AdminApp app, bool pairs = false)
    {
        using var db = app.CreateDbContext();
        var e = new Experience { Company = "Seed Co", Title = "Seed Role", Start = "2020", End = "2021", Summary = "Did seed things.", SortOrder = 1, ConcurrencyStamp = ContentEditor.NewStamp() };
        var k = new Skill { Name = "Seeding", Group = "Garden", SortOrder = 1, ConcurrencyStamp = ContentEditor.NewStamp() };
        var p = new Project { Title = "Seed Project", Slug = "seed-project", Summary = "A seeded project.", SortOrder = 1, ConcurrencyStamp = ContentEditor.NewStamp() };
        var c = new Certification { Name = "Seed Certificate", Issuer = "Seed Board", Status = CertificationStatus.Earned, Date = "2020", SortOrder = 1, ConcurrencyStamp = ContentEditor.NewStamp() };
        var talk = new Talk { Title = "Seed Talk", Type = TalkType.Talk, Event = "Seed Conf", Date = new DateOnly(2024, 5, 1), SortOrder = 1, ConcurrencyStamp = ContentEditor.NewStamp() };
        var word = new Testimonial { Quote = "Seeded praise that is long enough to be a quote.", Author = "Seed Person", AuthorTitle = "Seed Role", SortOrder = 1, ConcurrencyStamp = ContentEditor.NewStamp() };
        var shot = "/uploads/" + new string('b', 32) + ".jpg";
        if (pairs)
        {
            // Two rows per table (so a Move has somewhere to go) and a gallery image (so a Remove has something to target).
            p.Screenshots = [new ProjectImage { Url = shot, Caption = "First" }, new ProjectImage { Url = "/uploads/" + new string('c', 32) + ".jpg", Caption = "Second" }];
            db.AddRange(
                new Experience { Company = "Second Co", Title = "Second Role", Start = "2021", End = "2022", Summary = "More.", SortOrder = 2, ConcurrencyStamp = ContentEditor.NewStamp() },
                new Skill { Name = "Second skill", Group = "Garden", SortOrder = 2, ConcurrencyStamp = ContentEditor.NewStamp() },
                new Project { Title = "Second Project", Slug = "second-project", Summary = "Another.", SortOrder = 2, ConcurrencyStamp = ContentEditor.NewStamp() },
                new Certification { Name = "Second Certificate", Issuer = "Seed Board", Status = CertificationStatus.Earned, Date = "2021", SortOrder = 2, ConcurrencyStamp = ContentEditor.NewStamp() },
                new Talk { Title = "Second Talk", Type = TalkType.Video, Event = "Other Conf", Date = new DateOnly(2023, 3, 1), SortOrder = 2, ConcurrencyStamp = ContentEditor.NewStamp() },
                new Testimonial { Quote = "More seeded praise, also long enough.", Author = "Second Person", SortOrder = 2, ConcurrencyStamp = ContentEditor.NewStamp() });
        }
        db.AddRange(e, k, p, c, talk, word);
        await db.SaveChangesAsync();
        return new Seeded(e.Id, k.Id, p.Id, c.Id, shot, talk.Id, word.Id);
    }

    /// <summary>Every column the editors can change, as one string, to prove a request left the content alone.</summary>
    public static async Task<string> SnapshotAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        var parts = new List<string>();
        parts.AddRange((await db.Experiences.AsNoTracking().OrderBy(x => x.Id).ToListAsync()).Select(x => $"E{x.Id}|{x.Company}|{x.Title}|{x.Start}|{x.End}|{x.Current}|{x.Summary}|{string.Join(",", x.Highlights)}|{string.Join(",", x.Tech)}|{x.SortOrder}|{x.ConcurrencyStamp}"));
        parts.AddRange((await db.Skills.AsNoTracking().OrderBy(x => x.Id).ToListAsync()).Select(x => $"S{x.Id}|{x.Name}|{x.Group}|{x.SortOrder}|{x.ConcurrencyStamp}"));
        parts.AddRange((await db.Projects.AsNoTracking().OrderBy(x => x.Id).ToListAsync()).Select(x =>
            $"P{x.Id}|{x.Title}|{x.Slug}|{x.Role}|{x.Org}|{x.Period}|{x.Summary}|{x.Category}|{x.Problem}|{x.Approach}|{x.HeroImage}|{x.HeroImageAlt}|{string.Join(",", x.Screenshots.Select(i => i.Url + "=" + i.Caption))}|{string.Join(",", x.Impact)}|{string.Join(",", x.Tech)}|{string.Join(",", x.Links.Select(l => l.Label + "=" + l.Url))}|{x.Featured}|{x.SortOrder}|{x.ConcurrencyStamp}"));
        parts.AddRange((await db.Certifications.AsNoTracking().OrderBy(x => x.Id).ToListAsync()).Select(x => $"C{x.Id}|{x.Name}|{x.Issuer}|{x.Status}|{x.Date}|{x.Category}|{x.CredentialUrl}|{x.CertificateUrl}|{x.BadgeImageUrl}|{x.SortOrder}|{x.ConcurrencyStamp}"));
        parts.AddRange((await db.Talks.AsNoTracking().OrderBy(x => x.Id).ToListAsync()).Select(x => $"T{x.Id}|{x.Title}|{x.Type}|{x.Event}|{x.Date}|{x.YoutubeId}|{x.Url}|{x.SortOrder}|{x.ConcurrencyStamp}"));
        parts.AddRange((await db.Testimonials.AsNoTracking().OrderBy(x => x.Id).ToListAsync()).Select(x => $"M{x.Id}|{x.Quote}|{x.Author}|{x.AuthorTitle}|{x.SourceUrl}|{x.Relation}|{x.Featured}|{x.SortOrder}|{x.ConcurrencyStamp}"));
        parts.AddRange((await db.ListStamps.AsNoTracking().OrderBy(x => x.Section).ToListAsync()).Select(x => $"L{x.Section}|{x.Stamp}"));
        return string.Join("\n", parts);
    }

    public static async Task<int> CountAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return await db.Experiences.CountAsync() + await db.Skills.CountAsync() + await db.Projects.CountAsync() + await db.Certifications.CountAsync() + await db.Talks.CountAsync() + await db.Testimonials.CountAsync();
    }
}
