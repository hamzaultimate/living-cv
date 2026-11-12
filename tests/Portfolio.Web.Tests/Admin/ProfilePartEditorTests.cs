using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The hero, metrics and About editors: what a save writes, what the public site then shows, what is refused, and what the row buttons do.</summary>
public class ProfilePartEditorTests
{
    private static async Task<(AdminApp App, HttpClient Owner, HttpClient Visitor)> BlankAsync(string template = "software")
    {
        var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, template);
        return (app, owner, app.NewClient());
    }

    /// <summary>The structured data blocks of a page, for search engines.</summary>
    private static string JsonLd(string html)
    {
        var blocks = Regex.Matches(html, @"<script type=""application/ld(?:\+|&#x2B;)json"">(.*?)</script>", RegexOptions.Singleline).Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(blocks);
        return string.Join("\n", blocks);
    }

    // ---------------------------------------------------------------- hero

    [Fact]
    public async Task TheHero_IsSaved_ShownOnTheHomePage_AndTheShareImageFollows()
    {
        var (app, owner, visitor) = await BlankAsync();
        using var _ = app;
        var ogBefore = await visitor.GetByteArrayAsync("/og.png"); // warms the generated cache
        var sitemapBefore = await visitor.GetStringAsync("/sitemap.xml");

        var save = await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields("Nurse in Leeds", "Calm hands", "steady practice", "Eight years on acute wards."));

        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        Assert.Equal(ProfileFlow.Hero, WizardFlow.Where(save));
        var p = await ProfileFlow.ProfileAsync(app);
        Assert.Equal(("Nurse in Leeds", "Calm hands", "steady practice", "Eight years on acute wards."), (p.Hero.Eyebrow, p.Hero.Headline, p.Hero.HeadlineAccent, p.Hero.Lede));
        var home = await visitor.GetStringAsync("/");
        Assert.Contains("Nurse in Leeds", home);
        Assert.Contains("Calm hands", home);
        Assert.Contains("steady practice", home);
        Assert.Contains("Eight years on acute wards.", home);
        Assert.NotEqual(ogBefore, await visitor.GetByteArrayAsync("/og.png")); // the tagline is drawn from the hero, and the cached image was evicted
        Assert.Equal(sitemapBefore, await visitor.GetStringAsync("/sitemap.xml")); // the hero is not a page: nothing in the sitemap moves
        Assert.DoesNotContain("Calm hands", JsonLd(home)); // and it does not leak into the markup for search engines
        Assert.Contains("Home page hero saved. Your public pages now use it.", await owner.GetStringAsync(ProfileFlow.Hero)); // the name is the manifest's
    }

    [Fact]
    public async Task AnEmptyOptionalHeroField_ClearsIt_AndTheHeadlineIsRequired()
    {
        var (app, owner, visitor) = await BlankAsync();
        using var _ = app;
        await ProfileFlow.SeedTextAsync(app);

        var noHeadline = await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields(headline: "   "));
        Assert.Equal(HttpStatusCode.OK, noHeadline.StatusCode);
        Assert.Contains("Headline is required.", await noHeadline.Content.ReadAsStringAsync());
        Assert.Equal("Seed headline", (await ProfileFlow.ProfileAsync(app)).Hero.Headline);

        var cleared = await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields(eyebrow: "", accent: "", lede: ""));
        Assert.Equal(HttpStatusCode.Redirect, cleared.StatusCode);
        var hero = (await ProfileFlow.ProfileAsync(app)).Hero;
        Assert.Null(hero.Eyebrow);
        Assert.Null(hero.HeadlineAccent);
        Assert.Null(hero.Lede);
        Assert.Equal("A new headline", hero.Headline);
    }

    // ---------------------------------------------------------------- metrics

    [Fact]
    public async Task Metrics_AreSaved_InOrder_BlankRowsAreDropped_AndTheHomePageShowsThem()
    {
        var (app, owner, visitor) = await BlankAsync();
        using var _ = app;
        var fields = ProfileFlow.MetricsFields(("42", "ms", "median latency"), ("", "", ""), ("99.9", "%", "uptime"), ("7", "", ""));

        var save = await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, fields);

        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var stored = (await ProfileFlow.ProfileAsync(app)).Metrics;
        Assert.Equal(["42/ms/median latency", "99.9/%/uptime", "7//"], stored.Select(m => $"{m.Value}/{m.Unit}/{m.Label}"));
        var home = await visitor.GetStringAsync("/");
        var a = home.IndexOf("median latency", StringComparison.Ordinal);
        var b = home.IndexOf("uptime", StringComparison.Ordinal);
        Assert.True(a > 0 && b > a, "the metrics appear on the home page in the saved order");
        Assert.Contains(">42<", home.Replace("<span", "<").Replace(" ", ""));
    }

    [Fact]
    public async Task AMetricNeedsAValue_AndTheMessageNamesTheRow()
    {
        var (app, owner, _) = await BlankAsync();
        using var _a = app;
        await ProfileFlow.SeedTextAsync(app);
        var before = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));

        var response = await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, ProfileFlow.MetricsFields(("1", "", "ok"), ("", "ms", "orphan unit")));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Metric 2 value is required.", html);
        Assert.Contains("<a href=\"#Form_Rows_1__Value\">", html);
        Assert.Matches(@"id=""Form_Rows_1__Value""[^>]*aria-invalid=""true""", html);
        Assert.Contains("value=\"orphan unit\"", html); // what was typed stays
        Assert.Equal(before, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
    }

    [Fact]
    public async Task MetricsAreBounded_ByCount_AndByEachFieldsLength()
    {
        var (app, owner, _) = await BlankAsync();
        using var _a = app;
        var eight = Enumerable.Range(1, 8).Select(i => ($"{i}", "u", "l")).ToArray();
        var nine = Enumerable.Range(1, 9).Select(i => ($"{i}", "u", "l")).ToArray();

        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, ProfileFlow.MetricsFields(eight))).StatusCode);
        var tooMany = await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, ProfileFlow.MetricsFields(nine));
        Assert.Equal(HttpStatusCode.OK, tooMany.StatusCode);
        Assert.Contains("At most 8 metric rows.", await tooMany.Content.ReadAsStringAsync());
        Assert.Equal(8, (await ProfileFlow.ProfileAsync(app)).Metrics.Count);

        var tooLong = new[]
        {
            (Row: (new string('v', 21), "x", "x"), Message: "Metric 1 value can be at most 20"),
            (Row: ("x", new string('u', 21), "x"), Message: "Metric 1 unit can be at most 20"),
            (Row: ("x", "x", new string('l', 81)), Message: "Metric 1 label can be at most 80"),
        };
        foreach (var (row, message) in tooLong)
        {
            var response = await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, ProfileFlow.MetricsFields(row));
            Assert.Contains(message, await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(8, (await ProfileFlow.ProfileAsync(app)).Metrics.Count);
    }

    [Fact]
    public async Task TheRowButtons_ChangeTheRowsOnThePageOnly_NothingIsWrittenUntilSave()
    {
        var (app, owner, _) = await BlankAsync();
        using var _a = app;
        await ProfileFlow.SeedTextAsync(app);
        var before = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));
        var rows = ProfileFlow.MetricsFields(("10", "ms", "first"), ("20", "rps", "second"), ("30", "%", "third"));
        string Fingerprint() => ProfileFlow.Fingerprint(ProfileFlow.ProfileAsync(app).Result);

        var up = await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics + "?handler=Move&index=2&direction=up", rows, tokenFrom: ProfileFlow.Metrics);
        var upHtml = await up.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        Assert.Matches(@"name=""Form.Rows\[1\].Value""[^>]*value=""30""|value=""30""[^>]*name=""Form.Rows\[1\].Value""", upHtml); // third is now second
        Assert.Matches(@"id=""Form_Rows_2__Value""[^>]*value=""20""|value=""20""[^>]*id=""Form_Rows_2__Value""", upHtml);
        Assert.Contains("role=\"status\"", upHtml);
        Assert.Contains("Metric 3 moved up to position 2. It is not saved until you save.", upHtml);
        Assert.Matches(@"<button[^>]*id=""row-1-up""[^>]*autofocus=""autofocus""|<button[^>]*autofocus=""autofocus""[^>]*id=""row-1-up""", upHtml); // focus follows the moved row
        Assert.Contains("data-has-errors=\"true\"", upHtml); // the unsaved changes warning starts on
        Assert.Equal(before, Fingerprint());

        var removed = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics + "?handler=Remove&index=0", rows, tokenFrom: ProfileFlow.Metrics)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("value=\"first\"", removed);
        Assert.Contains("Metric 1 removed. It is not saved until you save.", removed);
        Assert.Contains("id=\"add-row\"", removed);
        Assert.Equal(before, Fingerprint());

        var added = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics + "?handler=Add", rows, tokenFrom: ProfileFlow.Metrics)).Content.ReadAsStringAsync();
        Assert.Contains("name=\"Form.Rows[3].Value\"", added);
        Assert.Contains("Metric 4 added.", added);
        Assert.Matches(@"id=""Form_Rows_3__Value""[^>]*autofocus=""autofocus""|autofocus=""autofocus""[^>]*id=""Form_Rows_3__Value""", added);
        Assert.Equal(before, Fingerprint());
    }

    [Fact]
    public async Task TheRowButtons_AreRefusedAtTheEnds_AndAtTheLimit_WithoutWriting()
    {
        var (app, owner, _) = await BlankAsync();
        using var _a = app;
        var eight = ProfileFlow.MetricsFields(Enumerable.Range(1, 8).Select(i => ($"{i}", "u", "l")).ToArray());
        var one = ProfileFlow.MetricsFields(("1", "u", "l"));
        var before = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));

        var full = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics + "?handler=Add", eight, tokenFrom: ProfileFlow.Metrics)).Content.ReadAsStringAsync();
        Assert.Contains("You can have at most 8 metric rows.", full);
        Assert.DoesNotContain("Form.Rows[8]", full);
        Assert.DoesNotContain("id=\"add-row\"", full);
        var top = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics + "?handler=Move&index=0&direction=up", one, tokenFrom: ProfileFlow.Metrics)).Content.ReadAsStringAsync();
        Assert.Contains("That row cannot move any further.", top);
        var gone = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics + "?handler=Remove&index=5", one, tokenFrom: ProfileFlow.Metrics)).Content.ReadAsStringAsync();
        Assert.Contains("is not on the page any more", gone);
        var sideways = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics + "?handler=Move&index=0&direction=sideways", one, tokenFrom: ProfileFlow.Metrics)).Content.ReadAsStringAsync();
        Assert.Contains("That row cannot move any further.", sideways);
        Assert.Equal(before, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
    }

    // ---------------------------------------------------------------- about

    [Fact]
    public async Task TheAboutText_IsSaved_AndTheAboutPageShowsEveryPart()
    {
        var (app, owner, visitor) = await BlankAsync();
        using var _ = app;

        var save = await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields("Who I am", "First para.\r\n\r\nSecond para\r\nsame paragraph.\r\n\r\n\r\nThird.", "Studying", "Wards\nTeaching\n\n  Audit  ",
            ("Care", "Safe and kind."), ("", ""), ("Teaching", "Passing it on.")));

        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var about = (await ProfileFlow.ProfileAsync(app)).About;
        Assert.Equal("Who I am", about.Headline);
        Assert.Equal(["First para.", "Second para\nsame paragraph.", "Third."], about.Paragraphs);
        Assert.Equal(["Wards", "Teaching", "Audit"], about.Focus);
        Assert.Equal(["Care/Safe and kind.", "Teaching/Passing it on."], about.ValueProps.Select(v => $"{v.Title}/{v.Body}"));
        Assert.Equal("Studying", about.CurrentlyNote);
        var page = await visitor.GetStringAsync("/about");
        foreach (var text in new[] { "Who I am", "First para.", "Third.", "Studying", "Wards", "Audit", "Care", "Safe and kind.", "Passing it on." })
            Assert.Contains(text, page);
        Assert.True(page.IndexOf("Safe and kind.", StringComparison.Ordinal) < page.IndexOf("Passing it on.", StringComparison.Ordinal), "the cards appear in the saved order");
    }

    [Fact]
    public async Task TheAboutCards_AreBoundedAndNeedATitleAndABody_AndTheRowButtonsWork()
    {
        var (app, owner, _) = await BlankAsync();
        using var _a = app;
        var before = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));

        var half = await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields(cards: [("Only a title", "")]));
        Assert.Contains("Card 1 text is required.", await half.Content.ReadAsStringAsync());
        var seven = await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields(cards: Enumerable.Range(1, 7).Select(i => ($"T{i}", $"B{i}")).ToArray()));
        Assert.Contains("At most 6 card rows.", await seven.Content.ReadAsStringAsync());
        Assert.Equal(before, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));

        var two = ProfileFlow.AboutFields(cards: [("First", "one"), ("Second", "two")]);
        var moved = await (await ProfileFlow.PostAsync(owner, ProfileFlow.About + "?handler=Move&index=0&direction=down", two, tokenFrom: ProfileFlow.About)).Content.ReadAsStringAsync();
        Assert.Matches(@"name=""Form.Cards\[0\].Title""[^>]*value=""Second""|value=""Second""[^>]*name=""Form.Cards\[0\].Title""", moved);
        Assert.Contains("Card 1 moved down to position 2. It is not saved until you save.", moved);
        var added = await (await ProfileFlow.PostAsync(owner, ProfileFlow.About + "?handler=Add", two, tokenFrom: ProfileFlow.About)).Content.ReadAsStringAsync();
        Assert.Contains("name=\"Form.Cards[2].Title\"", added);
        var removed = await (await ProfileFlow.PostAsync(owner, ProfileFlow.About + "?handler=Remove&index=1", two, tokenFrom: ProfileFlow.About)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("value=\"Second\"", removed);
        Assert.Equal(before, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
    }

    [Fact]
    public async Task AboutLimits_AreEnforced_PerParagraphAndPerLine()
    {
        var (app, owner, _) = await BlankAsync();
        using var _a = app;

        await ProfileFlow.SeedTextAsync(app);
        async Task<string> Post(Dictionary<string, string> f) => await (await ProfileFlow.PostAsync(owner, ProfileFlow.About, f)).Content.ReadAsStringAsync();

        Assert.Contains("at most 2000 characters", await Post(ProfileFlow.AboutFields(paragraphs: new string('p', 2001))));
        Assert.Contains("at most 10 paragraphs", await Post(ProfileFlow.AboutFields(paragraphs: string.Join("\n\n", Enumerable.Range(0, 11).Select(i => "p" + i)))));
        Assert.Contains("at most 30 lines", await Post(ProfileFlow.AboutFields(focus: string.Join("\n", Enumerable.Range(0, 31).Select(i => "f" + i)))));
        Assert.Contains("each line can be at most 120", await Post(ProfileFlow.AboutFields(focus: new string('f', 121))));
        Assert.Contains("can be at most 300", await Post(ProfileFlow.AboutFields(headline: new string('h', 301))));
        Assert.Contains("can be at most 300", await Post(ProfileFlow.AboutFields(note: new string('n', 301))));
        Assert.Contains("Card 1 title can be at most 120", await Post(ProfileFlow.AboutFields(cards: [(new string('t', 121), "b")])));
        Assert.Contains("Card 1 text can be at most 500", await Post(ProfileFlow.AboutFields(cards: [("t", new string('b', 501))])));
        Assert.Contains("Headline is required.", await Post(ProfileFlow.AboutFields(headline: "")));
        // The limits themselves are accepted.
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields(headline: new string('h', 300), paragraphs: new string('p', 2000), note: new string('n', 300),
            focus: new string('f', 120), cards: [(new string('t', 120), new string('b', 500))]))).StatusCode);
    }

    // ---------------------------------------------------------------- hostile text

    public static IEnumerable<object[]> TextFields()
    {
        foreach (var f in new[] { "Eyebrow", "Headline", "HeadlineAccent", "Lede" }) yield return [ProfileFlow.Hero, "Form." + f];
        foreach (var f in new[] { "Value", "Unit", "Label" }) yield return [ProfileFlow.Metrics, $"Form.Rows[0].{f}"];
        foreach (var f in new[] { "Headline", "ParagraphsText", "CurrentlyNote", "FocusText" }) yield return [ProfileFlow.About, "Form." + f];
        foreach (var f in new[] { "Title", "Body" }) yield return [ProfileFlow.About, $"Form.Cards[0].{f}"];
    }

    private static Dictionary<string, string> Valid(string path) => path switch
    {
        ProfileFlow.Hero => ProfileFlow.HeroFields(),
        ProfileFlow.Metrics => ProfileFlow.MetricsFields(("5", "ms", "latency")),
        _ => ProfileFlow.AboutFields(cards: [("Title", "Body")]),
    };

    [Theory]
    [MemberData(nameof(TextFields))]
    public async Task MarkupInAnyTextField_IsRefused_NothingIsSaved_AndItIsShownEscapedOnTheRedrawnForm(string path, string field)
    {
        var (app, owner, _) = await BlankAsync();
        using var _a = app;
        await ProfileFlow.SeedTextAsync(app);
        var before = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));

        // Short enough for the narrowest field (20 characters), so the refusal is the markup rule and not the length rule.
        foreach (var payload in new[] { "<script>x</script>", "<img onerror=a>", "<!-- x -->", "<!DOCTYPE svg>", "</p><p>", "<?xml?>", "<b>bold</b>", "ok <i>x</i>" })
        {
            var fields = Valid(path);
            fields[field] = payload;
            var response = await ProfileFlow.PostAsync(owner, path, fields);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("is plain text: remove anything that looks like a tag", html);
            Assert.False(html.Contains(payload), $"raw payload in page: {payload}"); // the redrawn form never carries it raw ...
            Assert.Contains(HtmlEncoder.Default.Encode(payload), html); // ... only encoded, so the owner can correct it
            Assert.Equal(before, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
        }
    }

    [Theory]
    [MemberData(nameof(TextFields))]
    public async Task ControlCharactersInAnyTextField_AreRefused(string path, string field)
    {
        var (app, owner, _) = await BlankAsync();
        using var _a = app;
        var before = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));

        foreach (var payload in new[] { "bell\u0007here", "nul\u0000here", "esc\u001bhere", "del\u007fhere", "next\u0085line" })
        {
            var fields = Valid(path);
            fields[field] = payload;
            var response = await ProfileFlow.PostAsync(owner, path, fields);

            // The framework itself refuses a NUL in a form (400); every other control character reaches the page's own check.
            if (payload.Contains('\u0000')) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            else
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("contains characters that are not allowed", await response.Content.ReadAsStringAsync());
            }
            Assert.Equal(before, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
        }
    }

    [Fact]
    public async Task TextThatIsNotMarkup_IsKeptAsTyped_AndEveryPageEncodesItWhenRendering()
    {
        var (app, owner, visitor) = await BlankAsync();
        using var _ = app;
        var texts = new[] { "5 < 6 & 7 > 3", "\"quoted\" and 'single'", "&lt;b&gt;already&lt;/b&gt;", "javascript:alert(1)", "{{7*7}} @(1+1) @Model", "[link](http://example.com)", "%3Cscript%3E" };

        for (var i = 0; i < texts.Length; i++)
        {
            var t = texts[i];
            Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields(t + " e", t + " h", t + " a", t + " l"))).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, ProfileFlow.MetricsFields((t.Length > 20 ? "9" : t, t[..Math.Min(t.Length, 20)], t)))).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields(t + " h", t + " p", t + " n", t + " f", (t + " t", t + " b")))).StatusCode);

            var home = await visitor.GetStringAsync("/");
            var about = await visitor.GetStringAsync("/about");
            foreach (var (page, shown) in new[] { (home, new[] { t + " e", t + " h", t + " a", t + " l", t }), (about, new[] { t + " h", t + " p", t + " n", t + " f", t + " t", t + " b" }) })
                foreach (var s in shown)
                {
                    var encoded = HtmlEncoder.Default.Encode(s);
                    Assert.Contains(encoded, page);
                    if (encoded != s) Assert.DoesNotContain(s, page.Replace(encoded, "")); // the raw form appears nowhere outside its encoded form
                }
            Assert.DoesNotContain("<a href=\"http://example.com\"", about); // text is never turned into a link
            Assert.DoesNotContain("href=\"javascript:", home + about);
        }
    }

    // ---------------------------------------------------------------- first run

    [Fact]
    public async Task OnAFirstRun_PublishingKeepsAPartTheOwnerAlreadyEdited_AndReplacesThePlaceholderOnes()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await app.NewClient().GetAsync("/"); // the first public read creates the profile row from the Site config
        var placeholder = await ProfileFlow.ProfileAsync(app);
        Assert.NotEmpty(placeholder.Metrics); // the Site config's persona is what a new site shows until the wizard publishes
        Assert.NotEmpty(placeholder.About.Paragraphs);

        // The owner edits the metrics before publishing; the hero and About text are still the placeholder.
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, ProfileFlow.MetricsFields(("3", "yrs", "in practice")))).StatusCode);
        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await WizardFlow.PickTemplateAsync(owner, "software")));
        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields("Casey Morgan", "Specialist", "casey@owner.example", "Leeds, UK", "Specialist who keeps the lights on."))));
        Assert.Equal(HttpStatusCode.Redirect, (await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields("https://casey.example"))).StatusCode);

        var p = await ProfileFlow.ProfileAsync(app);
        Assert.Equal(["3/yrs/in practice"], p.Metrics.Select(m => $"{m.Value}/{m.Unit}/{m.Label}")); // the edit survived the publish
        Assert.Equal("Casey Morgan", p.Hero.Headline); // the placeholder hero was replaced by the owner's answers
        Assert.Equal(["Specialist who keeps the lights on."], p.About.Paragraphs);
        Assert.Empty(p.About.ValueProps);
    }

    [Fact]
    public async Task OnAFirstRun_AnEditedHeroAndAbout_SurvivePublishing_AndAnUntouchedSiteIsExactlyAsBefore()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields(headline: "My own headline"))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields(headline: "My own about"))).StatusCode);
        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await WizardFlow.PickTemplateAsync(owner, "software")));
        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(await WizardFlow.SaveBasicsAsync(owner)));
        Assert.Equal(HttpStatusCode.Redirect, (await WizardFlow.PublishAsync(owner)).StatusCode);

        var p = await ProfileFlow.ProfileAsync(app);

        Assert.Equal("My own headline", p.Hero.Headline);
        Assert.Equal("My own about", p.About.Headline);
        Assert.Empty(p.Metrics); // the one part left alone was still the placeholder, so it was replaced
    }

    [Fact]
    public async Task AFirstRunEditFormOpenedBeforePublishing_IsRefusedAfterIt_SoThePublishIsNeverOverwrittenBlindly()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        var stamp = await ProfileFlow.StampAsync(owner, ProfileFlow.Hero);
        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await WizardFlow.PickTemplateAsync(owner, "software")));
        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(await WizardFlow.SaveBasicsAsync(owner)));
        Assert.Equal(HttpStatusCode.Redirect, (await WizardFlow.PublishAsync(owner)).StatusCode);

        var response = await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields(headline: "From the old form"), stamp: stamp);

        Assert.Contains("Nothing was saved.", await response.Content.ReadAsStringAsync());
        Assert.NotEqual("From the old form", (await ProfileFlow.ProfileAsync(app)).Hero.Headline);
    }

    [Fact]
    public async Task TheAboutHeadline_IsRequiredOnlyOnceThePageHasOne_SoAFreshlyPublishedSiteCanSaveItsParagraphs()
    {
        var (app, owner, visitor) = await BlankAsync();
        using var _ = app;
        Assert.Null((await ProfileFlow.ProfileAsync(app)).About.Headline); // what the wizard publishes

        var form = await owner.GetStringAsync(ProfileFlow.About);
        Assert.Matches(@"<label for=""Form_Headline"">Headline<span class=""adm-optional""> \(optional\)</span>", form); // not marked required while there is none
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields(headline: "", paragraphs: "Only paragraphs."))).StatusCode);
        Assert.Equal(["Only paragraphs."], (await ProfileFlow.ProfileAsync(app)).About.Paragraphs);

        // Once a headline exists it is required, and emptying it is refused.
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields(headline: "Now there is one"))).StatusCode);
        Assert.DoesNotMatch(@"<label for=""Form_Headline"">Headline<span", await owner.GetStringAsync(ProfileFlow.About));
        var refused = await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields(headline: " "));
        Assert.Contains("Headline is required.", await refused.Content.ReadAsStringAsync());
        Assert.Equal("Now there is one", (await ProfileFlow.ProfileAsync(app)).About.Headline);
    }
}
