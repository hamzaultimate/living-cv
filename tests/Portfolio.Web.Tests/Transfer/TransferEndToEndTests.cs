using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Transfer;

/// <summary>
/// The whole story on the real app: a site the wizard published, exported, emptied and imported again shows the same public pages; a blank site gets its content from a file;
/// and an export from one template can be imported into another, with the settings the other template cannot use staying inert.
/// </summary>
public class TransferEndToEndTests
{
    private static async Task<HttpClient> SampleSiteAsync(AdminApp app, string template)
    {
        var owner = await WizardFlow.FirstRunClientAsync(app);
        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await WizardFlow.PickTemplateAsync(owner, template)));
        await WizardFlow.SaveBasicsAsync(owner);
        var publish = await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields("https://casey.example", "", StarterChoice.Sample));
        Assert.Equal("/admin", WizardFlow.Where(publish));
        return owner;
    }

    /// <summary>Adds one row to every table the sample does not fill, a draft, and text for the profile, so every table and the profile are in play.</summary>
    private static async Task FillEverythingAsync(AdminApp app, string template)
    {
        await ContentFlow.SeedRowsAsync(app);
        await ProfileFlow.SeedTextAsync(app);
        using var db = app.CreateDbContext();
        // Distinct positions, so the order of two rows never depends on how a database breaks a tie.
        var n = 900;
        foreach (var r in await db.Experiences.OrderBy(x => x.Id).ToListAsync()) r.SortOrder = ++n;
        foreach (var r in await db.Projects.OrderBy(x => x.Id).ToListAsync()) r.SortOrder = ++n;
        foreach (var r in await db.Certifications.OrderBy(x => x.Id).ToListAsync()) r.SortOrder = ++n;
        foreach (var r in await db.Testimonials.OrderBy(x => x.Id).ToListAsync()) r.SortOrder = ++n;
        foreach (var g in (await db.Skills.ToListAsync()).GroupBy(s => s.Group))
            foreach (var r in g.OrderBy(x => x.SortOrder).ThenBy(x => x.Id)) r.SortOrder = ++n;
        db.BlogPosts.Add(new BlogPost { Title = "A hidden draft", Slug = "a-hidden-draft", Date = new DateOnly(2025, 6, 1), Draft = true, Tags = ["Draft"], BodyMarkdown = "Only a draft.", CoverAlt = null, ConcurrencyStamp = ContentEditor.NewStamp() });
        db.Talks.Add(new Talk { Title = "A second talk", Type = TalkType.Video, YoutubeId = "dQw4w9WgXcQ", Date = new DateOnly(2023, 1, 2), SortOrder = ++n, ConcurrencyStamp = ContentEditor.NewStamp() });
        var profile = await db.SiteProfiles.SingleAsync();
        var accent = app.Services.GetRequiredService<ITemplateCatalog>().Get(template).Theme.Accents.First().Key;
        profile.AccentColor = accent;
        profile.Sections = [new SectionSetting { Key = "blog", Label = "Journal", Shown = true }, new SectionSetting { Key = "contact", Enabled = true, Shown = true, SortOrder = 0 }];
        await db.SaveChangesAsync();
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
    }

    private static async Task<List<(string Path, HttpStatusCode Status, string Body)>> PublicPagesAsync(AdminApp app, (string Project, string Post) slugs)
    {
        var (project, post) = slugs;
        var anyone = app.NewClient();
        var pages = new List<(string, HttpStatusCode, string)>();
        foreach (var (_, path) in DemoSite.SnapshotPages)
        {
            var real = path.Replace("{project}", project).Replace("{post}", post);
            var response = await anyone.GetAsync(real);
            pages.Add((real, response.StatusCode, DemoSite.Normalise(await response.Content.ReadAsStringAsync())));
        }
        pages.Add(("/blog/a-hidden-draft", (await anyone.GetAsync("/blog/a-hidden-draft")).StatusCode, ""));
        return pages;
    }

    [Theory]
    [InlineData("software")]
    [InlineData("healthcare")]
    public async Task ExportEmptyAndImport_GivesTheSamePublicPages_EveryTableAndTheProfileIncluded(string template)
    {
        using var app = new AdminApp();
        var owner = await SampleSiteAsync(app, template);
        await FillEverythingAsync(app, template);
        var slugs = DemoSite.FirstSlugs(app);
        var before = await PublicPagesAsync(app, slugs);
        var exported = await TransferFlow.ExportTextAsync(owner);
        var counts = await DemoSite.RowCountsAsync(app);
        Assert.All(counts.Where(c => c.Key != "profiles" && c.Key != "talks" || template == "software"), c => Assert.True(c.Value >= 2 || c.Key == "profiles", $"{c.Key} has rows to carry"));

        // Empty every table and change the profile.
        using (var db = app.CreateDbContext())
        {
            db.Experiences.RemoveRange(db.Experiences);
            db.Skills.RemoveRange(db.Skills);
            db.Projects.RemoveRange(db.Projects);
            db.Certifications.RemoveRange(db.Certifications);
            db.Talks.RemoveRange(db.Talks);
            db.Testimonials.RemoveRange(db.Testimonials);
            db.BlogPosts.RemoveRange(db.BlogPosts);
            db.ListStamps.RemoveRange(db.ListStamps);
            var profile = await db.SiteProfiles.SingleAsync();
            profile.OwnerName = "Somebody Else";
            profile.Hero = new ProfileHero { Headline = "Something else" };
            profile.Metrics = [];
            profile.About = new ProfileAbout { Headline = "Other" };
            profile.Sections = [];
            profile.AccentColor = null;
            profile.Summary = "Other summary.";
            profile.ConcurrencyStamp = ProfileEditor.NewStamp();
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        Assert.Equal(0, await ContentFlow.CountAsync(app));
        Assert.NotEqual((await PublicPagesAsync(app, slugs))[0].Body, before[0].Body); // the site really is different now

        var page = await TransferFlow.ImportAsync(owner, exported, applyProfile: true);

        Assert.Contains("0 were not imported", page);
        Assert.Contains("Applied: ", page);
        var after = await PublicPagesAsync(app, slugs);
        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].Path, after[i].Path);
            Assert.True(before[i].Status == after[i].Status, $"{before[i].Path}: {before[i].Status} then {after[i].Status}");
            Assert.True(before[i].Body == after[i].Body, $"{before[i].Path} differs after the round trip");
        }
        Assert.Equal(exported, await TransferFlow.ExportTextAsync(owner)); // and the data itself is the same, field for field
        Assert.Equal(counts, await DemoSite.RowCountsAsync(app));
    }

    [Theory]
    [InlineData("software")]
    [InlineData("healthcare")]
    public async Task ABlankSite_GetsItsContentAndProfileFromAFile_AndThePublicPagesShowIt(string template)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, template);
        var anyone = app.NewClient();
        Assert.DoesNotContain("Hand-written headline", await anyone.GetStringAsync("/"));

        var file = ImportHarness.File(
            profile: new
            {
                ownerName = "Pat Writer", summary = "Writes things by hand.", hero = new { headline = "Hand-written headline", lede = "A lede from a file." },
                metrics = new[] { new { value = "42", unit = (string?)null, label = "Answers" } }, about = new { headline = "About a file", paragraphs = new[] { "Paragraph from a file." } },
            },
            content: new
            {
                experience = new object[] { new { company = "Filed Org", title = "Clerk", start = "2018", end = "2020", summary = "Kept the files." } },
                skills = new object[] { new { name = "Filing", group = "Records" } },
                projects = new object[] { new { title = "Filing project", slug = "filing-project", summary = "A project from a file.", heroImage = "/img/projects/telemetry.svg", heroImageAlt = "A chart" } },
                certifications = new object[] { new { name = "Filing certificate", issuer = "The Registry", status = "Earned", date = "2019" } },
                testimonials = new object[] { new { quote = "A file said this, and it is long enough.", author = "A Colleague" } },
                talks = new object[] { new { title = "A talk about files", date = "2024-01-02" } },
                blog = new object[] { new { title = "A post from a file", slug = "post-from-a-file", date = "2025-01-01", draft = false, body = "Body from a file." } },
            });

        var page = await TransferFlow.ImportAsync(owner, file, applyProfile: true);

        Assert.Contains("7 added", page);
        Assert.Contains("Applied: ", page);
        var home = await anyone.GetStringAsync("/");
        foreach (var text in new[] { "Hand-written headline", "A lede from a file.", "Answers", "Pat Writer" }) Assert.Contains(text, home);
        Assert.Contains("A colleague", home, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Paragraph from a file.", await anyone.GetStringAsync("/about"));
        Assert.Contains("Filed Org", await anyone.GetStringAsync("/experience"));
        Assert.Contains("Filing", await anyone.GetStringAsync("/skills"));
        Assert.Contains("Filing project", await anyone.GetStringAsync("/projects"));
        Assert.Equal(HttpStatusCode.OK, (await anyone.GetAsync("/projects/filing-project")).StatusCode);
        Assert.Contains("Filing certificate", await anyone.GetStringAsync("/certifications"));
        // Talks and the blog are off in Healthcare: the rows are there, the pages are not (a template hides, it never deletes).
        var shown = template == "software" ? HttpStatusCode.OK : HttpStatusCode.NotFound;
        Assert.Equal(shown, (await anyone.GetAsync("/talks")).StatusCode);
        Assert.Equal(shown, (await anyone.GetAsync("/blog/post-from-a-file")).StatusCode);
        using var db = app.CreateDbContext();
        Assert.Equal(1, await db.Talks.CountAsync());
        Assert.Equal(1, await db.BlogPosts.CountAsync());
        Assert.Equal(HttpStatusCode.OK, (await anyone.GetAsync("/cv")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anyone.GetAsync("/sitemap.xml")).StatusCode);
        Assert.Contains("/projects/filing-project", await anyone.GetStringAsync("/sitemap.xml"));
    }

    [Fact]
    public async Task AnExportFromSoftware_ImportedIntoHealthcareAsRowsOnly_LeavesTheProfileAndTemplateAlone()
    {
        string exported;
        using (var source = new AdminApp())
        {
            var owner = await SampleSiteAsync(source, "software");
            await FillEverythingAsync(source, "software");
            exported = await TransferFlow.ExportTextAsync(owner);
        }
        using var app = new AdminApp();
        var target = await ContentFlow.BlankSiteAsync(app, "healthcare");
        var profileBefore = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));

        var page = await TransferFlow.ImportAsync(target, exported, applyProfile: false);

        Assert.Contains("0 were not imported", page);
        Assert.Equal(profileBefore, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
        Assert.Equal("healthcare", (await ProfileFlow.ProfileAsync(app)).TemplateKey);
        using var db = app.CreateDbContext();
        Assert.True(await db.Skills.CountAsync() > 3);
        Assert.True(await db.BlogPosts.AnyAsync(b => b.Slug == "seed-post"));
        var skills = await app.NewClient().GetStringAsync("/skills");
        Assert.Contains("Seeding", skills);
        Assert.Equal(HttpStatusCode.NotFound, (await app.NewClient().GetAsync("/blog")).StatusCode); // healthcare leaves the blog off
    }

    [Fact]
    public async Task AnExportFromSoftware_WithItsProfile_SwitchesTheTemplate_ExactlyAsThePreviewSaid()
    {
        string exported;
        using (var source = new AdminApp())
        {
            var owner = await SampleSiteAsync(source, "software");
            await FillEverythingAsync(source, "software");
            exported = await TransferFlow.ExportTextAsync(owner);
        }
        using var app = new AdminApp();
        var target = await ContentFlow.BlankSiteAsync(app, "healthcare");

        var preview = await (await TransferFlow.PreviewAsync(target, exported, applyProfile: true)).Content.ReadAsStringAsync();
        Assert.Contains("Template: Healthcare to Software &amp; IT", preview);
        Assert.Equal("healthcare", (await ProfileFlow.ProfileAsync(app)).TemplateKey); // nothing yet
        await TransferFlow.ConfirmAsync(target, TransferFlow.TokenOf(preview));

        Assert.Equal("software", (await ProfileFlow.ProfileAsync(app)).TemplateKey);
        Assert.Equal(HttpStatusCode.OK, (await app.NewClient().GetAsync("/blog")).StatusCode);
    }

    [Fact]
    public async Task SettingsForSectionsTheTargetTemplateLacks_AreInert_NeverStored_NeverSwitchedOn()
    {
        string exported;
        using (var source = new AdminApp())
        {
            var owner = await SampleSiteAsync(source, "software");
            await FillEverythingAsync(source, "software");
            using (var db = source.CreateDbContext())
            {
                var p = await db.SiteProfiles.SingleAsync();
                p.Sections = [new SectionSetting { Key = "blog", Label = "Journal", Shown = true }, new SectionSetting { Key = "testimonials", Label = "Praise", Shown = true }, new SectionSetting { Key = "skills", Label = "Abilities" }];
                await db.SaveChangesAsync();
            }
            exported = await TransferFlow.ExportTextAsync(owner);
        }
        // A site whose template has no blog or testimonials section, importing a file that says it was made for that template.
        var lean = TemplateDirs.WithExtra("lean", node =>
        {
            node["name"] = "Lean";
            var keep = node["sections"]!.AsArray().Where(s => s!["key"]!.GetValue<string>() is not ("blog" or "testimonials")).Select(s => s!.DeepClone()).ToList();
            node["sections"] = new JsonArray(keep.ToArray());
        });
        using var app = AdminApp.WithTemplates(lean);
        var target = await ContentFlow.BlankSiteAsync(app, "lean");
        var file = JsonNode.Parse(exported)!.AsObject();
        file["profile"]!["templateKey"] = "lean";

        var preview = await (await TransferFlow.PreviewAsync(target, file.ToJsonString(), applyProfile: true)).Content.ReadAsStringAsync();
        Assert.Contains("do not have", preview.Replace("that the template does not have", "do not have"));
        Assert.Contains("blog, testimonials", preview);
        await TransferFlow.ConfirmAsync(target, TransferFlow.TokenOf(preview));

        var profile = await ProfileFlow.ProfileAsync(app);
        Assert.Equal("lean", profile.TemplateKey);
        Assert.DoesNotContain(profile.Sections, s => s.Key is "blog" or "testimonials"); // inert settings are not stored at all
        Assert.Contains(profile.Sections, s => s.Key == "skills" && s.Label == "Abilities"); // the ones the template lists are applied
        var anyone = app.NewClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync("/blog")).StatusCode);
        Assert.DoesNotContain("/blog", await anyone.GetStringAsync("/sitemap.xml"));
        using var db2 = app.CreateDbContext();
        Assert.True(await db2.BlogPosts.AnyAsync(), "the posts were imported: a template hides, it never deletes");
        Assert.True(await db2.Testimonials.AnyAsync());
    }

    [Fact]
    public async Task AProfileThatWouldLeaveNoMenuSection_IsRefusedWhole_AndTheRowsStillImport()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var hideAll = new[] { "about", "experience", "projects", "skills", "certifications", "blog", "talks", "contact" }.Select(k => new { key = k, shown = false }).ToArray();

        var page = await TransferFlow.ImportAsync(owner, ImportHarness.File(profile: new { ownerName = "Must Not Apply", sections = hideAll }, content: new { skills = new object[] { new { name = "Still imported", group = "G" } } }), applyProfile: true);

        Assert.Contains("at least one", page);
        Assert.Contains("1 added", page);
        Assert.Equal("Casey Morgan", (await ProfileFlow.ProfileAsync(app)).OwnerName);
        Assert.Contains("Still imported", await app.NewClient().GetStringAsync("/skills"));
    }

    [Fact]
    public async Task TheDashboard_LinksToThePage_AndThePageLinksToTheExport()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await owner.GetAsync("/admin");

        var dashboard = await owner.GetStringAsync("/admin");
        var page = await owner.GetStringAsync(TransferFlow.Page);
        var download = await owner.GetAsync("/admin/transfer/export");

        Assert.Contains("href=\"/admin/transfer\"", dashboard);
        Assert.Contains("href=\"/admin/transfer/export\"", page);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }
}
