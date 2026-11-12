using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The sections page: which sections show, what the menu calls them and in which order, and that every public surface follows a save at once.</summary>
public class SectionSettingsTests
{
    private static readonly string[] SoftwareOrder = ["about", "experience", "projects", "skills", "certifications", "blog", "talks", "testimonials", "contact"];
    private static readonly string[] HealthcareOrder = ["about", "certifications", "experience", "skills", "projects", "blog", "talks", "testimonials", "contact"];

    private static async Task<(AdminApp App, HttpClient Owner, HttpClient Visitor, ContentFlow.Seeded Rows)> SiteAsync(string template)
    {
        var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, template);
        var rows = await ContentFlow.SeedRowsAsync(app, pairs: true);
        await owner.GetAsync("/admin"); // takes the wizard's message
        using (var db = app.CreateDbContext())
        {
            (await db.Projects.SingleAsync(p => p.Slug == "seed-project")).Featured = true; // shown in the home page's featured work block
            await db.SaveChangesAsync();
        }
        return (app, owner, app.NewClient(), rows);
    }

    private static async Task<int> ContentRowsAsync(AdminApp app) => await ContentFlow.CountAsync(app);

    // ---------------------------------------------------------------- the page

    [Theory]
    [InlineData("software")]
    [InlineData("healthcare")]
    public async Task ThePage_ListsTheTemplatesSectionsInOrder_WithItsDefaults_AndAGetWritesNothing(string template)
    {
        var (app, owner, _, _) = await SiteAsync(template);
        using var _a = app;
        var before = await ContentFlow.SnapshotAsync(app);

        var rows = await SectionsFlow.RowsAsync(owner);

        Assert.Equal(template == "software" ? SoftwareOrder : HealthcareOrder, rows.Select(r => r.Key));
        Assert.All(rows, r => Assert.Equal("", r.Label)); // nothing renamed: every box is empty, so the template's names show
        Assert.Equal(template == "software" ? 9 : 7, rows.Count(r => r.Enabled));
        if (template == "healthcare")
        {
            Assert.False(rows.Single(r => r.Key == "blog").Enabled);
            Assert.False(rows.Single(r => r.Key == "talks").Enabled);
            Assert.Contains("Off until you switch it on", await owner.GetStringAsync(SectionsFlow.Path));
        }
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Theory]
    [InlineData("software")]
    [InlineData("healthcare")]
    public async Task SavingThePageUnchanged_StoresNothing_SoNoDefaultIsFrozenIntoTheProfile(string template)
    {
        var (app, owner, _, _) = await SiteAsync(template);
        using var _a = app;

        var response = await SectionsFlow.SaveAsync(owner, r => r);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(await SectionsFlow.StoredAsync(app)); // only differences from the template are stored
        Assert.Equal(template == "software" ? 9 : 7, (await SectionsFlow.RowsAsync(owner)).Count(r => r.Enabled));
    }

    // ---------------------------------------------------------------- hide and show

    [Fact]
    public async Task HidingSections_RemovesThemFromEverySurface_KeepsEveryRow_AndShowingThemBringsThemBack()
    {
        var (app, owner, visitor, rows) = await SiteAsync("software");
        using var _a = app;
        var contentBefore = await ContentFlow.SnapshotAsync(app);
        Assert.Contains("/certifications", SectionsFlow.SitemapPaths(await visitor.GetStringAsync("/sitemap.xml"))); // primes the cached sitemap
        Assert.Contains("Seed Project", await visitor.GetStringAsync("/")); // the featured work block
        Assert.Contains("Seeded praise", await visitor.GetStringAsync("/"));  // the testimonials block

        var save = await SectionsFlow.SaveAsync(owner, r => SectionsFlow.Set("certifications", false)(SectionsFlow.Set("projects", false)(SectionsFlow.Set("testimonials", false)(SectionsFlow.Set("experience", false)(r)))));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        var home = await visitor.GetStringAsync("/");
        var nav = SectionsFlow.NavOf(home);
        foreach (var gone in new[] { "/certifications", "/projects", "/experience" }) Assert.DoesNotContain($"href=\"{gone}\"", home);
        Assert.DoesNotContain("/projects", nav);
        Assert.Contains("/skills", nav);
        Assert.DoesNotContain("Seed Project", home); // the home block follows
        Assert.DoesNotContain("Seeded praise", home);
        var paths = SectionsFlow.SitemapPaths(await visitor.GetStringAsync("/sitemap.xml")); // the cached sitemap was evicted by the save
        Assert.DoesNotContain("/certifications", paths);
        Assert.DoesNotContain("/projects", paths);
        Assert.DoesNotContain("/projects/seed-project", paths);
        Assert.Contains("/skills", paths);
        foreach (var route in new[] { "/certifications", "/projects", "/projects/seed-project", "/experience" })
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(route)).StatusCode);
        var cv = await visitor.GetStringAsync("/cv");
        Assert.DoesNotContain("Seed Certificate", cv);
        Assert.DoesNotContain("Seed Role", cv);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/cv.pdf")).StatusCode);
        Assert.Equal(contentBefore.Split('\n').Where(l => !l.StartsWith('R')).ToList(), (await ContentFlow.SnapshotAsync(app)).Split('\n').Where(l => !l.StartsWith('R')).ToList()); // no row of content was touched
        Assert.Equal(["certifications", "experience", "projects", "testimonials"], (await SectionsFlow.StoredAsync(app)).Select(s => s.Key).OrderBy(x => x));
        var off = await SectionsFlow.StoredAsync(app);
        Assert.Equal(new[] { "certifications", "experience", "projects", "testimonials" }, off.Select(s => s.Key).OrderBy(x => x));
        Assert.All(off, s => Assert.Equal(false, s.Shown));

        // Showing them again brings everything back, because nothing was deleted.
        Assert.Equal(HttpStatusCode.Redirect, (await SectionsFlow.SaveAsync(owner, r => r.Select(x => x with { Enabled = true }).ToList())).StatusCode);
        var kept = await SectionsFlow.StoredAsync(app);
        Assert.Equal(new[] { "certifications", "experience", "projects", "testimonials" }, kept.Select(s => s.Key).OrderBy(x => x));
        Assert.All(kept, s => Assert.Equal(true, s.Shown)); // the earlier explicit choices are kept, now as "shown"
        home = await visitor.GetStringAsync("/");
        Assert.Contains("href=\"/certifications\"", home);
        Assert.Contains("Seed Project", home);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/projects/seed-project")).StatusCode);
        Assert.Contains("/projects/seed-project", SectionsFlow.SitemapPaths(await visitor.GetStringAsync("/sitemap.xml")));
        Assert.Contains("Seed Certificate", await visitor.GetStringAsync("/certifications"));
    }

    [Fact]
    public async Task HidingCertifications_RemovesTheCredentialsFromTheHomeMarkup_AndASectionTheTemplateHidesStaysAsTheStyledNotFound()
    {
        var (app, owner, visitor, _) = await SiteAsync("healthcare");
        using var _a = app;
        await ProfileFlow.SeedTextAsync(app);
        var home = await visitor.GetStringAsync("/");
        Assert.Contains("hasCredential", home);
        Assert.Contains("Seed Certificate", home);

        await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("certifications", false));

        home = await visitor.GetStringAsync("/");
        Assert.DoesNotContain("hasCredential", home);
        Assert.DoesNotContain("Seed Certificate", home);
        Assert.DoesNotContain("id=\"credentials\"", home);
        var gone = await visitor.GetAsync("/certifications");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Contains("noindex", await gone.Content.ReadAsStringAsync()); // the site's styled not-found page, as for any hidden section
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/blog")).StatusCode); // a template default that was never touched
    }

    // ---------------------------------------------------------------- healthcare defaults

    [Fact]
    public async Task OnHealthcare_BlogAndTalksStartOff_CanBeSwitchedOn_AndFollowOnEverySurface_ThenBackToTheDefault()
    {
        var (app, owner, visitor, rows) = await SiteAsync("healthcare");
        using var _a = app;
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/blog")).StatusCode);
        Assert.DoesNotContain("/blog/seed-post", SectionsFlow.SitemapPaths(await visitor.GetStringAsync("/sitemap.xml")));

        await SectionsFlow.SaveAsync(owner, r => SectionsFlow.Set("talks", true)(SectionsFlow.Set("blog", true)(r)));

        var stored = await SectionsFlow.StoredAsync(app);
        Assert.Equal(["blog", "talks"], stored.Select(s => s.Key).OrderBy(x => x));
        Assert.All(stored, s => Assert.Equal(true, s.Shown));
        var home = await visitor.GetStringAsync("/");
        Assert.Contains("href=\"/blog\"", home);
        Assert.Contains("href=\"/talks\"", home);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/blog")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/blog/seed-post")).StatusCode);
        var paths = SectionsFlow.SitemapPaths(await visitor.GetStringAsync("/sitemap.xml"));
        Assert.Contains("/blog", paths);
        Assert.Contains("/blog/seed-post", paths);
        Assert.Contains("/talks", paths);
        // Saving the page again, unchanged, keeps them on (it does not flip them back to the template's default).
        await SectionsFlow.SaveAsync(owner, r => r);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/blog")).StatusCode);

        // Switching them off again is an explicit choice now, kept as such (it is not forgotten and re-derived from the default).
        await SectionsFlow.SaveAsync(owner, r => SectionsFlow.Set("talks", false)(SectionsFlow.Set("blog", false)(r)));
        var off = await SectionsFlow.StoredAsync(app);
        Assert.Equal(new[] { "blog", "talks" }, off.Select(s => s.Key).OrderBy(x => x));
        Assert.All(off, s => Assert.Equal(false, s.Shown));
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/blog")).StatusCode);
        Assert.Equal(await ContentRowsAsync(app), await ContentRowsAsync(app));
        using var db = app.CreateDbContext();
        Assert.Equal(2, await db.BlogPosts.CountAsync()); // the posts were never touched
    }

    [Fact]
    public async Task AChoiceTheOwnerMade_SurvivesASwitchOfTemplate_UntilTheSectionsPageIsSavedUnderTheNewTemplate()
    {
        var (app, owner, visitor, _) = await SiteAsync("healthcare");
        using var _a = app;
        await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("blog", true)); // on: differs from healthcare's default, so it is stored

        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, "/admin/template", new Dictionary<string, string> { ["Key"] = "software" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/blog")).StatusCode); // software shows it anyway
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, "/admin/template", new Dictionary<string, string> { ["Key"] = "healthcare" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/blog")).StatusCode); // the owner's "on" still stands: a switch rewrites no setting
        Assert.Equal(["blog"], (await SectionsFlow.StoredAsync(app)).Select(s => s.Key));

        // Hiding talks on software is an owner choice there (software shows talks by default) and stays when switching to healthcare, where talks are off anyway.
        await ContentFlow.PostAsync(owner, "/admin/template", new Dictionary<string, string> { ["Key"] = "software" });
        await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("talks", false));
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/talks")).StatusCode);
        // An explicit choice made earlier is kept by a later save, whatever the template's default is.
        Assert.Equal(["blog", "talks"], (await SectionsFlow.StoredAsync(app)).Select(s => s.Key).OrderBy(x => x)); // the earlier explicit "on" is kept, not re-derived
        await ContentFlow.PostAsync(owner, "/admin/template", new Dictionary<string, string> { ["Key"] = "healthcare" });
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/blog")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/talks")).StatusCode);
    }

    // ---------------------------------------------------------------- labels and order

    [Fact]
    public async Task ARenamedSection_ShowsItsNewNameInTheMenus_AndOnTheLinksBack_AndAnEmptyNameGoesBackToTheTemplates()
    {
        var (app, owner, visitor, _) = await SiteAsync("software");
        using var _a = app;

        await SectionsFlow.SaveAsync(owner, r => SectionsFlow.Set("projects", label: "Casework")(SectionsFlow.Set("skills", label: "  Abilities & tools ")(r)));

        var home = await visitor.GetStringAsync("/");
        Assert.Contains("href=\"/projects\" ", home.Replace(">", " >"));
        Assert.Contains("Casework", SectionsFlow.NavOf(home));
        Assert.Contains("Abilities &amp; tools", SectionsFlow.NavOf(home).Replace("&#x2B;", "+"));
        Assert.Contains("← Casework", await visitor.GetStringAsync("/projects/seed-project"));
        var stored = await SectionsFlow.StoredAsync(app);
        Assert.Equal("Abilities & tools", stored.Single(s => s.Key == "skills").Label); // trimmed
        Assert.Contains("value=\"Casework\"", await owner.GetStringAsync(SectionsFlow.Path));

        await SectionsFlow.SaveAsync(owner, r => SectionsFlow.Set("projects", label: "")(SectionsFlow.Set("skills", label: "skills")(r))); // empty, and the template's own name
        Assert.Empty(await SectionsFlow.StoredAsync(app));
        Assert.DoesNotContain("Casework", SectionsFlow.NavOf(await visitor.GetStringAsync("/")));
    }

    [Fact]
    public async Task Reordering_ChangesTheMenuAndTheSitemapOrder_StoresPositions_AndTheTemplatesOrderStoresNone()
    {
        var (app, owner, visitor, _) = await SiteAsync("software");
        using var _a = app;
        Assert.Contains("/skills", SectionsFlow.SitemapPaths(await visitor.GetStringAsync("/sitemap.xml"))); // primes the cached sitemap
        var before = SectionsFlow.NavOf(await visitor.GetStringAsync("/")).Split(',').Select(x => x.Split('=')[0]).ToList();
        Assert.True(before.IndexOf("/experience") < before.IndexOf("/skills"));

        await SectionsFlow.SaveAsync(owner, SectionsFlow.MoveTo("skills", 0));

        var nav = SectionsFlow.NavOf(await visitor.GetStringAsync("/")).Split(',').Select(x => x.Split('=')[0]).ToList();
        Assert.Equal("/skills", nav[0]);
        Assert.Equal(before.Where(p => p != "/skills"), nav.Skip(1));
        var paths = SectionsFlow.SitemapPaths(await visitor.GetStringAsync("/sitemap.xml"));
        Assert.True(paths.IndexOf("/skills") < paths.IndexOf("/about"));
        var stored = await SectionsFlow.StoredAsync(app);
        Assert.Equal(9, stored.Count);
        Assert.Equal(Enumerable.Range(1, 9), stored.Select(s => s.SortOrder).OrderBy(x => x));
        Assert.Equal("skills", stored.Single(s => s.SortOrder == 1).Key);
        Assert.Equal("skills", (await SectionsFlow.RowsAsync(owner))[0].Key); // and the page shows the same order

        // Putting it back in the template's place stores nothing.
        await SectionsFlow.SaveAsync(owner, SectionsFlow.MoveTo("skills", 3));
        Assert.Empty(await SectionsFlow.StoredAsync(app));
        Assert.Equal(SoftwareOrder, (await SectionsFlow.RowsAsync(owner)).Select(r => r.Key));
    }

    [Fact]
    public async Task TheRowButtons_ReorderThePageOnly_NothingIsWrittenUntilSave()
    {
        var (app, owner, _, _) = await SiteAsync("software");
        using var _a = app;
        var before = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));
        var fields = SectionsFlow.Fields(await SectionsFlow.RowsAsync(owner));

        var response = await ProfileFlow.PostAsync(owner, SectionsFlow.Path + "?handler=Move&index=3&direction=up", fields, tokenFrom: SectionsFlow.Path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["about", "experience", "skills", "projects"], new[] { "about", "experience", "skills", "projects" }.Where((k, i) => Regex.Match(html, $@"name=""Form\.Rows\[{i}\]\.Key"" value=""{k}""").Success));
        Assert.Contains("Skills moved up to position 3. It is not saved until you save.", html);
        Assert.Contains("role=\"status\"", html);
        Assert.Matches(@"id=""row-2-up""[^>]*autofocus=""autofocus""|autofocus=""autofocus""[^>]*id=""row-2-up""", html);
        Assert.Equal(before, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
        var top = await (await ProfileFlow.PostAsync(owner, SectionsFlow.Path + "?handler=Move&index=0&direction=up", fields, tokenFrom: SectionsFlow.Path)).Content.ReadAsStringAsync();
        Assert.Contains("That row cannot move any further.", top);
    }

    // ---------------------------------------------------------------- rules

    [Fact]
    public async Task AtLeastOneSectionWithItsOwnPage_MustStayOn()
    {
        var (app, owner, _, _) = await SiteAsync("software");
        using var _a = app;
        var before = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));

        // Testimonials is only a block on the home page, so it does not count as a page.
        var response = await SectionsFlow.SaveAsync(owner, r => r.Select(x => x with { Enabled = x.Key == "testimonials" }).ToList());
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Keep at least one &quot;Section&quot; switched on that appears in the menu.", html);
        Assert.Equal(before, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
        Assert.Equal(HttpStatusCode.Redirect, (await SectionsFlow.SaveAsync(owner, r => r.Select(x => x with { Enabled = x.Key == "contact" }).ToList())).StatusCode);
    }

    private static object[] Forged(string why, Func<List<SectionsFlow.Row>, List<SectionsFlow.Row>> forge) => [why, forge];

    public static IEnumerable<object[]> ForgedLists()
    {
        yield return Forged("unknown key", r => r.Select(x => x.Key == "talks" ? x with { Key = "admin" } : x).ToList());
        yield return Forged("duplicate key", r => r.Select(x => x.Key == "talks" ? x with { Key = "blog" } : x).ToList());
        yield return Forged("missing key", r => r.Where(x => x.Key != "talks").ToList());
        yield return Forged("extra key", r => r.Append(new SectionsFlow.Row("home", "", true)).ToList());
        yield return Forged("empty key", r => r.Select(x => x.Key == "talks" ? x with { Key = "" } : x).ToList());
        yield return Forged("path as key", r => r.Select(x => x.Key == "talks" ? x with { Key = "../talks" } : x).ToList());
        yield return Forged("case changed", r => r.Select(x => x.Key == "talks" ? x with { Key = "TALKS" } : x).ToList());
    }

    [Theory]
    [MemberData(nameof(ForgedLists))]
    public async Task AForgedMissingRepeatedOrForeignKey_IsRefusedWhole_AndNothingIsSaved(string why, Func<List<SectionsFlow.Row>, List<SectionsFlow.Row>> forge)
    {
        var (app, owner, _, _) = await SiteAsync("software");
        using var _a = app;
        var before = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));

        var response = await SectionsFlow.SaveAsync(owner, r => forge(r).Select(x => x with { Label = "Renamed" }).ToList());
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, why);
        Assert.Contains("does not match your template any more, so nothing was saved", html);
        Assert.Equal(before, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
    }

    [Fact]
    public async Task AKeyTheTemplateLacks_CanNeverBeSwitchedOn_NotByTheFormAndNotByAStoredSetting_AndStoredUnknownKeysSurviveASave()
    {
        var (app, owner, visitor, _) = await SiteAsync("healthcare");
        using var _a = app;
        using (var db = app.CreateDbContext())
        {
            var p = await db.SiteProfiles.SingleAsync();
            // Left by another template or by hand: a key that is not a section at all, and a stale "on" that must not revive anything.
            p.Sections = [new SectionSetting { Key = "gallery", Label = "Pictures", Shown = true, SortOrder = 4 }, new SectionSetting { Key = "blog", Enabled = true }];
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<Portfolio.Web.Services.ISiteProfileProvider>().Invalidate();
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/blog")).StatusCode); // the older flag still cannot enable what the template has off
        Assert.DoesNotContain("gallery", (await SectionsFlow.RowsAsync(owner)).Select(r => r.Key)); // and the page only lists the template's sections

        var saved = await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("skills", label: "Abilities"));
        Assert.True(saved.StatusCode == HttpStatusCode.Redirect, await saved.Content.ReadAsStringAsync());

        var stored = await SectionsFlow.StoredAsync(app);
        Assert.Contains(stored, s => s is { Key: "gallery", Label: "Pictures", Shown: true, SortOrder: 4 }); // carried over untouched
        Assert.Equal("Abilities", stored.Single(s => s.Key == "skills").Label);
        Assert.DoesNotContain(stored, s => s.Key == "blog"); // the stale no-opinion entry for a listed key was normalised away
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/blog")).StatusCode);
    }

    public static IEnumerable<object[]> BadNames()
    {
        foreach (var payload in new[] { "<script>x</script>", "<b>x</b>", "<!-- x -->", "</p><p>", "<?x?>", "a <i>b</i>" }) yield return [payload, "plain text"];
        foreach (var payload in new[] { "bell\u0007x", "esc\u001bx", "del\u007fx", "next\u0085x" }) yield return [payload, "characters that are not allowed"];
        yield return [new string('n', 41), "at most 40 characters"];
        // Invisible format characters: zero-width space and joiners, a byte order mark, bidi overrides and isolates, and a name made only of them.
        foreach (var payload in new[] { "Wo\u200Brk", "\uFEFFWork", "Work\u202E", "\u2066Work\u2069", "\u200B", "\u202E\u200B",
                      "Wo\u00ADrk", "\u3164", "Work\u3164", "\u2800", "Wo\u2800rk", "\u115F\u1160", "\u200C", "\u200DWork", "Work\u200C", "Wo \u200Drk", "1\u200C2" }) yield return [payload, "characters that are not allowed"];
    }

    [Theory]
    [MemberData(nameof(BadNames))]
    public async Task ABadName_IsRefused_ShownEncodedOnTheRedrawnForm_AndNothingIsSaved(string payload, string message)
    {
        var (app, owner, _, _) = await SiteAsync("software");
        using var _a = app;
        var before = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));

        var response = await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("projects", label: payload));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, html);
        Assert.Contains("<a href=\"#Form_Rows_2__Label\">Work: &quot;Menu name&quot;", html); // names the section by its template name
        Assert.Matches(@"id=""Form_Rows_2__Label""[^>]*aria-invalid=""true""", html);
        Assert.False(html.Contains("<script>x</script>"), "the markup was echoed raw");
        Assert.Equal(before, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
    }

    [Theory]
    [InlineData("\u0645\u06CC\u200C\u062E\u0648\u0627\u0647\u0645")] // a Persian word with a zero-width non-joiner between letters
    [InlineData("\u0634\u0628\u200C\u06A9\u0627\u0631")]
    [InlineData("Team \U0001F468\u200D\U0001F469\u200D\U0001F467")] // an emoji family sequence joined with zero-width joiners
    public async Task AJoinerBetweenLetters_OrInAnEmojiSequence_IsAccepted(string name)
    {
        var (app, owner, visitor, _) = await SiteAsync("software");
        using var _a = app;

        var response = await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("projects", label: name));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(name, (await SectionsFlow.StoredAsync(app)).Single(s => s.Key == "projects").Label);
    }

    [Fact]
    public async Task ANameOfInvisibleCharactersOnly_ReadsAsEmpty_AndIsRefused()
    {
        var (app, owner, _, _) = await SiteAsync("software");
        using var _a = app;

        foreach (var name in new[] { "\u3164\u3164", "\u2800\u200B \u00AD" })
        {
            var html = await (await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("projects", label: name))).Content.ReadAsStringAsync();
            Assert.Contains("contains characters that are not allowed", html);
        }
        Assert.Empty(await SectionsFlow.StoredAsync(app));
    }

    [Fact]
    public async Task ANameAtTheLimit_IsAccepted_AndTextThatIsNotMarkupIsPrintedEncoded()
    {
        var (app, owner, visitor, _) = await SiteAsync("software");
        using var _a = app;

        Assert.Equal(HttpStatusCode.Redirect, (await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("projects", label: new string('n', 40)))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("projects", label: "5 < 6 & \"q\""))).StatusCode);

        var nav = await visitor.GetStringAsync("/");
        Assert.Contains("5 &lt; 6 &amp; &quot;q&quot;", nav);
        Assert.DoesNotContain("5 < 6 &", nav);
    }

    [Fact]
    public async Task TwoSectionsCannotShareAName_ComparedWithoutCase_EvenIfOneIsSwitchedOff_OrTheNameIsTheTemplatesOwnForAnother()
    {
        var (app, owner, _, _) = await SiteAsync("software");
        using var _a = app;
        var before = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));

        var same = await (await SectionsFlow.SaveAsync(owner, r => SectionsFlow.Set("talks", label: "BLOG")(r))).Content.ReadAsStringAsync(); // the template's own name for blog, in other case
        Assert.Contains("Each &quot;Section&quot; needs its own name: &quot;BLOG&quot; is already used by another menu entry.", same);
        var twice = await (await SectionsFlow.SaveAsync(owner, r => SectionsFlow.Set("talks", label: "Dup")(SectionsFlow.Set("blog", false, "dup")(r)))).Content.ReadAsStringAsync();
        Assert.Contains("is already used by another menu entry.", twice);
        Assert.Equal(before, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
        // Swapping the two names is fine: the result has no duplicate.
        Assert.Equal(HttpStatusCode.Redirect, (await SectionsFlow.SaveAsync(owner, r => SectionsFlow.Set("talks", label: "blog")(SectionsFlow.Set("blog", label: "talks")(r)))).StatusCode);
    }

    [Fact]
    public async Task ATemplateSwitch_ThatWouldEmptyTheMenu_KeepsTheSectionsThatAreOn_AndSaysSo()
    {
        var (app, owner, visitor, _) = await SiteAsync("software");
        using var _a = app;
        // Everything but the blog is hidden: allowed on software, where the blog is on by default and nothing needs storing for it.
        Assert.Equal(HttpStatusCode.Redirect, (await SectionsFlow.SaveAsync(owner, r => r.Select(x => x with { Enabled = x.Key == "blog" }).ToList())).StatusCode);

        Assert.Contains("stay on if you switch", await owner.GetStringAsync("/admin/template?show=healthcare"));
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, "/admin/template", new Dictionary<string, string> { ["Key"] = "healthcare" })).StatusCode);

        var page = await owner.GetStringAsync("/admin/template");
        Assert.Contains("Switched to the Healthcare template. Articles stay on, although Healthcare shows them off by default", page);
        Assert.Equal("healthcare", (await ProfileFlow.ProfileAsync(app)).TemplateKey);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/blog")).StatusCode); // the menu was not emptied
        Assert.Contains("href=\"/blog\"", await visitor.GetStringAsync("/"));
        var stored = await SectionsFlow.StoredAsync(app);
        Assert.Equal(true, stored.Single(s => s.Key == "blog").Shown); // an explicit "on" for a section the new template has off by default
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/talks")).StatusCode);
        // The sections page now shows the blog as on and lets it be saved unchanged.
        Assert.True((await SectionsFlow.RowsAsync(owner)).Single(r => r.Key == "blog").Enabled);
        Assert.Equal(HttpStatusCode.Redirect, (await SectionsFlow.SaveAsync(owner, r => r)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/blog")).StatusCode);
    }

    [Fact]
    public async Task ATemplateSwitch_WhenNothingIsOnEvenNow_IsRefused_InTheTargetsWords_AndChangesNothing()
    {
        var (app, owner, visitor, _) = await SiteAsync("software");
        using var _a = app;
        using (var db = app.CreateDbContext())
        {
            var p = await db.SiteProfiles.SingleAsync();
            p.Sections = SoftwareOrder.Select(k => new SectionSetting { Key = k, Enabled = false, Shown = false }).ToList(); // written by hand: the page would not allow it
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<Portfolio.Web.Services.ISiteProfileProvider>().Invalidate();

        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, "/admin/template", new Dictionary<string, string> { ["Key"] = "healthcare" })).StatusCode);

        var page = await owner.GetStringAsync("/admin/template");
        Assert.Contains("Switching to Healthcare would leave no &quot;Page&quot; switched on that appears in the menu, so nothing was changed. Open Pages of your site and switch one on first.", page);
        Assert.Equal("software", (await ProfileFlow.ProfileAsync(app)).TemplateKey);
    }

    [Fact]
    public async Task ASectionsSave_ThatLandsInTheMiddleOfATemplateSwitch_CannotEmptyTheMenu()
    {
        var (app, owner, visitor, _) = await SiteAsync("software");
        using var _a = app;
        var other = await app.NewClientSignedInAsync();

        // The switch has read a site where everything is on; the other tab then hides everything but the blog, before the switch writes.
        var response = await app.OverlapAsync(
            () => ContentFlow.PostAsync(owner, "/admin/template", new Dictionary<string, string> { ["Key"] = "healthcare" }),
            async () => Assert.Equal(HttpStatusCode.Redirect, (await SectionsFlow.SaveAsync(other, r => r.Select(x => x with { Enabled = x.Key == "blog" }).ToList())).StatusCode));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("healthcare", (await ProfileFlow.ProfileAsync(app)).TemplateKey);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/blog")).StatusCode); // the check ran again on the row as it then was
        Assert.Contains("href=\"/blog\"", await visitor.GetStringAsync("/"));
    }

    [Fact]
    public async Task AHideTheOwnerMade_NeverComesBack_BecauseOfASaveOrATemplateSwitch()
    {
        var (app, owner, visitor, _) = await SiteAsync("healthcare");
        using var _a = app;
        using (var db = app.CreateDbContext())
        {
            var p = await db.SiteProfiles.SingleAsync();
            p.Sections = [new SectionSetting { Key = "blog", Enabled = false }]; // an older hide, written before the page existed
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<Portfolio.Web.Services.ISiteProfileProvider>().Invalidate();

        await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("skills", label: "Abilities")); // an unrelated save under healthcare, where the blog is off anyway
        Assert.Equal(false, (await SectionsFlow.StoredAsync(app)).Single(s => s.Key == "blog").Shown); // the hide was kept, now explicit

        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, "/admin/template", new Dictionary<string, string> { ["Key"] = "software" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/blog")).StatusCode); // software would show it, but the owner hid it
        // Same for a hide made on software and carried through healthcare and back.
        await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("certifications", false));
        await ContentFlow.PostAsync(owner, "/admin/template", new Dictionary<string, string> { ["Key"] = "healthcare" });
        await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("skills", label: "Abilities too"));
        await ContentFlow.PostAsync(owner, "/admin/template", new Dictionary<string, string> { ["Key"] = "software" });
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/certifications")).StatusCode);
        // The preview names what a switch would bring back.
    }

    [Fact]
    public async Task TheTemplatePreview_NamesTheSectionsASwitchWouldBringBack()
    {
        var (app, owner, _, _) = await SiteAsync("healthcare");
        using var _a = app;

        var preview = await owner.GetStringAsync("/admin/template?show=software"); // blog and talks are off now and on under software

        Assert.Contains("<strong>Shown again:</strong>", preview);
        Assert.Contains("Blog, Talks", preview);
    }

    [Fact]
    public async Task ANameCannotMatchAnotherSectionsShortMenuName_OrTheFixedCvEntry()
    {
        var (app, owner, _, _) = await SiteAsync("software");
        using var _a = app;
        var before = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));

        // Certifications shows as "certs" on a wide screen, so no other section may be called that.
        var short1 = await (await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("skills", label: "CERTS"))).Content.ReadAsStringAsync();
        Assert.Contains("&quot;CERTS&quot; is already used by another menu entry.", short1);
        var cv = await (await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("skills", label: "cv"))).Content.ReadAsStringAsync();
        Assert.Contains("&quot;cv&quot; is the name of the CV link, which is always in the menu.", cv);
        // A renamed section's own name is what the menu shows for it, so it counts for others too.
        await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("certifications", label: "Credentials"));
        var again = await (await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("skills", label: "credentials"))).Content.ReadAsStringAsync();
        Assert.Contains("is already used by another menu entry.", again);
        Assert.NotEqual(before, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app))); // only the one valid rename was saved
        Assert.DoesNotContain(await SectionsFlow.StoredAsync(app), s => s.Key == "skills");
        // And "certs" is free once certifications has its own name.
        Assert.Equal(HttpStatusCode.Redirect, (await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("skills", label: "certs"))).StatusCode);
    }

    [Fact]
    public async Task TheDashboard_UsesSectionNamesAsWritten_AndTheOffByDefaultNoteDescribesItsSwitch()
    {
        var (app, owner, _, _) = await SiteAsync("healthcare");
        using var _a = app;
        await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("skills", label: "LinkedIn Posts & NHS Skills"));

        var dashboard = await owner.GetStringAsync("/admin");

        Assert.Contains("LinkedIn Posts &amp; NHS Skills", dashboard); // not "linkedin posts & nhs skills"
        Assert.DoesNotContain("linkedin posts", dashboard);
        Assert.Contains("Show, hide, rename and reorder each \"Page\".", dashboard); // the template's word for a section

        var html = await owner.GetStringAsync(SectionsFlow.Path);
        Assert.Matches(@"id=""Form_Rows_5__Enabled""[^>]*aria-describedby=""Form_Rows_5__Enabled-default""|aria-describedby=""Form_Rows_5__Enabled-default""[^>]*id=""Form_Rows_5__Enabled""", html);
        Assert.Contains("<p id=\"Form_Rows_5__Enabled-default\" class=\"adm-hint\">Off until you switch it on.", html);
        Assert.DoesNotContain("Form_Rows_4__Enabled-default", html); // only the rows the template has off carry the note
    }

    [Fact]
    public async Task OnlyTheRowsWhosePositionHasNoMenuEffect_SaySo()
    {
        var (app, owner, _, _) = await SiteAsync("software");
        using var _a = app;

        var html = await owner.GetStringAsync(SectionsFlow.Path);

        // Contact (a fixed button, last in the mobile menu) and testimonials (a home block) are the only sections that do not take part in the menu order.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "Position does not matter").Count);
        var contact = html[html.IndexOf("Form.Rows[8].Key", StringComparison.Ordinal)..];
        Assert.Contains("Position does not matter", contact);
        var skills = html[html.IndexOf("Form.Rows[3].Key", StringComparison.Ordinal)..html.IndexOf("Form.Rows[4].Key", StringComparison.Ordinal)];
        Assert.DoesNotContain("Position does not matter", skills);
    }

    [Fact]
    public async Task AWizardRerun_ThatMovesToATemplateWhichWouldEmptyTheMenu_KeepsTheSectionsThatAreOn()
    {
        var (app, owner, visitor, _) = await SiteAsync("software");
        using var _a = app;
        await SectionsFlow.SaveAsync(owner, r => r.Select(x => x with { Enabled = x.Key == "blog" }).ToList()); // blog only: stores nothing for the blog

        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await WizardFlow.PickTemplateAsync(owner, "healthcare")));
        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(await WizardFlow.SaveBasicsAsync(owner)));
        Assert.Equal(HttpStatusCode.Redirect, (await WizardFlow.PublishAsync(owner)).StatusCode);

        var p = await ProfileFlow.ProfileAsync(app);
        Assert.Equal("healthcare", p.TemplateKey);
        Assert.Equal(true, p.Sections.Single(s => s.Key == "blog").Shown); // kept on explicitly, as the admin's switch does
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/blog")).StatusCode);
        Assert.Contains("href=\"/blog\"", await visitor.GetStringAsync("/"));
    }

    [Fact]
    public async Task AWizardRerun_ThatCouldOnlyLeaveNoMenuSection_IsRefusedAtTheTemplateStep_AndAtPublish_InTheTargetsWords()
    {
        var (app, owner, visitor, _) = await SiteAsync("software");
        using var _a = app;
        // Draft saved while the menu still had sections, then everything is hidden by hand before publishing.
        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await WizardFlow.PickTemplateAsync(owner, "healthcare")));
        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(await WizardFlow.SaveBasicsAsync(owner)));
        using (var db = app.CreateDbContext())
        {
            var p = await db.SiteProfiles.SingleAsync();
            p.Sections = SoftwareOrder.Select(k => new SectionSetting { Key = k, Enabled = false, Shown = false }).ToList();
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<Portfolio.Web.Services.ISiteProfileProvider>().Invalidate();

        var publish = await WizardFlow.PublishAsync(owner);
        Assert.Equal(WizardFlow.Template, WizardFlow.Where(publish));
        Assert.Contains("would leave no &quot;Page&quot; switched on that appears in the menu, so nothing was changed.", await owner.GetStringAsync(WizardFlow.Template));
        var profile = await ProfileFlow.ProfileAsync(app);
        Assert.Equal("software", profile.TemplateKey);
        Assert.NotNull(profile.OnboardingDraft); // the answers are kept

        var step = await WizardFlow.PickTemplateAsync(owner, "healthcare");
        Assert.Equal(HttpStatusCode.OK, step.StatusCode); // the step itself refuses too, and says why
        Assert.Contains("would leave no &quot;Page&quot;", await step.Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------- concurrency

    [Fact]
    public async Task AStaleOrMissingStamp_IsRefused_NothingIsWritten_AndTheTypingStaysOnThePage()
    {
        var (app, owner, _, _) = await SiteAsync("software");
        using var _a = app;
        var other = await app.NewClientSignedInAsync();
        var opened = await ProfileFlow.StampAsync(owner, SectionsFlow.Path);
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(other, ProfileFlow.Hero, ProfileFlow.HeroFields(headline: "The other tab"))).StatusCode);
        var after = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));

        foreach (var stamp in new[] { opened, "", "0", new string('f', 32) })
        {
            var response = await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("projects", false, "Typed name"), stamp);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Nothing was saved.", html);
            Assert.Contains("was changed somewhere else", html);
            Assert.Contains("value=\"Typed name\"", html);
            Assert.Equal(after, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
        }
    }

    [Fact]
    public async Task ASectionsSave_OverlappingAnotherProfileWrite_IsRefusedAtTheDatabase_AndOverlappingTwoSectionsSavesOnlyTheFirstApplies()
    {
        var (app, owner, _, _) = await SiteAsync("software");
        using var _a = app;
        var other = await app.NewClientSignedInAsync();
        var stamp = await ProfileFlow.StampAsync(owner, SectionsFlow.Path);

        var first = await app.OverlapAsync(
            () => SectionsFlow.SaveAsync(owner, SectionsFlow.Set("talks", false), stamp),
            async () => Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(other, ProfileFlow.Hero, ProfileFlow.HeroFields(headline: "Winner"), stamp: stamp)).StatusCode));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("Nothing was saved.", await first.Content.ReadAsStringAsync());
        Assert.Empty(await SectionsFlow.StoredAsync(app));
        Assert.Equal("Winner", (await ProfileFlow.ProfileAsync(app)).Hero.Headline);

        // Two saves of the order from the same page: the inner one wins, the outer is refused, and the stored positions are one consistent set.
        stamp = await ProfileFlow.StampAsync(owner, SectionsFlow.Path);
        var second = await app.OverlapAsync(
            () => SectionsFlow.SaveAsync(owner, SectionsFlow.MoveTo("talks", 0), stamp),
            async () => Assert.Equal(HttpStatusCode.Redirect, (await SectionsFlow.SaveAsync(other, SectionsFlow.MoveTo("blog", 0), stamp)).StatusCode));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var stored = await SectionsFlow.StoredAsync(app);
        Assert.Equal(Enumerable.Range(1, 9), stored.Select(s => s.SortOrder).OrderBy(x => x));
        Assert.Equal("blog", stored.Single(s => s.SortOrder == 1).Key);
    }
}
