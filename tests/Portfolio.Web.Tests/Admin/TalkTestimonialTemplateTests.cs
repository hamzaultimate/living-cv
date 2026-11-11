using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The talks and testimonials editors take their words and field visibility from the manifest, messages included.</summary>
public class TalkTestimonialTemplateTests
{
    // Words that exist nowhere in the app: if they show up, they came from this manifest.
    private static string Quirky(Action<JsonObject>? more = null) => TemplateDirs.WithExtra("quirky", node =>
    {
        node["name"] = "Quirky";
        node["editor"] = JsonNode.Parse("""
        {
          "talks": { "itemLabel": "Gabfest", "fields": {
            "title": { "label": "Gabfest name", "hint": "Gabfest name hint." },
            "typeVideo": { "label": "Gizmo clip" }, "typeTalk": { "label": "Gizmo speech" },
            "event": { "hidden": true }, "url": { "hidden": true }, "youtubeId": { "label": "Clip" }, "date": { "label": "Gabbed on" } } },
          "testimonials": { "itemLabel": "Kudo", "fields": {
            "quote": { "label": "Kudo text" }, "author": { "label": "Kudo giver" }, "authorTitle": { "hidden": true }, "sourceUrl": { "hidden": true } } }
        }
        """);
        foreach (var s in node["sections"]!.AsArray().Select(s => s!.AsObject()))
        {
            if (s["key"]!.GetValue<string>() == "talks") s["label"] = "gabfests & gossip";
            if (s["key"]!.GetValue<string>() == "testimonials") s["label"] = "kudos";
        }
        more?.Invoke(node);
    });

    [Fact]
    public async Task EveryLabelAndMessage_ComesFromTheManifest_NotFromThePage()
    {
        using var app = AdminApp.WithTemplates(Quirky());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");

        var hub = await owner.GetStringAsync("/admin/content");
        Assert.Contains("Gabfests &amp; gossip", hub);
        Assert.Contains("Add gabfest", hub);
        Assert.Contains("Kudos", hub);
        Assert.Contains("Add kudo", hub);

        var talkForm = await owner.GetStringAsync(ContentFlow.Talks + "/edit");
        Assert.Contains("Gabfest name", talkForm);
        Assert.Contains("Gabfest name hint.", talkForm);
        Assert.Contains("Clip", talkForm);
        Assert.Contains("Gabbed on", talkForm);
        Assert.Contains("Add gabfest", talkForm);
        Assert.Contains(">Gizmo clip</option>", talkForm);
        Assert.Contains(">Gizmo speech</option>", talkForm);
        Assert.DoesNotContain(">Video</option>", talkForm);
        Assert.DoesNotContain(">Talk</option>", talkForm);
        Assert.DoesNotContain("Video and link", talkForm);
        var kudoForm = await owner.GetStringAsync(ContentFlow.Testimonials + "/edit");
        Assert.Contains("Kudo text", kudoForm);
        Assert.Contains("Kudo giver", kudoForm);
        Assert.Contains("Save kudo", kudoForm);
        Assert.Contains("Gabfests &amp; gossip", await owner.GetStringAsync(ContentFlow.Talks));
        Assert.Contains("No entries yet", await owner.GetStringAsync(ContentFlow.Testimonials));

        // Messages: not found, duplicate, bad date, the confirmation page.
        await owner.GetAsync($"{ContentFlow.Talks}/edit/999");
        Assert.Contains("That gabfest no longer exists.", await owner.GetStringAsync(ContentFlow.Talks));
        await owner.GetAsync($"{ContentFlow.Testimonials}/delete/999");
        Assert.Contains("That kudo no longer exists.", await owner.GetStringAsync(ContentFlow.Testimonials));

        await ContentFlow.PostAsync(owner, ContentFlow.Talks + "/edit", ContentFlow.Fields(("Title", "Dup")));
        var dup = await (await ContentFlow.PostAsync(owner, ContentFlow.Talks + "/edit", ContentFlow.Fields(("Title", "dup")))).Content.ReadAsStringAsync();
        Assert.Contains("already have a gabfest called", dup);
        Assert.Contains("Gabfest name", dup);
        var badDate = await (await ContentFlow.PostAsync(owner, ContentFlow.Talks + "/edit", ContentFlow.Fields(("Title", "Other"), ("Date", "never")))).Content.ReadAsStringAsync();
        Assert.Contains("Gabbed on must be a date", badDate);
        var badClip = await (await ContentFlow.PostAsync(owner, ContentFlow.Talks + "/edit", ContentFlow.Fields(("Title", "Other"), ("YoutubeId", "nope")))).Content.ReadAsStringAsync();
        Assert.Contains("Clip must be a YouTube link", badClip);

        await ContentFlow.PostAsync(owner, ContentFlow.Testimonials + "/edit", ContentFlow.Fields(("Quote", "Same opening, long enough to repeat."), ("Author", "Pat")));
        var dupKudo = await (await ContentFlow.PostAsync(owner, ContentFlow.Testimonials + "/edit", ContentFlow.Fields(("Quote", "Same opening, long enough to repeat."), ("Author", "Pat")))).Content.ReadAsStringAsync();
        Assert.Contains("already have a kudo from", dupKudo);
        var noQuote = await (await ContentFlow.PostAsync(owner, ContentFlow.Testimonials + "/edit", ContentFlow.Fields(("Quote", ""), ("Author", "Pat")))).Content.ReadAsStringAsync();
        Assert.Contains("Kudo text is required.", noQuote);

        var id = (await ContentFlow.IdsAsync(app, db => db.Talks.Select(t => t.Id))).Single();
        var confirm = await owner.GetStringAsync($"{ContentFlow.Talks}/delete/{id}");
        Assert.Contains("Remove this gabfest?", confirm);
        Assert.Contains("Remove gabfest", confirm);

        // None of the neutral nouns leaks into those screens.
        foreach (var text in new[] { talkForm, kudoForm, dup, dupKudo, confirm })
        {
            Assert.DoesNotContain("Add talk", text);
            Assert.DoesNotContain("Add testimonial", text);
            Assert.DoesNotContain("called \"Dup\" at", text);
        }
    }

    [Fact]
    public async Task AFieldTheTemplateHides_IsNotDrawn_NotValidated_NotReadFromThePost_AndItsStoredDataSurvivesAnEdit()
    {
        using var app = AdminApp.WithTemplates(Quirky());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        using (var db = app.CreateDbContext())
        {
            db.Talks.Add(new Talk { Title = "Kept", Event = "Kept Conf", Url = "https://example.org/kept", YoutubeId = "aqz-KE-bpKQ", Date = new DateOnly(2024, 2, 3), SortOrder = 1, ConcurrencyStamp = "s1" });
            db.Testimonials.Add(new Testimonial { Quote = "Kept quote, long enough to be real.", Author = "Kept Person", AuthorTitle = "Kept Role", SourceUrl = "https://example.org/src", SortOrder = 1, ConcurrencyStamp = "s2" });
            await db.SaveChangesAsync();
        }
        var talkId = (await ContentFlow.IdsAsync(app, db => db.Talks.Select(t => t.Id))).Single();
        var kudoId = (await ContentFlow.IdsAsync(app, db => db.Testimonials.Select(t => t.Id))).Single();

        var talkForm = await owner.GetStringAsync($"{ContentFlow.Talks}/edit/{talkId}");
        Assert.DoesNotContain("Form.Event", talkForm);
        Assert.DoesNotContain("Form.Url", talkForm);
        Assert.Contains("Form.YoutubeId", talkForm);
        var kudoForm = await owner.GetStringAsync($"{ContentFlow.Testimonials}/edit/{kudoId}");
        Assert.DoesNotContain("Form.AuthorTitle", kudoForm);
        Assert.DoesNotContain("Form.SourceUrl", kudoForm);

        // A post that names the hidden fields (with values that would be refused if they were read) neither fails nor changes them.
        var talk = ContentFlow.Fields(("Title", "Kept, renamed"), ("Event", "<script>"), ("Url", "javascript:alert(1)"), ("Date", "2024-02-03"), ("YoutubeId", "aqz-KE-bpKQ"));
        talk["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Talks}/edit/{talkId}");
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, $"{ContentFlow.Talks}/edit/{talkId}", talk)).StatusCode);
        var kudo = ContentFlow.Fields(("Quote", "Kept quote, edited and long enough."), ("Author", "Kept Person"), ("AuthorTitle", "<b>x</b>"), ("SourceUrl", "javascript:alert(1)"));
        kudo["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Testimonials}/edit/{kudoId}");
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, $"{ContentFlow.Testimonials}/edit/{kudoId}", kudo)).StatusCode);

        using var check = app.CreateDbContext();
        var t = await check.Talks.AsNoTracking().SingleAsync();
        Assert.Equal(("Kept, renamed", "Kept Conf", "https://example.org/kept", "aqz-KE-bpKQ", new DateOnly(2024, 2, 3)), (t.Title, t.Event, t.Url, t.YoutubeId, t.Date));
        var m = await check.Testimonials.AsNoTracking().SingleAsync();
        Assert.Equal(("Kept quote, edited and long enough.", "Kept Role", "https://example.org/src"), (m.Quote, m.AuthorTitle, m.SourceUrl));
    }

    [Fact]
    public async Task TheConfirmationPage_ListsOnlyFieldsTheTemplateShows()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, "healthcare"); // hides the video field
        using (var db = app.CreateDbContext())
        {
            db.Talks.Add(new Talk { Title = "Grand rounds", Event = "Ward meeting", YoutubeId = "aqz-KE-bpKQ", Date = new DateOnly(2024, 2, 3), ConcurrencyStamp = "s" });
            await db.SaveChangesAsync();
        }
        var id = (await ContentFlow.IdsAsync(app, db => db.Talks.Select(t => t.Id))).Single();

        var confirm = await owner.GetStringAsync($"{ContentFlow.Talks}/delete/{id}");

        Assert.Contains("Ward meeting", confirm);
        Assert.DoesNotContain("aqz-KE-bpKQ", confirm);
    }

    [Fact]
    public async Task ARequiredField_CannotBeHidden_ByAManifest()
    {
        var dir = TemplateDirs.WithExtra("greedy", node =>
        {
            node["name"] = "Greedy";
            node["editor"] = JsonNode.Parse("""{ "talks": { "fields": { "title": { "hidden": true } } }, "testimonials": { "fields": { "quote": { "hidden": true } } } }""");
        });
        // The template is skipped with a logged error, so the site falls back to the default one: the editors still show their required fields.
        using var app = AdminApp.WithTemplates(dir);
        var owner = await ContentFlow.BlankSiteAsync(app);

        Assert.Contains("Form.Title", await owner.GetStringAsync(ContentFlow.Talks + "/edit"));
        Assert.Contains("Form.Quote", await owner.GetStringAsync(ContentFlow.Testimonials + "/edit"));
    }

    [Fact]
    public async Task Healthcare_ShowsTheGuidanceOnTheNewEditors_AndNotesThatPresentationsAreNotShown_WhileReferencesAre()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, "healthcare");

        var talks = await owner.GetStringAsync(ContentFlow.Talks);
        var talkForm = await owner.GetStringAsync(ContentFlow.Talks + "/edit");
        var kudoList = await owner.GetStringAsync(ContentFlow.Testimonials);
        var kudoForm = await owner.GetStringAsync(ContentFlow.Testimonials + "/edit");

        Assert.Contains("Never publish patient-identifiable information", talkForm);
        Assert.Contains("Never publish patient-identifiable information", kudoForm);
        Assert.Contains("does not show this section on the public site", talks); // presentations are off in this template
        Assert.DoesNotContain("does not show this section", kudoList);           // references are on
        Assert.Contains("Presentations", talks);
        Assert.Contains("Add presentation", talks);
        Assert.Contains("Reference text", kudoForm);
        Assert.Contains("Referee", kudoForm);
        Assert.DoesNotContain("Form.YoutubeId", talkForm);                       // hidden by the template
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"{ContentFlow.Talks}/edit")).StatusCode);
        // The editor is reachable and saves although the public page is off; the entry is kept for a template that uses it.
        var save = await ContentFlow.PostAsync(owner, ContentFlow.Talks + "/edit", ContentFlow.Fields(("Title", "Grand rounds")));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        Assert.Single(await ContentFlow.IdsAsync(app, db => db.Talks.Select(t => t.Id)));
    }
}
