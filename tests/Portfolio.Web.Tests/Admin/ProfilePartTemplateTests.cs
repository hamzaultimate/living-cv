using System.Net;
using System.Text.Json.Nodes;
using Portfolio.Data.Entities;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The hero, metrics and About editors take their words and field visibility from the manifest, messages included, and a hidden field keeps its stored data.</summary>
public class ProfilePartTemplateTests
{
    // Words that exist nowhere in the app: if they show up, they came from this manifest.
    private static string Quirky(Action<JsonObject>? more = null) => TemplateDirs.WithExtra("quirky", node =>
    {
        node["name"] = "Quirky";
        node["editor"] = JsonNode.Parse("""
        {
          "hero": { "fields": {
            "heading": { "label": "Zing intro", "hint": "Zing intro hint." }, "eyebrow": { "hidden": true }, "headline": { "label": "Zing banner" },
            "headlineAccent": { "hidden": true }, "lede": { "label": "Zing blurb" } } },
          "metrics": { "itemLabel": "Zoink", "fields": {
            "heading": { "label": "Zoink board", "hint": "Zoink board hint." }, "value": { "label": "Zoink amount" }, "unit": { "hidden": true }, "label": { "label": "Zoink caption" } } },
          "about": { "itemLabel": "Zonk", "fields": {
            "heading": { "label": "Zonk page" }, "headline": { "label": "Zonk title" }, "paragraphs": { "hidden": true }, "valueProps": { "label": "Zonk tiles", "hint": "Zonk tiles hint." },
            "valuePropTitle": { "label": "Zonk tile name" }, "valuePropBody": { "label": "Zonk tile text" }, "currentlyNote": { "hidden": true }, "focus": { "label": "Zonk themes" } } }
        }
        """);
        more?.Invoke(node);
    });

    private static readonly string[] NeutralNouns = ["Add metric", "Metric 1", "Metric 2", "Card 1", "Add card", "Home page introduction", "Headline metrics", "page-heading\">About page", "Edit the about page", "Value cards", "Eyebrow"];

    [Fact]
    public async Task EveryLabelHintAndMessage_ComesFromTheManifest_NotFromThePage()
    {
        using var app = AdminApp.WithTemplates(Quirky());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        await ProfileFlow.SeedTextAsync(app);
        var seen = new List<string>();

        var dashboard = await owner.GetStringAsync("/admin");
        Assert.Contains("Zing intro", dashboard);
        Assert.Contains("Zoink board", dashboard);
        Assert.Contains("Zonk page", dashboard);
        var identity = await owner.GetStringAsync(ProfileFlow.Identity);
        Assert.Contains(">Zing intro</a>", identity);
        Assert.Contains(">Zoink board</a>", identity);
        Assert.Contains(">Zonk page</a>", identity);

        var hero = await owner.GetStringAsync(ProfileFlow.Hero);
        Assert.Contains("<h1 id=\"page-heading\">Zing intro</h1>", hero);
        Assert.Contains("Zing intro hint.", hero);
        Assert.Contains("Zing banner", hero);
        Assert.Contains("Zing blurb", hero);
        Assert.Contains("Save zing intro", hero);
        var metrics = await owner.GetStringAsync(ProfileFlow.Metrics);
        Assert.Contains("Zoink board hint.", metrics);
        Assert.Contains("Zoink amount", metrics);
        Assert.Contains("Zoink caption", metrics);
        Assert.Contains("<legend>Zoink 1</legend>", metrics);
        Assert.Contains("aria-label=\"Move up: Zoink 2\"", metrics);
        Assert.Contains("Add zoink", metrics);
        Assert.Contains("Save zoink board", metrics);
        var about = await owner.GetStringAsync(ProfileFlow.About);
        Assert.Contains("<h1 id=\"page-heading\">Zonk page</h1>", about);
        Assert.Contains("Zonk title", about);
        Assert.Contains("Zonk tiles hint.", about);
        Assert.Contains("Zonk tile name", about);
        Assert.Contains("Zonk tile text", about);
        Assert.Contains("Zonk themes", about);
        Assert.Contains("<legend>Zonk 2</legend>", about);
        Assert.Contains("Add zonk", about);
        seen.AddRange([dashboard, identity, hero, metrics, about]);

        // Messages: validation, the row buttons, the limits, the empty state, the save message and a refused stale save.
        var noValue = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, ProfileFlow.MetricsFields(("1", "", "ok"), ("", "", "orphan caption")))).Content.ReadAsStringAsync();
        Assert.Contains("Zoink 2 zoink amount is required.", noValue);
        var noTitle = await (await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields(headline: "", cards: [("t", "")]))).Content.ReadAsStringAsync();
        Assert.Contains("Zonk title is required.", noTitle);
        Assert.Contains("Zonk 1 zonk tile text is required.", noTitle);
        var noBanner = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields(headline: ""))).Content.ReadAsStringAsync();
        Assert.Contains("Zing banner is required.", noBanner);
        var rows = ProfileFlow.MetricsFields(("1", "", "a"));
        var added = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics + "?handler=Add", rows, tokenFrom: ProfileFlow.Metrics)).Content.ReadAsStringAsync();
        Assert.Contains("Zoink 2 added. It is not saved until you save.", added);
        var removed = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics + "?handler=Remove&index=0", rows, tokenFrom: ProfileFlow.Metrics)).Content.ReadAsStringAsync();
        Assert.Contains("Zoink 1 removed.", removed);
        Assert.Contains("There are no zoink rows yet", removed);
        Assert.Contains("Add zoink", removed);
        var full = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics + "?handler=Add", ProfileFlow.MetricsFields(Enumerable.Range(1, 8).Select(i => ($"{i}", "", "l")).ToArray()), tokenFrom: ProfileFlow.Metrics)).Content.ReadAsStringAsync();
        Assert.Contains("You can have at most 8 zoink rows.", full);
        var tooMany = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, ProfileFlow.MetricsFields(Enumerable.Range(1, 9).Select(i => ($"{i}", "", "l")).ToArray()))).Content.ReadAsStringAsync();
        Assert.Contains("At most 8 zoink rows.", tooMany);
        var tooManyCards = await (await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields(cards: Enumerable.Range(1, 7).Select(i => ($"t{i}", $"b{i}")).ToArray()))).Content.ReadAsStringAsync();
        Assert.Contains("At most 6 zonk rows.", tooManyCards);
        var stale = await ProfileFlow.StampAsync(owner, ProfileFlow.Hero);
        await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields());
        Assert.Contains("Zing intro saved. Your public pages now use it.", await owner.GetStringAsync(ProfileFlow.Hero));
        var conflict = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields(), stamp: stale)).Content.ReadAsStringAsync();
        Assert.Contains("Zing intro was changed somewhere else", conflict);
        var aboutConflict = await (await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields(), stamp: stale)).Content.ReadAsStringAsync();
        Assert.Contains("Zonk page was changed somewhere else", aboutConflict);
        var metricsConflict = await (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, ProfileFlow.MetricsFields(("1", "", "")), stamp: stale)).Content.ReadAsStringAsync();
        Assert.Contains("Zoink board was changed somewhere else", metricsConflict);
        seen.AddRange([noValue, noTitle, noBanner, added, removed, full, tooMany, tooManyCards, conflict, aboutConflict, metricsConflict]);

        // None of the neutral nouns leaks into any of those screens.
        for (var i = 0; i < seen.Count; i++)
            foreach (var noun in NeutralNouns)
                Assert.False(seen[i].Contains(noun), $"screen {i} carries the neutral wording \"{noun}\"");
    }

    [Fact]
    public async Task AHiddenField_IsNotDrawn_NotValidated_NotReadFromThePost_AndItsStoredDataSurvivesEveryEdit()
    {
        using var app = AdminApp.WithTemplates(Quirky());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        await ProfileFlow.SeedTextAsync(app);

        // Drawing: the hidden controls are absent, the visible ones present.
        var hero = await owner.GetStringAsync(ProfileFlow.Hero);
        Assert.DoesNotContain("Form.Eyebrow", hero);
        Assert.DoesNotContain("Form.HeadlineAccent", hero);
        Assert.Contains("Form.Headline", hero);
        Assert.Contains("Form.Lede", hero);
        var metrics = await owner.GetStringAsync(ProfileFlow.Metrics);
        Assert.DoesNotContain(".Unit", metrics);
        Assert.Contains("Form.Rows[0].Source", metrics);
        var about = await owner.GetStringAsync(ProfileFlow.About);
        Assert.DoesNotContain("Form.ParagraphsText", about);
        Assert.DoesNotContain("Form.CurrentlyNote", about);
        Assert.Contains("Form.FocusText", about);

        // Reading and validating: a crafted post carrying hostile values for the hidden fields neither fails nor writes them.
        var heroFields = ProfileFlow.HeroFields(eyebrow: "<script>x</script>", accent: "<b>x</b>", headline: "Visible headline", lede: "Visible lede.");
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, heroFields)).StatusCode);
        var h = (await ProfileFlow.ProfileAsync(app)).Hero;
        Assert.Equal(("Seed eyebrow", "Visible headline", "seed accent", "Visible lede."), (h.Eyebrow, h.Headline, h.HeadlineAccent, h.Lede));

        var aboutFields = ProfileFlow.AboutFields(headline: "Visible about", paragraphs: "<script>x</script>", note: "<b>x</b>", focus: "Visible focus", ("Visible title", "Visible body."));
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.About, aboutFields)).StatusCode);
        var a = (await ProfileFlow.ProfileAsync(app)).About;
        Assert.Equal(["Seed one.", "Seed two."], a.Paragraphs);
        Assert.Equal("Seed note", a.CurrentlyNote);
        Assert.Equal("Visible about", a.Headline);
        Assert.Equal(["Visible focus"], a.Focus);
        Assert.Equal("Visible title/Visible body.", $"{a.ValueProps.Single().Title}/{a.ValueProps.Single().Body}");
    }

    [Fact]
    public async Task AHiddenMetricColumn_FollowsItsRow_WhenRowsAreReorderedOrRemoved_AndNeverLeaksToAnotherRow()
    {
        using var app = AdminApp.WithTemplates(Quirky());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        await ProfileFlow.SeedTextAsync(app); // 10 ms first, 20 rps second

        // The two rows swapped, as the Move buttons would leave the form, and a third row added: the units stay with their rows, the new row has none.
        var swapped = new Dictionary<string, string>
        {
            ["Form.Rows[0].Source"] = "1", ["Form.Rows[0].Value"] = "20", ["Form.Rows[0].Label"] = "second",
            ["Form.Rows[1].Source"] = "0", ["Form.Rows[1].Value"] = "10", ["Form.Rows[1].Label"] = "first",
            ["Form.Rows[2].Value"] = "30", ["Form.Rows[2].Label"] = "third",
        };
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, swapped)).StatusCode);
        Assert.Equal(["20/rps/second", "10/ms/first", "30//third"], (await ProfileFlow.ProfileAsync(app)).Metrics.Select(m => $"{m.Value}/{m.Unit}/{m.Label}"));

        // A forged source (out of range, negative, or one a second row also names) borrows nothing.
        var forged = new Dictionary<string, string>
        {
            ["Form.Rows[0].Source"] = "0", ["Form.Rows[0].Value"] = "1", ["Form.Rows[0].Label"] = "a",
            ["Form.Rows[1].Source"] = "0", ["Form.Rows[1].Value"] = "2", ["Form.Rows[1].Label"] = "b",
            ["Form.Rows[2].Source"] = "99", ["Form.Rows[2].Value"] = "3", ["Form.Rows[2].Label"] = "c",
            ["Form.Rows[3].Source"] = "-1", ["Form.Rows[3].Value"] = "4", ["Form.Rows[3].Label"] = "d",
        };
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, forged)).StatusCode);
        Assert.Equal(new string?[] { "rps", null, null, null }, (await ProfileFlow.ProfileAsync(app)).Metrics.Select(m => m.Unit)); // only the first row, the one that named source 0 first, got the stored unit

        // Removing a row removes its hidden column with it, and the row that is left keeps its own.
        var one = new Dictionary<string, string> { ["Form.Rows[0].Source"] = "0", ["Form.Rows[0].Value"] = "1", ["Form.Rows[0].Label"] = "kept" };
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, one)).StatusCode);
        Assert.Equal(["1/rps/kept"], (await ProfileFlow.ProfileAsync(app)).Metrics.Select(m => $"{m.Value}/{m.Unit}/{m.Label}"));
    }

    [Fact]
    public async Task APageForASectionTheTemplateDoesNotShow_SaysSo_KeepsTheTextAndStillSaves()
    {
        using var app = AdminApp.WithTemplates(Quirky(n => n["sections"]!.AsArray().First(s => s!["key"]!.GetValue<string>() == "about")!["enabled"] = false));
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        await ProfileFlow.SeedTextAsync(app);

        var page = await owner.GetStringAsync(ProfileFlow.About);
        Assert.Contains("does not show this page on the public site", page);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/admin/profile/hero")).StatusCode);
        Assert.DoesNotContain("does not show this page", await owner.GetStringAsync(ProfileFlow.Hero)); // the hero is a home page block, never switched off

        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields(headline: "Kept for later"))).StatusCode);
        Assert.Equal("Kept for later", (await ProfileFlow.ProfileAsync(app)).About.Headline);
        Assert.Equal(HttpStatusCode.NotFound, (await app.NewClient().GetAsync("/about")).StatusCode);
    }

    [Fact]
    public async Task TheShippedTemplates_TalkInTheirOwnWords_AndHealthcareHidesWhatItDoesNotUse()
    {
        using (var software = new AdminApp())
        {
            var owner = await ContentFlow.BlankSiteAsync(software);
            await ProfileFlow.SeedTextAsync(software);
            Assert.Contains("<h1 id=\"page-heading\">Home page hero</h1>", await owner.GetStringAsync(ProfileFlow.Hero));
            var metrics = await owner.GetStringAsync(ProfileFlow.Metrics);
            Assert.Contains("Headline metrics", metrics);
            Assert.Contains("Form.Rows[0].Unit", metrics);
            Assert.Contains("Form.HeadlineAccent", await owner.GetStringAsync(ProfileFlow.Hero));
            Assert.DoesNotContain("Before you publish", metrics); // software is not a sensitive template
        }

        using var healthcare = new AdminApp();
        var nurse = await ContentFlow.BlankSiteAsync(healthcare, "healthcare");
        await ProfileFlow.SeedTextAsync(healthcare);
        var hero = await nurse.GetStringAsync(ProfileFlow.Hero);
        Assert.Contains("Home page introduction", hero);
        Assert.Contains("Professional summary", hero);
        Assert.DoesNotContain("Form.HeadlineAccent", hero);
        Assert.Contains("Before you publish", hero); // the sensitive template's guidance
        var figures = await nurse.GetStringAsync(ProfileFlow.Metrics);
        Assert.Contains("<h1 id=\"page-heading\">Key figures</h1>", figures);
        Assert.Contains("<legend>Figure 1</legend>", figures);
        Assert.Contains("What it measures", figures);
        Assert.DoesNotContain(".Unit", figures);
        Assert.Contains("Before you publish", figures);
        var about = await nurse.GetStringAsync(ProfileFlow.About);
        Assert.Contains("Highlights", about);
        Assert.Contains("Clinical focus", about);
        Assert.Contains("Highlight 1", about);
        Assert.Contains("Before you publish", about);

        // The hidden unit survives an edit made in that template.
        var rows = new Dictionary<string, string> { ["Form.Rows[0].Source"] = "0", ["Form.Rows[0].Value"] = "11", ["Form.Rows[0].Label"] = "changed", ["Form.Rows[1].Source"] = "1", ["Form.Rows[1].Value"] = "21", ["Form.Rows[1].Label"] = "changed too" };
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(nurse, ProfileFlow.Metrics, rows)).StatusCode);
        Assert.Equal(["11/ms/changed", "21/rps/changed too"], (await ProfileFlow.ProfileAsync(healthcare)).Metrics.Select(m => $"{m.Value}/{m.Unit}/{m.Label}"));
        Assert.Equal("seed accent", (await ProfileFlow.ProfileAsync(healthcare)).Hero.HeadlineAccent);
    }
}
