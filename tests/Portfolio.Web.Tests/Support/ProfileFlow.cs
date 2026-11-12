using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Tests.Support;

/// <summary>Drives the profile editors (identity, hero, metrics, About) over HTTP the way a browser would: GET for the token and the stamp, POST the form.</summary>
public static class ProfileFlow
{
    public const string Identity = "/admin/profile";
    public const string Hero = "/admin/profile/hero";
    public const string Metrics = "/admin/profile/metrics";
    public const string About = "/admin/profile/about";

    public static readonly string[] Parts = [Hero, Metrics, About];

    public static Task<string> StampAsync(HttpClient client, string path) => ContentFlow.StampAsync(client, path);

    /// <summary>Posts <paramref name="fields"/> to <paramref name="path"/> with the stamp the page carries now (a form opened just before). Pass <paramref name="stamp"/> to post an older one.</summary>
    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, IEnumerable<KeyValuePair<string, string>> fields, string? stamp = null, string? tokenFrom = null)
    {
        var all = fields.ToDictionary(f => f.Key, f => f.Value);
        all["Form.Stamp"] = stamp ?? await StampAsync(client, tokenFrom ?? path.Split('?')[0]);
        return await ContentFlow.PostAsync(client, path, all, tokenFrom);
    }

    public static Dictionary<string, string> HeroFields(string eyebrow = "Eyebrow text", string headline = "A new headline", string accent = "and its accent", string lede = "A new introduction.") =>
        ContentFlow.Fields(("Eyebrow", eyebrow), ("Headline", headline), ("HeadlineAccent", accent), ("Lede", lede));

    /// <summary>The fields of the metrics form for the rows given as (value, unit, label), each taking its own position as its source.</summary>
    public static Dictionary<string, string> MetricsFields(params (string Value, string Unit, string Label)[] rows)
    {
        var fields = new Dictionary<string, string>();
        for (var i = 0; i < rows.Length; i++)
        {
            fields[$"Form.Rows[{i}].Source"] = i.ToString();
            fields[$"Form.Rows[{i}].Value"] = rows[i].Value;
            fields[$"Form.Rows[{i}].Unit"] = rows[i].Unit;
            fields[$"Form.Rows[{i}].Label"] = rows[i].Label;
        }
        return fields;
    }

    public static Dictionary<string, string> AboutFields(string headline = "An about headline", string paragraphs = "First paragraph.\n\nSecond paragraph.", string note = "Currently learning.",
        string focus = "One focus\nAnother focus", params (string Title, string Body)[] cards)
    {
        var fields = ContentFlow.Fields(("Headline", headline), ("ParagraphsText", paragraphs), ("CurrentlyNote", note), ("FocusText", focus));
        for (var i = 0; i < cards.Length; i++)
        {
            fields[$"Form.Cards[{i}].Title"] = cards[i].Title;
            fields[$"Form.Cards[{i}].Body"] = cards[i].Body;
        }
        return fields;
    }

    public static async Task<SiteProfile> ProfileAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return await db.SiteProfiles.AsNoTracking().SingleAsync();
    }

    /// <summary>Writes the hero, metrics and About text straight to the row (stamped, like the editors do), so a test has something to keep, show or lose.</summary>
    public static async Task SeedTextAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        var p = await db.SiteProfiles.SingleAsync();
        p.Hero = new ProfileHero { Eyebrow = "Seed eyebrow", Headline = "Seed headline", HeadlineAccent = "seed accent", Lede = "Seed lede." };
        p.Metrics = [new ProfileMetric { Value = "10", Unit = "ms", Label = "first" }, new ProfileMetric { Value = "20", Unit = "rps", Label = "second" }];
        p.About = new ProfileAbout
        {
            Headline = "Seed about", Paragraphs = ["Seed one.", "Seed two."], CurrentlyNote = "Seed note", Focus = ["Seed focus", "Seed focus two"],
            ValueProps = [new ProfileValueProp { Title = "Seed title", Body = "Seed body." }, new ProfileValueProp { Title = "Second title", Body = "Second body." }],
        };
        p.ConcurrencyStamp = ProfileEditor.NewStamp();
        await db.SaveChangesAsync();
    }

    /// <summary>Every profile column the editors can change, as one string, to prove a request left the profile alone.</summary>
    public static string Fingerprint(SiteProfile p) => string.Join("|",
        p.OwnerName, p.BrandName, p.Role, p.JobTitle, p.Url, p.Summary, p.Location, string.Join(",", p.KnowsAbout), p.PhotoUrl, p.PhotoUrlLight, p.TemplateKey, p.AccentColor,
        p.Hero.Eyebrow, p.Hero.Headline, p.Hero.HeadlineAccent, p.Hero.Lede,
        string.Join(";", p.Metrics.Select(m => $"{m.Value}/{m.Unit}/{m.Label}")),
        p.About.Headline, string.Join(";", p.About.Paragraphs), string.Join(";", p.About.ValueProps.Select(v => v.Title + "/" + v.Body)), p.About.CurrentlyNote, string.Join(";", p.About.Focus),
        p.Phone, p.WhatsApp, p.Email, string.Join(",", p.SecondaryEmails), p.LinkedIn, p.GitHub, p.YouTube,
        string.Join(";", p.Sections.Select(s => $"{s.Key}/{s.Label}/{s.Enabled}/{s.SortOrder}")), p.OnboardingCompletedAt, p.OnboardingOutcome, p.OnboardingDraft, p.ConcurrencyStamp);
}
