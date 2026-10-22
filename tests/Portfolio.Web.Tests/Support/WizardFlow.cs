using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;

namespace Portfolio.Web.Tests.Support;

/// <summary>Drives the onboarding wizard over HTTP the way a browser would: GET for the token, POST the form.</summary>
public static class WizardFlow
{
    public const string Template = "/admin/welcome";
    public const string Basics = "/admin/welcome/basics";
    public const string Photo = "/admin/welcome/photo";
    public const string Review = "/admin/welcome/review";

    public static readonly string[] Pages = [Template, Basics, Photo, Review];

    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, IEnumerable<KeyValuePair<string, string>> fields, string? tokenFrom = null)
    {
        var token = await AdminApp.GetTokenAsync(client, tokenFrom ?? path.Split('?')[0]);
        return await AdminApp.PostFormAsync(client, path, token, fields);
    }

    public static Task<HttpResponseMessage> PickTemplateAsync(HttpClient client, string key) =>
        PostAsync(client, Template, new Dictionary<string, string> { ["Key"] = key });

    public static Dictionary<string, string> BasicsFields(string name = "Jordan Alexander", string title = "Registered Nurse", string email = "jordan@owner.example",
        string location = "Leeds, UK", string summary = "Nurse with eight years in acute care and a passion for teaching.") => new()
    {
        ["Form.OwnerName"] = name, ["Form.JobTitle"] = title, ["Form.Email"] = email, ["Form.Location"] = location, ["Form.Summary"] = summary,
    };

    public static Task<HttpResponseMessage> SaveBasicsAsync(HttpClient client, Dictionary<string, string>? fields = null) =>
        PostAsync(client, Basics, fields ?? BasicsFields());

    public static Dictionary<string, string> ReviewFields(string url = "https://jordan.example", string accent = "", string content = StarterChoice.Blank) => new()
    {
        ["Form.SiteUrl"] = url, ["Form.Accent"] = accent, ["Form.Content"] = content,
    };

    public static Task<HttpResponseMessage> SaveChoicesAsync(HttpClient client, Dictionary<string, string>? fields = null) =>
        PostAsync(client, Review + "?handler=Save", fields ?? ReviewFields(), Review);

    public static Task<HttpResponseMessage> PublishAsync(HttpClient client, Dictionary<string, string>? fields = null) =>
        PostAsync(client, Review + "?handler=Publish", fields ?? ReviewFields(), Review);

    public static async Task<HttpResponseMessage> UploadPhotoAsync(HttpClient client, byte[] bytes, string fileName = "me.jpg", string contentType = "image/jpeg")
    {
        var token = await AdminApp.GetTokenAsync(client, Photo);
        var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(file, "Photo", fileName);
        return await client.PostAsync(Photo + "?handler=Upload", content);
    }

    /// <summary>Runs /setup, signs in the way the redirect says and returns the client sitting on the wizard.</summary>
    public static async Task<HttpClient> FirstRunClientAsync(AdminApp app)
    {
        var client = app.NewClient();
        var setup = await app.RunSetupAsync(client);
        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);
        var loginPath = setup.Headers.Location!.OriginalString;
        var token = await AdminApp.GetTokenAsync(client, loginPath);
        var login = await AdminApp.PostFormAsync(client, loginPath, token, new Dictionary<string, string> { ["UserName"] = AdminApp.UserName, ["Password"] = AdminApp.Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/admin/welcome", login.Headers.Location!.OriginalString);
        return client;
    }

    /// <summary>The next page of a redirect, as a path.</summary>
    public static string Where(HttpResponseMessage response) => response.Headers.Location?.OriginalString ?? "";

    public static async Task<SiteProfile> ProfileAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return await db.SiteProfiles.AsNoTracking().SingleAsync();
    }

    public static async Task<OnboardingDraft?> DraftAsync(AdminApp app) => OnboardingDraft.Parse((await ProfileAsync(app)).OnboardingDraft);

    /// <summary>The columns the public site reads, as one string, to prove a step left them alone.</summary>
    public static string PublicFingerprint(SiteProfile p) =>
        string.Join("|", p.TemplateKey, p.OwnerName, p.BrandName, p.Role, p.JobTitle, p.Url, p.Summary, p.Location, p.Email, p.PhotoUrl, p.AccentColor,
            p.Hero.Eyebrow, p.Hero.Headline, p.Hero.Lede, p.Metrics.Count, p.About.Paragraphs.Count, string.Join(",", p.KnowsAbout), p.LinkedIn, p.GitHub, p.Phone);

    public static async Task<string> BodyAsync(HttpResponseMessage r) => await r.Content.ReadAsStringAsync();

    public static readonly Regex InputPattern = new(@"<(input|select|textarea)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
