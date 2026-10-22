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

    public static readonly string[] Sections = [Experience, Skills, Projects, Certifications];

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

    private static readonly Regex StampPattern = new("name=\"(?:Form\\.)?Stamp\"[^>]*value=\"([^\"]*)\"", RegexOptions.Compiled);

    /// <summary>The concurrency stamp a form carries, as a browser would send it back.</summary>
    public static async Task<string> StampAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = StampPattern.Match(html);
        Assert.True(match.Success, $"No stamp on {path}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

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
}
