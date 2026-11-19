using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>
/// The admin and cross-cutting paths for every template on an archetype other than Software and Healthcare (gallery, results and the showcase presets), through data only: wizard, preview,
/// switching, section settings, sitemap and structured data, export and import, and a blank wizard-finished site end to end. Driven by
/// <see cref="GalleryArchetypeTests.Keys"/>, <see cref="ResultsArchetypeTests.Keys"/> and <see cref="ShowcaseArchetypeTests.Keys"/>: a further template is covered by adding its key there.
/// </summary>
public class ArchetypeTemplateFlowTests
{
    public static readonly string[] Keys = [.. GalleryArchetypeTests.Keys, .. ResultsArchetypeTests.Keys, .. ShowcaseArchetypeTests.Keys];

    public static TheoryData<string> KeyData => new(Keys);

    /// <summary>What marks one work card in the archetype's own layout (the showcase presets use the shared work page, so its card).</summary>
    private static string Card(string key) =>
        Definition(key).Archetype == Archetypes.Results ? "data-results-card"
        : Definition(key).Archetype == Archetypes.Showcase ? "<article class=\"card group flex flex-col\""
        : "<article class=\"group relative";

    /// <summary>What marks a case-study page in the archetype's own layout.</summary>
    private static string DetailMarker(string key) =>
        Definition(key).Archetype == Archetypes.Showcase ? "<article class=\"container-page py-16 md:py-20\">" : "Next ";

    private static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "Templates");
    private static TemplateDefinition Definition(string key) => new TemplateCatalog(TemplatesDir).Get(key);
    /// <summary>The content tables only: the profile row is created by the first owner request, whatever it asks for.</summary>
    private static Dictionary<string, int> Content(Dictionary<string, int> counts) => counts.Where(c => c.Key != "profiles").ToDictionary(c => c.Key, c => c.Value);

    private static string Plain(string html) => WebUtility.HtmlDecode(html);

    /// <summary>A blank wizard-finished site in <paramref name="key"/> with one row in each table, the project featured.</summary>
    private static async Task<(AdminApp App, HttpClient Owner, HttpClient Visitor)> SiteAsync(string key)
    {
        var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, key);
        await ContentFlow.SeedRowsAsync(app, pairs: true);
        await owner.GetAsync("/admin");
        using (var db = app.CreateDbContext())
        {
            var p = await db.Projects.SingleAsync(x => x.Slug == "seed-project");
            p.Featured = true;
            p.HeroImage = "/img/projects/telemetry.svg";
            p.HeroImageAlt = "A seeded cover";
            p.Tech = ["Seeding"];
            p.Org = "Seedclient";
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        return (app, owner, app.NewClient());
    }

    // ---------------------------------------------------------------- wizard

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task TheWizardTemplateStep_OffersTheTemplate_WithItsThumbnail_AndSampleContentPublishesALiveSite(string key)
    {
        var template = Definition(key);
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var step = Plain(await owner.GetStringAsync($"{WizardFlow.Template}?show={key}"));
        Assert.Contains($">{template.Profession}</h3>", step);
        Assert.Contains($"href=\"/admin/welcome/thumb/{key}\"", step);
        Assert.Contains($"value=\"{key}\"", step);
        Assert.Contains(template.Name, step);
        var thumb = await owner.GetAsync($"/admin/welcome/thumb/{key}");
        Assert.Equal(HttpStatusCode.OK, thumb.StatusCode);
        Assert.Equal("image/png", thumb.Content.Headers.ContentType?.MediaType);

        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await WizardFlow.PickTemplateAsync(owner, key)));
        var basics = Plain(await owner.GetStringAsync(WizardFlow.Basics));
        Assert.Contains(template.HeroSuggestions.Title, basics);
        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields("Robin Vale", template.HeroSuggestions.Title, "robin@owner.example", "Oslo", template.HeroSuggestions.Summary))));
        var review = Plain(await owner.GetStringAsync(WizardFlow.Review));
        foreach (var accent in template.Theme.Accents) Assert.Contains(accent.Label, review);
        Assert.Contains("Sample", review, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.Redirect, (await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields("https://robin.example", content: StarterChoice.Sample))).StatusCode);

        var starter = new TemplateCatalog(TemplatesDir).GetStarter(key)!;
        var visitor = app.NewClient();
        var home = await visitor.GetStringAsync("/");
        Assert.Contains($"data-template=\"{template.Theme.Default}\"", home);
        Assert.Contains("Robin Vale", home); // the owner's name, not the sample persona's
        Assert.DoesNotContain(starter.Profile.OwnerName, home);
        foreach (var p in starter.Projects.Where(p => p.Featured)) Assert.Contains(WebUtility.HtmlEncode(p.Title), home);
        foreach (var p in starter.Projects) Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync($"/projects/{p.Slug}")).StatusCode);
        Assert.Contains(starter.Projects[0].Slug, await visitor.GetStringAsync("/sitemap.xml"));
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/cv")).StatusCode); // the PDF of a project with impact lines needs fonts a test host lacks (known gap), so /cv.pdf is checked on the blank site
    }

    // ---------------------------------------------------------------- preview

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task ThePreview_ShowsTheGalleryLayoutToTheOwnerOnly_AndSavesNothing(string key)
    {
        var template = Definition(key);
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var (project, _) = DemoSite.FirstSlugs(app);
        var owner = await app.SignedInClientAsync();
        var visitor = app.NewClient();

        foreach (var path in new[] { "/", "/projects", $"/projects/{project}" })
        {
            var preview = await owner.GetAsync($"{path}?preview={key}");
            var html = await preview.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.Contains($"data-template=\"{template.Theme.Default}\"", html);
            Assert.Contains("Previewing the", html);
            Assert.Contains("noindex", html);
            Assert.Contains("no-store", preview.Headers.CacheControl?.ToString() ?? "");
            Assert.Contains("Telemetry", html); // the same rows, drawn by the gallery layout
            if (path != "/") Assert.Contains(path == "/projects" ? Card(key) : DetailMarker(key), html);
            // The showcase marker is also in the other archetypes' case-study partials, so a showcase key must also lack their "Next" navigation and result tiles.
            if (path != "/" && template.Archetype == Archetypes.Showcase) { Assert.DoesNotContain("Next ", html); Assert.DoesNotContain("data-results", html); }

            // A visitor asking for the same thing gets the saved (software) page, unchanged.
            var plain = await (await visitor.GetAsync($"{path}?preview={key}")).Content.ReadAsStringAsync();
            Assert.Equal(DemoSite.Normalise(await visitor.GetStringAsync(path)), DemoSite.Normalise(plain));
            Assert.DoesNotContain("data-template", plain);
        }
        using var db = app.CreateDbContext();
        Assert.NotEqual(key, await db.SiteProfiles.Select(p => p.TemplateKey).SingleAsync());
    }

    // ---------------------------------------------------------------- switching

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task SwitchingToTheTemplateAndBack_DeletesNothing_AndRestoresTheSoftwarePagesExactly(string key)
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var owner = await app.SignedInClientAsync();
        var visitor = app.NewClient();
        var (project, _) = DemoSite.FirstSlugs(app);
        var counts = Content(await DemoSite.RowCountsAsync(app));
        var before = new Dictionary<string, string>();
        foreach (var path in new[] { "/", "/projects", $"/projects/{project}", "/experience", "/sitemap.xml" }) before[path] = DemoSite.Normalise(await visitor.GetStringAsync(path));

        Assert.Equal(HttpStatusCode.Redirect, (await DemoSite.SwitchTemplateAsync(owner, key)).StatusCode);

        Assert.Equal(counts, Content(await DemoSite.RowCountsAsync(app)));
        var template = Definition(key);
        var projects = await visitor.GetStringAsync("/projects");
        Assert.Contains($"data-template=\"{template.Theme.Default}\"", projects);
        Assert.Contains(Card(key), projects);
        Assert.Contains("Telemetry", projects);
        Assert.Contains(template.Copy.Experience.Heading, Plain(await visitor.GetStringAsync("/experience")));
        Assert.Contains($"/projects/{project}", await visitor.GetStringAsync("/sitemap.xml"));
        Assert.Contains("Contoso Cloud", await visitor.GetStringAsync("/cv")); // content the template shows differently is still there

        Assert.Equal(HttpStatusCode.Redirect, (await DemoSite.SwitchTemplateAsync(owner, "software")).StatusCode);
        Assert.Equal(counts, Content(await DemoSite.RowCountsAsync(app)));
        foreach (var (path, html) in before) Assert.Equal(html, DemoSite.Normalise(await visitor.GetStringAsync(path)));
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task ASwitch_ThatWouldEmptyTheMenu_KeepsWhatIsOn_AndIsRefusedWhenNothingIs(string key)
    {
        var template = Definition(key);
        var off = template.Sections.First(s => !s.Enabled);   // a section the template shows off by default
        var (app, owner, visitor) = await SiteAsync("software");
        using var _app = app;
        Assert.Equal(HttpStatusCode.Redirect, (await SectionsFlow.SaveAsync(owner, r => r.Select(x => x with { Enabled = x.Key == off.Key }).ToList())).StatusCode);

        Assert.Equal(HttpStatusCode.Redirect, (await DemoSite.SwitchTemplateAsync(owner, key)).StatusCode);

        Assert.Contains($"Switched to the {template.Name} template", Plain(await owner.GetStringAsync("/admin/template")));
        Assert.Equal(key, (await ProfileFlow.ProfileAsync(app)).TemplateKey);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(SectionRegistry.PathFor(off.Key)!)).StatusCode); // the menu was not emptied
        Assert.Equal(true, (await SectionsFlow.StoredAsync(app)).Single(s => s.Key == off.Key).Shown);

        // Settings written by hand that hide everything: the switch is refused, in the target's words, and changes nothing.
        using (var db = app.CreateDbContext())
        {
            var p = await db.SiteProfiles.SingleAsync();
            p.TemplateKey = "software";
            p.Sections = SectionRegistry.Keys.Select(k => new SectionSetting { Key = k, Enabled = false, Shown = false }).ToList();
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        Assert.Equal(HttpStatusCode.Redirect, (await DemoSite.SwitchTemplateAsync(owner, key)).StatusCode);
        var itemWord = Plain(await owner.GetStringAsync("/admin/template"));
        Assert.Contains($"Switching to {template.Name} would leave no", itemWord);
        Assert.Equal("software", (await ProfileFlow.ProfileAsync(app)).TemplateKey);
    }

    // ---------------------------------------------------------------- section settings, sitemap and structured data

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task SectionSettings_DriveTheMenuSitemapAndStructuredData_AndHidingNeverDeletes(string key)
    {
        var template = Definition(key);
        var (app, owner, visitor) = await SiteAsync(key);
        using var _app = app;
        var rows = await SectionsFlow.RowsAsync(owner);
        Assert.Equal(template.Sections.Select(s => s.Key), rows.Select(r => r.Key)); // the page lists the template's sections in the template's order
        var off = template.Sections.Where(s => !s.Enabled).Select(s => s.Key).ToList();
        Assert.Equal(off.OrderBy(x => x), rows.Where(r => !r.Enabled).Select(r => r.Key).OrderBy(x => x));

        var sitemap = SectionsFlow.SitemapPaths(await visitor.GetStringAsync("/sitemap.xml"));
        Assert.Contains("/projects", sitemap);
        Assert.Contains("/projects/seed-project", sitemap);
        foreach (var k in off) Assert.DoesNotContain(SectionRegistry.PathFor(k), sitemap);
        var detail = await visitor.GetStringAsync("/projects/seed-project");
        Assert.Contains("\"@type\":\"CreativeWork\"", detail);
        // The results layout's client strip is made of the work's client names, so it follows the work section.
        var hasStrip = template.Archetype == Archetypes.Results;
        Assert.Equal(hasStrip, (await visitor.GetStringAsync("/")).Contains("id=\"clients\""));
        var counts = await ContentFlow.CountAsync(app);

        // Hide the work section, rename another, move one to the front and switch on what the template leaves off.
        var save = await SectionsFlow.SaveAsync(owner, r => SectionsFlow.MoveTo("skills", 0)(SectionsFlow.Set("about", label: "Reach me")(SectionsFlow.Set("projects", false)(r.Select(x => off.Contains(x.Key) ? x with { Enabled = true } : x).ToList()))));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/projects/seed-project")).StatusCode);
        var home = await visitor.GetStringAsync("/");
        Assert.DoesNotContain("id=\"work\"", home);
        Assert.DoesNotContain("href=\"/projects\"", home);
        Assert.DoesNotContain("id=\"clients\"", home);   // no work section, so no client names
        Assert.DoesNotContain("Seedclient", home);
        Assert.Contains("Reach me", home);
        var nav = SectionsFlow.NavOf(home).Split(',').Select(x => x.Split('=')[0]).ToList();
        Assert.Equal("/skills", nav[0]);
        foreach (var k in off) Assert.Contains(SectionRegistry.PathFor(k), nav);
        var paths = SectionsFlow.SitemapPaths(await visitor.GetStringAsync("/sitemap.xml"));
        Assert.DoesNotContain("/projects", paths);
        Assert.DoesNotContain("/projects/seed-project", paths);
        foreach (var k in off) Assert.Contains(SectionRegistry.PathFor(k), paths);
        Assert.DoesNotContain("seed-project", await visitor.GetStringAsync("/cv"));
        Assert.Equal(counts, await ContentFlow.CountAsync(app)); // hiding deleted nothing

        // Back on: the case study and its structured data return.
        await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("projects", true));
        Assert.Contains("\"@type\":\"CreativeWork\"", await visitor.GetStringAsync("/projects/seed-project"));
        Assert.Equal(hasStrip, (await visitor.GetStringAsync("/")).Contains("id=\"clients\""));
        Assert.Contains("/projects/seed-project", SectionsFlow.SitemapPaths(await visitor.GetStringAsync("/sitemap.xml")));
    }

    // ---------------------------------------------------------------- export and import

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task AnExportFromTheTemplate_ImportsIntoTheOthers_AndTheirsImportsIntoIt_WithoutChangingTheTemplate(string key)
    {
        var (source, sourceOwner, _) = await SiteAsync(key);
        using var _s = source;
        var file = await TransferFlow.ExportTextAsync(sourceOwner);
        Assert.Contains("\"seed-project\"", file);
        Assert.Contains($"\"templateKey\": \"{key}\"", file);

        foreach (var other in new[] { "software", "healthcare" }.Concat(Keys.Where(k => k != key)))
        {
            using var target = new AdminApp();
            var owner = await ContentFlow.BlankSiteAsync(target, other);
            var report = await TransferFlow.ImportAsync(owner, file);
            Assert.Contains("Seed Project", report);
            Assert.Equal(other, (await ProfileFlow.ProfileAsync(target)).TemplateKey); // content only: the target keeps its own template
            var projects = await target.NewClient().GetStringAsync("/projects");
            Assert.Contains("Seed Project", projects);
            if (Keys.Contains(other)) Assert.Contains(Card(other), projects); // drawn by that template's own layout
            else { Assert.DoesNotContain("data-results-card", projects); Assert.DoesNotContain("<article class=\"group relative", projects); }
            Assert.Equal(HttpStatusCode.OK, (await target.NewClient().GetAsync("/projects/seed-project")).StatusCode);
        }

        // And back the other way: a software export lands in the template's grid.
        var (software, softwareOwner, _) = await SiteAsync("software");
        using var _w = software;
        var softwareFile = await TransferFlow.ExportTextAsync(softwareOwner);
        using var gallery = new AdminApp();
        var galleryOwner = await ContentFlow.BlankSiteAsync(gallery, key);
        Assert.Contains("Seed Project", await TransferFlow.ImportAsync(galleryOwner, softwareFile));
        Assert.Equal(key, (await ProfileFlow.ProfileAsync(gallery)).TemplateKey);
        var grid = await gallery.NewClient().GetStringAsync("/projects");
        Assert.Contains(Card(key), grid);
        Assert.Contains("Seed Project", grid);
        Assert.Contains("Second Project", grid);
        // Importing the profile too switches to the file's template, which is installed; a section the template lacks would stay inert.
        Assert.Contains("seed-project", await gallery.NewClient().GetStringAsync("/sitemap.xml"));
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task AHandCraftedExport_WithSectionKeysTheTargetLacks_LeavesThemInert(string key)
    {
        // A copy of the template that has no journal and no screenings: those keys, and one nothing knows, are "sections the target lacks".
        var dir = TemplateDirs.WithExtra("lean-layout", node =>
        {
            node["name"] = "Lean Layout";
            var sections = node["sections"]!.AsArray();
            foreach (var drop in sections.Where(s => s!["key"]!.GetValue<string>() is "blog" or "talks").ToList()) sections.Remove(drop);
        }, baseTemplate: key, starterFrom: key);
        try
        {
            using var app = AdminApp.WithTemplates(dir);
            var owner = await ContentFlow.BlankSiteAsync(app, "lean-layout");
            var visitor = app.NewClient();
            var file = ImportHarness.File(
                profile: new { sections = new object[] { new { key = "blog", label = "Dispatches", shown = true }, new { key = "talks", shown = true }, new { key = "photo-wall", shown = true }, new { key = "projects", label = "Folios" } } },
                content: new { skills = new object[] { new { name = "Imported craft", group = "Photography" } } });

            var preview = Plain(await (await TransferFlow.PreviewAsync(owner, file, applyProfile: true)).Content.ReadAsStringAsync());
            Assert.Contains("for sections that the template does not have", preview);
            Assert.Contains("blog, talks, photo-wall", preview);

            var report = await TransferFlow.ImportAsync(owner, file, applyProfile: true);
            Assert.Contains("Applied: ", report);
            var stored = (await ProfileFlow.ProfileAsync(app)).Sections;
            Assert.DoesNotContain(stored, s => s.Key is "blog" or "talks" or "photo-wall"); // never stored, so never switchable on later
            Assert.Contains(stored, s => s.Key == "projects" && s.Label == "Folios");       // the section the template does list was applied
            Assert.Equal("lean-layout", (await ProfileFlow.ProfileAsync(app)).TemplateKey);

            var home = await visitor.GetStringAsync("/");
            Assert.Contains("Folios", home);
            Assert.DoesNotContain("Dispatches", home);
            Assert.DoesNotContain("href=\"/blog\"", home);
            Assert.DoesNotContain("href=\"/talks\"", home);
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/blog")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/talks")).StatusCode);
            Assert.DoesNotContain("/blog", SectionsFlow.SitemapPaths(await visitor.GetStringAsync("/sitemap.xml")));
            Assert.DoesNotContain("photo-wall", await visitor.GetStringAsync("/sitemap.xml"));
            Assert.Contains("Imported craft", await visitor.GetStringAsync("/skills")); // the rest of the file imported normally
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---------------------------------------------------------------- a blank wizard-finished site, end to end

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task ABlankWizardFinishedSite_WorksEndToEnd_InTheTemplate(string key)
    {
        var template = Definition(key);
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, key);
        await owner.GetAsync("/admin");
        var visitor = app.NewClient();

        // The public site, empty: every page answers, with the template's own empty states.
        var home = await visitor.GetStringAsync("/");
        Assert.Contains($"data-template=\"{template.Theme.Default}\"", home);
        Assert.Contains("Casey Morgan", home);
        Assert.Contains(TemplateView.Text(template.Copy.Home.WorkEmptyTitle).ToString()!, home);
        foreach (var path in new[] { "/projects", "/about", "/experience", "/skills", "/certifications", "/contact", "/cv", "/sitemap.xml", "/robots.txt" })
            Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/cv.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/og.png")).StatusCode);

        // The admin speaks in the template's words.
        var editor = Plain(await owner.GetStringAsync(ContentFlow.Projects + "/edit"));
        foreach (var field in new[] { "heroImage", "problem", "approach" })
        {
            var label = template.Editor.Projects.Fields.TryGetValue(field, out var f) ? f.Label : null;
            if (label is not null) Assert.Contains(label, editor);
        }
        Assert.Contains(template.Editor.Sections.Fields["heading"].Label!, Plain(await owner.GetStringAsync(SectionsFlow.Path)));
        Assert.Contains(template.Editor.Transfer.Fields["heading"].Label!, Plain(await owner.GetStringAsync(TransferFlow.Page)));
        Assert.Contains(template.Editor.Hero.Fields["heading"].Label!, Plain(await owner.GetStringAsync(ProfileFlow.Hero)));
        foreach (var path in new[] { "/admin", "/admin/template", ProfileFlow.Identity, ProfileFlow.Metrics, ProfileFlow.About, ContentFlow.Experience, ContentFlow.Skills, ContentFlow.Projects, ContentFlow.Certifications, ContentFlow.Talks, ContentFlow.Testimonials, ContentFlow.Blog })
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(path)).StatusCode);

        // Add a case study the way the owner would, and it appears in the gallery.
        var stamp = await ContentFlow.StampAsync(owner, ContentFlow.Projects + "/edit");
        var created = await ContentFlow.PostAsync(owner, ContentFlow.Projects + "/edit", ContentFlow.Fields(
            ("Form.Title", "First case study"), ("Form.Summary", "What it was."), ("Form.Category", "Research"), ("Form.Featured", "true"), ("Form.Stamp", stamp)));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var grid = await visitor.GetStringAsync("/projects");
        Assert.Contains("First case study", grid);
        Assert.Contains(Card(key), grid);
        Assert.Contains("First case study", await visitor.GetStringAsync("/"));
        Assert.Contains("/projects/first-case-study", await visitor.GetStringAsync("/sitemap.xml"));
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/projects/first-case-study")).StatusCode);
    }
}
