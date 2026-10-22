using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;
using Portfolio.Web.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>Edge cases of the content editors found in review.</summary>
public class ContentReviewFixTests
{
    private static string Templates(Action<JsonObject> editor) => TemplateDirs.WithExtra("quirky", node =>
    {
        node["name"] = "Quirky";
        var e = new JsonObject();
        editor(e);
        node["editor"] = e;
    });

    [Fact]
    public async Task HiddenScreenshots_RemoveTheImagesLink_RefuseThePage_AndKeepTheStoredImages()
    {
        var dir = Templates(e => e["projects"] = JsonNode.Parse("""{ "fields": { "screenshots": { "hidden": true } } }"""));
        using var app = AdminApp.WithTemplates(dir);
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        var seeded = await ContentFlow.SeedRowsAsync(app, pairs: true);
        var before = await ContentFlow.SnapshotAsync(app);

        Assert.DoesNotContain("/images/", await owner.GetStringAsync(ContentFlow.Projects));
        var page = await owner.GetAsync($"{ContentFlow.Projects}/images/{seeded.ProjectId}");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        var path = $"{ContentFlow.Projects}/images/{seeded.ProjectId}";
        var stamp = await ContentFlow.StampAsync(owner, $"{ContentFlow.Projects}/edit/{seeded.ProjectId}"); // the project's real, current stamp
        foreach (var (handler, fields) in new[]
                 {
                     ("Upload", new Dictionary<string, string> { ["stamp"] = stamp, ["Caption"] = "x" }),
                     ("Remove", new Dictionary<string, string> { ["stamp"] = stamp, ["url"] = seeded.ScreenshotUrl }),
                     ("Move", new Dictionary<string, string> { ["stamp"] = stamp, ["url"] = seeded.ScreenshotUrl, ["direction"] = "down" }),
                 })
        {
            Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, $"{path}?handler={handler}", fields, ContentFlow.Projects + "/edit")).StatusCode);
            Assert.Contains("not used by your template", await owner.GetStringAsync(ContentFlow.Projects));
        }
        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Theory]
    [InlineData("A|B", "C")]
    [InlineData("A", "B|C")]
    public async Task ABarInARole_IsRefused_BecauseTheSeederKeysOnCompanyAndTitle(string company, string title)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var response = await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields(company, title));

        Assert.Contains("cannot contain the | character", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await ContentFlow.CountAsync(app));
    }

    [Fact]
    public async Task Reordering_DoesNotInvalidateAFormThatIsOpenForAnotherRow()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields("A Co", "First"));
        await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields("B Co", "Second"));
        List<Experience> Rows() { using var db = app.CreateDbContext(); return db.Experiences.AsNoTracking().OrderBy(x => x.SortOrder).ToList(); }
        var rows = Rows();
        var path = $"{ContentFlow.Experience}/edit/{rows[1].Id}";
        var openForm = await ContentFlow.StampAsync(owner, path);

        await ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}?handler=Move&id={rows[0].Id}", new Dictionary<string, string> { ["direction"] = "down" }, ContentFlow.Experience);

        Assert.Equal(rows.Select(r => r.ConcurrencyStamp), Rows().OrderBy(r => r.Id).Select(r => r.ConcurrencyStamp)); // positions changed, stamps did not
        var fields = ContentFlow.RoleFields("B Co", "Second, edited");
        fields["Form.Stamp"] = openForm;
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, fields)).StatusCode);
        Assert.Contains(Rows(), r => r.Title == "Second, edited");
    }

    [Fact]
    public async Task AMoveOverAConcurrentEdit_IsStillRefused()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields("A Co", "First"));
        await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields("B Co", "Second"));
        using var db = app.CreateDbContext();
        var ids = db.Experiences.OrderBy(x => x.SortOrder).Select(x => x.Id).ToList();
        var scope = await db.Experiences.OrderBy(x => x.SortOrder).ToListAsync();
        // Another request edits the first row after this one loaded it.
        using (var other = app.CreateDbContext())
        {
            var row = await other.Experiences.FirstAsync(x => x.Id == ids[0]);
            row.ConcurrencyStamp = "edited-elsewhere";
            await other.SaveChangesAsync();
        }

        // The real editor; a conflict returns before it touches the caches, so no profile editor is needed here.
        var outcome = await new Portfolio.Web.Services.ContentEditor(db, null!, null!).MoveAsync(scope, ids[0], 1, default);

        Assert.Equal(Portfolio.Web.Services.SaveOutcome.Conflict, outcome);
    }

    [Fact]
    public async Task EveryMessage_NamesTheEntryInTheTemplatesWords()
    {
        var dir = Templates(e =>
        {
            e["experience"] = JsonNode.Parse("""{ "itemLabel": "Zorblax post" }""");
            e["skills"] = JsonNode.Parse("""{ "itemLabel": "Quibble" }""");
        });
        using var app = AdminApp.WithTemplates(dir);
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");

        await owner.GetAsync($"{ContentFlow.Experience}/edit/999");
        Assert.Contains("That zorblax post no longer exists.", await owner.GetStringAsync(ContentFlow.Experience));
        await owner.GetAsync($"{ContentFlow.Skills}/edit/999");
        Assert.Contains("That quibble no longer exists.", await owner.GetStringAsync(ContentFlow.Skills));

        await ContentFlow.PostAsync(owner, ContentFlow.Skills + "/edit", ContentFlow.Fields(("Name", "Dup"), ("Group", "G")));
        var again = await ContentFlow.PostAsync(owner, ContentFlow.Skills + "/edit", ContentFlow.Fields(("Name", "dup"), ("Group", "G")));
        Assert.Contains("already have a quibble called", await again.Content.ReadAsStringAsync());
        await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields());
        var again2 = await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields());
        Assert.Contains("already have a zorblax post called", await again2.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ASlugTheProjectAlreadyHas_IsKeptAsStored_AndCheckedOnlyWhenChanged()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
        {
            db.Projects.Add(new Project { Title = "Old", Slug = "Legacy_Slug.v1", Summary = "s", SortOrder = 1 });
            await db.SaveChangesAsync();
        }
        int id; using (var db = app.CreateDbContext()) id = db.Projects.Single().Id;
        var path = $"{ContentFlow.Projects}/edit/{id}";

        var same = ContentFlow.Fields(("Title", "Old, edited"), ("Summary", "s"), ("Slug", "Legacy_Slug.v1"));
        same["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, same)).StatusCode);
        using (var db = app.CreateDbContext()) Assert.Equal("Legacy_Slug.v1", db.Projects.Single().Slug);

        var changed = ContentFlow.Fields(("Title", "Old, edited"), ("Summary", "s"), ("Slug", "Still_Bad"));
        changed["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        Assert.Equal(HttpStatusCode.OK, (await ContentFlow.PostAsync(owner, path, changed)).StatusCode);
    }

    [Fact]
    public async Task WhenTheTemplateHidesCurrent_AnEndDateEndsTheRole_AndNothingElseChangesIt()
    {
        var dir = Templates(e => e["experience"] = JsonNode.Parse("""{ "fields": { "current": { "hidden": true } } }"""));
        using var app = AdminApp.WithTemplates(dir);
        var owner = await ContentFlow.BlankSiteAsync(app, "quirky");
        using (var db = app.CreateDbContext())
        {
            db.Experiences.Add(new Experience { Company = "Co", Title = "Role", Start = "2019", Current = true, Summary = "s", SortOrder = 1 });
            await db.SaveChangesAsync();
        }
        int id; using (var db = app.CreateDbContext()) id = db.Experiences.Single().Id;
        var path = $"{ContentFlow.Experience}/edit/{id}";
        Assert.DoesNotContain("Form.Current", await owner.GetStringAsync(path));

        var noEnd = ContentFlow.Fields(("Company", "Co"), ("Title", "Role"), ("Start", "2019"), ("End", ""), ("Summary", "edited"));
        noEnd["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        await ContentFlow.PostAsync(owner, path, noEnd);
        using (var db = app.CreateDbContext()) Assert.True(db.Experiences.Single().Current);

        var ended = ContentFlow.Fields(("Company", "Co"), ("Title", "Role"), ("Start", "2019"), ("End", "2023"), ("Summary", "edited"));
        ended["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        await ContentFlow.PostAsync(owner, path, ended);
        using (var db = app.CreateDbContext())
        {
            var row = db.Experiences.Single();
            Assert.False(row.Current);
            Assert.Equal("2023", row.End);
        }
    }

    [Fact]
    public async Task ARefusedProjectSave_LeavesNoCoverFileBehind()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, ContentFlow.Projects + "/edit", ContentFlow.Fields(("Title", "P"), ("Summary", "s")));
        int id; using (var db = app.CreateDbContext()) id = db.Projects.Single().Id;
        var path = $"{ContentFlow.Projects}/edit/{id}";
        var fields = ContentFlow.Fields(("Title", "P2"), ("Summary", "s"), ("HeroAlt", "alt"), ("Stamp", "stale-stamp"));

        var response = await ContentFlow.PostMultipartAsync(owner, path, fields,
            ("Form.HeroFile", TestImages.Split(SKEncodedImageFormat.Jpeg, 80, 60), "c.jpg", "image/jpeg"));

        Assert.Contains("changed or removed somewhere else", await response.Content.ReadAsStringAsync());
        Assert.Empty(Directory.GetFiles(app.UploadsPath));
        using var check = app.CreateDbContext();
        Assert.Null(check.Projects.Single().HeroImage);
    }

    [Fact]
    public async Task AFormShownAgainWithErrors_StartsDirty_SoLeavingItPrompts()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, ContentFlow.Projects + "/edit", ContentFlow.Fields(("Title", "P"), ("Summary", "s")));
        int id; using (var db = app.CreateDbContext()) id = db.Projects.Single().Id;

        Assert.Contains("data-has-errors=\"false\"", await owner.GetStringAsync(ContentFlow.Experience + "/edit"));
        var invalid = await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields(company: ""));
        Assert.Contains("data-unsaved-warning data-has-errors=\"true\"", await invalid.Content.ReadAsStringAsync());

        var images = $"{ContentFlow.Projects}/images/{id}";
        var plain = await owner.GetStringAsync(images);
        Assert.Contains("data-unsaved-warning", plain);
        Assert.Contains("data-has-errors=\"false\"", plain);
        Assert.Contains("admin-unsaved", plain);
        var refused = await ContentFlow.PostMultipartAsync(owner, images + "?handler=Upload", new Dictionary<string, string> { ["stamp"] = await ContentFlow.StampAsync(owner, images), ["Caption"] = "" }, null, images);
        Assert.Contains("data-has-errors=\"true\"", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ARoleWithABarThatIsAlreadyStored_CanBeSavedUntouchedInThoseFields_ButNotChangedToAnother()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
        {
            db.Experiences.Add(new Experience { Company = "A|B", Title = "C|D", Start = "2019", End = "2020", Summary = "s", SortOrder = 1 });
            await db.SaveChangesAsync();
        }
        int id; using (var db = app.CreateDbContext()) id = db.Experiences.Single().Id;
        var path = $"{ContentFlow.Experience}/edit/{id}";

        var same = ContentFlow.RoleFields("A|B", "C|D", "2019", "2020", "edited");
        same["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, same)).StatusCode);

        var other = ContentFlow.RoleFields("X|Y", "C|D", "2019", "2020", "edited");
        other["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        Assert.Equal(HttpStatusCode.OK, (await ContentFlow.PostAsync(owner, path, other)).StatusCode);
        using var check = app.CreateDbContext();
        Assert.Equal("A|B", check.Experiences.Single().Company);
    }
}
