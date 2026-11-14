using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;
using G = Portfolio.Web.Tests.Templates.GalleryArchetypeTests;

namespace Portfolio.Web.Tests.Templates;

/// <summary>
/// The results layout archetype (the headline-figure band is the hero) and every template that uses it. Driven by <see cref="Keys"/>: a further results
/// template (Sales & BD) is covered by adding its key to that list. The admin and cross-cutting paths are in <see cref="ArchetypeTemplateFlowTests"/>.
/// </summary>
public class ResultsArchetypeTests
{
    /// <summary>Every shipped template whose archetype is results.</summary>
    public static readonly string[] Keys = ["marketing-content"];

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

    public static TheoryData<string, string> KeyAndPopulated
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var key in Keys) foreach (var content in new[] { "sample", "full" }) data.Add(key, content);
            return data;
        }
    }

    private static string ResultsLayouts => Path.Combine(G.Layouts, "results");
    private static string Plain(string html) => WebUtility.HtmlDecode(html);
    private static string ItemOf(TemplateDefinition t) => t.Sections.Single(s => s.Key == "projects").ItemLabel!;
    private const string Fit = "grid-cols-[repeat(auto-fit,minmax(min(100%,var(--results-card-min,20rem)),1fr))]";

    /// <summary>Rules every results page keeps: alt text on every picture, one h1, headings that never skip a level, no players, no inline script, no handlers, no hot-linked pictures, every link named.</summary>
    private static void AssertWellFormedResultsPage(string path, string html)
    {
        var main = G.MainOf(html);
        Assert.DoesNotMatch(@"<img\b(?![^>]*\balt=""[^""]+"")", main);
        Assert.Single(Regex.Matches(main, "<h1[ >]"));
        var levels = Regex.Matches(main, @"<h([1-6])[ >]").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        for (var i = 1; i < levels.Count; i++) Assert.True(levels[i] <= levels[i - 1] + 1, $"{path}: heading level jumps from h{levels[i - 1]} to h{levels[i]}");
        Assert.DoesNotMatch(@"<(iframe|video|audio|object|embed)\b", main);
        Assert.DoesNotMatch(@"<script\b(?![^>]*type=""application/ld)", main);
        Assert.DoesNotMatch(@"\son[a-z]+\s*=", main);
        Assert.DoesNotMatch(@"https?://[^""'\s]*\.(png|jpe?g|webp|gif|svg)", main);
        foreach (Match a in Regex.Matches(main, @"<a\b[^>]*>(.*?)</a>", RegexOptions.Singleline))
            Assert.False(string.IsNullOrWhiteSpace(Regex.Replace(a.Groups[1].Value, "<[^>]+>", "")), $"{path}: a link has no text: {a.Value}");
    }

    // ---------------------------------------------------------------- the manifest and its files

    [Theory]
    [MemberData(nameof(KeyData))]
    public void ATemplateOnTheResultsArchetype_HasItsLayoutThemeStarterAndThumbnail(string key)
    {
        var template = G.Definition(key);
        var catalog = new TemplateCatalog(G.TemplatesDir);

        Assert.Equal(key, template.Key);
        Assert.Equal(Archetypes.Results, template.Archetype);
        Assert.Contains(Archetypes.Results, Archetypes.Implemented);
        foreach (var partial in new[] { "_Home", "_Projects", "_ProjectDetail", "_ResultCard" })
            Assert.True(File.Exists(Path.Combine(ResultsLayouts, partial + ".cshtml")), $"Layouts/results/{partial}.cshtml is missing");
        Assert.True(File.Exists(Path.Combine(TemplateDirs.Web, "Styles", "themes", template.Theme.Default + ".css")));
        Assert.NotNull(catalog.GetStarter(key));
        Assert.NotNull(catalog.GetPreviewImagePath(key));
        Assert.True(template.Theme.Accents.Count >= 3);
        Assert.Contains(template.Sections, s => s.Key == SectionRegistry.Projects && !string.IsNullOrWhiteSpace(s.ItemLabel));
        Assert.True(Archetypes.ShowsSkillsOnHome(template.Archetype));
        Assert.True(Archetypes.ShowsClientsOnHome(template.Archetype));
        Assert.False(Archetypes.ShowsClientsOnHome(Archetypes.Gallery) || Archetypes.ShowsClientsOnHome(Archetypes.Showcase) || Archetypes.ShowsClientsOnHome(Archetypes.Credentials));
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void TheThumbnail_IsARealScreenshotOfTheRightSize(string key)
    {
        var path = new TemplateCatalog(G.TemplatesDir).GetPreviewImagePath(key)!;
        var bytes = File.ReadAllBytes(path);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
        Assert.Equal(1200, (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19]);
        Assert.Equal(800, (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23]);
        Assert.True(bytes.Length > 30_000, "a screenshot of a rendered page is not this small");
    }

    [Fact]
    public void TheResultsPartials_ContainNoScriptNoHandlersNoPlayersNoTemplateKeyChecks_AndNoFixedButtonWords()
    {
        var files = Directory.EnumerateFiles(ResultsLayouts, "*.cshtml").ToList();
        Assert.True(files.Count >= 4);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var without = Regex.Replace(text, @"<script type=""application/ld\+json"">@Html\.Raw\(Model\.\w+\)</script>", "");
            Assert.DoesNotMatch(@"<script\b", without);
            Assert.DoesNotMatch(@"\son[a-z]+\s*=", text);
            Assert.DoesNotMatch(@"<(iframe|video|audio|object|embed)\b", text);
            Assert.DoesNotMatch(@"(?i)templateKey|\.Key\s*==|Definition\.Key", text);
            // The words a button or chip carries come from optional manifest wording, never from the markup.
            Assert.DoesNotMatch(@">\s*(Download CV|LinkedIn|All|Worked with|Next|Read|Watch)\b[^<@]*<", text);
            Assert.DoesNotMatch(@">\s*View \w", text);
        }
        Assert.Contains("copy.DownloadCvText()", File.ReadAllText(Path.Combine(ResultsLayouts, "_Home.cshtml")));
        Assert.Contains("copy.LinkedInText()", File.ReadAllText(Path.Combine(ResultsLayouts, "_Home.cshtml")));
        Assert.Contains("copy.FilterAllText()", File.ReadAllText(Path.Combine(ResultsLayouts, "_Projects.cshtml")));
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void TheStarterContent_IsFictional_UsesOnlyBundledPictures_DescribesEveryOne_AndCarriesResults(string key)
    {
        var starter = new TemplateCatalog(G.TemplatesDir).GetStarter(key)!;

        Assert.True(starter.Fictional);
        Assert.Contains("fictional", starter.Profile.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("example.org", starter.Profile.Email);
        Assert.True(starter.Profile.Metrics.Count >= 3, "the headline band needs at least three figures");
        Assert.NotEmpty(starter.Projects);
        Assert.True(starter.Projects.Count(p => p.Featured) >= 3, "the home grid needs at least three featured projects");
        Assert.Contains(starter.Projects, p => !p.Featured);
        Assert.Contains(starter.Projects, p => p.Screenshots.Count > 0);
        Assert.All(starter.Projects, p => Assert.NotEmpty(p.Impact));
        Assert.Contains(starter.Projects, p => ImpactLine.Split(p.Impact[0]).HasFigure);
        Assert.Contains(starter.Projects, p => !ImpactLine.Split(p.Impact[0]).HasFigure); // a result that does not start with a figure is shown whole
        Assert.True(starter.Projects.Select(p => p.Org).Where(o => !string.IsNullOrWhiteSpace(o)).Distinct().Count() >= 3, "the client strip needs a few names");
        Assert.All(starter.Experiences, e => Assert.Contains("fictional", e.Company, StringComparison.OrdinalIgnoreCase));
        Assert.All(starter.Projects, p => Assert.Contains("(sample)", p.Title));
        Assert.All(starter.Projects.Where(p => !string.IsNullOrWhiteSpace(p.Org)), p => Assert.Contains("(fictional)", p.Org));
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
            Assert.All(p.Links, l => Assert.StartsWith("https://example.org/", l.Url));
        }
        foreach (var url in images)
        {
            Assert.StartsWith($"/img/starter/{key}/", url);
            var file = Path.Combine(G.Wwwroot, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(file), $"{url} is not bundled under wwwroot");
            Assert.True(new FileInfo(file).Length < 60 * 1024, $"{url} is larger than a starter placeholder should be");
            var text = File.ReadAllText(file);
            Assert.DoesNotMatch(@"(?i)<script|<text|<image|https?://(?!www\.w3\.org)", text); // no script, no font-dependent text, no linked or embedded picture
        }
        Assert.Equal(images.Count, images.Distinct().Count());
    }

    // ---------------------------------------------------------------- rendering: blank, sample and full content

    [Theory]
    [MemberData(nameof(KeyAndContent))]
    public async Task EveryPublicPage_AnswersInTheResultsLayout_ForBlankSampleAndFullContent(string key, string content)
    {
        using var app = await G.SiteAsync(key, content);
        var client = app.NewClient();
        var home = await G.GetAsync(client, "/");

        Assert.Contains($"data-template=\"{G.Definition(key).Theme.Default}\"", home);
        AssertWellFormedResultsPage("/", home);
        AssertWellFormedResultsPage("/projects", await G.GetAsync(client, "/projects"));
        foreach (var path in new[] { "/about", "/experience", "/skills", "/certifications", "/contact", "/cv" })
        {
            var page = await G.GetAsync(client, path);
            Assert.Single(Regex.Matches(G.MainOf(page), "<h1[ >]"));
            Assert.DoesNotContain("data-results", page); // the shared pages and /cv keep the Software look
        }
        await G.GetAsync(client, "/talks", HttpStatusCode.NotFound);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task ABlankSite_ShowsTheTemplatesEmptyStatesAndItsNotFoundWords_WithoutABandOrAStrip(string key)
    {
        using var app = await G.SiteAsync(key, "blank");
        var template = G.Definition(key);
        var client = app.NewClient();
        using (var db = app.CreateDbContext())
        {
            (await db.SiteProfiles.SingleAsync()).Metrics = [];
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();

        var home = await G.GetAsync(client, "/");
        var projects = await G.GetAsync(client, "/projects");
        Assert.Contains(G.Escaped(template.Copy.Home.WorkEmptyTitle), home);
        Assert.Contains(G.Escaped(template.Copy.Projects.EmptyTitle), projects);
        Assert.DoesNotContain("data-project-grid", projects);
        Assert.DoesNotContain("data-results-card", home + projects);
        Assert.DoesNotContain("data-results-band", home);
        Assert.DoesNotContain("id=\"clients\"", home);
        var missing = await G.GetAsync(client, "/projects/nothing-here", HttpStatusCode.NotFound);
        Assert.Contains(G.Escaped(ItemOf(template) + " not found"), missing);
    }

    [Theory]
    [MemberData(nameof(KeyAndPopulated))]
    public async Task TheWorkGrid_ShowsEveryProjectInTheLayoutsCard_AndTheHomeGridOnlyTheFeaturedOnes(string key, string content)
    {
        using var app = await G.SiteAsync(key, content);
        var template = G.Definition(key);
        var client = app.NewClient();
        var home = await G.GetAsync(client, "/");
        var projects = await G.GetAsync(client, "/projects");
        using var db = app.CreateDbContext();
        var rows = await db.Projects.OrderBy(p => p.SortOrder).ToListAsync();

        Assert.Equal(rows.Count, Regex.Matches(projects, "data-results-card").Count);
        foreach (var p in rows) Assert.Contains($"href=\"/projects/{p.Slug}\"", projects);
        Assert.Equal(rows.Count(p => p.Featured), Regex.Matches(home, "data-results-card").Count);
        Assert.Contains("data-project-grid", projects);
        var categories = rows.Select(p => p.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (categories.Count > 1)
        {
            foreach (var c in categories) Assert.Contains($"data-filter=\"{WebUtility.HtmlEncode(c)}\"", projects);
            Assert.Matches($@"data-filter=""all""[^>]*>{Regex.Escape(G.Escaped(template.Copy.Projects.FilterAllText()))}</button>", projects);
        }
        Assert.DoesNotContain("data-lightbox", projects);
        Assert.Contains("id=\"work\"", home);
        Assert.Contains("id=\"skills\"", home);
        const string badge = "text-[10px] uppercase tracking-wider text-accent";
        Assert.Contains(badge, projects);
        Assert.DoesNotContain(badge, home);
        Assert.Contains(Fit, home);
        Assert.Contains(Fit, projects);
        Assert.DoesNotContain("repeat(auto-fill", home + projects);
        Assert.Contains("repeat(auto-fit,minmax(min(100%,var(--results-card-min,20rem)),1fr))", File.ReadAllText(Path.Combine(G.Wwwroot, "css", "app.css")));
        Assert.Contains(G.Escaped(template.Copy.Projects.ReadItemFor(ItemOf(template))), home);
    }

    [Theory]
    [MemberData(nameof(KeyAndPopulated))]
    public async Task EveryCaseStudy_HasItsHeadingsGalleryResultsAndStructuredData(string key, string content)
    {
        using var app = await G.SiteAsync(key, content);
        var template = G.Definition(key);
        var copy = template.Copy;
        var client = app.NewClient();
        using var db = app.CreateDbContext();

        foreach (var p in await db.Projects.OrderBy(p => p.SortOrder).ToListAsync())
        {
            var detail = await G.GetAsync(client, $"/projects/{p.Slug}");
            AssertWellFormedResultsPage($"/projects/{p.Slug}", detail);
            Assert.Contains("\"@type\":\"CreativeWork\"", detail);
            Assert.Contains("\"@type\":\"BreadcrumbList\"", detail);
            var shown = p.Screenshots.Where(s => !Media.IsVideo(s.Url)).ToList();
            Assert.Equal(shown.Count, Regex.Matches(detail, "data-lightbox-item").Count);
            foreach (var s in shown) Assert.Contains($"alt=\"{WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(s.Caption) ? copy.Projects.AltFor(p.Title) : s.Caption)}\"", detail);
            var lines = p.Impact.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            Assert.Equal(lines.Count > 0 ? 1 : 0, Regex.Matches(detail, "data-results-impact").Count);
            // Every impact line is on the page whole: the figure set large plus the words after it.
            var text = Plain(detail);
            foreach (var line in lines.Select(ImpactLine.Split))
            {
                if (line.HasFigure) Assert.Contains(">" + line.Figure + "<", text);
                if (line.Rest.Length > 0) Assert.Contains(">" + line.Rest + "<", text);
            }
            Assert.Contains(string.IsNullOrWhiteSpace(p.Problem) && string.IsNullOrWhiteSpace(p.Approach) ? G.Escaped(copy.Projects.NoWriteup) : G.Escaped(copy.Projects.ProblemHeading), detail);
            var next = G.Escaped(copy.Projects.NextItemFor(ItemOf(template)));
            Assert.Contains($"aria-label=\"{next}\"", detail);
            Assert.Contains(next, detail);
        }
    }

    // ---------------------------------------------------------------- the hero reads the profile's key figures and the work's impact lines

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task TheHeroBand_ShowsEveryKeyFigureWithItsUnitAndLabel_AndIsHiddenWhenThereAreNone(string key)
    {
        using var app = await G.SiteAsync(key, "sample");
        var client = app.NewClient();
        using (var db = app.CreateDbContext())
        {
            var profile = await db.SiteProfiles.SingleAsync();
            profile.Metrics = [new ProfileMetric { Value = "9.5", Unit = "points", Label = "Zeta <b>lift</b>" }, new ProfileMetric { Value = new string('9', 40), Unit = "", Label = "Long" }, new ProfileMetric { Value = "+3", Unit = "", Label = "Signed" }];
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();

        var home = await G.GetAsync(client, "/");
        var band = Regex.Match(home, "<ul[^>]*data-results-band.*?</ul>", RegexOptions.Singleline).Value;
        Assert.Equal(3, Regex.Matches(band, "data-metric").Count);
        Assert.Contains("class=\"break-words font-display", band);
        Assert.Contains("9.5<span class=\"text-xl font-medium text-muted\"> points</span>", band);
        Assert.Contains("Zeta &lt;b&gt;lift&lt;/b&gt;", band);
        Assert.Contains(new string('9', 40), band);
        Assert.Contains("min-w-0", band);
        // The band is part of the hero, above the clients strip and the work.
        Assert.True(home.IndexOf("data-results-band", StringComparison.Ordinal) < home.IndexOf("id=\"work\"", StringComparison.Ordinal));
        Assert.True(home.IndexOf("<h1", StringComparison.Ordinal) < home.IndexOf("data-results-band", StringComparison.Ordinal));

        using (var db = app.CreateDbContext())
        {
            (await db.SiteProfiles.SingleAsync()).Metrics = [];
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        Assert.DoesNotContain("data-results-band", await G.GetAsync(client, "/"));
    }

    [Fact]
    public async Task TheStarterFigures_AreAllOnTheHome()
    {
        using var app = await G.SiteAsync(Keys[0], "sample");
        var starter = new TemplateCatalog(G.TemplatesDir).GetStarter(Keys[0])!;

        var home = Plain(await G.GetAsync(app.NewClient(), "/"));

        Assert.Equal(starter.Profile.Metrics.Count, Regex.Matches(home, "data-metric").Count);
        foreach (var m in starter.Profile.Metrics) { Assert.Contains(">" + m.Label + "<", home); Assert.Contains(m.Value, home); }
    }

    [Theory]
    [InlineData("+38% click-through", "+38%", "click-through")]
    [InlineData("2.1M emails delivered", "2.1M", "emails delivered")]
    [InlineData("$1,200 saved per month", "$1,200", "saved per month")]
    [InlineData("10x faster", "10x", "faster")]
    [InlineData("14% fewer unsubscribes", "14%", "fewer unsubscribes")]
    [InlineData("  120  articles  ", "120", "articles")]
    [InlineData("99%", "99%", "")]
    [InlineData("Open rate up from 31% to 44%", "", "Open rate up from 31% to 44%")]
    [InlineData("3rd place", "", "3rd place")]
    [InlineData("1234567890123 users", "", "1234567890123 users")]
    [InlineData("<script>alert(1)</script>", "", "<script>alert(1)</script>")]
    [InlineData("", "", "")]
    [InlineData(null, "", "")]
    public void AnImpactLine_IsSplitIntoItsLeadingFigureAndTheRest_AndNeverLosesText(string? line, string figure, string rest)
    {
        var split = ImpactLine.Split(line);

        Assert.Equal(figure, split.Figure);
        Assert.Equal(rest, split.Rest);
        Assert.Equal(figure.Length > 0, split.HasFigure);
        Assert.Equal(Regex.Replace(line ?? "", @"\s+", " ").Trim(), (split.Figure + " " + split.Rest).Trim());
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task EveryProjectFieldTheEditorOffers_AppearsOnThePublicSite(string key)
    {
        using var app = await G.SiteAsync(key, "blank");
        using (var db = app.CreateDbContext())
        {
            db.Projects.Add(new Project
            {
                Title = "Fieldtitle", Slug = "fields", Role = "Fieldrole", Org = "Fieldorg", Period = "Fieldperiod", Category = "Fieldcategory", Summary = "Fieldsummary",
                Problem = "Fieldproblem", Approach = "Fieldapproach", Impact = ["+7% Fieldimpactone", "Fieldimpacttwo"], Tech = ["Fieldtech"],
                HeroImage = "/img/projects/telemetry.svg", HeroImageAlt = "Fieldcoveralt", Featured = true, SortOrder = 1,
                Screenshots = [new ProjectImage { Url = "/img/projects/telemetry-1.svg", Caption = "Fieldcaption" }],
                Links = [new Link { Label = "Fieldlink", Url = "https://example.org/l" }], Proofs = [new Proof { Label = "Fieldproof", Url = "https://example.org/p" }],
            });
            db.Testimonials.Add(new Testimonial { Quote = "Fieldquote", Author = "Fieldauthor", AuthorTitle = "Fieldauthortitle", SourceUrl = "https://example.org/s", Featured = true, SortOrder = 1 });
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
        var client = app.NewClient();

        var detail = Plain(await G.GetAsync(client, "/projects/fields"));
        foreach (var word in new[] { "Fieldtitle", "Fieldrole", "Fieldorg", "Fieldperiod", "Fieldsummary", "Fieldproblem", "Fieldapproach", "Fieldimpactone", "Fieldimpacttwo", "+7%", "Fieldtech", "Fieldcoveralt", "Fieldcaption", "Fieldlink", "Fieldproof", "https://example.org/p" })
            Assert.Contains(word, detail);
        var home = Plain(await G.GetAsync(client, "/"));
        var projects = Plain(await G.GetAsync(client, "/projects"));
        foreach (var page in new[] { home, projects })
            foreach (var word in new[] { "Fieldtitle", "Fieldorg", "Fieldcategory", "Fieldsummary", "Fieldtech", "Fieldcoveralt", "+7%", "Fieldimpactone" })
                Assert.Contains(word, page);
        Assert.Contains("Fieldcategory", projects); // and the filter has it once there is more than one category
        foreach (var word in new[] { "Fieldquote", "Fieldauthor", "Fieldauthortitle", "https://example.org/s", "Fieldorg" }) Assert.Contains(word, home);
        // Only the first result is on the card; the others are on the case study.
        Assert.DoesNotContain("Fieldimpacttwo", home + projects);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task TheClientStrip_ListsEachDistinctClientOnce_InWorkOrder_AsPlainTextWordmarks(string key)
    {
        using var app = await G.SiteAsync(key, "blank");
        using (var db = app.CreateDbContext())
        {
            db.Projects.AddRange(
                new Project { Title = "A", Slug = "a", Summary = "s", Org = "Zed <b>Corp</b>", SortOrder = 1 },
                new Project { Title = "B", Slug = "b", Summary = "s", Org = "  Acme  ", SortOrder = 2 },
                new Project { Title = "C", Slug = "c", Summary = "s", Org = "zed <b>corp</b>", SortOrder = 3 },
                new Project { Title = "D", Slug = "d", Summary = "s", Org = "   ", SortOrder = 4 },
                new Project { Title = "E", Slug = "e", Summary = "s", Org = null, SortOrder = 5 },
                new Project { Title = "F", Slug = "f", Summary = "s", Org = new string('w', 90), SortOrder = 6 });
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();

        var home = await G.GetAsync(app.NewClient(), "/");
        var strip = Regex.Match(home, "<section id=\"clients\".*?</section>", RegexOptions.Singleline).Value;
        var names = Regex.Matches(strip, @"<li[^>]*>(?:<span[^>]*></span>)?(.*?)</li>").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();

        Assert.Equal(["Zed <b>Corp</b>", "Acme", new string('w', 90)], names);
        Assert.Contains("Zed &lt;b&gt;Corp&lt;/b&gt;", strip);
        Assert.Contains(G.Escaped(G.Definition(key).Copy.Home.ClientsHeadingText()), strip);
        Assert.Matches(@"<span class=""mr-2 inline-block h-2 w-2 rotate-45 bg-accent"" aria-hidden=""true""></span>", strip); // the mark is decoration
        Assert.DoesNotMatch(@"<img|<svg", strip);                                                                              // text wordmarks only, never a logo picture
        Assert.Contains("min-w-0 max-w-full break-words", strip);
    }

    // ---------------------------------------------------------------- hostile text, video, bad addresses

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task HostileTextIsEncoded_VideoIsALinkNeverAPlayer_AndLongWordsWrap(string key)
    {
        using var app = await G.SiteAsync(key, "full");
        var copy = G.Definition(key).Copy.Projects;
        var client = app.NewClient();
        var projects = await G.GetAsync(client, "/projects");
        var home = await G.GetAsync(client, "/");
        var film = await G.GetAsync(client, "/projects/film-reel");
        var overflow = await G.GetAsync(client, "/projects/overflow");

        Assert.DoesNotContain("<script>alert(1)", projects);
        Assert.Contains("Film &quot;reel&quot; &lt;b&gt;&amp; more", projects);
        Assert.DoesNotContain("src=\"/media/", projects + home + film);
        Assert.DoesNotMatch(@"<(video|iframe|source)\b", projects + home + film);
        Assert.Matches($@"<a href=""/media/reel\.mp4"" target=""_blank"" rel=""noopener""[^>]*>{Regex.Escape(G.Escaped(copy.WatchVideoText()))}</a>", projects);
        Assert.Matches($@"<a href=""/media/clip\.webm"" target=""_blank"" rel=""noopener""[^>]*>A clip — {Regex.Escape(G.Escaped(copy.WatchVideoText()))}</a>", film);
        Assert.Equal(2, Regex.Matches(film, "data-lightbox-item").Count);
        var odd = await G.GetAsync(client, "/projects/odd");
        Assert.DoesNotContain("evil.example", odd);

        // Long unbroken words wrap: the layout's own utility classes are on every place such text lands.
        var cat = new string('c', 60);
        Assert.Contains($"<p class=\"break-words text-[11px] uppercase tracking-wider text-muted\">{cat}</p>", projects);
        Assert.Matches($@"class=""filter-chip max-w-full break-words"" data-filter=""{cat}""", projects);
        Assert.Contains($"<p class=\"mt-1 break-words text-xs text-muted\">{new string('o', 60)}</p>", projects); // the client line
        Assert.Contains("<p class=\"mt-3 min-w-0 break-words border-l-2 border-accent pl-3\" data-results-lead>", projects);
        Assert.Contains("<span class=\"block break-words text-sm text-text\">" + new string('i', 80) + "</span>", projects); // a result with no leading figure is shown whole
        Assert.Contains("<span class=\"block break-words text-lg leading-snug text-text\">" + new string('i', 80) + "</span>", overflow);
        foreach (var page in new[] { projects, home, overflow })
        {
            Assert.DoesNotMatch(@"class=""tag""", page);
            Assert.Contains("class=\"tag max-w-full break-words\"", page);
        }
        Assert.Contains("<p class=\"mt-3 break-words font-mono text-xs text-muted\">", overflow);
        Assert.Contains("class=\"group relative mx-auto flex w-full min-w-0 max-w-[var(--results-card-max,40rem)] flex-col\"", projects);
        Assert.Matches(@"<div class=""flex h-full w-full items-end p-5"" aria-hidden=""true"">\s*<span[^>]*>NP</span>", projects);
        Assert.DoesNotMatch(@"<div class=""flex h-full w-full items-end p-5""(?! aria-hidden=""true"")", projects + home);
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task AFigureTooLongToSetLarge_IsShownAsPlainText_AndAScriptInAResultIsEncoded(string key)
    {
        using var app = await G.SiteAsync(key, "blank");
        using (var db = app.CreateDbContext())
        {
            db.Projects.Add(new Project { Title = "Odd results", Slug = "odd-results", Summary = "s", Featured = true, SortOrder = 1,
                Impact = ["12345678901234567890 reads", "<script>alert(1)</script> and +5%", "7% <b>bold</b>", "   "] });
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();

        var detail = await G.GetAsync(app.NewClient(), "/projects/odd-results");
        var list = Regex.Match(detail, "<ul[^>]*data-results-impact.*?</ul>", RegexOptions.Singleline).Value;

        Assert.Equal(3, Regex.Matches(list, "<li ").Count); // the blank line is not a result
        Assert.Contains(">12345678901234567890 reads<", list);
        Assert.Single(Regex.Matches(list, "font-display text-4xl")); // only "7%" is set large
        Assert.DoesNotContain("<script>alert", detail);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; and &#x2B;5%", list);
        Assert.Contains("&lt;b&gt;bold&lt;/b&gt;", list);
    }

    // ---------------------------------------------------------------- labels come from the manifest

    private static string UniqueWordsDir(string baseKey, string newKey)
    {
        return TemplateDirs.WithExtra(newKey, node =>
        {
            node["name"] = "Zork Results";
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
    public async Task TheResultsPartials_ReadEveryLabelMessageAndOptionalWordFromTheManifest(string key)
    {
        var original = G.Definition(key);
        var dir = UniqueWordsDir(key, "zork-results");
        try
        {
            using var app = AdminApp.WithTemplates(dir);
            var owner = await ContentFlow.BlankSiteAsync(app, "zork-results");
            var client = app.NewClient();
            var item = "Zorkitemprojects";
            var work = "zorkwordprojects";

            var blankHome = await G.GetAsync(client, "/");
            var blankProjects = await G.GetAsync(client, "/projects");
            Assert.Contains("zork-home-workEmptyTitle", blankHome);
            Assert.Contains("zork-home-workEmptyBody", blankHome);
            Assert.Contains("zork-projects-emptyTitle", blankProjects);
            Assert.Contains("zork-projects-emptyBody", blankProjects);
            Assert.Contains("zork-projects-heading", blankProjects);
            Assert.Contains(">zork-home-downloadCv<", blankHome); // the hero buttons are the template's words
            var missing = await G.GetAsync(client, "/projects/none", HttpStatusCode.NotFound);
            Assert.Contains($"{item} not found", missing);
            Assert.Contains($"← Back to {work}", missing);

            using (var db = app.CreateDbContext())
            {
                var profile = await db.SiteProfiles.SingleAsync();
                profile.LinkedIn = "https://www.linkedin.com/in/example/";
                db.Projects.AddRange(
                    new Project { Title = "Alpha", Slug = "alpha", Summary = "First.", Category = "Cat one", Org = "Orgone", Tech = ["Tooling"], Impact = ["+9% Gain"], HeroImage = "/img/projects/telemetry.svg", Featured = true, SortOrder = 1, Screenshots = [new ProjectImage { Url = "/img/projects/telemetry-1.svg" }, new ProjectImage { Url = "/img/projects/telemetry-2.svg", Caption = "Captioned" }] },
                    new Project { Title = "Beta", Slug = "beta", Summary = "Second.", Category = "Cat two", HeroImage = "/img/projects/ledger.svg", Featured = true, SortOrder = 2, Problem = "A problem." },
                    new Project { Title = "Gamma", Slug = "gamma", Summary = "Third.", SortOrder = 3 });
                db.Skills.Add(new Skill { Name = "Skilled", Group = "Research", SortOrder = 1 });
                await db.SaveChangesAsync();
            }
            app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();

            var home = await G.GetAsync(client, "/");
            var list = await G.GetAsync(client, "/projects");
            var alpha = await G.GetAsync(client, "/projects/alpha");
            var beta = await G.GetAsync(client, "/projects/beta");
            var gamma = await G.GetAsync(client, "/projects/gamma");

            foreach (var word in new[] { "zork-home-workEyebrow", "zork-home-workHeading", "zork-home-skillsHeading", "zork-home-contactHeading", "zork-home-viewWork", "zork-home-allWork", "zork-home-clientsHeading", "zork-projects-readItem" })
                Assert.Contains(word, home);
            Assert.Contains(">zork-home-downloadCv<", home);
            Assert.Contains(">zork-home-linkedIn<", home);
            foreach (var word in new[] { "zork-projects-eyebrow", "zork-projects-heading", "zork-projects-lead", "zork-projects-featuredBadge", "zork-projects-readItem", ">zork-projects-filterAll<" }) Assert.Contains(word, list);
            Assert.Contains("aria-label=\"zork-projects-filterLabel\"", list);
            foreach (var word in new[] { "zork-projects-problemHeading", "zork-projects-stackHeading", "zork-projects-screenshotsHeading" }) Assert.Contains(word, alpha + beta);
            Assert.Contains(item, alpha);
            Assert.Contains($"← {work}", alpha);
            Assert.Contains(">zork-projects-nextItem<", alpha);
            Assert.Contains("aria-label=\"zork-projects-galleryLabel\"", alpha);
            Assert.Contains("aria-label=\"zork-projects-openImageLabel: Captioned\"", alpha);
            Assert.Contains("alt=\"zork-projects-imageAlt Alpha\"", alpha);
            Assert.Contains("zork-projects-noWriteup", gamma);

            // None of the template's real wording survives anywhere on the pages the partials draw.
            var visible = WebUtility.HtmlDecode(DemoSite.WithoutScripts(string.Concat(home, list, alpha, beta, gamma, blankHome, blankProjects, missing)));
            var words = original.Copy.Home.GetType().GetProperties().Select(p => (string?)p.GetValue(original.Copy.Home))
                .Concat(original.Copy.Projects.GetType().GetProperties().Select(p => (string?)p.GetValue(original.Copy.Projects)))
                .Where(s => !string.IsNullOrWhiteSpace(s) && !s.Contains("{title}") && s.Contains(' ')).Distinct().ToList();
            Assert.NotEmpty(words);
            foreach (var w in words) Assert.DoesNotContain(w!, visible);
            foreach (var w in new[] { original.Copy.Home.DownloadCv, original.Copy.Home.LinkedIn, original.Copy.Home.ClientsHeading, original.Copy.Projects.FilterAll })
                Assert.DoesNotContain(w, visible);
            Assert.DoesNotContain("Case study", visible);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task TheOptionalWords_FallBackToThePlainEnglishDefaults_WhenTheManifestLeavesThemOut(string key)
    {
        var dir = TemplateDirs.WithExtra("plain-results", node =>
        {
            node["name"] = "Plain Results";
            node["sections"]!.AsArray().Single(s => s!["key"]!.GetValue<string>() == "projects")!["label"] = "zorkwork";
            node["sections"]!.AsArray().Single(s => s!["key"]!.GetValue<string>() == "projects")!["itemLabel"] = "Zorkitem";
            foreach (var (group, name) in new[] { ("home", "viewWork"), ("home", "allWork"), ("home", "downloadCv"), ("home", "linkedIn"), ("home", "clientsHeading"), ("projects", "nextItem"), ("projects", "readItem"), ("projects", "watchVideo"), ("projects", "filterAll") })
                node["copy"]![group]!.AsObject().Remove(name);
        }, baseTemplate: key, starterFrom: key);
        try
        {
            using var app = AdminApp.WithTemplates(dir);
            await ContentFlow.BlankSiteAsync(app, "plain-results");
            using (var db = app.CreateDbContext())
            {
                (await db.SiteProfiles.SingleAsync()).LinkedIn = "https://www.linkedin.com/in/example/";
                db.Projects.AddRange(
                    new Project { Title = "Alpha", Slug = "alpha", Summary = "First.", Featured = true, SortOrder = 1, Org = "Orgone", Category = "One", HeroImage = "/media/a.mp4" },
                    new Project { Title = "Beta", Slug = "beta", Summary = "Second.", Featured = true, SortOrder = 2, Category = "Two" });
                await db.SaveChangesAsync();
            }
            app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();
            var client = app.NewClient();

            var home = await G.GetAsync(client, "/");
            Assert.Contains(">View zorkwork<", home);
            Assert.Contains(">All zorkwork →<", home);
            Assert.Contains(">Download CV<", home);
            Assert.Contains(">LinkedIn<", home);
            Assert.Contains(">Worked with<", home);
            Assert.Contains("Read zorkitem →", home);
            Assert.Contains(">Watch the video ↗<", home);
            Assert.Contains(">All</button>", await G.GetAsync(client, "/projects"));
            var detail = await G.GetAsync(client, "/projects/alpha");
            Assert.Contains(">Next zorkitem<", detail);
            Assert.Contains("aria-label=\"Next zorkitem\"", detail);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void TheOptionalWords_DefaultToEnglish_AndUseTheirOwnTextWhenSet()
    {
        var home = new HomeCopy();
        var projects = new ProjectsCopy();

        Assert.Equal(("Download CV", "LinkedIn", "Worked with"), (home.DownloadCvText(), home.LinkedInText(), home.ClientsHeadingText()));
        Assert.Equal("All", projects.FilterAllText());
        home = new HomeCopy { DownloadCv = " ", LinkedIn = "", ClientsHeading = "\t" };
        Assert.Equal(("Download CV", "LinkedIn", "Worked with"), (home.DownloadCvText(), home.LinkedInText(), home.ClientsHeadingText()));
        home = new HomeCopy { DownloadCv = "Get the CV", LinkedIn = "Profile", ClientsHeading = "Clients" };
        Assert.Equal(("Get the CV", "Profile", "Clients"), (home.DownloadCvText(), home.LinkedInText(), home.ClientsHeadingText()));
        Assert.Equal("Everything", new ProjectsCopy { FilterAll = "Everything" }.FilterAllText());
    }

    [Fact]
    public void EveryOptionalWord_IsInTheSchema_AndTheTokenRuleTable()
    {
        var schema = File.ReadAllText(Path.Combine(G.TemplatesDir, "template.schema.json"));
        foreach (var path in new[] { "copy.home.downloadCv", "copy.home.linkedIn", "copy.home.clientsHeading", "copy.projects.filterAll" })
        {
            Assert.True(CopyText.AllowedTokens.ContainsKey(path), $"{path} has no token rule");
            Assert.Empty(CopyText.AllowedTokens[path]);
            Assert.Contains($"\"{path[(path.LastIndexOf('.') + 1)..]}\"", schema);
        }
    }

    // ---------------------------------------------------------------- accessibility and security

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task TheLayout_HasLandmarksFocusReducedMotionAndHiddenDecoration_AndTheStylesheetKeepsBothColourModes(string key)
    {
        using var app = await G.SiteAsync(key, "sample");
        var client = app.NewClient();
        var theme = G.Definition(key).Theme.Default!;
        var home = await G.GetAsync(client, "/");
        var projects = await G.GetAsync(client, "/projects");
        using var db = app.CreateDbContext();
        var project = await db.Projects.Where(p => p.Screenshots.Count > 0).OrderBy(p => p.SortOrder).FirstAsync();
        var detail = await G.GetAsync(client, "/projects/" + project.Slug);

        foreach (var page in new[] { home, projects, detail })
        {
            Assert.Matches(@"<header\b", page);
            Assert.Single(Regex.Matches(page, "<main\\b"));
            Assert.Matches(@"<footer\b", page);
            Assert.Matches(@"<a [^>]*href=""#main-content""", page);   // the skip link
            Assert.Matches(@"<nav\b[^>]*>", page);
        }
        // Decoration is hidden from screen readers: the read-more cue, the client diamond and the initials tile; the picture's visible caption is read once, through the button.
        Assert.Matches(@"<p class=""mt-3 text-xs text-muted group-hover:text-accent"" aria-hidden=""true"">", home);
        Assert.DoesNotMatch(@"<p class=""mt-3 text-xs text-muted group-hover:text-accent""(?! aria-hidden)", home + projects);
        Assert.Matches(@"inline-block h-2 w-2 rotate-45 bg-accent"" aria-hidden=""true""", home);
        foreach (var s in project.Screenshots)
        {
            Assert.Contains($"aria-label=\"{G.Definition(key).Copy.Projects.OpenImageLabel}: {WebUtility.HtmlEncode(s.Caption!)}\"", detail);
            Assert.Matches($@"<figcaption[^>]*aria-hidden=""true""[^>]*>{Regex.Escape(WebUtility.HtmlEncode(s.Caption!))}</figcaption>", detail);
        }
        Assert.Matches(@"role=""img"" aria-label=""[^""]+"">", home); // the initials tile standing in for a photo has a name
        Assert.Contains("motion-reduce:transition-none", home);
        Assert.Contains("motion-reduce:group-hover:scale-100", detail);
        // One stretched link per card: one tab stop, no nested links.
        foreach (Match card in Regex.Matches(projects, "<article class=\"group relative.*?</article>", RegexOptions.Singleline))
            Assert.Single(Regex.Matches(card.Value, "<a href=\"/projects/"));

        var css = File.ReadAllText(Path.Combine(G.Wwwroot, "css", "app.css"));
        Assert.Contains(":focus-visible", css);
        Assert.Matches(@"@media \(prefers-reduced-motion:\s*reduce\)\{[^@]*?\.motion-reduce\\:transition-none", css);
        Assert.Matches($@"\[data-template=[""']?{Regex.Escape(theme)}[""']?\]\[data-theme=[""']?light", css);
        Assert.Matches($@"\[data-template=[""']?{Regex.Escape(theme)}[""']?\]\[data-theme=[""']?dark", css);
        Assert.Contains("line-clamp", css);
        Assert.Contains("var(--results-band-min,11rem)", css.Replace("\\2c ", ","));
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void TheThemeSetsOnlyVariables_WithBothModes_AndTheTokenContrastThatTheColourTestsAlsoCheck(string key)
    {
        var theme = G.Definition(key).Theme.Default!;
        var css = File.ReadAllText(Path.Combine(TemplateDirs.Web, "Styles", "themes", theme + ".css"));

        Assert.Contains($":root[data-template=\"{theme}\"][data-theme=\"light\"]", css);
        Assert.Contains($":root[data-template=\"{theme}\"][data-theme=\"dark\"]", css);
        foreach (var token in new[] { "--c-bg", "--c-surface", "--c-surface-2", "--c-border", "--c-text", "--c-muted", "--c-accent", "--c-accent-strong" })
            Assert.Equal(2, Regex.Matches(css, Regex.Escape(token) + ":").Count); // once for dark, once for light
        // The layout variables it sets always have a fallback in the layout, so a theme that leaves one out still renders.
        foreach (Match v in Regex.Matches(css, @"(--results-[a-z-]+):"))
            foreach (var file in Directory.EnumerateFiles(ResultsLayouts, "*.cshtml"))
                if (File.ReadAllText(file).Contains($"var({v.Groups[1].Value}")) Assert.Matches($@"var\({Regex.Escape(v.Groups[1].Value)},[^)]+\)", File.ReadAllText(file));
        foreach (var file in Directory.EnumerateFiles(ResultsLayouts, "*.cshtml"))
            Assert.DoesNotMatch(@"var\(--results-[a-z-]+\)", File.ReadAllText(file)); // never without a fallback
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public async Task APublicResultsPage_HasTheSameSecurityHeadersAsSoftware_AndNoThirdPartyScriptOrFrame(string key)
    {
        using var results = await G.SiteAsync(key, "sample");
        using var software = new AdminApp();
        await DemoSite.SeedAsync(software);
        var (project, _) = DemoSite.FirstSlugs(software);

        foreach (var (resultsPath, softwarePath) in new[] { ("/", "/"), ("/projects", "/projects"), ("/projects/" + await G.FirstSlugAsync(results), "/projects/" + project) })
        {
            var g = await results.NewClient().GetAsync(resultsPath);
            var s = await software.NewClient().GetAsync(softwarePath);
            string[] volatileHeaders = ["Date", "ETag", "Content-Length", "Age", "Last-Modified", "Expires"];
            string Flat(HttpResponseMessage r) => string.Join("\n", r.Headers.Concat(r.Content.Headers)
                .Where(h => !volatileHeaders.Contains(h.Key, StringComparer.OrdinalIgnoreCase))
                .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase).Select(h => h.Key + ": " + string.Join(",", h.Value)));
            Assert.Equal(Flat(s), Flat(g));
            var html = await g.Content.ReadAsStringAsync();
            Assert.DoesNotContain("<iframe", html);
            Assert.DoesNotMatch(@"<script[^>]*\ssrc=""https?://", html);
            Assert.DoesNotMatch(@"<script\b(?![^>]*type=""application/ld)", G.MainOf(html));
        }
    }

    [Theory]
    [MemberData(nameof(KeyData))]
    public void AResultsTemplate_DoesNotInheritSoftwareWording(string key)
    {
        var software = new TemplateCatalog(G.TemplatesDir).Get("software");
        var template = G.Definition(key);
        var neutral = new[] { "Skills", "Experience", "Links", "featured", "View certificate", "Curriculum vitae — {name}, {title}.", "Selected work" };

        foreach (var group in typeof(TemplateCopy).GetProperties())
            foreach (var prop in group.PropertyType.GetProperties().Where(p => p.PropertyType == typeof(string)))
            {
                var a = (string?)prop.GetValue(group.GetValue(software.Copy));
                var b = (string?)prop.GetValue(group.GetValue(template.Copy));
                if (neutral.Contains(b) || string.IsNullOrEmpty(b)) continue;
                Assert.True(a != b, $"copy.{group.Name}.{prop.Name} is identical to software's: {b}");
            }
        Assert.DoesNotMatch(@"\.NET|GitHub|software|developer|clinical|patient|licen[cs]e", System.Text.Json.JsonSerializer.Serialize(template.Copy));
    }
}
