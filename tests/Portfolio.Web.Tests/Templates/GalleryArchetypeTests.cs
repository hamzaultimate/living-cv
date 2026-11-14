using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>
/// The gallery layout archetype and every template that uses it. The tests are driven by <see cref="Keys"/>: a further gallery
/// template (Photography) is covered by adding its key to that list.
/// </summary>
public class GalleryArchetypeTests
{
    /// <summary>Every shipped template whose archetype is gallery.</summary>
    public static readonly string[] Keys = ["design-ux", "photography-film"];

    public static TheoryData<string> KeyData => new(Keys);

    public static TheoryData<string, string> KeyAndContent
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var key in Keys) foreach (var content in new[] { "blank", "sample", "full" }) data.Add(key, content);
            return data;
        }
    }

    private static string Wwwroot => Path.Combine(TemplateDirs.Web, "wwwroot");
    private static string Layouts => Path.Combine(TemplateDirs.Web, "Pages", "Shared", "Layouts");
    private static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "Templates");

    private static TemplateDefinition Definition(string key) => new TemplateCatalog(TemplatesDir).Get(key);

    private static async Task<string> GetAsync(HttpClient client, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.GetAsync(path);
        Assert.True(response.StatusCode == expected, $"{path} returned {(int)response.StatusCode}, expected {(int)expected}");
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>The page's own content: what the layout partials rendered, without the shared header, footer and scripts.</summary>
    private static string MainOf(string html)
    {
        var start = html.IndexOf("<main", StringComparison.Ordinal);
        var end = html.IndexOf("</main>", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "page has no <main> landmark");
        return html[start..end];
    }

    /// <summary>Rules every gallery page keeps: alt text on every picture, one h1, headings that never skip a level, no players, no inline script, no handlers.</summary>
    private static void AssertWellFormedGalleryPage(string path, string html)
    {
        var main = MainOf(html);
        Assert.DoesNotMatch(@"<img\b(?![^>]*\balt=""[^""]+"")", main);
        Assert.Single(Regex.Matches(main, "<h1[ >]"));
        var levels = Regex.Matches(main, @"<h([1-6])[ >]").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        for (var i = 1; i < levels.Count; i++) Assert.True(levels[i] <= levels[i - 1] + 1, $"{path}: heading level jumps from h{levels[i - 1]} to h{levels[i]}");
        Assert.DoesNotMatch(@"<(iframe|video|audio|object|embed)\b", main);
        Assert.DoesNotMatch(@"<script\b(?![^>]*type=""application/ld)", main);
        Assert.DoesNotMatch(@"\son[a-z]+\s*=", main);
        Assert.DoesNotMatch(@"https?://[^""'\s]*\.(png|jpe?g|webp|gif|svg)", main); // pictures are bundled or uploaded, never hot-linked
        // Every link in the work grid has a name a screen reader can read.
        foreach (Match a in Regex.Matches(main, @"<a\b[^>]*>(.*?)</a>", RegexOptions.Singleline))
            Assert.False(string.IsNullOrWhiteSpace(Regex.Replace(a.Groups[1].Value, "<[^>]+>", "")), $"{path}: a link has no text: {a.Value}");
    }

    // ---------------------------------------------------------------- the manifest and its files

    [Theory]
    [MemberData(nameof(KeyData))]
    public void ATemplateOnTheGalleryArchetype_HasItsLayoutThemeStarterAndThumbnail(string key)
    {
        var template = Definition(key);

        Assert.Equal(key, template.Key);
        Assert.Equal(Archetypes.Gallery, template.Archetype);
        Assert.Contains(Archetypes.Gallery, Archetypes.Implemented);
        foreach (var partial in new[] { "_Home", "_Projects", "_ProjectDetail", "_WorkCard" })
            Assert.True(File.Exists(Path.Combine(Layouts, "gallery", partial + ".cshtml")), $"Layouts/gallery/{partial}.cshtml is missing");
        Assert.True(File.Exists(Path.Combine(TemplateDirs.Web, "Styles", "themes", template.Theme.Default + ".css")));
        Assert.NotNull(new TemplateCatalog(TemplatesDir).GetStarter(key));
        Assert.NotNull(new TemplateCatalog(TemplatesDir).GetPreviewImagePath(key));
        Assert.True(template.Theme.Accents.Count >= 3);
        Assert.Contains(template.Sections, s => s.Key == SectionRegistry.Projects && !string.IsNullOrWhiteSpace(s.ItemLabel));
    }

    [Fact]
    public void EveryImplementedArchetype_HasAHomePartial_AndOnlyTheGalleryTakesOverTheWorkPages()
    {
        foreach (var archetype in Archetypes.Implemented)
            Assert.True(File.Exists(Path.Combine(Layouts, archetype, "_Home.cshtml")), $"{archetype} has no home partial");

        // The other archetypes keep the shared work pages, so their output is the shared markup and nothing else.
        foreach (var archetype in new[] { Archetypes.Showcase, Archetypes.Credentials })
        {
            Assert.False(File.Exists(Path.Combine(Layouts, archetype, "_Projects.cshtml")));
            Assert.False(File.Exists(Path.Combine(Layouts, archetype, "_ProjectDetail.cshtml")));
        }
        Assert.True(File.Exists(Path.Combine(Layouts, "gallery", "_Projects.cshtml")) && File.Exists(Path.Combine(Layouts, "gallery", "_ProjectDetail.cshtml")));
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void TheStarterContent_IsFictional_UsesOnlyBundledPictures_AndDescribesEveryOne(string key)
    {
        var starter = new TemplateCatalog(TemplatesDir).GetStarter(key)!;

        Assert.True(starter.Fictional);
        Assert.Contains("fictional", starter.Profile.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("example.org", starter.Profile.Email);
        Assert.NotEmpty(starter.Projects);
        Assert.True(starter.Projects.Count(p => p.Featured) >= 3, "the home grid needs at least three featured projects");
        Assert.Contains(starter.Projects, p => !p.Featured);
        Assert.Contains(starter.Projects, p => p.Screenshots.Count > 0);
        Assert.All(starter.Experiences, e => Assert.Contains("fictional", e.Company, StringComparison.OrdinalIgnoreCase));
        Assert.All(starter.Projects, p => Assert.Contains("(sample)", p.Title));
        var images = new List<string>();
        foreach (var p in starter.Projects)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.HeroImage), $"{p.Slug} has no cover");
            Assert.False(string.IsNullOrWhiteSpace(p.HeroImageAlt), $"{p.Slug} cover has no description");
            Assert.False(Media.IsVideo(p.HeroImage));
            images.Add(p.HeroImage!);
            foreach (var shot in p.Screenshots)
            {
                Assert.False(string.IsNullOrWhiteSpace(shot.Caption), $"{p.Slug} has a picture without a description");
                images.Add(shot.Url);
            }
            Assert.All(p.Links, l => Assert.StartsWith("https://example.org/", l.Url)); // links point at a reserved example domain
        }
        foreach (var url in images)
        {
            Assert.StartsWith("/img/", url);
            var file = Path.Combine(Wwwroot, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(file), $"{url} is not bundled under wwwroot");
            Assert.True(new FileInfo(file).Length < 60 * 1024, $"{url} is larger than a starter placeholder should be");
            Assert.DoesNotContain("<script", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(images.Count, images.Distinct().Count());
    }

    [Fact]
    public void TheGalleryPartials_ContainNoScriptNoHandlersNoPlayersAndNoTemplateKeyChecks()
    {
        var files = Directory.EnumerateFiles(Path.Combine(Layouts, "gallery"), "*.cshtml").ToList();
        Assert.True(files.Count >= 4);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var without = Regex.Replace(text, @"<script type=""application/ld\+json"">@Html\.Raw\(Model\.\w+\)</script>", "");
            Assert.DoesNotMatch(@"<script\b", without);
            Assert.DoesNotMatch(@"\son[a-z]+\s*=", text);
            Assert.DoesNotMatch(@"<(iframe|video|audio|object|embed)\b", text);
            Assert.DoesNotMatch(@"(?i)templateKey|\.Key\s*==|Definition\.Key", text);
        }
        foreach (var file in new[] { "Projects.cshtml", "ProjectDetail.cshtml", "Index.cshtml" }.Select(f => Path.Combine(TemplateDirs.Web, "Pages", f)))
            Assert.DoesNotMatch(@"(?i)templateKey|\bKey\s*==\s*""", File.ReadAllText(file));
    }

    // ---------------------------------------------------------------- rendering: blank, sample and full content

    private static async Task<AdminApp> SiteAsync(string key, string content)
    {
        var app = new AdminApp();
        await DemoSite.ApplyStarterAsync(app, key);
        using (var db = app.CreateDbContext())
        {
            if (content == "blank")
            {
                db.Projects.RemoveRange(db.Projects);
                db.Experiences.RemoveRange(db.Experiences);
                db.Skills.RemoveRange(db.Skills);
                db.Certifications.RemoveRange(db.Certifications);
                db.Testimonials.RemoveRange(db.Testimonials);
            }
            else if (content == "full")
            {
                var order = 100;
                db.Projects.AddRange(
                    // A cover that is a video file: the gallery shows the text tile and never a player.
                    new Project { Title = "Film \"reel\" <b>& more", Slug = "film-reel", Summary = "A reel. <script>alert(1)</script>", Category = "Motion", HeroImage = "/media/reel.mp4", Featured = true, SortOrder = order++,
                        Screenshots = [new ProjectImage { Url = "/media/clip.webm", Caption = "A clip" }, new ProjectImage { Url = "/img/projects/telemetry-1.svg", Caption = "A still" }, new ProjectImage { Url = "/img/projects/telemetry-2.svg" }],
                        Links = [new Link { Label = "Watch the film", Url = "https://example.org/film" }] },
                    new Project { Title = "No picture at all", Slug = "no-picture", Summary = "Text only.", Featured = true, SortOrder = order++ },
                    // A picture whose address is not safe to link, and one with a blank caption.
                    new Project { Title = "Odd addresses", Slug = "odd", Summary = "Odd pictures.", HeroImage = "/img/projects/telemetry.svg", HeroImageAlt = "   ", SortOrder = order++,
                        Screenshots = [new ProjectImage { Url = "//evil.example/x.mp4", Caption = "Evil" }, new ProjectImage { Url = "/img/projects/telemetry-1.svg", Caption = "  " }] },
                    new Project { Title = "Uncategorised", Slug = "plain", Summary = "No category, no tools.", HeroImage = "/img/projects/telemetry.svg", SortOrder = order++ });
                for (var i = 0; i < 9; i++)
                    db.Projects.Add(new Project { Title = $"Extra work {i}", Slug = $"extra-{i}", Summary = "More work.", Category = i % 2 == 0 ? "Even" : "Odd", HeroImage = "/img/projects/ledger.svg", Featured = i < 2, SortOrder = order++, Tech = ["a", "b", "c", "d"] });
            }
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        return app;
    }

    public static TheoryData<string, string> KeyAndPopulated
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var key in Keys) foreach (var content in new[] { "sample", "full" }) data.Add(key, content);
            return data;
        }
    }

    private static string ItemOf(TemplateDefinition t) => t.Sections.Single(s => s.Key == "projects").ItemLabel!;

    [Theory]
    [MemberData(nameof(KeyAndContent))]
    public async Task EveryPublicPage_AnswersInTheGalleryLayout_ForBlankSampleAndFullContent(string key, string content)
    {
        using var app = await SiteAsync(key, content);
        var client = app.NewClient();
        var home = await GetAsync(client, "/");

        Assert.Contains($"data-template=\"{Definition(key).Theme.Default}\"", home);
        AssertWellFormedGalleryPage("/", home);
        AssertWellFormedGalleryPage("/projects", await GetAsync(client, "/projects"));
        foreach (var path in new[] { "/about", "/experience", "/skills", "/certifications", "/contact", "/cv" })
        {
            var page = await GetAsync(client, path);
            Assert.Single(Regex.Matches(MainOf(page), "<h1[ >]"));
        }
        await GetAsync(client, "/talks", HttpStatusCode.NotFound); // off by default, and a template-off section is never shown
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task ABlankSite_ShowsTheTemplatesEmptyStatesAndItsNotFoundWords(string key)
    {
        using var app = await SiteAsync(key, "blank");
        var template = Definition(key);
        var client = app.NewClient();

        var home = await GetAsync(client, "/");
        var projects = await GetAsync(client, "/projects");
        Assert.Contains(Escaped(template.Copy.Home.WorkEmptyTitle), home);
        Assert.Contains(Escaped(template.Copy.Projects.EmptyTitle), projects);
        Assert.DoesNotContain("data-project-grid", projects);
        Assert.DoesNotContain("<article class=\"group", home);
        var missing = await GetAsync(client, "/projects/nothing-here", HttpStatusCode.NotFound);
        Assert.Contains(Escaped(ItemOf(template) + " not found"), missing);
    }

    [Theory]
    [MemberData(nameof(KeyAndPopulated))]
    public async Task TheWorkGrid_ShowsEveryProjectInTheLayoutsCard_AndTheHomeGridOnlyTheFeaturedOnes(string key, string content)
    {
        using var app = await SiteAsync(key, content);
        var template = Definition(key);
        var client = app.NewClient();
        var home = await GetAsync(client, "/");
        var projects = await GetAsync(client, "/projects");
        using var db = app.CreateDbContext();
        var rows = await db.Projects.OrderBy(p => p.SortOrder).ToListAsync();

        Assert.Equal(rows.Count, Regex.Matches(projects, "<article class=\"group relative").Count);
        foreach (var p in rows) Assert.Contains($"href=\"/projects/{p.Slug}\"", projects);
        Assert.Equal(rows.Count(p => p.Featured), Regex.Matches(home, "<article class=\"group relative").Count);
        Assert.Contains("data-project-grid", projects);
        var categories = rows.Select(p => p.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (categories.Count > 1) foreach (var c in categories) Assert.Contains($"data-filter=\"{WebUtility.HtmlEncode(c)}\"", projects);
        Assert.DoesNotContain("data-lightbox", projects); // cards open the case study; the lightbox is for the pictures inside it
        Assert.Contains("id=\"work\"", home);
        Assert.Contains("id=\"skills\"", home);
        // The "featured" mark belongs to the work page: the home grid is all featured work already.
        const string badge = "text-[10px] uppercase tracking-wider text-accent";
        Assert.Contains(badge, projects);
        Assert.DoesNotContain(badge, home);
        Assert.Contains(Escaped(template.Copy.Projects.ReadItemFor(ItemOf(template))), home);
    }

    [Theory]
    [MemberData(nameof(KeyAndPopulated))]
    public async Task EveryCaseStudy_HasItsHeadingsGalleryAndStructuredData(string key, string content)
    {
        using var app = await SiteAsync(key, content);
        var template = Definition(key);
        var copy = template.Copy;
        var client = app.NewClient();
        using var db = app.CreateDbContext();

        foreach (var p in await db.Projects.OrderBy(p => p.SortOrder).ToListAsync())
        {
            var detail = await GetAsync(client, $"/projects/{p.Slug}");
            AssertWellFormedGalleryPage($"/projects/{p.Slug}", detail);
            Assert.Contains("\"@type\":\"CreativeWork\"", detail);
            Assert.Contains("\"@type\":\"BreadcrumbList\"", detail);
            var shown = p.Screenshots.Where(s => !Media.IsVideo(s.Url)).ToList();
            Assert.Equal(shown.Count, Regex.Matches(detail, "data-lightbox-item").Count);
            if (shown.Count > 0) Assert.Contains($"aria-label=\"{copy.Projects.GalleryLabel}\"", detail);
            foreach (var s in shown) Assert.Contains($"alt=\"{WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(s.Caption) ? copy.Projects.AltFor(p.Title) : s.Caption)}\"", detail);
            Assert.Contains(string.IsNullOrWhiteSpace(p.Problem) && string.IsNullOrWhiteSpace(p.Approach) ? Escaped(copy.Projects.NoWriteup) : Escaped(copy.Projects.ProblemHeading), detail);
            var next = Escaped(copy.Projects.NextItemFor(ItemOf(template)));
            Assert.Contains($"aria-label=\"{next}\"", detail); // the navigation is named by the template's own words
            Assert.Contains(next, detail);
        }
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task HostileTextIsEncoded_VideoIsALinkNeverAPlayer_AndAProjectWithNothingGetsAnInitialsTile(string key)
    {
        using var app = await SiteAsync(key, "full");
        var copy = Definition(key).Copy.Projects;
        var client = app.NewClient();
        var projects = await GetAsync(client, "/projects");
        var home = await GetAsync(client, "/");
        var film = await GetAsync(client, "/projects/film-reel");

        Assert.DoesNotContain("<script>alert(1)", projects);
        Assert.Contains("Film &quot;reel&quot; &lt;b&gt;&amp; more", projects);

        // A video file as the cover: not an image, not a player, but a plain link, on the card and on the page.
        Assert.DoesNotContain("src=\"/media/", projects + home + film);
        Assert.DoesNotMatch(@"<(video|iframe|source)\b", projects + home + film);
        Assert.Matches($@"<a href=""/media/reel\.mp4"" target=""_blank"" rel=""noopener""[^>]*>{Regex.Escape(Escaped(copy.WatchVideoText()))}</a>", projects);
        Assert.Matches($@"<a href=""/media/reel\.mp4"" target=""_blank"" rel=""noopener""[^>]*>{Regex.Escape(Escaped(copy.WatchVideoText()))}</a>", film);
        // A video picture with a caption is linked by its caption; the entry's own links still render beside it.
        Assert.Matches(@"<a href=""/media/clip\.webm"" target=""_blank"" rel=""noopener""[^>]*>A clip</a>", film);
        Assert.Contains("<a href=\"https://example.org/film\" target=\"_blank\" rel=\"noopener\"", film);
        Assert.Contains("Watch the film →", film);
        Assert.Equal(2, Regex.Matches(film, "data-lightbox-item").Count); // the two image pictures only

        // An address that is not a site path or an http(s) address is never put in a link.
        var odd = await GetAsync(client, "/projects/odd");
        Assert.DoesNotContain("evil.example", odd);
        Assert.Matches(@"alt=""" + Regex.Escape(WebUtility.HtmlEncode(copy.AltFor("Odd addresses"))) + @"""", odd); // blank caption and blank HeroImageAlt both fall back

        // A project with nothing to show gets a decorative initials tile that is hidden from screen readers.
        Assert.Matches(@"<div class=""flex h-full w-full items-end p-5"" aria-hidden=""true"">\s*<span[^>]*>NP</span>", projects);
        Assert.DoesNotMatch(@"<div class=""flex h-full w-full items-end p-5""(?! aria-hidden=""true"")", projects + home);
    }

    private static string Escaped(string text) => TemplateView.Text(text).ToString()!;

    // ---------------------------------------------------------------- labels come from the manifest

    /// <summary>A copy of the template whose every wording is a unique made-up word, so a hardcoded word cannot hide behind a lucky match.</summary>
    private static string UniqueWordsDir(string baseKey, string newKey)
    {
        return TemplateDirs.WithExtra(newKey, node =>
        {
            node["name"] = "Zork Gallery";
            foreach (var (group, value) in node["copy"]!.AsObject().ToList())
                foreach (var (prop, _) in value!.AsObject().ToList())
                    value[prop] = prop switch
                    {
                        "imageAlt" => $"zork-{group}-{prop} {{title}}",
                        "description" => $"zork-{group}-{prop} for {{name}}",
                        _ => $"zork-{group}-{prop}",
                    };
            foreach (var section in node["sections"]!.AsArray())
            {
                section!["label"] = "zorkword" + section["key"];
                if (section["shortLabel"] is not null) section["shortLabel"] = "zorkshort" + section["key"];
                if (section["itemLabel"] is not null) section["itemLabel"] = "Zorkitem" + section["key"];
            }
        }, baseTemplate: baseKey, starterFrom: baseKey);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task TheGalleryPartials_ReadEveryLabelAndMessageFromTheManifest(string key)
    {
        var original = Definition(key);
        var dir = UniqueWordsDir(key, "zork-gallery");
        try
        {
            using var app = AdminApp.WithTemplates(dir);
            var owner = await ContentFlow.BlankSiteAsync(app, "zork-gallery");
            var client = app.NewClient();
            var item = "Zorkitemprojects";
            var work = "zorkwordprojects";

            // The blank site shows the template's own empty-state messages and not-found words.
            var blankHome = await GetAsync(client, "/");
            var blankProjects = await GetAsync(client, "/projects");
            Assert.Contains("zork-home-workEmptyTitle", blankHome);
            Assert.Contains("zork-home-workEmptyBody", blankHome);
            Assert.Contains("zork-projects-emptyTitle", blankProjects);
            Assert.Contains("zork-projects-emptyBody", blankProjects);
            Assert.Contains("zork-projects-heading", blankProjects);
            var missing = await GetAsync(client, "/projects/none", HttpStatusCode.NotFound);
            Assert.Contains($"{item} not found", missing);
            Assert.Contains($"← Back to {work}", missing);

            using (var db = app.CreateDbContext())
            {
                db.Projects.AddRange(
                    new Project { Title = "Alpha", Slug = "alpha", Summary = "First.", Category = "Cat one", Tech = ["Tooling"], HeroImage = "/img/projects/telemetry.svg", Featured = true, SortOrder = 1, Screenshots = [new ProjectImage { Url = "/img/projects/telemetry-1.svg" }, new ProjectImage { Url = "/img/projects/telemetry-2.svg", Caption = "Captioned" }] },
                    new Project { Title = "Beta", Slug = "beta", Summary = "Second.", Category = "Cat two", HeroImage = "/img/projects/ledger.svg", Featured = true, SortOrder = 2, Problem = "A problem." },
                    new Project { Title = "Gamma", Slug = "gamma", Summary = "Third.", SortOrder = 3 });
                db.Skills.Add(new Skill { Name = "Skilled", Group = "Research", SortOrder = 1 });
                await db.SaveChangesAsync();
            }
            app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();

            var home = await GetAsync(client, "/");
            var list = await GetAsync(client, "/projects");
            var alpha = await GetAsync(client, "/projects/alpha");
            var beta = await GetAsync(client, "/projects/beta");
            var gamma = await GetAsync(client, "/projects/gamma");

            foreach (var word in new[] { "zork-home-workEyebrow", "zork-home-workHeading", "zork-home-skillsHeading", "zork-home-contactHeading" }) Assert.Contains(word, home);
            Assert.Contains("zork-home-viewWork", home);
            Assert.Contains("zork-home-allWork", home);
            Assert.Contains("zork-projects-readItem", home);
            Assert.Contains("alt=\"zork-projects-imageAlt Alpha\"", home);
            foreach (var word in new[] { "zork-projects-eyebrow", "zork-projects-heading", "zork-projects-lead", "zork-projects-featuredBadge" }) Assert.Contains(word, list);
            Assert.Contains("aria-label=\"zork-projects-filterLabel\"", list);
            Assert.Contains("zork-projects-readItem", list);
            foreach (var word in new[] { "zork-projects-problemHeading", "zork-projects-stackHeading", "zork-projects-screenshotsHeading" }) Assert.Contains(word, alpha + beta);
            Assert.Contains(item, alpha);
            Assert.Contains($"← {work}", alpha);
            Assert.Contains(">zork-projects-nextItem<", alpha);
            Assert.Contains("aria-label=\"zork-projects-nextItem\"", alpha);
            Assert.Contains("aria-label=\"zork-projects-galleryLabel\"", alpha);
            Assert.Contains("aria-label=\"zork-projects-openImageLabel\"", alpha);
            Assert.Contains("aria-label=\"zork-projects-openImageLabel: Captioned\"", alpha);
            Assert.Contains("alt=\"zork-projects-imageAlt Alpha\"", alpha);
            Assert.Contains("zork-projects-noWriteup", gamma);
            Assert.Contains("zork-projects-problemHeading", beta);

            // None of the template's real wording survives anywhere on the pages the partials draw.
            var visible = WebUtility.HtmlDecode(DemoSite.WithoutScripts(string.Concat(home, list, alpha, beta, gamma, blankHome, blankProjects, missing)));
            var words = original.Copy.Home.GetType().GetProperties().Select(p => (string?)p.GetValue(original.Copy.Home))
                .Concat(original.Copy.Projects.GetType().GetProperties().Select(p => (string?)p.GetValue(original.Copy.Projects)))
                .Where(s => !string.IsNullOrWhiteSpace(s) && !s.Contains("{title}") && s.Contains(' ')).Distinct().ToList();
            Assert.NotEmpty(words);
            foreach (var w in words) Assert.DoesNotContain(w!, visible);
            Assert.DoesNotContain("Case study", visible);
            Assert.DoesNotContain(">work<", visible);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task TheOptionalVerbs_FallBackToThePlainEnglishPatterns_BuiltFromTheSectionAndItemLabels(string key)
    {
        var dir = TemplateDirs.WithExtra("plain-gallery", node =>
        {
            node["name"] = "Plain Gallery";
            node["sections"]!.AsArray().Single(s => s!["key"]!.GetValue<string>() == "projects")!["label"] = "zorkwork";
            node["sections"]!.AsArray().Single(s => s!["key"]!.GetValue<string>() == "projects")!["itemLabel"] = "Zorkitem";
            foreach (var (group, name) in new[] { ("home", "viewWork"), ("home", "allWork"), ("projects", "nextItem"), ("projects", "readItem"), ("projects", "watchVideo") })
                node["copy"]![group]!.AsObject().Remove(name);
        }, baseTemplate: key, starterFrom: key);
        try
        {
            using var app = AdminApp.WithTemplates(dir);
            await ContentFlow.BlankSiteAsync(app, "plain-gallery");
            using (var db = app.CreateDbContext())
            {
                db.Projects.AddRange(
                    new Project { Title = "Alpha", Slug = "alpha", Summary = "First.", Featured = true, SortOrder = 1, HeroImage = "/media/a.mp4" },
                    new Project { Title = "Beta", Slug = "beta", Summary = "Second.", Featured = true, SortOrder = 2 });
                await db.SaveChangesAsync();
            }
            app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
            var client = app.NewClient();

            var home = await GetAsync(client, "/");
            Assert.Contains(">View zorkwork<", home);
            Assert.Contains(">All zorkwork →<", home);
            Assert.Contains("Read zorkitem →", home);
            Assert.Contains(">Watch the video ↗<", home);
            var detail = await GetAsync(client, "/projects/alpha");
            Assert.Contains(">Next zorkitem<", detail);
            Assert.Contains("aria-label=\"Next zorkitem\"", detail);
            Assert.Contains(">Watch the video ↗</a>", detail);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private sealed class FakeView : Microsoft.AspNetCore.Mvc.ViewEngines.IView
    {
        public string Path => "fake";
        public Task RenderAsync(Microsoft.AspNetCore.Mvc.Rendering.ViewContext context) => Task.CompletedTask;
    }

    private sealed class FakeViews : Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine
    {
        private readonly bool _found;
        public List<string> Asked { get; } = [];
        public FakeViews(bool found) => _found = found;
        public IReadOnlyList<Microsoft.AspNetCore.Mvc.ViewEngines.IViewEngine> ViewEngines => [];
        public Microsoft.AspNetCore.Mvc.ViewEngines.ViewEngineResult FindView(Microsoft.AspNetCore.Mvc.ActionContext context, string viewName, bool isMainPage)
        {
            Asked.Add(viewName);
            return _found ? Microsoft.AspNetCore.Mvc.ViewEngines.ViewEngineResult.Found(viewName, new FakeView()) : Microsoft.AspNetCore.Mvc.ViewEngines.ViewEngineResult.NotFound(viewName, [viewName]);
        }
        public Microsoft.AspNetCore.Mvc.ViewEngines.ViewEngineResult GetView(string? executingFilePath, string viewPath, bool isMainPage) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("../gallery")]
    [InlineData("gallery/../showcase")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("Gallery")]
    [InlineData("gallery ")]
    [InlineData("unknown")]
    [InlineData("results")]
    public void FindingAPartial_RefusesAnArchetypeThatIsNotImplemented_WithoutAskingTheViewEngine(string archetype)
    {
        var views = new FakeViews(found: true);

        Assert.Null(LayoutPartials.Find(views, null!, archetype, LayoutPartials.Projects));
        Assert.Empty(views.Asked);
    }

    [Fact]
    public void FindingAPartial_ReturnsTheNameOnlyWhenTheViewEngineHasIt()
    {
        var present = new FakeViews(found: true);
        var absent = new FakeViews(found: false);

        Assert.Equal("Layouts/gallery/_Projects", LayoutPartials.Find(present, null!, "gallery", LayoutPartials.Projects));
        Assert.Null(LayoutPartials.Find(absent, null!, "showcase", LayoutPartials.Projects));
        Assert.Equal(["Layouts/showcase/_Projects"], absent.Asked);
    }

    [Theory]
    [InlineData("/media/a.mp4", true)]
    [InlineData("https://example.org/a.mp4", true)]
    [InlineData("http://example.org/a.mp4", true)]
    [InlineData("//evil.example/a.mp4", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,x", false)]
    [InlineData("a.mp4", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyASitePathOrAnHttpAddress_IsLinkable(string? url, bool expected) => Assert.Equal(expected, LayoutPartials.IsLinkable(url));

    // ---------------------------------------------------------------- accessibility and security

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task TheCaseStudyGallery_NamesItsControlsOnce_RespectsReducedMotion_AndTheStylesheetKeepsFocusAndBothColourModes(string key)
    {
        using var app = await SiteAsync(key, "sample");
        var client = app.NewClient();
        var copy = Definition(key).Copy.Projects;
        var theme = Definition(key).Theme.Default!;
        using var db = app.CreateDbContext();
        var project = await db.Projects.Where(p => p.Screenshots.Count > 0).OrderBy(p => p.SortOrder).FirstAsync();
        var detail = await GetAsync(client, "/projects/" + project.Slug);
        var home = await GetAsync(client, "/");

        // The lightbox button carries the picture's name; the visible caption beside it is hidden from screen readers so it is not read twice.
        foreach (var s in project.Screenshots)
        {
            Assert.Contains($"aria-label=\"{copy.OpenImageLabel}: {WebUtility.HtmlEncode(s.Caption!)}\"", detail);
            Assert.Matches($@"<figcaption[^>]*aria-hidden=""true""[^>]*>{Regex.Escape(WebUtility.HtmlEncode(s.Caption!))}</figcaption>", detail);
        }
        Assert.Matches(@"<button type=""button""[^>]*data-lightbox-item", detail); // the script's hooks are unchanged
        Assert.Contains("data-lightbox-gallery", detail);

        // Reduced motion: the hover zoom on cards and on the case-study pictures has a variant that switches it off.
        Assert.Contains("motion-reduce:transition-none", home);
        Assert.Contains("motion-reduce:group-hover:scale-100", detail);

        var css = File.ReadAllText(Path.Combine(Wwwroot, "css", "app.css"));
        Assert.Contains(":focus-visible", css);
        Assert.Matches(@"@media \(prefers-reduced-motion:\s*reduce\)\{[^@]*?\.motion-reduce\\:transition-none", css);
        Assert.Matches($@"\[data-template=[""']?{Regex.Escape(theme)}[""']?\]\[data-theme=[""']?light", css);
        Assert.Matches($@"\[data-template=[""']?{Regex.Escape(theme)}[""']?\]\[data-theme=[""']?dark", css);
        Assert.Contains("line-clamp", css);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task ACoverDescription_IsPreferredOverTheFallback_OnTheCardsAndTheCaseStudy(string key)
    {
        using var app = await SiteAsync(key, "sample");
        var copy = Definition(key).Copy.Projects;
        var client = app.NewClient();
        using var db = app.CreateDbContext();
        var project = await db.Projects.Where(p => p.Featured).OrderBy(p => p.SortOrder).FirstAsync();
        var plain = await db.Projects.Where(p => !p.Featured).OrderBy(p => p.SortOrder).FirstAsync();
        plain.HeroImageAlt = null; // no description of its own
        await db.SaveChangesAsync();
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        Assert.False(string.IsNullOrWhiteSpace(project.HeroImageAlt));

        var own = $"alt=\"{WebUtility.HtmlEncode(project.HeroImageAlt!)}\"";
        var fallback = $"alt=\"{WebUtility.HtmlEncode(copy.AltFor(project.Title))}\"";
        foreach (var path in new[] { "/", "/projects", "/projects/" + project.Slug })
        {
            var html = await GetAsync(client, path);
            Assert.Contains(own, html);
            Assert.DoesNotContain(fallback, html);
        }
        // Without one, the card and the case study agree on the template's own fallback.
        var plainFallback = $"alt=\"{WebUtility.HtmlEncode(copy.AltFor(plain.Title))}\"";
        Assert.Contains(plainFallback, await GetAsync(client, "/projects"));
        Assert.Contains(plainFallback, await GetAsync(client, "/projects/" + plain.Slug));
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task TheHome_ShowsTheMetrics_AndHidesTheStripWhenThereAreNone(string key)
    {
        using var app = await SiteAsync(key, "sample");
        var starter = new TemplateCatalog(TemplatesDir).GetStarter(key)!;
        var client = app.NewClient();

        var home = await GetAsync(client, "/");
        Assert.NotEmpty(starter.Profile.Metrics);
        foreach (var m in starter.Profile.Metrics) { Assert.Contains($">{WebUtility.HtmlEncode(m.Label)}<", home); Assert.Contains(WebUtility.HtmlEncode(m.Value).Replace("+", "&#x2B;"), home); } // Razor also encodes a plus sign
        Assert.Equal(starter.Profile.Metrics.Count, Regex.Matches(home, "class=\"metric-value\"").Count);

        using (var db = app.CreateDbContext())
        {
            (await db.SiteProfiles.SingleAsync()).Metrics = [];
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        Assert.DoesNotContain("metric-value", await GetAsync(client, "/"));
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task TheHomeSkillStrip_FollowsTheManifestsGroupOrder_AndPutsOtherGroupsLast(string key)
    {
        using var app = await SiteAsync(key, "sample");
        var template = Definition(key);
        using (var db = app.CreateDbContext())
        {
            db.Skills.Add(new Skill { Name = "Odd one", Group = "Aardvark", SortOrder = 99 }); // sorts first alphabetically, but the manifest does not list it
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();

        var section = Regex.Match(await GetAsync(app.NewClient(), "/"), "<section id=\"skills\".*?</section>", RegexOptions.Singleline).Value;
        var groups = Regex.Matches(section, "<p class=\"eyebrow\">(.*?)</p>").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();

        Assert.Equal(template.SkillGroups.Concat(["Aardvark"]), groups);
    }

    private static async Task<string> FirstSlugAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return await db.Projects.OrderBy(p => p.SortOrder).Select(p => p.Slug).FirstAsync();
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task APublicGalleryPage_HasTheSameSecurityHeadersAsSoftware_AndNoThirdPartyScriptOrFrame(string key)
    {
        using var gallery = await SiteAsync(key, "sample");
        using var software = new AdminApp();
        await DemoSite.SeedAsync(software);
        var (project, _) = DemoSite.FirstSlugs(software);

        foreach (var (galleryPath, softwarePath) in new[] { ("/", "/"), ("/projects", "/projects"), ("/projects/" + await FirstSlugAsync(gallery), "/projects/" + project) })
        {
            var g = await gallery.NewClient().GetAsync(galleryPath);
            var s = await software.NewClient().GetAsync(softwarePath);
            string[] volatileHeaders = ["Date", "ETag", "Content-Length", "Age", "Last-Modified", "Expires"];
            string Flat(HttpResponseMessage r) => string.Join("\n", r.Headers.Concat(r.Content.Headers)
                .Where(h => !volatileHeaders.Contains(h.Key, StringComparer.OrdinalIgnoreCase))
                .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase).Select(h => h.Key + ": " + string.Join(",", h.Value)));
            Assert.Equal(Flat(s), Flat(g));
            var html = await g.Content.ReadAsStringAsync();
            Assert.DoesNotContain("<iframe", html);
            // The only scripts are the layout's own (theme, site.js, structured data); none comes from another origin.
            Assert.DoesNotMatch(@"<script[^>]*\ssrc=""https?://", html);
        }
    }

    // ---------------------------------------------------------------- templates and the shared wording rules

    [Theory]
    [MemberData(nameof(KeyData))]
    public void AGalleryTemplate_DoesNotInheritSoftwareWording(string key)
    {
        var software = new TemplateCatalog(TemplatesDir).Get("software");
        var template = Definition(key);
        var neutral = new[] { "Skills", "Experience", "Links", "featured", "Experience", "View certificate", "Curriculum vitae — {name}, {title}.", "Work", "Selected work", "Writing", "Case studies" };

        foreach (var group in typeof(TemplateCopy).GetProperties())
            foreach (var prop in group.PropertyType.GetProperties().Where(p => p.PropertyType == typeof(string)))
            {
                var a = (string?)prop.GetValue(group.GetValue(software.Copy));
                var b = (string?)prop.GetValue(group.GetValue(template.Copy));
                if (neutral.Contains(b) || string.IsNullOrEmpty(b)) continue; // optional wording is left empty by templates that do not set it
                Assert.True(a != b, $"copy.{group.Name}.{prop.Name} is identical to software's: {b}");
            }
        Assert.DoesNotMatch(@"\.NET|GitHub|software|developer|clinical|patient|licen[cs]e", System.Text.Json.JsonSerializer.Serialize(template.Copy));
    }
}
