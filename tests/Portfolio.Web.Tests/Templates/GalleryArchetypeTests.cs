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
    public static readonly string[] Keys = ["design-ux"];

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
                    new Project { Title = "Uncategorised", Slug = "plain", Summary = "No category, no tools.", HeroImage = "/img/projects/telemetry.svg", SortOrder = order++ });
                for (var i = 0; i < 9; i++)
                    db.Projects.Add(new Project { Title = $"Extra work {i}", Slug = $"extra-{i}", Summary = "More work.", Category = i % 2 == 0 ? "Even" : "Odd", HeroImage = "/img/projects/ledger.svg", Featured = i < 2, SortOrder = order++, Tech = ["a", "b", "c", "d"] });
            }
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        return app;
    }

    [Theory]
    [MemberData(nameof(KeyAndContent))]
    public async Task EveryPublicPage_RendersInTheGalleryLayout_ForBlankSampleAndFullContent(string key, string content)
    {
        using var app = await SiteAsync(key, content);
        var template = Definition(key);
        var copy = template.Copy;
        var client = app.NewClient();
        var theme = template.Theme.Default!;

        var home = await GetAsync(client, "/");
        var projects = await GetAsync(client, "/projects");
        Assert.Contains($"data-template=\"{theme}\"", home);
        AssertWellFormedGalleryPage("/", home);
        AssertWellFormedGalleryPage("/projects", projects);
        foreach (var path in new[] { "/about", "/experience", "/skills", "/certifications", "/contact", "/cv" })
        {
            var page = await GetAsync(client, path);
            Assert.Contains("<main", page);
            Assert.Single(Regex.Matches(MainOf(page), "<h1[ >]"));
        }
        await GetAsync(client, "/talks", HttpStatusCode.NotFound); // off by default, and a template-off section is never shown

        using var db = app.CreateDbContext();
        var rows = await db.Projects.OrderBy(p => p.SortOrder).ToListAsync();
        if (content == "blank")
        {
            Assert.Contains(Escaped(copy.Home.WorkEmptyTitle), home);
            Assert.Contains(Escaped(copy.Projects.EmptyTitle), projects);
            Assert.DoesNotContain("data-project-grid", projects);
            Assert.DoesNotContain("<article class=\"group", home);
            var missing = await GetAsync(client, "/projects/nothing-here", HttpStatusCode.NotFound);
            Assert.Contains(Escaped(template.Sections.Single(s => s.Key == "projects").ItemLabel + " not found"), missing);
            return;
        }

        // Every project is on the work page, in the layout's card, with a picture or a tile, and links to its case study.
        Assert.Equal(rows.Count, Regex.Matches(projects, "<article class=\"group relative").Count);
        foreach (var p in rows) Assert.Contains($"href=\"/projects/{p.Slug}\"", projects);
        Assert.Equal(rows.Count(p => p.Featured), Regex.Matches(home, "<article class=\"group relative").Count);
        Assert.Contains("data-project-grid", projects);
        var categories = rows.Select(p => p.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (categories.Count > 1) foreach (var c in categories) Assert.Contains($"data-filter=\"{WebUtility.HtmlEncode(c)}\"", projects);
        Assert.Contains("motion-reduce:transition-none", home); // the hover zoom respects reduced motion
        Assert.DoesNotContain("data-lightbox", projects); // cards open the case study; the lightbox is for the pictures inside it
        Assert.Contains("id=\"work\"", home);
        Assert.Contains("id=\"skills\"", home);

        foreach (var p in rows)
        {
            var detail = await GetAsync(client, $"/projects/{p.Slug}");
            AssertWellFormedGalleryPage($"/projects/{p.Slug}", detail);
            Assert.Contains("\"@type\":\"CreativeWork\"", detail);
            Assert.Contains("\"@type\":\"BreadcrumbList\"", detail);
            var shown = p.Screenshots.Where(s => !Media.IsVideo(s.Url)).ToList();
            Assert.Equal(shown.Count, Regex.Matches(detail, "data-lightbox-item").Count);
            if (shown.Count > 0) Assert.Contains($"aria-label=\"{copy.Projects.GalleryLabel}\"", detail);
            foreach (var s in shown) Assert.Contains($"alt=\"{WebUtility.HtmlEncode(s.Caption ?? copy.Projects.AltFor(p.Title))}\"", detail);
            Assert.Contains(string.IsNullOrWhiteSpace(p.Problem) && string.IsNullOrWhiteSpace(p.Approach) ? Escaped(copy.Projects.NoWriteup) : Escaped(copy.Projects.ProblemHeading), detail);
            Assert.Contains(Escaped($"Next {template.Sections.Single(s => s.Key == "projects").ItemLabel!.ToLowerInvariant()}"), detail);
        }

        if (content == "full")
        {
            // Hostile text is encoded; a video cover and video pictures never become a player; a project with nothing shows a text tile.
            Assert.DoesNotContain("<script>alert(1)", projects);
            Assert.Contains("Film &quot;reel&quot; &lt;b&gt;&amp; more", projects);
            var film = await GetAsync(client, "/projects/film-reel");
            Assert.DoesNotContain("reel.mp4", film);
            Assert.DoesNotContain("clip.webm", film);
            Assert.Contains("Watch the film", film);
            Assert.Contains("aria-hidden=\"true\"", await GetAsync(client, "/projects/no-picture") + projects);
            Assert.DoesNotContain("reel.mp4", projects + home);
        }
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
            Assert.Contains($"View {work}", home);
            Assert.Contains($"All {work} →", home);
            Assert.Contains($"Read {item.ToLowerInvariant()} →", home);
            Assert.Contains("alt=\"zork-projects-imageAlt Alpha\"", home);
            foreach (var word in new[] { "zork-projects-eyebrow", "zork-projects-heading", "zork-projects-lead", "zork-projects-featuredBadge" }) Assert.Contains(word, list);
            Assert.Contains("aria-label=\"zork-projects-filterLabel\"", list);
            Assert.Contains($"Read {item.ToLowerInvariant()} →", list);
            foreach (var word in new[] { "zork-projects-problemHeading", "zork-projects-stackHeading", "zork-projects-screenshotsHeading" }) Assert.Contains(word, alpha + beta);
            Assert.Contains(item, alpha);
            Assert.Contains($"← {work}", alpha);
            Assert.Contains($"Next {item.ToLowerInvariant()}", alpha);
            Assert.Contains($"aria-label=\"{work}\"", alpha);
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

    // ---------------------------------------------------------------- accessibility and security

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task ThePages_HaveLandmarksAFocusableSkipLink_AndTheStylesheetHasFocusAndBothColourModes(string key)
    {
        using var app = await SiteAsync(key, "sample");
        var client = app.NewClient();
        var theme = Definition(key).Theme.Default!;

        foreach (var path in new[] { "/", "/projects", "/projects/" + (await FirstSlugAsync(app)) })
        {
            var html = await GetAsync(client, path);
            Assert.Contains("<html lang=\"en\"", html);
            Assert.Contains("<a href=\"#main-content\" class=\"sr-only focus:not-sr-only", html);
            Assert.Contains("<main id=\"main-content\"", html);
            Assert.Contains("<header", html);
            Assert.Contains("<nav", html);
            Assert.Contains("<footer", html);
            Assert.Contains("name=\"viewport\"", html);
        }

        var css = File.ReadAllText(Path.Combine(Wwwroot, "css", "app.css"));
        Assert.Contains(":focus-visible", css);
        Assert.Contains("prefers-reduced-motion", css + ".motion-reduce");
        Assert.Matches($@"\[data-template=[""']?{Regex.Escape(theme)}[""']?\]\[data-theme=[""']?light", css);
        Assert.Matches($@"\[data-template=[""']?{Regex.Escape(theme)}[""']?\]\[data-theme=[""']?dark", css);
        Assert.Contains("line-clamp", css);
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
                if (neutral.Contains(b)) continue;
                Assert.True(a != b, $"copy.{group.Name}.{prop.Name} is identical to software's: {b}");
            }
        Assert.DoesNotMatch(@"\.NET|GitHub|software|developer|clinical|patient|licen[cs]e", System.Text.Json.JsonSerializer.Serialize(template.Copy));
    }
}
