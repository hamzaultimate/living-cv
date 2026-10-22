using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The onboarding wizard from /setup to a live site, for both shipped templates, plus what must not change on the way.</summary>
public class OnboardingFlowTests
{
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed record Generated(string Sitemap, string CvName, string OgHash, string CvHash);

    // The CV PDF of the full demo persona cannot be rendered on a host without the fonts it names (a known limit of the test host), so a
    // seeded site is compared without it; the empty-site flows below compare it too.
    private static async Task<Generated> GeneratedAsync(HttpClient client, bool withCv = true)
    {
        var og = await client.GetByteArrayAsync("/og.png");
        var sitemap = await client.GetStringAsync("/sitemap.xml");
        if (!withCv) return new Generated(sitemap, "", Hash(og), "");
        var cv = await client.GetAsync("/cv.pdf");
        Assert.Equal(HttpStatusCode.OK, cv.StatusCode);
        return new Generated(sitemap, cv.Content.Headers.ContentDisposition!.FileName!.Trim('"'), Hash(og), Hash(await cv.Content.ReadAsByteArrayAsync()));
    }

    // ---------------------------------------------------------------- end to end

    [Theory]
    [InlineData("software", "Casey Morgan", "Platform Specialist", "Specialist who keeps the lights on and writes things down.")]
    [InlineData("healthcare", "Casey Morgan", "Nurse Educator", "Nurse educator who teaches new graduates and supports their first year.")]
    public async Task FromSetupToALiveSite_InTheChosenTemplate(string key, string name, string title, string summary)
    {
        using var app = new AdminApp();
        var anonymous = app.NewClient();
        var before = await GeneratedAsync(anonymous);
        Assert.Contains("alex-rivera", before.CvName);
        Assert.Contains("https://owner.example/about", before.Sitemap); // the placeholder identity is what the site shows before the wizard

        var owner = await WizardFlow.FirstRunClientAsync(app);
        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await WizardFlow.PickTemplateAsync(owner, key)));
        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(name, title, "casey@owner.example", "Leeds, UK", summary))));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(WizardFlow.Photo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(WizardFlow.Review)).StatusCode);
        var publish = await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields("https://casey.example"));
        Assert.Equal(HttpStatusCode.Redirect, publish.StatusCode);
        Assert.Equal("/admin", WizardFlow.Where(publish));

        var profile = await WizardFlow.ProfileAsync(app);
        Assert.NotNull(profile.OnboardingCompletedAt);
        Assert.Null(profile.OnboardingDraft);
        Assert.Equal(key, profile.TemplateKey);

        var home = await anonymous.GetStringAsync("/");
        Assert.Contains(name, home);
        Assert.Contains(title, home);
        Assert.Contains(summary, home);
        Assert.Contains(key == "healthcare" ? "data-template=\"healthcare\"" : "<html lang=\"en\">", home);
        // Nothing of the placeholder persona survives under the new name.
        foreach (var path in new[] { "/", "/about", "/contact", "/experience", "/certifications" })
        {
            var html = await anonymous.GetStringAsync(path);
            Assert.DoesNotContain("Alex Rivera", html);
            Assert.DoesNotContain("alexrivera", html);
            Assert.DoesNotContain("~20k", html);
            Assert.DoesNotContain("example-alex-rivera", html);
        }
        var about = await anonymous.GetStringAsync("/about");
        Assert.Contains(summary, about);
        var contact = await anonymous.GetStringAsync("/contact");
        Assert.Contains("casey@owner.example", contact);
        Assert.Contains("Leeds, UK", contact);
        Assert.DoesNotContain(">Online<", contact);

        // The generated assets were cached before the wizard ran; publishing evicted them.
        var after = await GeneratedAsync(anonymous);
        Assert.Equal("casey-morgan-cv.pdf", after.CvName);
        Assert.Contains("https://casey.example/about", after.Sitemap);
        Assert.DoesNotContain("owner.example", after.Sitemap);
        Assert.NotEqual(before.OgHash, after.OgHash);
        Assert.NotEqual(before.CvHash, after.CvHash);

        // The wording of the other profession never leaks into content the wizard produced.
        foreach (var path in new[] { "/", "/about", "/contact", "/experience", "/certifications", "/skills" })
        {
            var text = DemoSite.WithoutScripts(await anonymous.GetStringAsync(path));
            if (key == "healthcare") Assert.DoesNotMatch(DemoSite.SoftwareWording, text);
            else Assert.DoesNotMatch(DemoSite.HealthcareWording, text);
        }
    }

    [Fact]
    public async Task TheSitemapFollowsTheChosenTemplate_HealthcareHidesBlogAndTalks()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        await WizardFlow.SaveBasicsAsync(owner);
        await WizardFlow.PublishAsync(owner);

        var sitemap = await app.NewClient().GetStringAsync("/sitemap.xml");

        Assert.Contains("https://jordan.example/certifications", sitemap);
        Assert.DoesNotContain("/blog", sitemap);
        Assert.DoesNotContain("/talks", sitemap);
        Assert.Equal(HttpStatusCode.NotFound, (await app.NewClient().GetAsync("/blog")).StatusCode);
    }

    [Fact]
    public async Task MarkupAndQuotes_AreEncodedOnTheLiveSite()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(name: "Ann & \"Bo\" O'Neil", summary: "I like 5 > 3 & \"quotes\"."));
        await WizardFlow.PublishAsync(owner);

        var html = await app.NewClient().GetStringAsync("/");

        Assert.Contains("Ann &amp; &quot;Bo&quot; O&#x27;Neil", html);
        Assert.DoesNotContain("Ann & \"Bo\"", html);
        Assert.Contains("I like 5 &gt; 3 &amp; &quot;quotes&quot;.", html);
    }

    // ---------------------------------------------------------------- the draft is not the site

    [Fact]
    public async Task UntilPublished_NoStepChangesWhatTheSiteShows_OrItsGeneratedFiles()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var anonymous = app.NewClient();
        var homeBefore = DemoSite.Normalise(await anonymous.GetStringAsync("/"));
        var aboutBefore = DemoSite.Normalise(await anonymous.GetStringAsync("/about"));
        var generatedBefore = await GeneratedAsync(anonymous, withCv: false);
        var rowBefore = WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app));
        var counts = await DemoSite.RowCountsAsync(app);
        var version = app.Services.GetRequiredService<ISiteProfileProvider>().Version;

        var owner = await app.SignedInClientAsync();
        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(name: "Draft Person"));
        Assert.Equal(HttpStatusCode.Redirect, (await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Jpeg, 64, 64))).StatusCode);
        await WizardFlow.SaveChoicesAsync(owner, WizardFlow.ReviewFields("https://draft.example", "violet", StarterChoice.Sample));

        var draft = await WizardFlow.DraftAsync(app);
        Assert.Equal("Draft Person", draft!.OwnerName);
        Assert.NotNull(draft.PhotoDraft);
        Assert.Equal(rowBefore, WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app)));
        Assert.Equal(version, app.Services.GetRequiredService<ISiteProfileProvider>().Version);
        Assert.Equal(counts, await DemoSite.RowCountsAsync(app));
        Assert.Equal(homeBefore, DemoSite.Normalise(await anonymous.GetStringAsync("/")));
        Assert.Equal(aboutBefore, DemoSite.Normalise(await anonymous.GetStringAsync("/about")));
        var generatedAfter = await GeneratedAsync(anonymous, withCv: false);
        Assert.Equal(generatedBefore, generatedAfter);
        Assert.DoesNotContain("draft.example", generatedAfter.Sitemap);
        Assert.DoesNotContain("Draft Person", await anonymous.GetStringAsync("/"));
        Assert.DoesNotContain("--c-accent", await anonymous.GetStringAsync("/"));
        // The draft photo has no public file: nothing directly in the uploads folder, nothing reachable by URL.
        Assert.Empty(Directory.GetFiles(app.UploadsPath));
        var name = draft.PhotoDraft!;
        foreach (var url in new[] { $"/uploads/{name}", $"/uploads/.drafts/{name}", $"/uploads/%2edrafts/{name}", $"/uploads/.drafts%2f{name}", "/uploads/.drafts/" })
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task TheDraft_SurvivesARefreshAndANewSession_AndEachStepRestoresItsAnswers()
    {
        using var app = new AdminApp();
        var first = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(first, "healthcare");
        await WizardFlow.SaveBasicsAsync(first, WizardFlow.BasicsFields(name: "Resume Person", location: "Bristol"));
        await WizardFlow.SaveChoicesAsync(first, WizardFlow.ReviewFields("https://resume.example", "rose", StarterChoice.Sample));
        await first.PostAsync("/admin/logout", new FormUrlEncodedContent([]));

        var second = app.NewClient();
        await AdminApp.SignInAsync(second);
        var template = await second.GetStringAsync(WizardFlow.Template);
        var basics = await second.GetStringAsync(WizardFlow.Basics);
        var review = await second.GetStringAsync(WizardFlow.Review);

        Assert.Matches(new Regex("id=\"tpl-healthcare\"[^>]*checked"), template);
        Assert.DoesNotMatch(new Regex("id=\"tpl-software\"[^>]*checked"), template);
        Assert.Contains("value=\"Resume Person\"", basics);
        Assert.Contains("value=\"Bristol\"", basics);
        Assert.Contains("https://resume.example", review);
        Assert.Matches(new Regex("id=\"accent-rose\"[^>]*checked"), review);
        Assert.Matches(new Regex("id=\"content-sample\"[^>]*checked"), review);
        var dashboard = await second.GetStringAsync("/admin");
        Assert.Contains("Finish setting up", dashboard);
        Assert.Contains("href=\"/admin/welcome/photo\"", dashboard); // resumes at the first step that is not saved yet... after basics that is the photo
    }

    [Fact]
    public async Task AStepWithoutTheOnesBeforeIt_SendsTheOwnerBack()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        Assert.Equal("/admin/welcome", WizardFlow.Where(await owner.GetAsync(WizardFlow.Basics)));
        Assert.Equal("/admin/welcome", WizardFlow.Where(await WizardFlow.PostAsync(owner, WizardFlow.Basics, WizardFlow.BasicsFields(), WizardFlow.Template)));
        await WizardFlow.PickTemplateAsync(owner, "software");
        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await owner.GetAsync(WizardFlow.Photo)));
        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await owner.GetAsync(WizardFlow.Review)));
        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await WizardFlow.PostAsync(owner, WizardFlow.Review + "?handler=Publish", WizardFlow.ReviewFields(), WizardFlow.Template)));
        Assert.Null((await WizardFlow.ProfileAsync(app)).OnboardingCompletedAt);
    }

    [Fact]
    public async Task ChangingTheTemplate_ReplacesAnUntouchedSuggestion_ButKeepsWhatTheOwnerTyped()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();
        var catalog = app.Services.GetRequiredService<ITemplateCatalog>();
        var software = catalog.Get("software").HeroSuggestions;
        var healthcare = catalog.Get("healthcare").HeroSuggestions;

        await WizardFlow.PickTemplateAsync(owner, "software");
        var first = WebUtility.HtmlDecode(await owner.GetStringAsync(WizardFlow.Basics));
        Assert.Contains($"value=\"{software.Title}\"", first);
        Assert.Contains(software.Summary, first);

        // Save the suggestions untouched, then switch: they are replaced. A typed title is kept.
        await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(title: software.Title, summary: software.Summary));
        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        var second = WebUtility.HtmlDecode(await owner.GetStringAsync(WizardFlow.Basics));
        Assert.Contains($"value=\"{healthcare.Title}\"", second);
        Assert.Contains(healthcare.Summary, second);
        Assert.DoesNotContain(software.Summary, second);

        await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(title: "Ward Sister", summary: healthcare.Summary));
        await WizardFlow.PickTemplateAsync(owner, "software");
        var third = WebUtility.HtmlDecode(await owner.GetStringAsync(WizardFlow.Basics));
        Assert.Contains("value=\"Ward Sister\"", third);
        Assert.Contains(software.Summary, third);
    }

    // ---------------------------------------------------------------- suggestions come from the manifest

    [Fact]
    public async Task Suggestions_ComeFromTheManifest_NotFromTheRazorPage()
    {
        var dir = TemplateDirs.WithExtra("custom", n =>
        {
            n["name"] = "Custom";
            n["heroSuggestions"] = new System.Text.Json.Nodes.JsonObject
            {
                ["title"] = "Unique Suggested Title 7F3A",
                ["summary"] = "Unique suggested summary 91BC that no page contains.",
            };
        });
        try
        {
            using var app = AdminApp.WithTemplates(dir);
            var owner = await app.SignedInClientAsync();
            await WizardFlow.PickTemplateAsync(owner, "custom");

            var html = WebUtility.HtmlDecode(await owner.GetStringAsync(WizardFlow.Basics));

            Assert.Contains("value=\"Unique Suggested Title 7F3A\"", html);
            Assert.Contains("Unique suggested summary 91BC that no page contains.", html);
            Assert.DoesNotContain("Registered Nurse", html);
            Assert.DoesNotContain("Software Engineer", html);
            // And the other way round: the shipped pages hold none of the suggestion text.
            var source = Directory.EnumerateFiles(Path.Combine(TemplateDirs.Web, "Pages"), "*.cshtml*", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(Path.Combine(TemplateDirs.Web, "Pages"), "*.cs", SearchOption.AllDirectories));
            foreach (var file in source)
            {
                var text = File.ReadAllText(file);
                Assert.DoesNotContain("Registered Nurse", text);
                Assert.DoesNotContain("Software Engineer", text);
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task TheTemplateStep_ShowsCardsGroupedByProfession_WithThumbnailsAndWhatIsIncluded()
    {
        using var app = new AdminApp();
        var owner = await app.SignedInClientAsync();

        var html = WebUtility.HtmlDecode(await owner.GetStringAsync(WizardFlow.Template));

        Assert.Contains(">Software engineer</h2>", html);
        Assert.Contains(">Healthcare professional</h2>", html);
        Assert.Contains("src=\"/admin/welcome/thumb/software\"", html);
        Assert.Contains("src=\"/admin/welcome/thumb/healthcare\"", html);
        Assert.Contains("<strong>Pages:</strong> about, experience, work, skills, certifications, blog, talks, testimonials, contact", html);
        Assert.Contains("licences & registrations", html);
        Assert.Contains("Case study", html);
        Assert.Contains("Initiative", html);
        Assert.Equal(1, Regex.Matches(html, "Before you publish").Count); // only the sensitive template carries guidance
        Assert.Contains("patient-identifiable", html);
        Assert.Equal(2, Regex.Matches(html, "type=\"radio\" name=\"Key\"").Count);
    }

    [Fact]
    public async Task ATemplateWithoutAThumbnail_GetsADataDrivenCard()
    {
        var dir = TemplateDirs.WithExtra("plain", n => n["name"] = "Plain");
        try
        {
            using var app = AdminApp.WithTemplates(dir);
            var owner = await app.SignedInClientAsync();

            var html = await owner.GetStringAsync(WizardFlow.Template);

            Assert.Contains("adm-thumb adm-thumb-fallback", html);
            Assert.Contains(">Plain</div>", html);
            Assert.DoesNotContain("/admin/welcome/thumb/plain", html);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/admin/welcome/thumb/plain")).StatusCode);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---------------------------------------------------------------- starter content

    private static int SeedCount(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Seed", file)), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return doc.RootElement.GetArrayLength();
    }

    private static async Task PublishWithAsync(AdminApp app, HttpClient owner, string template, string content, string accent = "")
    {
        await WizardFlow.PickTemplateAsync(owner, template);
        await WizardFlow.SaveBasicsAsync(owner);
        var publish = await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields(accent: accent, content: content));
        Assert.Equal("/admin", WizardFlow.Where(publish));
    }

    [Fact]
    public async Task Blank_AddsNoRows_ToAnEmptySite()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        await PublishWithAsync(app, owner, "healthcare", StarterChoice.Blank);

        Assert.All((await DemoSite.RowCountsAsync(app)).Where(c => c.Key != "profiles"), c => Assert.Equal(0, c.Value));
    }

    [Fact]
    public async Task Sample_Healthcare_AddsItsFictionalStarterRows_ToAnEmptySite()
    {
        using var app = new AdminApp();
        var starter = app.Services.GetRequiredService<ITemplateCatalog>().GetStarter("healthcare")!;
        var owner = await WizardFlow.FirstRunClientAsync(app);

        await PublishWithAsync(app, owner, "healthcare", StarterChoice.Sample);

        var counts = await DemoSite.RowCountsAsync(app);
        Assert.Equal(starter.Experiences.Count, counts["experiences"]);
        Assert.Equal(starter.Skills.Count, counts["skills"]);
        Assert.Equal(starter.Projects.Count, counts["projects"]);
        Assert.Equal(starter.Certifications.Count, counts["certifications"]);
        Assert.Equal(starter.Testimonials.Count, counts["testimonials"]);
        using var db = app.CreateDbContext();
        Assert.All(await db.Experiences.ToListAsync(), e => Assert.Contains("fictional", e.Company, StringComparison.OrdinalIgnoreCase));
        // The identity is the owner's, not the sample persona's.
        var home = await app.NewClient().GetStringAsync("/");
        Assert.Contains("Jordan Alexander", home);
        Assert.DoesNotContain("Jordan Sample", home);
        Assert.Contains("(sample)", await app.NewClient().GetStringAsync("/certifications"));
    }

    [Fact]
    public async Task Sample_Software_LoadsTheBuiltInDemoPersonaRows_ThroughTheSeedFiles()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        await PublishWithAsync(app, owner, "software", StarterChoice.Sample);

        var counts = await DemoSite.RowCountsAsync(app);
        Assert.Equal(SeedCount("experiences.json"), counts["experiences"]);
        Assert.Equal(SeedCount("skills.json"), counts["skills"]);
        Assert.Equal(SeedCount("projects.json"), counts["projects"]);
        Assert.Equal(SeedCount("certifications.json"), counts["certifications"]);
        Assert.Equal(SeedCount("blog.json"), counts["posts"]);
        Assert.Equal(SeedCount("talks.json"), counts["talks"]);
        Assert.Equal(SeedCount("testimonials.json"), counts["testimonials"]);
        var home = await app.NewClient().GetStringAsync("/");
        Assert.Contains("Jordan Alexander", home);
        Assert.DoesNotContain("Alex Rivera", home);
    }

    [Fact]
    public async Task Sample_OnASiteThatAlreadyHasContent_NeverOverwritesOrDeletesAnything()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app); // every table has the demo persona's rows
        await app.NewClient().GetAsync("/");
        using (var db = app.CreateDbContext())
        {
            var first = await db.Experiences.OrderBy(e => e.Id).FirstAsync();
            first.Summary = "MY OWN EDIT";
            db.Skills.Add(new Skill { Name = "Hand added skill", Group = "Mine", SortOrder = 99 });
            await db.SaveChangesAsync();
        }
        var rowsBefore = await RowsAsync(app);
        var counts = await DemoSite.RowCountsAsync(app);
        var owner = await app.SignedInClientAsync();

        // The healthcare sample would add rows if any table were empty; none is.
        await PublishWithAsync(app, owner, "healthcare", StarterChoice.Sample);

        Assert.Equal(counts, await DemoSite.RowCountsAsync(app));
        Assert.Equal(rowsBefore, await RowsAsync(app));
        using var check = app.CreateDbContext();
        Assert.Equal("MY OWN EDIT", (await check.Experiences.OrderBy(e => e.Id).FirstAsync()).Summary);
        Assert.Contains(await check.Skills.Select(s => s.Name).ToListAsync(), n => n == "Hand added skill");
    }

    [Fact]
    public async Task Sample_OnASitePartlyFilled_AddsOnlyToTheEmptyTables()
    {
        using var app = new AdminApp();
        _ = app.Services;
        using (var db = app.CreateDbContext())
        {
            db.Skills.Add(new Skill { Name = "Only skill", Group = "Clinical", SortOrder = 1 });
            await db.SaveChangesAsync();
        }
        var starter = app.Services.GetRequiredService<ITemplateCatalog>().GetStarter("healthcare")!;
        var owner = await app.SignedInClientAsync();

        await PublishWithAsync(app, owner, "healthcare", StarterChoice.Sample);

        var counts = await DemoSite.RowCountsAsync(app);
        Assert.Equal(1, counts["skills"]);
        Assert.Equal(starter.Experiences.Count, counts["experiences"]);
        Assert.Equal(starter.Certifications.Count, counts["certifications"]);
    }

    [Fact]
    public async Task ReRunning_WithBlank_RemovesNothing_AndKeepsTheOwnersHeroAndAbout()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await PublishWithAsync(app, owner, "healthcare", StarterChoice.Sample);
        using (var db = app.CreateDbContext())
        {
            var p = await db.SiteProfiles.SingleAsync();
            p.Hero = new ProfileHero { Headline = "My own headline", Eyebrow = "My eyebrow" };
            p.About = new ProfileAbout { Paragraphs = ["My own paragraph."] };
            p.Metrics = [new ProfileMetric { Value = "9", Label = "mine" }];
            p.Role = "My role line";
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        var counts = await DemoSite.RowCountsAsync(app);

        // A second pass through the wizard on a finished site: a different template, name and blank content.
        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await WizardFlow.PickTemplateAsync(owner, "software")));
        var basics = await owner.GetStringAsync(WizardFlow.Basics);
        Assert.Contains("value=\"Jordan Alexander\"", basics); // a re-run starts from what the site says now
        await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(name: "Jordan A. Renamed"));
        var publish = await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields(content: StarterChoice.Blank));
        Assert.Equal("/admin", WizardFlow.Where(publish));

        Assert.Equal(counts, await DemoSite.RowCountsAsync(app));
        var profile = await WizardFlow.ProfileAsync(app);
        Assert.Equal("software", profile.TemplateKey);
        Assert.Equal("Jordan A. Renamed", profile.OwnerName);
        Assert.Equal("My own headline", profile.Hero.Headline);
        Assert.Equal(["My own paragraph."], profile.About.Paragraphs);
        Assert.Single(profile.Metrics);
        Assert.Equal("My role line", profile.Role); // the title did not change, so the role line stays
    }

    private static async Task<string> RowsAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return JsonSerializer.Serialize(new object[]
        {
            await db.Experiences.OrderBy(x => x.Id).ToListAsync(), await db.Skills.OrderBy(x => x.Id).ToListAsync(), await db.Projects.OrderBy(x => x.Id).ToListAsync(),
            await db.Certifications.OrderBy(x => x.Id).ToListAsync(), await db.BlogPosts.OrderBy(x => x.Id).ToListAsync(), await db.Talks.OrderBy(x => x.Id).ToListAsync(),
            await db.Testimonials.OrderBy(x => x.Id).ToListAsync(),
        }, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles });
    }

    // ---------------------------------------------------------------- completion, skip, re-run

    [Fact]
    public async Task AFreshSite_IsPending_AndAnExistingSite_IsNot_AndNothingIsBlockedEitherWay()
    {
        using var fresh = new AdminApp();
        var owner = await fresh.SignedInClientAsync();
        var dashboard = await owner.GetStringAsync("/admin");
        Assert.Contains("Finish setting up your site", dashboard);
        foreach (var path in new[] { "/admin/profile", "/admin/photo", "/admin/template", "/admin" })
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(path)).StatusCode); // the admin shell never traps an owner in the wizard

        using var existing = new AdminApp();
        await existing.NewClient().GetAsync("/"); // the profile row is created by the first read
        var other = await existing.SignedInClientAsync();
        using (var db = existing.CreateDbContext())
        {
            (await db.SiteProfiles.SingleAsync()).OnboardingCompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        var done = await other.GetStringAsync("/admin");
        Assert.DoesNotContain("Finish setting up", done);
        Assert.Contains("Run the guided setup again", done);
    }

    [Fact]
    public async Task Skipping_OnAFirstRun_KeepsThePlaceholderSite_LoadsTheDemoContent_AndCompletes()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        var before = WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app));

        var skip = await WizardFlow.PostAsync(owner, WizardFlow.Template + "?handler=Skip", new Dictionary<string, string>(), WizardFlow.Template);

        Assert.Equal("/admin", WizardFlow.Where(skip));
        var profile = await WizardFlow.ProfileAsync(app);
        Assert.NotNull(profile.OnboardingCompletedAt);
        Assert.Null(profile.OnboardingDraft);
        Assert.Equal(before, WizardFlow.PublicFingerprint(profile)); // the chosen (but unpublished) template was not applied
        Assert.Equal("software", profile.TemplateKey);
        Assert.Equal(SeedCount("experiences.json"), (await DemoSite.RowCountsAsync(app))["experiences"]);
        Assert.Contains("Alex Rivera", await app.NewClient().GetStringAsync("/"));
        Assert.DoesNotContain("Finish setting up", await owner.GetStringAsync("/admin"));
    }

    [Fact]
    public async Task Skipping_OnAFinishedSite_OnlyDiscardsTheDraft_AndNeverSeeds()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await PublishWithAsync(app, owner, "healthcare", StarterChoice.Blank); // finished, tables empty on purpose
        var completed = (await WizardFlow.ProfileAsync(app)).OnboardingCompletedAt;
        await WizardFlow.PickTemplateAsync(owner, "software");

        await WizardFlow.PostAsync(owner, WizardFlow.Template + "?handler=Skip", new Dictionary<string, string>(), WizardFlow.Template);

        var profile = await WizardFlow.ProfileAsync(app);
        Assert.Null(profile.OnboardingDraft);
        Assert.Equal(completed, profile.OnboardingCompletedAt);
        Assert.Equal("healthcare", profile.TemplateKey);
        Assert.Equal(0, (await DemoSite.RowCountsAsync(app))["experiences"]);
    }

    [Fact]
    public async Task StartingOver_DiscardsTheDraftAndItsPhoto_AndChangesNothingLive()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        await WizardFlow.SaveBasicsAsync(owner);
        await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Png, 32, 32), "a.png", "image/png");
        var before = WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app));
        Assert.Single(Directory.GetFiles(Path.Combine(app.UploadsPath, ".drafts")));

        var discard = await WizardFlow.PostAsync(owner, WizardFlow.Review + "?handler=Discard", new Dictionary<string, string>(), WizardFlow.Review);

        Assert.Equal("/admin/welcome", WizardFlow.Where(discard));
        Assert.Null((await WizardFlow.ProfileAsync(app)).OnboardingDraft);
        Assert.Equal(before, WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app)));
        Assert.Empty(Directory.GetFiles(Path.Combine(app.UploadsPath, ".drafts")));
    }

    // ---------------------------------------------------------------- publish: idempotent, atomic

    [Fact]
    public async Task PublishingTwice_AppliesOnce_AndTheReplayChangesNothing()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        await WizardFlow.SaveBasicsAsync(owner);
        var token = await AdminApp.GetTokenAsync(owner, WizardFlow.Review);
        var fields = WizardFlow.ReviewFields(content: StarterChoice.Sample);

        var first = await AdminApp.PostFormAsync(owner, WizardFlow.Review + "?handler=Publish", token, fields);
        var completed = (await WizardFlow.ProfileAsync(app)).OnboardingCompletedAt;
        var counts = await DemoSite.RowCountsAsync(app);
        var second = await AdminApp.PostFormAsync(owner, WizardFlow.Review + "?handler=Publish", token, fields); // the same request again

        Assert.Equal("/admin", WizardFlow.Where(first));
        Assert.Equal("/admin", WizardFlow.Where(second));
        Assert.Equal(completed, (await WizardFlow.ProfileAsync(app)).OnboardingCompletedAt);
        Assert.Equal(counts, await DemoSite.RowCountsAsync(app));
        Assert.True(counts["experiences"] > 0);
        Assert.Contains("already published", WebUtility.HtmlDecode(await owner.GetStringAsync("/admin")));
    }

    [Fact]
    public async Task APublishThatFailsPartWay_RollsEverythingBack_AndKeepsTheDraft()
    {
        // A template whose sample has two projects with the same slug: the unique index rejects the second after the profile was already updated.
        var dir = TemplateDirs.WithExtra("dupes", n => n["name"] = "Dupes", starter: n =>
        {
            var projects = n["projects"]!.AsArray();
            projects.Add(System.Text.Json.Nodes.JsonNode.Parse(projects[0]!.ToJsonString()));
        }, starterFrom: "healthcare");
        try
        {
            using var app = AdminApp.WithTemplates(dir);
            var owner = await WizardFlow.FirstRunClientAsync(app);
            await WizardFlow.PickTemplateAsync(owner, "dupes");
            await WizardFlow.SaveBasicsAsync(owner);
            await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Jpeg, 32, 32));
            await WizardFlow.SaveChoicesAsync(owner, WizardFlow.ReviewFields(content: StarterChoice.Sample));
            var before = WizardFlow.PublicFingerprint(await WizardFlow.ProfileAsync(app));
            var draftJson = (await WizardFlow.ProfileAsync(app)).OnboardingDraft;
            var version = app.Services.GetRequiredService<ISiteProfileProvider>().Version;

            var publish = await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields(content: StarterChoice.Sample));

            Assert.Equal(HttpStatusCode.InternalServerError, publish.StatusCode);
            var profile = await WizardFlow.ProfileAsync(app);
            Assert.Null(profile.OnboardingCompletedAt);
            Assert.Equal(before, WizardFlow.PublicFingerprint(profile));
            Assert.Contains("Jordan Alexander", profile.OnboardingDraft); // the draft is still there to retry
            Assert.Equal(OnboardingDraft.Parse(draftJson)!.PhotoDraft, OnboardingDraft.Parse(profile.OnboardingDraft)!.PhotoDraft);
            Assert.All((await DemoSite.RowCountsAsync(app)).Where(c => c.Key != "profiles"), c => Assert.Equal(0, c.Value));
            Assert.Empty(Directory.GetFiles(app.UploadsPath)); // the promoted copy of the photo was removed
            Assert.Equal(version, app.Services.GetRequiredService<ISiteProfileProvider>().Version);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task TheConcurrencyToken_StopsASecondPublishFromApplying()
    {
        using var app = new AdminApp();
        _ = app.Services;
        using (var seed = app.CreateDbContext())
        {
            seed.SiteProfiles.Add(new SiteProfile());
            await seed.SaveChangesAsync();
        }
        using var one = app.CreateDbContext();
        using var two = app.CreateDbContext();
        var a = await one.SiteProfiles.SingleAsync();
        var b = await two.SiteProfiles.SingleAsync();

        a.OnboardingCompletedAt = DateTimeOffset.UtcNow;
        await one.SaveChangesAsync();
        b.OnboardingCompletedAt = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => two.SaveChangesAsync());
    }
}
