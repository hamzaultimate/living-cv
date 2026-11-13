using System.Net;
using System.Text.Json.Nodes;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Transfer;

/// <summary>The export and import page takes every noun from the manifest: its own words from <c>editor.transfer</c>, the entries' from the other editors and the sections' names.</summary>
public class TransferTemplateTests
{
    // Words that exist nowhere in the app: if they show up, they came from this manifest.
    private static string Quirky() => TemplateDirs.WithExtra("quirky", node =>
    {
        node["name"] = "Quirky";
        foreach (var section in node["sections"]!.AsArray())
        {
            var key = section!["key"]!.GetValue<string>();
            section["label"] = "Zone " + key;
            section.AsObject().Remove("shortLabel");
        }
        node["editor"] = JsonNode.Parse("""
        {
          "experience": { "itemLabel": "Zrole", "fields": { "company": { "label": "Zorg" }, "title": { "label": "Ztitle" }, "start": { "label": "Zbegin" }, "end": { "label": "Zfinish" }, "summary": { "label": "Zsummary" } } },
          "skills": { "itemLabel": "Zskill", "fields": { "name": { "label": "Zskname" }, "group": { "label": "Zskgroup" } } },
          "projects": { "itemLabel": "Zproj", "fields": { "title": { "label": "Zprtitle" }, "slug": { "label": "Zprslug" }, "summary": { "label": "Zprsum" }, "heroImage": { "label": "Zprhero" }, "links": { "label": "Zprlinks" } } },
          "certifications": { "itemLabel": "Zcert", "fields": { "name": { "label": "Zcname" }, "status": { "label": "Zcstatus" }, "date": { "label": "Zcdate" } } },
          "talks": { "itemLabel": "Ztalk", "fields": { "title": { "label": "Zttitle" }, "date": { "label": "Ztdate" } } },
          "testimonials": { "itemLabel": "Zquote", "fields": { "quote": { "label": "Zqtext" }, "author": { "label": "Zqauthor" } } },
          "blog": { "itemLabel": "Zpost", "fields": { "title": { "label": "Zbtitle" }, "slug": { "label": "Zbslug" }, "date": { "label": "Zbdate" }, "body": { "label": "Zbbody" } } },
          "hero": { "itemLabel": "Zhero", "fields": { "heading": { "label": "Zhero page" }, "headline": { "label": "Zheadline" } } },
          "sections": { "itemLabel": "Zsec", "fields": { "heading": { "label": "Zsec map" } } },
          "transfer": { "itemLabel": "Zentry", "fields": {
            "heading": { "label": "Zport", "hint": "Zport hint." }, "exportHeading": { "label": "Zout", "hint": "Zout hint." }, "exportButton": { "label": "Zdownload" },
            "importHeading": { "label": "Zin", "hint": "Zin hint." }, "fileLabel": { "label": "Zfile" }, "applyProfile": { "label": "Zapply", "hint": "Zapply hint." },
            "previewButton": { "label": "Zpreview" }, "confirmButton": { "label": "Zconfirm" }, "discardButton": { "label": "Zdiscard" }, "profile": { "label": "Zprofile" },
            "authoritative": { "label": "Zauth", "hint": "Zauth hint." } } }
        }
        """);
    });

    // Words of the neutral defaults and of the shipped templates: none may appear where the manifest has words of its own.
    private static readonly string[] Neutral =
    [
        "Export and import", "Download the export file", "Preview the import", "Apply the import", "Discard the preview", "Export file (.json)", "Also apply the profile",
        "Start-up seeding is set to replace content", "Backup and portability", "Save and restore",
    ];

    private static string[] Nouns => ["skill", "role", "credential", "certificate", "testimonial", "case study", "post", "talk", "section"];

    private static void AssertOnlyTheManifestsWords(string label, string html)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(WebUtility.HtmlDecode(html), "<[^>]+>", " ");
        foreach (var neutral in Neutral)
            Assert.False(text.Contains(neutral, StringComparison.OrdinalIgnoreCase), $"{label} carries the wording \"{neutral}\"");
    }

    [Fact]
    public async Task ThePageAndTheDashboardTile_UseTheManifestsWords()
    {
        using var app = AdminApp.WithTemplates(Quirky());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        await owner.GetAsync("/admin");

        var dashboard = await owner.GetStringAsync("/admin");
        var page = await owner.GetStringAsync(TransferFlow.Page);

        Assert.Contains("href=\"/admin/transfer\"", dashboard);
        Assert.Contains("<h2>Zport</h2>", dashboard);
        Assert.Contains("<h1 id=\"page-heading\">Zport</h1>", page);
        foreach (var word in new[] { "Zport hint.", "Zout", "Zout hint.", "Zdownload", "Zin", "Zin hint.", "Zfile", "Zapply", "Zapply hint.", "Zpreview" })
            Assert.Contains(word, page);
        AssertOnlyTheManifestsWords("the page", page);
        AssertOnlyTheManifestsWords("the dashboard", dashboard.Replace("Download everything as one file, or load one. You see a preview first, and nothing is ever deleted.", ""));
        Assert.DoesNotContain("Zauth", page); // the notice is only for an authoritative start-up seeding
    }

    [Fact]
    public async Task EveryMessageOfAPreview_NamesThingsInTheManifestsWords()
    {
        using var app = AdminApp.WithTemplates(Quirky());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        await ContentFlow.SeedRowsAsync(app, pairs: true);

        var preview = await (await TransferFlow.PreviewAsync(owner, ImportHarness.File(content: new
        {
            experience = new object[] { new { company = "Seed Co", title = "Seed Role", summary = "Same." }, new { company = "seed co", title = "SEED ROLE" }, new { company = "A|B", title = "Role", start = "soon", summary = "x" } },
            skills = new object[] { new { name = "", group = "G" }, new { name = "<b>x</b>", group = "G" }, new { name = "Dup", group = "G" }, new { name = "dup", group = "G" } },
            projects = new object[] { new { title = "P", summary = "S", slug = "Bad Slug!" }, new { title = "Q", summary = "S", links = new[] { new { label = "L", url = "javascript:alert(1)" } } } },
            certifications = new object[] { new { name = "C", issuer = "I", status = "Nope" } },
            talks = new object[] { new { title = "T", date = "tomorrow" } },
            testimonials = new object[] { new { quote = "Praise that is long enough.", author = "Same Author" }, new { quote = "Praise that is long enough.", author = "SAME AUTHOR" } },
            blog = new object[] { new { title = "B", slug = "edit", date = "2025-13-01", body = "" } },
        }), applyProfile: false)).Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(preview);

        // The table captions and column headings: the manifest's section names and entry names.
        foreach (var word in new[] { "Zone experience", "Zone skills", "Zone projects", "Zone certifications", "Zone talks", "Zone testimonials", "Zone blog", "Zrole", "Zskill", "Zproj", "Zcert", "Ztalk", "Zquote", "Zpost" })
            Assert.Contains(word, decoded);
        // Field messages come through the editors' own labels.
        Assert.Contains("Zskname is required.", decoded);
        Assert.Contains("Zskname is plain text: remove anything that looks like a tag", decoded);
        Assert.Contains("Zorg cannot contain the | character.", decoded);
        Assert.Contains("Zbegin must be a year or a month and year", decoded);
        Assert.Contains("Zprslug can only use lower-case letters", decoded);
        Assert.Contains("Choose one of the listed values for zcstatus", decoded);
        Assert.Contains("Ztdate must be a date", decoded);
        Assert.Contains("Zbbody is required.", decoded);
        Assert.Contains("Zbdate must be a date written as year-month-day", decoded);
        Assert.Contains("\"edit\" is reserved. Choose a different zbslug.", decoded);
        // The within-file duplicates name the entry and what makes two the same.
        Assert.Contains("An earlier zrole in the file has the same zorg and ztitle.", decoded);
        Assert.Contains("An earlier zskill in the file has the same zskname.", decoded);
        Assert.Contains("An earlier zquote in the file has the same zqauthor and the start of the zqtext.", decoded);
        Assert.Contains("Zskill 1", decoded); // an unnamed entry is numbered with the manifest noun
        Assert.Contains("Zpreview", preview.Replace("Zconfirm", ""), StringComparison.Ordinal);
        AssertOnlyTheManifestsWords("the preview", preview);
        foreach (var neutral in new[] { "Skill ", " skill ", "Credential", "Testimonial", "Case study", "Job title" })
            Assert.DoesNotContain(neutral, decoded);
    }

    [Fact]
    public async Task TheResultAndItsStaleMessages_UseTheManifestsWords_IncludingTheProfileName()
    {
        using var app = AdminApp.WithTemplates(Quirky());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        await ContentFlow.SeedRowsAsync(app, pairs: true);
        var token = await TransferFlow.HoldAsync(owner, ImportHarness.File(
            profile: new { ownerName = "Different Name" },
            content: new { skills = new object[] { new { name = "Seeding", group = "Changed" } } }), applyProfile: true);
        var preview = await owner.GetStringAsync(TransferFlow.Page); // the page itself carries no preview; the held one is confirmed below
        _ = preview;
        using (var db = app.CreateDbContext())
        {
            var row = db.Skills.Single(x => x.Name == "Seeding");
            row.Group = "Elsewhere";
            row.ConcurrencyStamp = Portfolio.Web.Services.ContentEditor.NewStamp();
            await db.SaveChangesAsync();
        }
        var save = await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields(headline: "Edited meanwhile"));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        var result = WebUtility.HtmlDecode(await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync());

        Assert.Contains("This zskill was changed after the preview was made, so it was not updated and nothing was overwritten.", result);
        Assert.Contains("The zprofile was changed after the preview was made, so none of it was applied and nothing was overwritten.", result);
        Assert.Contains("Zone skills", result);
        AssertOnlyTheManifestsWords("the result", result);
        foreach (var neutral in new[] { "This skill", "The profile was" })
            Assert.DoesNotContain(neutral, result);

        var gone = WebUtility.HtmlDecode(await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync());
        Assert.Contains("so nothing was written", gone);
    }

    [Fact]
    public async Task TheCollisionAndRemovalMessages_NameTheEntryInTheManifestsWords()
    {
        using var app = AdminApp.WithTemplates(Quirky());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        await ContentFlow.SeedRowsAsync(app, pairs: true);
        var token = await TransferFlow.HoldAsync(owner, ImportHarness.Table("skills", new { name = "Seeding", group = "Changed" }, new { name = "Raced", group = "G" }));
        using (var db = app.CreateDbContext())
        {
            db.Skills.Remove(db.Skills.Single(x => x.Name == "Seeding"));
            db.Skills.Add(new Portfolio.Data.Entities.Skill { Name = "raced", Group = "Else", SortOrder = 9, ConcurrencyStamp = Portfolio.Web.Services.ContentEditor.NewStamp() });
            await db.SaveChangesAsync();
        }

        var result = WebUtility.HtmlDecode(await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync());

        Assert.Contains("This zskill was removed after the preview was made, so it was not updated.", result);
        Assert.Contains("A zskill with the same zskname was added after the preview was made, so this one was not added.", result);
    }

    [Fact]
    public async Task TheProfileMessages_UseTheTemplatesWordsForItsBlocks()
    {
        using var app = AdminApp.WithTemplates(Quirky());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        await ProfileFlow.SeedTextAsync(app);

        var preview = WebUtility.HtmlDecode(await (await TransferFlow.PreviewAsync(owner, ImportHarness.File(
            profile: new { hero = new { headline = "" }, ownerName = "New Name" }), applyProfile: true)).Content.ReadAsStringAsync());
        var ok = WebUtility.HtmlDecode(await (await TransferFlow.PreviewAsync(owner, ImportHarness.File(
            profile: new { hero = new { lede = "A lede." }, sections = new[] { new { key = "blog", label = "Notes" } } }), applyProfile: true)).Content.ReadAsStringAsync());

        Assert.Contains("<h3>Zprofile</h3>", preview);
        Assert.Contains("Zheadline is required.", preview);
        Assert.Contains("Changed: Zhero page", ok);
        Assert.Contains("Changed: Zsec map", ok);
        Assert.DoesNotContain("Changed: Home page", ok);
        AssertOnlyTheManifestsWords("the profile preview", preview + ok);
    }

    [Fact]
    public async Task TheAuthoritativeSeedingNotice_IsShownOnlyThen_InTheManifestsWords()
    {
        using var app = AdminApp.WithTemplates(Quirky(), new Dictionary<string, string?> { ["Seed:Mode"] = "Authoritative" });
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");

        var page = await owner.GetStringAsync(TransferFlow.Page);

        Assert.Contains("Zauth", page);
        Assert.Contains("Zauth hint.", page);
        Assert.Contains("role=\"note\"", page);

        using var plain = AdminApp.WithTemplates(Quirky());
        var plainOwner = await ContentFlow.BlankSiteAsync(plain, "quirky");
        Assert.DoesNotContain("Zauth", await plainOwner.GetStringAsync(TransferFlow.Page));
    }

    [Theory]
    [InlineData("software", "Backup and portability", "Download the export file")]
    [InlineData("healthcare", "Save and restore your portfolio", "Download my portfolio file")]
    public async Task BothShippedTemplates_HaveTheirOwnWords_AndSoftwaresIsItsOwn(string template, string heading, string button)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, template);

        var page = await owner.GetStringAsync(TransferFlow.Page);

        Assert.Contains($"<h1 id=\"page-heading\">{heading}</h1>", page);
        Assert.Contains(button, page);
        Assert.Contains(heading, await owner.GetStringAsync("/admin"));
        if (template == "healthcare")
        {
            Assert.Contains("Never put patient information in an entry.", page); // the sensitive template's guidance on every editor page
            Assert.Contains("Check the file", page);
        }
    }

    [Fact]
    public async Task ThePreviewAndResult_OfASensitiveTemplate_CarryItsWords_AndItsGuidance()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, "healthcare");

        var preview = await (await TransferFlow.PreviewAsync(owner, ImportHarness.Table("skills", new { name = "Wound care", group = "Clinical" }))).Content.ReadAsStringAsync();
        var token = TransferFlow.TokenOf(preview)!;
        var result = await (await TransferFlow.ConfirmAsync(owner, token)).Content.ReadAsStringAsync();

        Assert.Contains("Restore now", preview);
        Assert.Contains("Cancel", preview);
        Assert.Contains("Wound care", result);
        Assert.Matches(@"<caption>[^<]*\(1 in the file\)</caption>", preview);
    }
}
