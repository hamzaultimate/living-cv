using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>A site the wizard published blank is built from the admin alone, in both shipped templates, and goes back to blank the same way.</summary>
public class ContentEndToEndTests
{
    [Theory]
    [InlineData("software")]
    [InlineData("healthcare")]
    public async Task ABlankSite_IsBuiltFromTheAdmin_ShowsEverywhere_AndIsEmptiedAgain(string template)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, template);
        var anonymous = app.NewClient();
        Assert.All(new[] { ContentFlow.Experience, ContentFlow.Skills, ContentFlow.Projects, ContentFlow.Certifications },
            async path => Assert.Contains("No entries yet", await owner.GetStringAsync(path)));
        var sitemapBefore = await anonymous.GetStringAsync("/sitemap.xml");
        var homeBefore = await anonymous.GetStringAsync("/");
        Assert.DoesNotContain("Quokka Rota", homeBefore);

        // ---- create content in all four sections
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit",
            ContentFlow.RoleFields("Quokka Rota Ltd", "Rota Coordinator", "2019", "2022", "Built the rota for forty people."))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, ContentFlow.Skills + "/edit",
            ContentFlow.Fields(("Name", "Rota planning"), ("Group", template == "healthcare" ? "Clinical" : "Backend")))).StatusCode);
        var project = ContentFlow.Fields(("Title", "Quokka Rota Redesign"), ("Summary", "A calmer way to plan shifts."), ("Featured", "true"));
        project["Form.TechText"] = "Spreadsheets";
        project["Form.HeroAlt"] = "A weekly rota on a screen";
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostMultipartAsync(owner, ContentFlow.Projects + "/edit", project,
            ("Form.HeroFile", TestImages.Split(SKEncodedImageFormat.Jpeg, 160, 90), "cover.jpg", "image/jpeg"))).StatusCode);
        var projectId = (await ContentFlow.IdsAsync(app, db => db.Projects.Select(p => p.Id))).Single();
        var imagePath = $"{ContentFlow.Projects}/images/{projectId}";
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostMultipartAsync(owner, imagePath + "?handler=Upload",
            new Dictionary<string, string> { ["stamp"] = await ContentFlow.StampAsync(owner, imagePath), ["Caption"] = "The rota grid" },
            ("Image", TestImages.Split(SKEncodedImageFormat.Png, 120, 80), "grid.png", "image/png"), imagePath)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, ContentFlow.Certifications + "/edit",
            ContentFlow.Fields(("Name", "Quokka Rota Certificate"), ("Issuer", "Rota Board"), ("Status", "Earned"), ("Date", "2020")))).StatusCode);

        // ---- the public site shows all of it
        foreach (var (path, expected) in new[]
                 {
                     ("/experience", "Rota Coordinator"), ("/experience", "Built the rota for forty people."), ("/skills", "Rota planning"),
                     ("/projects", "Quokka Rota Redesign"), ("/projects/quokka-rota-redesign", "Spreadsheets"), ("/projects/quokka-rota-redesign", "The rota grid"),
                     ("/projects/quokka-rota-redesign", "A weekly rota on a screen"), ("/certifications", "Quokka Rota Certificate"), ("/", "Quokka Rota Redesign"),
                     ("/cv", "Rota Coordinator"), ("/cv", "Rota planning"), ("/cv", "Quokka Rota Redesign"), ("/cv", "Quokka Rota Certificate"),
                 })
            Assert.Contains(expected, await anonymous.GetStringAsync(path));
        var sitemap = await anonymous.GetStringAsync("/sitemap.xml");
        Assert.NotEqual(sitemapBefore, sitemap);
        Assert.Contains("https://casey.example/projects/quokka-rota-redesign", sitemap);
        var detail = await anonymous.GetStringAsync("/projects/quokka-rota-redesign");
        Assert.Contains("\"@type\":\"CreativeWork\"", detail);
        Assert.Contains("\"@type\":\"BreadcrumbList\"", detail);
        var home = await anonymous.GetStringAsync("/");
        if (template == "healthcare") Assert.Contains("\"hasCredential\"", home); // the earned entry is published as a credential
        else Assert.DoesNotContain("hasCredential", home);
        // No impact line on this project: the CV's "▸" glyph needs fonts the test host lacks (the same limit the other tests note for the demo persona).
        var cv = await anonymous.GetAsync("/cv.pdf");
        Assert.Equal(HttpStatusCode.OK, cv.StatusCode);
        Assert.True((await cv.Content.ReadAsByteArrayAsync()).Length > 1000);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/og.png")).StatusCode);

        // The hub counts what the owner added.
        var hub = await owner.GetStringAsync("/admin/content");
        Assert.Equal(4, Regex.Matches(hub, "1 entry").Count);

        // ---- edit one thing and see it change
        var roleId = (await ContentFlow.IdsAsync(app, db => db.Experiences.Select(p => p.Id))).Single();
        var rolePath = $"{ContentFlow.Experience}/edit/{roleId}";
        var edited = ContentFlow.RoleFields("Quokka Rota Ltd", "Senior Rota Coordinator", "2019", "2022", "Built the rota for forty people.");
        edited["Form.Stamp"] = await ContentFlow.StampAsync(owner, rolePath);
        await ContentFlow.PostAsync(owner, rolePath, edited);
        Assert.Contains("Senior Rota Coordinator", await anonymous.GetStringAsync("/experience"));
        Assert.DoesNotContain(">Rota Coordinator<", await anonymous.GetStringAsync("/experience"));

        // ---- delete it all again
        foreach (var (section, ids) in new[]
                 {
                     (ContentFlow.Experience, await ContentFlow.IdsAsync(app, db => db.Experiences.Select(p => p.Id))),
                     (ContentFlow.Skills, await ContentFlow.IdsAsync(app, db => db.Skills.Select(p => p.Id))),
                     (ContentFlow.Projects, await ContentFlow.IdsAsync(app, db => db.Projects.Select(p => p.Id))),
                     (ContentFlow.Certifications, await ContentFlow.IdsAsync(app, db => db.Certifications.Select(p => p.Id))),
                 })
            foreach (var id in ids)
            {
                var path = $"{section}/delete/{id}";
                Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, path) })).StatusCode);
            }

        Assert.Equal(0, await ContentFlow.CountAsync(app));
        Assert.Empty(Directory.GetFiles(app.UploadsPath)); // the project's uploads went with it, and nothing else was there
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/projects/quokka-rota-redesign")).StatusCode);
        foreach (var gone in new[] { "/experience", "/skills", "/projects", "/certifications", "/", "/cv" })
            Assert.DoesNotContain("Quokka Rota", await anonymous.GetStringAsync(gone));
        Assert.Equal(sitemapBefore, await anonymous.GetStringAsync("/sitemap.xml"));
        Assert.DoesNotContain("hasCredential", await anonymous.GetStringAsync("/"));
    }
}
