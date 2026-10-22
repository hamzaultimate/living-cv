using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The editors take their wording and field visibility from the template manifest, never from the page.</summary>
public class ContentTemplateTests
{
    // Words that exist nowhere in the app: if they show up, they came from this manifest.
    private static string TemplatesWithEditorWording() => TemplateDirs.WithExtra("quirky", node =>
    {
        node["name"] = "Quirky";
        node["editor"] = JsonNode.Parse("""
        {
          "experience": { "itemLabel": "Zorblax post", "fields": {
            "company": { "label": "Zorblax employer", "hint": "Zorblax employer hint." },
            "tech": { "hidden": true }, "highlights": { "hidden": true }, "current": { "hidden": true } } },
          "skills": { "itemLabel": "Quibble", "fields": { "name": { "label": "Quibble name" } } },
          "projects": { "fields": { "problem": { "hidden": true }, "approach": { "hidden": true }, "links": { "hidden": true },
                                    "tech": { "label": "Wobble tools" }, "heroImage": { "hidden": true }, "category": { "hidden": true } } },
          "certifications": { "itemLabel": "Glimmer", "fields": { "issuer": { "label": "Glimmer body" }, "badgeImageUrl": { "hidden": true }, "date": { "hidden": true } } }
        }
        """);
        var sections = node["sections"]!.AsArray();
        foreach (var s in sections.Select(s => s!.AsObject()))
        {
            if (s["key"]!.GetValue<string>() == "projects") s["itemLabel"] = "Quokka";
            if (s["key"]!.GetValue<string>() == "certifications") s["label"] = "glimmers & gleams";
            if (s["key"]!.GetValue<string>() == "skills") s["label"] = "quibbles";
        }
    });

    [Fact]
    public async Task EveryLabel_ComesFromTheManifest_OnTheHubTheListsAndTheForms()
    {
        using var app = AdminApp.WithTemplates(TemplatesWithEditorWording());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");

        var hub = await owner.GetStringAsync("/admin/content");
        Assert.Contains("Glimmers &amp; gleams", hub);
        Assert.Contains("Quibbles", hub);
        Assert.Contains("Add quokka", hub);
        Assert.Contains("Add zorblax post", hub);

        var experienceForm = await owner.GetStringAsync(ContentFlow.Experience + "/edit");
        Assert.Contains("Zorblax employer", experienceForm);
        Assert.Contains("Zorblax employer hint.", experienceForm);
        Assert.Contains("Add zorblax post", experienceForm);

        Assert.Contains("Quibble name", await owner.GetStringAsync(ContentFlow.Skills + "/edit"));
        var certForm = await owner.GetStringAsync(ContentFlow.Certifications + "/edit");
        Assert.Contains("Glimmer body", certForm);
        Assert.Contains("Wobble tools", await owner.GetStringAsync(ContentFlow.Projects + "/edit"));

        Assert.Contains("Glimmers &amp; gleams", await owner.GetStringAsync(ContentFlow.Certifications));
        Assert.Contains("Add quokka", await owner.GetStringAsync(ContentFlow.Projects));
        Assert.Contains("No entries yet", await owner.GetStringAsync(ContentFlow.Skills));
    }

    [Fact]
    public async Task AFieldTheTemplateHides_IsNotDrawn_AndNotValidated_AndNotReadFromThePost()
    {
        using var app = AdminApp.WithTemplates(TemplatesWithEditorWording());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");

        var form = await owner.GetStringAsync(ContentFlow.Experience + "/edit");
        Assert.DoesNotContain("Form.TechText", form);
        Assert.DoesNotContain("Form.HighlightsText", form);
        Assert.DoesNotContain("Form.Current", form);
        Assert.Contains("Form.End", form);
        var project = await owner.GetStringAsync(ContentFlow.Projects + "/edit");
        Assert.DoesNotContain("Form.Problem", project);
        Assert.DoesNotContain("Form.Links", project);
        Assert.DoesNotContain("Form.HeroFile", project);
        Assert.DoesNotContain("Form.Category", project);
        Assert.DoesNotContain("Form.BadgeImageUrl", await owner.GetStringAsync(ContentFlow.Certifications + "/edit"));

        // A crafted post that fills a hidden field with something invalid (or something valid) is simply not read.
        var fields = ContentFlow.RoleFields();
        fields["Form.TechText"] = "<script>alert(1)</script>";
        fields["Form.HighlightsText"] = "smuggled";
        var response = await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var db = app.CreateDbContext();
        var row = await db.Experiences.SingleAsync();
        Assert.Empty(row.Tech);
        Assert.Empty(row.Highlights);
    }

    [Fact]
    public async Task AHiddenFieldsStoredValue_SurvivesEveryEditPost_InEverySection()
    {
        using var app = AdminApp.WithTemplates(TemplatesWithEditorWording());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        using (var db = app.CreateDbContext())
        {
            db.Experiences.Add(new Experience { Company = "Keep Co", Title = "Keeper", Start = "2018", End = null, Current = true, Summary = "s", Highlights = ["kept highlight"], Tech = ["kept tool"], SortOrder = 1 });
            db.Projects.Add(new Project
            {
                Title = "Keep", Slug = "keep", Summary = "s", Category = "Kept category", Problem = "Kept problem", Approach = "Kept approach", HeroImage = "/img/projects/edge.svg",
                Links = [new Link { Label = "Kept link", Url = "https://example.com/kept" }], SortOrder = 1,
            });
            db.Certifications.Add(new Certification { Name = "Keep Cert", Issuer = "Board", Status = CertificationStatus.Earned, Date = "2019", BadgeImageUrl = "https://example.com/badge.png", SortOrder = 1 });
            await db.SaveChangesAsync();
        }
        using var read = app.CreateDbContext();
        var (e, p, c) = (await read.Experiences.AsNoTracking().SingleAsync(), await read.Projects.AsNoTracking().SingleAsync(), await read.Certifications.AsNoTracking().SingleAsync());

        // The forms as the browser posts them: only the fields that were drawn.
        var exp = ContentFlow.Fields(("Company", "Keep Co"), ("Title", "Keeper edited"), ("Start", "2018"), ("End", ""), ("Summary", "s edited"));
        exp["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Experience}/edit/{e.Id}");
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}/edit/{e.Id}", exp)).StatusCode);
        var proj = ContentFlow.Fields(("Title", "Keep edited"), ("Summary", "s edited"), ("Slug", "keep"));
        proj["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Projects}/edit/{p.Id}");
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, $"{ContentFlow.Projects}/edit/{p.Id}", proj)).StatusCode);
        var cert = ContentFlow.Fields(("Name", "Keep Cert edited"), ("Issuer", "Board"), ("Status", "Earned"));
        cert["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Certifications}/edit/{c.Id}");
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, $"{ContentFlow.Certifications}/edit/{c.Id}", cert)).StatusCode);

        using var db2 = app.CreateDbContext();
        var e2 = await db2.Experiences.AsNoTracking().SingleAsync();
        Assert.Equal(("Keeper edited", "s edited"), (e2.Title, e2.Summary));
        Assert.Equal(["kept highlight"], e2.Highlights);
        Assert.Equal(["kept tool"], e2.Tech);
        Assert.True(e2.Current);
        var p2 = await db2.Projects.AsNoTracking().SingleAsync();
        Assert.Equal("Keep edited", p2.Title);
        Assert.Equal(("Kept category", "Kept problem", "Kept approach", "/img/projects/edge.svg"), (p2.Category, p2.Problem, p2.Approach, p2.HeroImage));
        Assert.Equal("https://example.com/kept", Assert.Single(p2.Links).Url);
        var c2 = await db2.Certifications.AsNoTracking().SingleAsync();
        Assert.Equal("Keep Cert edited", c2.Name);
        Assert.Equal(("2019", "https://example.com/badge.png"), (c2.Date, c2.BadgeImageUrl));
    }

    [Fact]
    public async Task ASectionTheTemplateDoesNotUse_StaysEditable_AndSaysItIsNotShown()
    {
        var dir = TemplateDirs.WithExtra("lean", node =>
        {
            node["name"] = "Lean";
            var sections = node["sections"]!.AsArray();
            foreach (var s in sections.Select(s => s!.AsObject()).Where(s => s["key"]!.GetValue<string>() == "skills")) s["enabled"] = false;
        });
        using var app = AdminApp.WithTemplates(dir);
        var owner = await ContentFlow.BlankSiteAsync(app, "lean");

        Assert.Contains("does not show this section on the public site", await owner.GetStringAsync(ContentFlow.Skills));
        Assert.Contains("Not shown by your current template", await owner.GetStringAsync("/admin/content"));
        await ContentFlow.PostAsync(owner, ContentFlow.Skills + "/edit", ContentFlow.Fields(("Name", "Hidden skill"), ("Group", "Clinical")));
        using var db = app.CreateDbContext();
        Assert.Equal("Hidden skill", (await db.Skills.SingleAsync()).Name); // the row is kept
        Assert.Equal(HttpStatusCode.NotFound, (await app.NewClient().GetAsync("/skills")).StatusCode);
    }

    [Fact]
    public async Task SensitiveTemplates_ShowTheirGuidanceOnTheEditors_AndOthersDoNot()
    {
        using var healthcare = new AdminApp();
        var nurse = await ContentFlow.BlankSiteAsync(healthcare, "healthcare");
        var seeded = await ContentFlow.SeedRowsAsync(healthcare);
        var paths = new[]
        {
            "/admin/content", ContentFlow.Experience, ContentFlow.Experience + "/edit", ContentFlow.Skills, ContentFlow.Skills + "/edit",
            ContentFlow.Projects, ContentFlow.Projects + "/edit", ContentFlow.Certifications, ContentFlow.Certifications + "/edit",
        };
        foreach (var path in paths)
        {
            var html = await nurse.GetStringAsync(path);
            Assert.Contains("Never publish patient-identifiable information", html);
            Assert.Contains("Before you publish", html);
        }

        using var software = new AdminApp();
        var dev = await ContentFlow.BlankSiteAsync(software, "software");
        foreach (var path in paths) Assert.DoesNotContain("Before you publish", await dev.GetStringAsync(path));
    }

    [Fact]
    public async Task Healthcare_EditorsSpeakHealthcare_AndSoftwareEditorsSpeakSoftware()
    {
        using var healthcare = new AdminApp();
        var nurse = await ContentFlow.BlankSiteAsync(healthcare, "healthcare");
        var form = await nurse.GetStringAsync(ContentFlow.Experience + "/edit");
        Assert.Contains("Employer or placement", form);
        Assert.Contains("Never include patient information", form);
        Assert.Contains("Systems and equipment", form);
        Assert.DoesNotContain("Tech stack", form);
        Assert.Contains("Add initiative", await nurse.GetStringAsync(ContentFlow.Projects));
        Assert.Contains("Add licence or registration", await nurse.GetStringAsync(ContentFlow.Certifications));
        Assert.Contains("Clinical skills", await nurse.GetStringAsync(ContentFlow.Skills));
        Assert.DoesNotContain("Badge image link", await nurse.GetStringAsync(ContentFlow.Certifications + "/edit"));
        var status = await nurse.GetStringAsync(ContentFlow.Certifications + "/edit");
        Assert.Contains("Current", status); // the template's word for an earned status
        Assert.DoesNotMatch(@"case stud|\.NET|GitHub|developer|engineer", await nurse.GetStringAsync("/admin/content"));

        using var software = new AdminApp();
        var dev = await ContentFlow.BlankSiteAsync(software, "software");
        var devForm = await dev.GetStringAsync(ContentFlow.Experience + "/edit");
        Assert.Contains("Tech stack", devForm);
        Assert.Contains("Company", devForm);
        Assert.DoesNotContain("Employer or placement", devForm);
        Assert.Contains("Add case study", await dev.GetStringAsync(ContentFlow.Projects));
        Assert.Contains("Add certification", await dev.GetStringAsync(ContentFlow.Certifications));
        Assert.Contains("Badge image link", await dev.GetStringAsync(ContentFlow.Certifications + "/edit"));
    }

    [Fact]
    public async Task AdminProducedContent_ReadsAsHealthcare_OnEveryPublicPage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, "healthcare");
        await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields("Riverside Hospital", "Staff Nurse", "2019", "2022", "Cared for adults on a medical ward."));
        await ContentFlow.PostAsync(owner, ContentFlow.Skills + "/edit", ContentFlow.Fields(("Name", "Wound care"), ("Group", "Clinical")));
        var project = ContentFlow.Fields(("Title", "Falls reduction audit"), ("Summary", "An audit of falls on the ward."), ("Featured", "true"));
        project["Form.ImpactText"] = "Falls down by a quarter";
        await ContentFlow.PostAsync(owner, ContentFlow.Projects + "/edit", project);
        await ContentFlow.PostAsync(owner, ContentFlow.Certifications + "/edit", ContentFlow.Fields(("Name", "Nurse registration"), ("Issuer", "The Council"), ("Status", "Earned"), ("Date", "2018")));

        var anonymous = app.NewClient();
        foreach (var path in new[] { "/", "/about", "/experience", "/projects", "/projects/falls-reduction-audit", "/skills", "/certifications", "/cv", "/contact" })
        {
            var html = await anonymous.GetStringAsync(path);
            Assert.Contains("data-template=\"healthcare\"", html);
            var visible = DemoSite.WithoutScripts(html);
            Assert.False(DemoSite.SoftwareWording.IsMatch(visible), $"{path} shows software wording: {DemoSite.SoftwareWording.Match(visible).Value}");
        }
        Assert.Contains("Falls reduction audit", await anonymous.GetStringAsync("/projects"));
        Assert.Contains("Nurse registration", await anonymous.GetStringAsync("/certifications"));
    }
}
