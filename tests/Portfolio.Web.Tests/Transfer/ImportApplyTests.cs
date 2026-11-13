using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Transfer;

/// <summary>Confirming a preview: every write goes through the editors, nothing is deleted, order follows the file, and stale or foreign requests change nothing.</summary>
public class ImportApplyTests
{
    private static async Task<(AdminApp App, HttpClient Owner)> SiteAsync(string template = "software")
    {
        var app = new AdminApp();
        return (app, await ContentFlow.BlankSiteAsync(app, template));
    }

    private static string RichFile() => ImportHarness.File(content: new
    {
        experience = new object[]
        {
            new { company = "Alpha Org", title = "Lead", start = "2022", end = (string?)null, current = true, summary = "Led the work.", highlights = new[] { "One", "Two" }, tech = new[] { "X" } },
            new { company = "Beta Org", title = "Member", start = "2019", end = "2021", summary = "Joined in." },
        },
        skills = new object[] { new { name = "Alpha skill", group = "First" }, new { name = "Beta skill", group = "Second" }, new { name = "Gamma skill", group = "First" } },
        projects = new object[]
        {
            new
            {
                title = "Alpha project", slug = "alpha-project", summary = "The first.", role = "Lead", impact = new[] { "Faster" }, tech = new[] { "Y" }, featured = true,
                links = new[] { new { label = "Code", url = "https://example.com/code" } }, heroImage = "/img/projects/telemetry.svg", heroImageAlt = "A chart",
                screenshots = new[] { new { url = "/img/projects/telemetry-1.svg", caption = "Dashboard" } }, proofs = new[] { new { label = "Report", url = "/proofs/sample-report.svg", kind = "Pdf" } },
            },
            new { title = "Beta project", summary = "The second." },
        },
        certifications = new object[] { new { name = "Alpha cert", issuer = "Board", status = "Earned", date = "2020" }, new { name = "Beta cert", issuer = "Board", status = "Planned" } },
        talks = new object[] { new { title = "Alpha talk", type = "Talk", date = "2024-05-01", @event = "Conf" }, new { title = "Beta talk", type = "Video", youtubeId = "dQw4w9WgXcQ" } },
        testimonials = new object[] { new { quote = "Alpha praise, long enough.", author = "Alpha Person", featured = true }, new { quote = "Beta praise, long enough.", author = "Beta Person" } },
        blog = new object[]
        {
            new { title = "Alpha post", slug = "alpha-post", date = "2025-03-01", tags = new[] { "One" }, draft = false, description = "Out now.", body = "# Alpha\n\nPublished text." },
            new { title = "Beta draft", slug = "beta-draft", date = "2025-04-01", draft = true, body = "Draft text." },
        },
    });

    [Fact]
    public async Task ImportingIntoABlankSite_AddsEveryRow_StampedLikeTheEditorsDoIt_InTheFilesOrder()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var anyone = app.NewClient();
        var sitemapBefore = await anyone.GetStringAsync("/sitemap.xml"); // cached now

        var page = await TransferFlow.ImportAsync(owner, RichFile());

        Assert.Contains("Import finished", page);
        Assert.Contains("15 added, 0 updated, 0 already matched and 0 were not imported", page);
        using var db = app.CreateDbContext();
        Assert.Equal(["Lead", "Member"], await db.Experiences.OrderBy(x => x.SortOrder).Select(x => x.Title).ToListAsync());
        Assert.Equal([1, 2], await db.Experiences.OrderBy(x => x.SortOrder).Select(x => x.SortOrder).ToListAsync());
        Assert.Equal(["Alpha skill", "Gamma skill"], await db.Skills.Where(x => x.Group == "First").OrderBy(x => x.SortOrder).Select(x => x.Name).ToListAsync());
        Assert.Equal([1, 2], await db.Skills.Where(x => x.Group == "First").OrderBy(x => x.SortOrder).Select(x => x.SortOrder).ToListAsync());
        Assert.Equal(["alpha-project", "beta-project"], await db.Projects.OrderBy(x => x.SortOrder).Select(x => x.Slug).ToListAsync());
        Assert.Equal(["Alpha cert", "Beta cert"], await db.Certifications.OrderBy(x => x.SortOrder).Select(x => x.Name).ToListAsync());
        Assert.Equal(["Alpha Person", "Beta Person"], await db.Testimonials.OrderBy(x => x.SortOrder).Select(x => x.Author).ToListAsync());
        var stamps = (await db.Experiences.Select(x => x.ConcurrencyStamp).ToListAsync()).Concat(await db.Skills.Select(x => x.ConcurrencyStamp).ToListAsync())
            .Concat(await db.Projects.Select(x => x.ConcurrencyStamp).ToListAsync()).Concat(await db.Certifications.Select(x => x.ConcurrencyStamp).ToListAsync())
            .Concat(await db.Talks.Select(x => x.ConcurrencyStamp).ToListAsync()).Concat(await db.Testimonials.Select(x => x.ConcurrencyStamp).ToListAsync())
            .Concat(await db.BlogPosts.Select(x => x.ConcurrencyStamp).ToListAsync()).ToList();
        Assert.Equal(15, stamps.Count);
        Assert.All(stamps, s => Assert.Equal(32, s!.Length));
        Assert.Equal(stamps.Count, stamps.Distinct().Count());

        var project = await db.Projects.SingleAsync(x => x.Slug == "alpha-project");
        Assert.Equal("Code", project.Links.Single().Label);
        Assert.Equal("/img/projects/telemetry.svg", project.HeroImage);
        Assert.Equal("Dashboard", project.Screenshots.Single().Caption);
        Assert.Equal(ProofKind.Pdf, project.Proofs.Single().Kind);
        Assert.True(project.Featured);
        var role = await db.Experiences.SingleAsync(x => x.Company == "Alpha Org");
        Assert.True(role.Current);
        Assert.Null(role.End);
        Assert.True((await db.BlogPosts.SingleAsync(x => x.Slug == "beta-draft")).Draft);
        Assert.False((await db.BlogPosts.SingleAsync(x => x.Slug == "alpha-post")).Draft);

        // The caches moved on: the cached sitemap now lists what was imported, and only what is public.
        var sitemap = await anyone.GetStringAsync("/sitemap.xml");
        Assert.NotEqual(sitemapBefore, sitemap);
        Assert.Contains("/blog/alpha-post", sitemap);
        Assert.DoesNotContain("beta-draft", sitemap);
        Assert.Contains("/projects/alpha-project", sitemap);
        Assert.Contains("Alpha post", await anyone.GetStringAsync("/blog"));
        Assert.DoesNotContain("Beta draft", await anyone.GetStringAsync("/blog"));
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync("/blog/beta-draft")).StatusCode);
        Assert.Contains("Alpha org", (await anyone.GetStringAsync("/experience")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnImportedRow_CanBeEditedWithTheNormalEditor_UsingItsStamp()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        await TransferFlow.ImportAsync(owner, ImportHarness.Table("skills", new { name = "Editable", group = "Group" }));
        int id;
        using (var db = app.CreateDbContext()) id = (await db.Skills.SingleAsync()).Id;

        var stamp = await ContentFlow.StampAsync(owner, $"{ContentFlow.Skills}/edit/{id}");
        var save = await ContentFlow.PostAsync(owner, $"{ContentFlow.Skills}/edit/{id}", new Dictionary<string, string> { ["Form.Name"] = "Edited", ["Form.Group"] = "Group", ["Form.Stamp"] = stamp });

        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        using var after = app.CreateDbContext();
        Assert.Equal("Edited", (await after.Skills.SingleAsync()).Name);
    }

    [Fact]
    public async Task AnUpdate_WritesOnlyTheRowsItChanges_AndTheirStampsMoveOn_AndNothingIsDeleted()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        await ContentFlow.SeedRowsAsync(app, pairs: true);
        var before = await StampsAsync(app);
        var file = ImportHarness.File(content: new
        {
            skills = new object[] { new { name = "Seeding", group = "Soil" }, new { name = "Second skill" } }, // the first changes, the second says what it already is
            testimonials = new object[] { new { author = "Seed Person", quote = "Seeded praise that is long enough to be a quote.", authorTitle = "Updated role" } },
            blog = new object[] { new { slug = "seed-post", title = "Updated title" } },
        });

        var page = await TransferFlow.ImportAsync(owner, file);

        Assert.Contains("0 added, 3 updated, 1 already matched", page);
        var after = await StampsAsync(app);
        Assert.NotEqual(before["skill:Seeding"], after["skill:Seeding"]);
        Assert.Equal(before["skill:Second skill"], after["skill:Second skill"]); // unchanged rows are not written
        Assert.NotEqual(before["testimonial:Seed Person"], after["testimonial:Seed Person"]);
        Assert.NotEqual(before["post:seed-post"], after["post:seed-post"]);
        foreach (var key in before.Keys.Where(k => k is not ("skill:Seeding" or "testimonial:Seed Person" or "post:seed-post")))
            Assert.Equal(before[key], after[key]); // every other row of every table is exactly as it was
        Assert.Equal(before.Count, after.Count);
        using var db = app.CreateDbContext();
        Assert.Equal("Soil", (await db.Skills.SingleAsync(x => x.Name == "Seeding")).Group);
        Assert.Equal("Updated role", (await db.Testimonials.SingleAsync(x => x.Author == "Seed Person")).AuthorTitle);
        Assert.Equal("Updated title", (await db.BlogPosts.SingleAsync(x => x.Slug == "seed-post")).Title);
        Assert.Equal("Seeded praise that is long enough to be a quote.", (await db.Testimonials.SingleAsync(x => x.Author == "Seed Person")).Quote);
    }

    /// <summary>Every content row's stamp, by a name that identifies it.</summary>
    private static async Task<Dictionary<string, string?>> StampsAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        var all = new Dictionary<string, string?>();
        foreach (var r in await db.Experiences.AsNoTracking().ToListAsync()) all[$"role:{r.Company}|{r.Title}"] = r.ConcurrencyStamp;
        foreach (var r in await db.Skills.AsNoTracking().ToListAsync()) all[$"skill:{r.Name}"] = r.ConcurrencyStamp;
        foreach (var r in await db.Projects.AsNoTracking().ToListAsync()) all[$"project:{r.Slug}"] = r.ConcurrencyStamp;
        foreach (var r in await db.Certifications.AsNoTracking().ToListAsync()) all[$"cert:{r.Name}"] = r.ConcurrencyStamp;
        foreach (var r in await db.Talks.AsNoTracking().ToListAsync()) all[$"talk:{r.Title}"] = r.ConcurrencyStamp;
        foreach (var r in await db.Testimonials.AsNoTracking().ToListAsync()) all[$"testimonial:{r.Author}"] = r.ConcurrencyStamp;
        foreach (var r in await db.BlogPosts.AsNoTracking().ToListAsync()) all[$"post:{r.Slug}"] = r.ConcurrencyStamp;
        return all;
    }

    [Fact]
    public async Task NothingIsEverDeleted_ARowTheFileDoesNotNameStaysWhateverTheFileHolds()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        await ContentFlow.SeedRowsAsync(app, pairs: true);
        var before = await ContentFlow.CountAsync(app);

        await TransferFlow.ImportAsync(owner, ImportHarness.File(content: new
        {
            experience = new object[] { new { company = "Only Co", title = "Only", start = "2020", end = "2021", summary = "Only." } },
            skills = Array.Empty<object>(), blog = Array.Empty<object>(),
        }));

        Assert.Equal(before + 1, await ContentFlow.CountAsync(app));
        using var db = app.CreateDbContext();
        Assert.Contains(await db.Skills.Select(s => s.Name).ToListAsync(), n => n == "Seeding");
        Assert.Equal(2, await db.BlogPosts.CountAsync());
    }

    [Fact]
    public async Task TheFilesOrder_SetsPositions_WithinAGroup_AndRowsTheFileDoesNotNameFollowInTheirOrder()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        await TransferFlow.ImportAsync(owner, ImportHarness.Table("skills",
            new { name = "A", group = "G" }, new { name = "B", group = "G" }, new { name = "C", group = "G" }, new { name = "D", group = "G" }, new { name = "Other", group = "H" }));

        // The file names D and B (in that order), a changed C and a new E: D, B, C, E first in the file's order, A (not named) after them; the other group is untouched.
        await TransferFlow.ImportAsync(owner, ImportHarness.Table("skills", new { name = "D", group = "G" }, new { name = "B", group = "G" }, new { name = "E", group = "G" }, new { name = "C", group = "G" }));

        using var db = app.CreateDbContext();
        var group = await db.Skills.Where(x => x.Group == "G").OrderBy(x => x.SortOrder).ToListAsync();
        Assert.Equal(["D", "B", "E", "C", "A"], group.Select(x => x.Name).ToArray());
        Assert.Equal([1, 2, 3, 4, 5], group.Select(x => x.SortOrder).ToArray());
        Assert.Equal(1, (await db.Skills.SingleAsync(x => x.Name == "Other")).SortOrder);
        Assert.NotNull(await db.ListStamps.SingleOrDefaultAsync(x => x.Section == "skills"));
    }

    [Fact]
    public async Task AFileThatOnlyReordersRows_IsAChange_ThePreviewOffersItAndTheConfirmAppliesIt()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        await TransferFlow.ImportAsync(owner, ImportHarness.Table("skills", new { name = "A", group = "G" }, new { name = "B", group = "G" }, new { name = "C", group = "G" }));
        var reversed = ImportHarness.Table("skills", new { name = "C", group = "G" }, new { name = "B", group = "G" }, new { name = "A", group = "G" });

        var preview = await (await TransferFlow.PreviewAsync(owner, reversed)).Content.ReadAsStringAsync();
        Assert.Contains("0 will be added, 0 updated, 3 already match", preview);
        Assert.Contains("Apply the import", preview); // the rows are unchanged, but their order is not
        Assert.Contains("sets their order on the site", preview);
        using (var db = app.CreateDbContext()) Assert.Equal(["A", "B", "C"], await db.Skills.OrderBy(x => x.SortOrder).Select(x => x.Name).ToListAsync()); // a preview moves nothing

        var page = await (await TransferFlow.ConfirmAsync(owner, TransferFlow.TokenOf(preview))).Content.ReadAsStringAsync();

        Assert.Contains("now follows the file", page);
        Assert.DoesNotContain("Nothing was changed.", page);
        using var check = app.CreateDbContext();
        Assert.Equal(["C", "B", "A"], await check.Skills.OrderBy(x => x.SortOrder).Select(x => x.Name).ToListAsync());
    }

    [Fact]
    public async Task AFileInTheOrderTheSiteAlreadyHas_LeavesTheListStampAlone_SoAnOpenListPageStaysValid()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        await TransferFlow.ImportAsync(owner, ImportHarness.Table("skills", new { name = "A", group = "G" }, new { name = "B", group = "G" }));
        var open = await ContentFlow.ListStampAsync(owner, ContentFlow.Skills);

        await TransferFlow.ImportAsync(owner, ImportHarness.Table("skills", new { name = "A", group = "G" }, new { name = "B", group = "G" }));

        Assert.Equal(open, await ContentFlow.ListStampAsync(owner, ContentFlow.Skills));
    }

    [Fact]
    public async Task Talks_AndPosts_KeepTheirDateOrder_TheFilesOrderMeansNothingForThem()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        await TransferFlow.ImportAsync(owner, RichFile());

        var anyone = app.NewClient();
        var talks = await anyone.GetStringAsync("/talks");
        // Alpha talk is dated 2024-05-01 and Beta talk has no date: the page lists by date, whatever order the file gave.
        Assert.Contains("Alpha talk", talks);
        Assert.Contains("Beta talk", talks);
        Assert.True(talks.IndexOf("Alpha talk", StringComparison.Ordinal) < talks.IndexOf("Beta talk", StringComparison.Ordinal));
        using var db = app.CreateDbContext();
        Assert.Null(await db.ListStamps.SingleOrDefaultAsync(x => x.Section == "talks"));
        Assert.Null(await db.ListStamps.SingleOrDefaultAsync(x => x.Section == "blog"));
    }

    [Fact]
    public async Task AWithinFileDuplicate_IsRefused_AndTheFirstOneIsWhatIsWritten()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;

        var page = await TransferFlow.ImportAsync(owner, ImportHarness.Table("skills", new { name = "Dup", group = "First group" }, new { name = "dup", group = "Second group" }));

        Assert.Contains("1 added", page);
        Assert.Contains("1 were not imported", page);
        using var db = app.CreateDbContext();
        var skill = await db.Skills.SingleAsync();
        Assert.Equal(("Dup", "First group"), (skill.Name, skill.Group));
    }

    [Fact]
    public async Task AnUploadReference_IsKeptOnlyForAFileThatIsHere_AndNoFileIsEverWritten()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var hereName = new string('a', 32) + ".png";
        await File.WriteAllBytesAsync(Path.Combine(app.UploadsPath, hereName), TestImages.Split(SkiaSharp.SKEncodedImageFormat.Png, 8, 8));
        var filesBefore = Directory.GetFiles(app.UploadsPath, "*", SearchOption.AllDirectories).Select(Path.GetFileName).OrderBy(x => x).ToArray();

        var page = await TransferFlow.ImportAsync(owner, ImportHarness.Table("projects",
            new { title = "Has image", summary = "S.", heroImage = "/uploads/" + hereName, heroImageAlt = "Mine" },
            new { title = "Lost image", summary = "S.", heroImage = "/uploads/" + new string('c', 32) + ".png", heroImageAlt = "Gone", screenshots = new[] { new { url = "/uploads/" + new string('d', 32) + ".png", caption = "Gone too" } } }));

        Assert.Contains("2 added", page);
        Assert.Contains("not on this site", page);
        using var db = app.CreateDbContext();
        var kept = await db.Projects.SingleAsync(x => x.Title == "Has image");
        Assert.Equal("/uploads/" + hereName, kept.HeroImage);
        var lost = await db.Projects.SingleAsync(x => x.Title == "Lost image");
        Assert.Null(lost.HeroImage);
        Assert.Null(lost.HeroImageAlt); // the description goes with the image
        Assert.Empty(lost.Screenshots);
        Assert.Equal(filesBefore, Directory.GetFiles(app.UploadsPath, "*", SearchOption.AllDirectories).Select(Path.GetFileName).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task ImportedText_ReachesThePublicPagesOnlyEscaped()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        await TransferFlow.ImportAsync(owner, ImportHarness.File(content: new
        {
            skills = new object[] { new { name = "latency <50ms & \"fast\"", group = "Group 'one'" } },
            projects = new object[] { new { title = "Project {{x}} @(1+1) ${y}", summary = "javascript:alert(1) is only text here", problem = "5 < 6 & 7 > 3" } },
            testimonials = new object[] { new { quote = "Praise with <50ms & quotes \"here\", long enough.", author = "O'Neil & Sons" } },
            blog = new object[] { new { title = "Post with markup", slug = "markup-post", date = "2025-01-01", draft = false, body = "<script>alert('x')</script> <img src=x onerror=alert(1)> [click](javascript:alert(1))" } },
        }));
        var anyone = app.NewClient();

        var skills = await anyone.GetStringAsync("/skills");
        Assert.Contains("latency &lt;50ms &amp; &quot;fast&quot;", skills);
        Assert.DoesNotContain("latency <50ms", skills);
        var home = await anyone.GetStringAsync("/projects");
        Assert.DoesNotContain("<script>alert", home);
        var post = await anyone.GetStringAsync("/blog/markup-post");
        Assert.DoesNotContain("<script>alert('x')", post);
        Assert.DoesNotContain("<img src=x", post);
        Assert.DoesNotContain("href=\"javascript:", post);
        Assert.Contains("&lt;script&gt;", post);
    }

    // ---- the profile ----

    private static string ProfileFile(string name = "Imported Name") => ImportHarness.File(
        profile: new
        {
            ownerName = name, summary = "An imported summary.", location = "Imported City", hero = new { headline = "Imported headline", lede = "Imported lede." },
            metrics = new[] { new { value = "7", unit = "yrs", label = "Imported figure" } }, about = new { headline = "Imported about", paragraphs = new[] { "Imported paragraph." } },
            sections = new object[] { new { key = "blog", label = "Imported journal", shown = true }, new { key = "gallery", label = "Stray", shown = true } },
        },
        content: new { skills = new object[] { new { name = "With profile", group = "G" } } });

    [Fact]
    public async Task TheProfile_IsApplied_OnlyWhenTheBoxIsTicked_AndThroughTheProfileEditor()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var anyone = app.NewClient();
        var provider = app.Services.GetRequiredService<ISiteProfileProvider>();
        var before = await ProfileFlow.ProfileAsync(app);

        // Unticked: the rows are imported, the profile in the file is ignored.
        await TransferFlow.ImportAsync(owner, ProfileFile("Not Applied"), applyProfile: false);
        var unchanged = await ProfileFlow.ProfileAsync(app);
        Assert.Equal(ProfileFlow.Fingerprint(before), ProfileFlow.Fingerprint(unchanged));
        Assert.Equal("Casey Morgan", (await provider.GetAsync()).OwnerName);

        // Ticked: nothing changes at the preview, everything at the confirm.
        var token = await TransferFlow.HoldAsync(owner, ProfileFile(), applyProfile: true);
        Assert.Equal("Casey Morgan", (await provider.GetAsync()).OwnerName);
        Assert.Equal(ProfileFlow.Fingerprint(unchanged), ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
        var confirm = await TransferFlow.ConfirmAsync(owner, token);
        var page = await confirm.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Contains("was applied", page);
        var profile = await ProfileFlow.ProfileAsync(app);
        Assert.Equal("Imported Name", profile.OwnerName);
        Assert.NotEqual(unchanged.ConcurrencyStamp, profile.ConcurrencyStamp);
        Assert.Equal("Imported headline", profile.Hero.Headline);
        Assert.Equal("Imported figure", profile.Metrics.Single().Label);
        Assert.Equal("Imported paragraph.", profile.About.Paragraphs.Single());
        Assert.Equal("Imported City", profile.Location);
        // The sections for a key the template does not list were never stored.
        Assert.DoesNotContain(profile.Sections, s => s.Key == "gallery");
        Assert.Contains(profile.Sections, s => s.Key == "blog" && s.Label == "Imported journal" && s.Shown == true);
        Assert.Equal("Imported Name", (await provider.GetAsync()).OwnerName); // the provider follows the editor's write
        var home = await anyone.GetStringAsync("/");
        Assert.Contains("Imported headline", home);
        Assert.Contains("Imported figure", home);
        Assert.Contains("Imported about", await anyone.GetStringAsync("/about"));
    }

    [Fact]
    public async Task APreviewOfAnIdentityChange_NeverTouchesTheProviderOrTheRow_UntilConfirmed()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var provider = app.Services.GetRequiredService<ISiteProfileProvider>();
        var before = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));

        for (var i = 0; i < 3; i++) await TransferFlow.PreviewAsync(owner, ProfileFile(), applyProfile: true);

        Assert.Equal(before, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
        Assert.Equal("Casey Morgan", (await provider.GetAsync()).OwnerName);
    }

    [Fact]
    public async Task AProfileThatChangedAfterThePreview_IsRefusedWhole_RowsStillApply_AndNothingIsOverwritten()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var token = await TransferFlow.HoldAsync(owner, ProfileFile(), applyProfile: true);
        // Another tab edits the hero between the preview and the confirm.
        var save = await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields(headline: "Edited in another tab"));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var edited = await ProfileFlow.ProfileAsync(app);

        var page = await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync();

        Assert.Contains("was changed after the preview was made, so none of it was applied and nothing was overwritten", page);
        Assert.Equal(ProfileFlow.Fingerprint(edited), ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
        using var db = app.CreateDbContext();
        Assert.Equal("With profile", (await db.Skills.SingleAsync()).Name); // the rows are independent of the profile
    }

    [Fact]
    public async Task AProfileWriteThatLandsInTheMiddleOfTheConfirm_IsAConflictToo_NotAnOverwrite()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var token = await TransferFlow.HoldAsync(owner, ProfileFile(), applyProfile: true);
        var open = await ProfileFlow.StampAsync(owner, ProfileFlow.Hero);

        var page = await app.OverlapAsync(
            async () => await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync(),
            async () => Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields(headline: "Landed in the middle"), open)).StatusCode));

        Assert.Contains("so none of it was applied", page);
        var profile = await ProfileFlow.ProfileAsync(app);
        Assert.Equal("Landed in the middle", profile.Hero.Headline);
        Assert.Equal("Casey Morgan", profile.OwnerName);
    }

    [Fact]
    public async Task AProfileThatDoesNotChangeAnything_IsNotWritten_SoItsStampStays()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var before = await ProfileFlow.ProfileAsync(app);

        await TransferFlow.ImportAsync(owner, ImportHarness.File(profile: new { ownerName = "Casey Morgan" }, content: new { skills = new object[] { new { name = "x", group = "y" } } }), applyProfile: true);

        Assert.Equal(before.ConcurrencyStamp, (await ProfileFlow.ProfileAsync(app)).ConcurrencyStamp);
    }

    [Fact]
    public async Task ApplyingAProfileWithANewTemplate_SwitchesIt_AndAnUnknownOneIsRefusedWholeNeverGuessed()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;

        var refused = await TransferFlow.ImportAsync(owner, ImportHarness.File(profile: new { templateKey = "nope", ownerName = "Must Not Apply" }), applyProfile: true);
        Assert.Contains("is not installed on this site", refused);
        Assert.Equal("software", (await ProfileFlow.ProfileAsync(app)).TemplateKey);
        Assert.Equal("Casey Morgan", (await ProfileFlow.ProfileAsync(app)).OwnerName);

        await TransferFlow.ImportAsync(owner, ImportHarness.File(profile: new { templateKey = "healthcare" }), applyProfile: true);
        Assert.Equal("healthcare", (await ProfileFlow.ProfileAsync(app)).TemplateKey);
    }

    // ---- stale rows and collisions ----

    [Fact]
    public async Task ARowChangedAfterThePreview_IsRefusedAlone_NothingOverwritesIt_TheOthersApply()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        await ContentFlow.SeedRowsAsync(app, pairs: true);
        var token = await TransferFlow.HoldAsync(owner, ImportHarness.Table("skills", new { name = "Seeding", group = "From file" }, new { name = "Second skill", group = "From file" }, new { name = "Brand new", group = "From file" }));
        using (var db = app.CreateDbContext())
        {
            var row = await db.Skills.SingleAsync(x => x.Name == "Seeding");
            row.Group = "Edited elsewhere";
            row.ConcurrencyStamp = ContentEditor.NewStamp();
            await db.SaveChangesAsync();
        }

        var page = await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync();

        Assert.Contains("1 added, 1 updated", page);
        Assert.Contains("was changed after the preview was made, so it was not updated and nothing was overwritten", page);
        using var check = app.CreateDbContext();
        Assert.Equal("Edited elsewhere", (await check.Skills.SingleAsync(x => x.Name == "Seeding")).Group);
        Assert.Equal("From file", (await check.Skills.SingleAsync(x => x.Name == "Second skill")).Group);
        Assert.Equal("From file", (await check.Skills.SingleAsync(x => x.Name == "Brand new")).Group);
    }

    [Fact]
    public async Task ARowRemovedAfterThePreview_IsNotBroughtBack_ItIsRefusedWithTheReason()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        await ContentFlow.SeedRowsAsync(app, pairs: true);
        var token = await TransferFlow.HoldAsync(owner, ImportHarness.Table("skills", new { name = "Seeding", group = "Changed" }));
        using (var db = app.CreateDbContext())
        {
            db.Skills.Remove(await db.Skills.SingleAsync(x => x.Name == "Seeding"));
            await db.SaveChangesAsync();
        }

        var page = await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync();

        Assert.Contains("was removed after the preview was made", page);
        using var check = app.CreateDbContext();
        Assert.DoesNotContain(await check.Skills.Select(s => s.Name).ToListAsync(), n => n == "Seeding");
    }

    [Fact]
    public async Task ANewRowWhoseKeyAppearedAfterThePreview_IsNotAdded_SoNoDuplicateIsMade()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var token = await TransferFlow.HoldAsync(owner, ImportHarness.Table("skills", new { name = "Raced", group = "From file" }, new { name = "Not raced", group = "From file" }));
        using (var db = app.CreateDbContext())
        {
            db.Skills.Add(new Skill { Name = "raced", Group = "Someone else", SortOrder = 1, ConcurrencyStamp = ContentEditor.NewStamp() });
            await db.SaveChangesAsync();
        }

        var page = await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync();

        Assert.Contains("was added after the preview was made, so this one was not added", page);
        using var check = app.CreateDbContext();
        Assert.Equal(2, await check.Skills.CountAsync());
        Assert.Equal("Someone else", (await check.Skills.SingleAsync(x => x.Name == "raced")).Group);
    }

    [Fact]
    public async Task ABlogPostWhoseAddressNameWasTakenAfterThePreview_IsRefusedByTheDatabaseKey()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var token = await TransferFlow.HoldAsync(owner, ImportHarness.Table("blog", new { title = "Raced post", slug = "raced-post", date = "2025-01-01", body = "Text." }));
        using (var db = app.CreateDbContext())
        {
            db.BlogPosts.Add(new BlogPost { Title = "Someone else's", Slug = "raced-post", Date = new DateOnly(2025, 1, 1), BodyMarkdown = "x", ConcurrencyStamp = ContentEditor.NewStamp() });
            await db.SaveChangesAsync();
        }

        var page = await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync();

        Assert.Contains("0 added", page);
        Assert.Contains("1 were not imported", page);
        using var check = app.CreateDbContext();
        Assert.Equal("Someone else's", (await check.BlogPosts.SingleAsync()).Title);
    }

    // ---- real overlapping requests ----

    [Fact]
    public async Task AnEditorSaveThatLandsInTheMiddleOfTheImport_WinsOverTheRowItTouched_AndIsNeverOverwritten()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        await ContentFlow.SeedRowsAsync(app, pairs: true);
        int id;
        using (var db = app.CreateDbContext()) id = (await db.Skills.SingleAsync(x => x.Name == "Seeding")).Id;
        var token = await TransferFlow.HoldAsync(owner, ImportHarness.Table("skills", new { name = "Seeding", group = "From the file" }, new { name = "Another", group = "From the file" }));
        var stamp = await ContentFlow.StampAsync(owner, $"{ContentFlow.Skills}/edit/{id}");

        var page = await app.OverlapAsync(
            async () => await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync(),
            async () =>
            {
                var save = await ContentFlow.PostAsync(owner, $"{ContentFlow.Skills}/edit/{id}", new Dictionary<string, string> { ["Form.Name"] = "Seeding", ["Form.Group"] = "Edited in the middle", ["Form.Stamp"] = stamp });
                Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
            });

        Assert.Contains("was changed while the import was running, so it was not updated and nothing was overwritten", page);
        using var check = app.CreateDbContext();
        Assert.Equal("Edited in the middle", (await check.Skills.SingleAsync(x => x.Name == "Seeding")).Group);
        Assert.Equal("From the file", (await check.Skills.SingleAsync(x => x.Name == "Another")).Group); // the rest of the import went on
    }

    [Fact]
    public async Task AMoveThatLandsInTheMiddleOfTheOrderStep_MakesItAConflict_NoTwoRowsEverShareAPosition()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        await TransferFlow.ImportAsync(owner, ImportHarness.Table("skills", new { name = "One", group = "G" }, new { name = "Two", group = "G" }, new { name = "Three", group = "G" }));
        int one;
        using (var db = app.CreateDbContext()) one = (await db.Skills.SingleAsync(x => x.Name == "One")).Id;
        // The data in the file is what is stored, so only the order step writes: it asks for Three, Two, One.
        var token = await TransferFlow.HoldAsync(owner, ImportHarness.Table("skills", new { name = "Three", group = "G" }, new { name = "Two", group = "G" }, new { name = "One", group = "G" }));
        var open = await ContentFlow.ListStampAsync(owner, ContentFlow.Skills);

        var page = await app.OverlapAsync(
            async () => await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync(),
            async () => Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.MoveWithStampAsync(owner, ContentFlow.Skills, one, "down", open)).StatusCode));

        Assert.Contains("was not set, because the list was changed somewhere else", page);
        using var check = app.CreateDbContext();
        var positions = await check.Skills.OrderBy(x => x.SortOrder).Select(x => x.SortOrder).ToListAsync();
        Assert.Equal(positions.Distinct().Count(), positions.Count);
        Assert.Equal(["Two", "One", "Three"], await check.Skills.OrderBy(x => x.SortOrder).Select(x => x.Name).ToListAsync()); // the move won; the import's order was not applied
    }

    // ---- the token ----

    [Fact]
    public async Task APreviewIsConfirmedOnce_AndASecondConfirmWritesNothing()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var token = await TransferFlow.HoldAsync(owner, ImportHarness.Table("skills", new { name = "Once", group = "G" }));

        var first = await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync();
        using (var db = app.CreateDbContext())
        {
            db.Skills.RemoveRange(db.Skills);
            await db.SaveChangesAsync();
        }
        var second = await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync();

        Assert.Contains("1 added", first);
        Assert.Contains("expired, was already applied, or was made in another session, so nothing was written", second);
        using var check = app.CreateDbContext();
        Assert.Equal(0, await check.Skills.CountAsync());
    }

    [Fact]
    public async Task AnExpiredUnknownOrEmptyToken_WritesNothing()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var before = await ContentFlow.SnapshotAsync(app);
        var token = await TransferFlow.HoldAsync(owner, ImportHarness.Table("skills", new { name = "Late", group = "G" }));
        app.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        foreach (var bad in new[] { token, "", "0123456789abcdef0123456789abcdef", "not-a-token", new string('f', 31), new string('g', 32), token.ToUpperInvariant() + "0" })
        {
            var response = await TransferFlow.ConfirmAsync(owner, bad);
            var page = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("so nothing was written", page);
        }
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task ATokenJustBeforeItExpires_StillWorks()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var token = await TransferFlow.HoldAsync(owner, ImportHarness.Table("skills", new { name = "On time", group = "G" }));
        app.Clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));

        var page = await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync();

        Assert.Contains("1 added", page);
    }

    [Fact]
    public async Task APreviewBelongsToTheSessionThatMadeIt_AnotherSignInCannotConfirmIt_NorDiscardIt()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var other = await app.NewClientSignedInAsync();
        var token = await TransferFlow.HoldAsync(owner, ImportHarness.Table("skills", new { name = "Mine", group = "G" }));

        var stolen = await (await TransferFlow.ConfirmAsync(other, token)).Content.ReadAsStringAsync();
        var discard = await ContentFlow.PostAsync(other, "/admin/transfer/confirm?handler=Discard", new Dictionary<string, string> { ["Token"] = token }, "/admin/transfer");
        using (var db = app.CreateDbContext()) Assert.Equal(0, await db.Skills.CountAsync());

        Assert.Contains("so nothing was written", stolen);
        Assert.Equal(HttpStatusCode.Redirect, discard.StatusCode);
        var rightful = await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync(); // the foreign attempts did not use it up
        Assert.Contains("1 added", rightful);
    }

    [Fact]
    public async Task ASecondPreview_ReplacesTheFirstOfTheSameSession_AndOnlyTheLatestCanBeConfirmed()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var first = await TransferFlow.HoldAsync(owner, ImportHarness.Table("skills", new { name = "First", group = "G" }));
        var second = await TransferFlow.HoldAsync(owner, ImportHarness.Table("skills", new { name = "Second", group = "G" }));

        var old = await (await TransferFlow.ConfirmAsync(owner, first)).Content.ReadAsStringAsync();
        var latest = await (await TransferFlow.ConfirmAsync(owner, second)).Content.ReadAsStringAsync();

        Assert.Contains("so nothing was written", old);
        Assert.Contains("1 added", latest);
        using var db = app.CreateDbContext();
        Assert.Equal(["Second"], await db.Skills.Select(s => s.Name).ToListAsync());
    }

    [Fact]
    public async Task TheConfirmAppliesWhatThePreviewShowed_NotWhatTheBrowserSendsNow()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var token = await TransferFlow.HoldAsync(owner, ImportHarness.Table("skills", new { name = "Previewed", group = "G" }), applyProfile: false);

        // The post carries extra fields (another file's rows, a profile switch, a different name): none of them is read.
        var response = await ContentFlow.PostAsync(owner, "/admin/transfer/confirm", new Dictionary<string, string>
        {
            ["Token"] = token, ["ApplyProfile"] = "true", ["Form.Name"] = "Smuggled", ["Upload"] = "{\"schemaVersion\":1}", ["Plan"] = "x",
        }, "/admin/transfer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var db = app.CreateDbContext();
        Assert.Equal(["Previewed"], await db.Skills.Select(s => s.Name).ToListAsync());
        Assert.Equal("Casey Morgan", (await ProfileFlow.ProfileAsync(app)).OwnerName);
    }

    [Fact]
    public async Task DiscardingAPreview_WritesNothing_AndTheTokenIsGone()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var token = await TransferFlow.HoldAsync(owner, ImportHarness.Table("skills", new { name = "Discarded", group = "G" }));

        var discard = await ContentFlow.PostAsync(owner, "/admin/transfer/confirm?handler=Discard", new Dictionary<string, string> { ["Token"] = token }, "/admin/transfer");

        Assert.Equal(HttpStatusCode.Redirect, discard.StatusCode);
        Assert.Equal("/admin/transfer", discard.Headers.Location!.OriginalString);
        Assert.Contains("discarded", await owner.GetStringAsync("/admin/transfer"));
        Assert.Contains("so nothing was written", await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync());
        using var db = app.CreateDbContext();
        Assert.Equal(0, await db.Skills.CountAsync());
    }

    [Fact]
    public async Task APreviewWithNothingToWrite_OffersNoApplyButton()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        await ContentFlow.SeedRowsAsync(app);

        var preview = await (await TransferFlow.PreviewAsync(owner, ImportHarness.Table("skills", new { name = "Seeding", group = "Garden" }, new { name = "" }))).Content.ReadAsStringAsync();

        Assert.Contains("There is nothing to write", preview);
        Assert.DoesNotContain("Apply the import", preview);
        Assert.Contains("Already up to date", preview);
        Assert.Contains("Not imported", preview);
    }

    [Fact]
    public async Task ARefusedRowAndAGoodRow_InOneFile_ArePreviewedTogether_AndOnlyTheGoodOneIsWritten()
    {
        var (app, owner) = await SiteAsync();
        using var _ = app;
        var file = ImportHarness.Table("skills", new { name = "Good", group = "G" }, new { name = "<b>Bad</b>", group = "G" }, new { name = "Also good", group = "G" });

        var preview = await (await TransferFlow.PreviewAsync(owner, file)).Content.ReadAsStringAsync();
        Assert.Contains("2 will be added", preview);
        Assert.Contains("1 will not be imported", preview);
        Assert.Contains("plain text", preview);
        var token = TransferFlow.TokenOf(preview)!;
        var page = await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync();

        Assert.Contains("2 added", page);
        using var db = app.CreateDbContext();
        Assert.Equal(["Also good", "Good"], await db.Skills.OrderBy(s => s.Name).Select(s => s.Name).ToListAsync());
    }
}
