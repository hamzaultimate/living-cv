using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The sections page takes its words, its hidden fields and the sections that cannot be switched off from the manifest; no message names a noun of its own.</summary>
public class SectionTemplateTests
{
    // Words that exist nowhere in the app: if they show up, they came from this manifest.
    private static string Quirky(bool lockContact = false, string labelHidden = "", Action<JsonObject>? more = null) => TemplateDirs.WithExtra("quirky", node =>
    {
        node["name"] = "Quirky";
        node["editor"] = JsonNode.Parse($$"""
        {
          "sections": { "itemLabel": "Zork", "fields": {
            "heading": { "label": "Zork map", "hint": "Zork map hint." }, "label": { "label": "Zork tag"{{labelHidden}} }, "shown": { "label": "Zork visible" },
            "locked": { "label": "Zork fixed", "hint": "Zork fixed hint." }, "defaultOff": { "label": "Zork asleep", "hint": "Zork asleep hint." } } }
        }
        """);
        if (lockContact) node["sections"]!.AsArray().First(s => s!["key"]!.GetValue<string>() == "contact")!["required"] = true;
        more?.Invoke(node);
    });

    [Fact]
    public async Task EveryLabelHintAndMessage_ComesFromTheManifest_NotFromThePage()
    {
        using var app = AdminApp.WithTemplates(Quirky());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        await owner.GetAsync("/admin");
        var seen = new List<string>();

        var dashboard = await owner.GetStringAsync("/admin");
        Assert.Contains("Zork map", dashboard);
        var page = await owner.GetStringAsync(SectionsFlow.Path);
        Assert.Contains("<h1 id=\"page-heading\">Zork map</h1>", page);
        Assert.Contains("Zork map hint.", page);
        Assert.Contains("Zork tag", page);
        Assert.Contains("Zork visible", page);
        Assert.Contains("Zork asleep. Zork asleep hint.", page); // blog and talks are off by default in the derived template
        Assert.Contains("Save Zork map", page);
        seen.AddRange([dashboard, page]);

        var rows = await SectionsFlow.RowsAsync(owner);
        // Messages: a duplicate name, a list that does not match, the row button, a stale save and the save message.
        var dup = await (await SectionsFlow.SaveAsync(owner, r => SectionsFlow.Set("experience", label: "about")(r))).Content.ReadAsStringAsync();
        Assert.Contains("Each &quot;Zork&quot; needs its own name: &quot;about&quot; is already used by another menu entry.", dup);
        var forged = await (await SectionsFlow.SaveAsync(owner, r => r.Where(x => x.Key != "talks").ToList())).Content.ReadAsStringAsync();
        Assert.Contains("The list on this page does not match your template any more", forged);
        var moved = await (await ProfileFlow.PostAsync(owner, SectionsFlow.Path + "?handler=Move&index=2&direction=up", SectionsFlow.Fields(rows), tokenFrom: SectionsFlow.Path)).Content.ReadAsStringAsync();
        Assert.Contains("moved up to position 2. It is not saved until you save.", moved);
        var stale = await ProfileFlow.StampAsync(owner, SectionsFlow.Path);
        await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("skills", label: "Abilities"));
        Assert.Contains("Zork map saved. Your public pages now use it.", await owner.GetStringAsync(SectionsFlow.Path));
        var conflict = await (await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("skills", label: "Other"), stale)).Content.ReadAsStringAsync();
        Assert.Contains("Zork map was changed somewhere else", conflict);
        seen.AddRange([dup, forged, moved, conflict]);

        for (var i = 0; i < seen.Count; i++)
            foreach (var neutral in new[] { "Keep at least one section", "Each section needs", "list of sections", "Always shown", "Off until you switch it on", "Name in the menu", "Show on the site", "Section moved", "Site sections" })
                Assert.False(seen[i].Contains(neutral), $"screen {i} carries the neutral wording \"{neutral}\"");
    }

    [Fact]
    public async Task TheAtLeastOneRule_UsesTheManifestsNoun()
    {
        using var app = AdminApp.WithTemplates(Quirky());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");

        var html = await (await SectionsFlow.SaveAsync(owner, r => r.Select(x => x with { Enabled = x.Key == "testimonials" }).ToList())).Content.ReadAsStringAsync();

        Assert.Contains("Keep at least one &quot;Zork&quot; switched on that appears in the menu.", html);
    }

    [Fact]
    public async Task ASectionTheTemplateRequires_IsAlwaysOn_HasNoSwitch_AndCannotBeTurnedOffByAForgedPost()
    {
        using var app = AdminApp.WithTemplates(Quirky(lockContact: true));
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        var visitor = app.NewClient();

        var page = await owner.GetStringAsync(SectionsFlow.Path);
        Assert.Contains("Zork fixed. Zork fixed hint.", page);
        Assert.DoesNotContain("Form_Rows_8__Enabled", page); // no switch for the last row, contact
        Assert.Contains("Form_Rows_7__Enabled", page);

        // Everything else off and contact "off" in the post: the site still has contact, and the save is accepted because a required page remains.
        var rows = (await SectionsFlow.RowsAsync(owner)).Select(r => r with { Enabled = false }).ToList();
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, SectionsFlow.Path, SectionsFlow.Fields(rows))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/contact")).StatusCode);
        Assert.Contains("href=\"/contact\"", await visitor.GetStringAsync("/"));
        Assert.DoesNotContain(await SectionsFlow.StoredAsync(app), s => s.Key == "contact"); // nothing is stored for a section that cannot change
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/about")).StatusCode);
    }

    [Fact]
    public async Task ARequiredSection_IgnoresAStoredHide_Too()
    {
        using var app = AdminApp.WithTemplates(Quirky(lockContact: true));
        await ContentFlow.BlankSiteAsync(app, "quirky");
        using (var db = app.CreateDbContext())
        {
            var p = await db.SiteProfiles.SingleAsync();
            p.Sections = [new SectionSetting { Key = "contact", Enabled = false, Shown = false }];
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<Portfolio.Web.Services.ISiteProfileProvider>().Invalidate();

        Assert.Equal(HttpStatusCode.OK, (await app.NewClient().GetAsync("/contact")).StatusCode);
    }

    [Fact]
    public async Task AHideStoredForASectionTheTemplateRequires_SurvivesASave_SoItDoesNotComeBackAfterATemplateRoundTrip()
    {
        using var app = AdminApp.WithTemplates(Quirky(lockContact: true));
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        using (var db = app.CreateDbContext())
        {
            var p = await db.SiteProfiles.SingleAsync();
            p.Sections = [new SectionSetting { Key = "contact", Enabled = false, Shown = false }, new SectionSetting { Key = "about", Enabled = false }];
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<Portfolio.Web.Services.ISiteProfileProvider>().Invalidate();

        Assert.Equal(HttpStatusCode.Redirect, (await SectionsFlow.SaveAsync(owner, SectionsFlow.Set("skills", label: "Abilities"))).StatusCode);

        var stored = await SectionsFlow.StoredAsync(app);
        Assert.Equal(false, stored.Single(s => s.Key == "contact").Shown); // carried over unchanged; still inert while the template requires contact
        Assert.False(stored.Single(s => s.Key == "contact").Enabled);
        Assert.Equal(HttpStatusCode.OK, (await app.NewClient().GetAsync("/contact")).StatusCode);
        Assert.Equal(false, stored.Single(s => s.Key == "about").Shown); // and an older hide on an ordinary section is kept as an explicit one
    }

    [Fact]
    public async Task AHiddenNameField_IsNotDrawn_NotValidated_NotReadFromThePost_AndTheStoredNamesSurviveASave()
    {
        using var app = AdminApp.WithTemplates(Quirky(labelHidden: ", \"hidden\": true"));
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        using (var db = app.CreateDbContext())
        {
            var p = await db.SiteProfiles.SingleAsync();
            p.Sections = [new SectionSetting { Key = "skills", Label = "Kept name" }];
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<Portfolio.Web.Services.ISiteProfileProvider>().Invalidate();

        var page = await owner.GetStringAsync(SectionsFlow.Path);
        Assert.DoesNotContain("__Label", page);
        Assert.Contains("Form_Rows_0__Enabled", page);

        // The post carries hostile names for every row, which are neither read nor validated; the switch and the order still work.
        var rows = (await SectionsFlow.RowsAsync(owner)).Select(r => r with { Label = "<script>x</script>", Enabled = r.Key != "certifications" }).ToList();
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, SectionsFlow.Path, SectionsFlow.Fields(rows))).StatusCode);

        var stored = await SectionsFlow.StoredAsync(app);
        Assert.Equal("Kept name", stored.Single(s => s.Key == "skills").Label);
        Assert.DoesNotContain(stored, s => s.Label?.Contains('<') == true);
        Assert.Equal(false, stored.Single(s => s.Key == "certifications").Shown);
    }

    [Fact]
    public async Task TheShippedTemplates_WordThePage_InTheirOwnWords()
    {
        using (var software = new AdminApp())
        {
            var owner = await ContentFlow.BlankSiteAsync(software);
            var page = await owner.GetStringAsync(SectionsFlow.Path);
            Assert.Contains("<h1 id=\"page-heading\">Site sections</h1>", page);
            Assert.Contains("Menu name", page);
            Assert.DoesNotContain("Before you publish", page);
        }
        using var healthcare = new AdminApp();
        var nurse = await ContentFlow.BlankSiteAsync(healthcare, "healthcare");
        var html = await nurse.GetStringAsync(SectionsFlow.Path);
        Assert.Contains("<h1 id=\"page-heading\">Pages of your site</h1>", html);
        Assert.Contains("Not shown by default for this profession.", html);
        Assert.Contains("Before you publish", html); // the sensitive template's guidance
        var bad = await (await SectionsFlow.SaveAsync(nurse, r => r.Select(x => x with { Enabled = false }).ToList())).Content.ReadAsStringAsync();
        Assert.Contains("Keep at least one &quot;Page&quot; switched on that appears in the menu.", bad);
    }
}
