using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The blog editor takes its words and field visibility from the manifest, messages included; the blog may be off in a template.</summary>
public class BlogTemplateTests
{
    private const string Edit = ContentFlow.Blog + "/edit";

    // Words that exist nowhere in the app: if they show up, they came from this manifest.
    private static string Quirky() => TemplateDirs.WithExtra("quirky", node =>
    {
        node["name"] = "Quirky";
        node["editor"] = JsonNode.Parse("""
        {
          "blog": { "itemLabel": "Scribble", "fields": {
            "title": { "label": "Scribble name", "hint": "Scribble name hint." },
            "body": { "label": "Scribble text" }, "date": { "label": "Scribbled on" }, "slug": { "label": "Scribble address" },
            "draft": { "label": "Keep it secret" }, "tags": { "hidden": true }, "description": { "hidden": true }, "cover": { "hidden": true } } }
        }
        """);
        foreach (var s in node["sections"]!.AsArray().Select(s => s!.AsObject()))
            if (s["key"]!.GetValue<string>() == "blog") s["label"] = "scribbles & doodles";
    });

    [Fact]
    public async Task EveryLabelAndMessage_ComesFromTheManifest_NotFromThePage()
    {
        using var app = AdminApp.WithTemplates(Quirky());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");

        var hub = await owner.GetStringAsync("/admin/content");
        Assert.Contains("Scribbles &amp; doodles", hub);
        Assert.Contains("Add scribble", hub);

        var form = await owner.GetStringAsync(Edit);
        foreach (var word in new[] { "Scribble name", "Scribble name hint.", "Scribble text", "Scribbled on", "Scribble address", "Keep it secret", "Add scribble", "Save scribble" })
            Assert.Contains(word, form);
        Assert.Contains("Scribbles &amp; doodles", await owner.GetStringAsync(ContentFlow.Blog));
        Assert.Contains("scribbled on", await owner.GetStringAsync(ContentFlow.Blog));
        Assert.Contains("No entries yet", await owner.GetStringAsync(ContentFlow.Blog));

        await owner.GetAsync($"{Edit}/999");
        Assert.Contains("That scribble no longer exists.", await owner.GetStringAsync(ContentFlow.Blog));
        await ContentFlow.PostAsync(owner, Edit, ContentFlow.Fields(("Title", "Dup"), ("Date", "2025-01-01"), ("Body", "x"), ("Slug", "same-one")));
        var dup = WebUtility.HtmlDecode(await (await ContentFlow.PostAsync(owner, Edit, ContentFlow.Fields(("Title", "Dup two"), ("Date", "2025-01-01"), ("Body", "x"), ("Slug", "same-one")))).Content.ReadAsStringAsync());
        Assert.Contains("already used by another scribble", dup);
        var noBody = await (await ContentFlow.PostAsync(owner, Edit, ContentFlow.Fields(("Title", "No text"), ("Date", "2025-01-01"), ("Body", "")))).Content.ReadAsStringAsync();
        Assert.Contains("Scribble text is required.", noBody);
        var badDate = await (await ContentFlow.PostAsync(owner, Edit, ContentFlow.Fields(("Title", "Bad date"), ("Date", "never"), ("Body", "x")))).Content.ReadAsStringAsync();
        Assert.Contains("Scribbled on must be a date", badDate);
        var id = (await ContentFlow.IdsAsync(app, db => db.BlogPosts.Select(b => b.Id))).Single();
        var confirm = await owner.GetStringAsync($"{ContentFlow.Blog}/delete/{id}");
        Assert.Contains("Remove this scribble?", confirm);
        Assert.Contains("Remove scribble", confirm);

        foreach (var text in new[] { form, dup, noBody, confirm })
        {
            Assert.DoesNotContain("Add post", text);
            Assert.DoesNotContain("another post", text);
            Assert.DoesNotContain("Remove post", text);
        }
    }

    [Fact]
    public async Task AFieldTheTemplateHides_IsNotDrawn_NotReadFromThePost_AndItsStoredDataSurvivesAnEdit()
    {
        using var app = AdminApp.WithTemplates(Quirky());
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        var name = new string('b', 32) + ".jpg";
        using (var db = app.CreateDbContext())
        {
            db.BlogPosts.Add(new BlogPost
            {
                Title = "Kept", Slug = "kept", Date = new DateOnly(2024, 2, 3), Description = "Kept description.", Tags = ["Kept", "Tags"],
                Cover = "/uploads/" + name, CoverAlt = "Kept alt", BodyMarkdown = "Kept body.", ConcurrencyStamp = "s",
            });
            await db.SaveChangesAsync();
        }
        var id = (await ContentFlow.IdsAsync(app, db => db.BlogPosts.Select(b => b.Id))).Single();
        var path = $"{Edit}/{id}";

        var form = await owner.GetStringAsync(path);
        foreach (var hidden in new[] { "Form.Tags", "Form.Description", "Form.CoverFile", "Form.CoverAlt", "Form.RemoveCover" }) Assert.DoesNotContain(hidden, form);
        Assert.Contains("Form.Body", form);

        // A crafted post that names the hidden fields (with values that would be refused if read) neither fails nor changes them.
        var fields = ContentFlow.Fields(("Title", "Kept, renamed"), ("Slug", "kept"), ("Date", "2024-02-03"), ("Body", "Edited body."), ("Draft", "false"),
            ("Tags", "<b>x</b>"), ("Description", "<script>"), ("CoverAlt", "Hacked"), ("RemoveCover", "true"));
        fields["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, fields)).StatusCode);

        using var check = app.CreateDbContext();
        var post = await check.BlogPosts.AsNoTracking().SingleAsync();
        Assert.Equal(("Kept, renamed", "Edited body.", "Kept description.", "/uploads/" + name, "Kept alt"), (post.Title, post.BodyMarkdown, post.Description, post.Cover, post.CoverAlt));
        Assert.Equal(["Kept", "Tags"], post.Tags);
    }

    [Fact]
    public async Task Healthcare_KeepsTheBlogOff_ShowsTheGuidanceAndTheNote_AndNothingLeaksToThePublicSite()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, "healthcare");

        var list = await owner.GetStringAsync(ContentFlow.Blog);
        var form = await owner.GetStringAsync(Edit);
        Assert.Contains("does not show this section on the public site", list);
        Assert.Contains("Never publish patient-identifiable information", form);
        Assert.Contains("Article title", form);
        Assert.Contains("Article text", form);
        Assert.Contains("Add article", list);

        var save = await ContentFlow.PostAsync(owner, Edit, ContentFlow.Fields(("Title", "Grand rounds notes"), ("Date", "2025-01-01"), ("Body", "Aggregate lessons."), ("Draft", "false")));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var client = app.NewClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/blog")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/blog/grand-rounds-notes")).StatusCode);
        var sitemap = await client.GetStringAsync("/sitemap.xml");
        Assert.DoesNotContain("/blog", sitemap);
        Assert.DoesNotContain("grand-rounds-notes", sitemap);
        Assert.DoesNotContain("Grand rounds notes", await client.GetStringAsync("/"));
        Assert.Single(await ContentFlow.IdsAsync(app, db => db.BlogPosts.Select(b => b.Id))); // kept for a template that uses it
    }
}
