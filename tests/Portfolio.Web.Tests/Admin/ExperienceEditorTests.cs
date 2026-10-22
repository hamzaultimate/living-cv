using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class ExperienceEditorTests
{
    private const string Edit = ContentFlow.Experience + "/edit";

    private static async Task<List<Experience>> RowsAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return await db.Experiences.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
    }

    [Fact]
    public async Task CreatingARole_Persists_AndShowsOnThePublicPage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var fields = ContentFlow.RoleFields();
        fields["Form.HighlightsText"] = "Led the ward rota\n\nMentored two students";
        fields["Form.TechText"] = "Cerner\nEMIS";
        var create = await ContentFlow.PostAsync(owner, Edit, fields);

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var row = Assert.Single(await RowsAsync(app));
        Assert.Equal(("Northwind Health", "Staff Nurse", "2019", "2022", false), (row.Company, row.Title, row.Start, row.End, row.Current));
        Assert.Equal(["Led the ward rota", "Mentored two students"], row.Highlights);
        Assert.Equal(["Cerner", "EMIS"], row.Tech);
        Assert.False(string.IsNullOrEmpty(row.ConcurrencyStamp));
        var page = await app.NewClient().GetStringAsync("/experience");
        Assert.Contains("Staff Nurse", page);
        Assert.Contains("Northwind Health", page);
        Assert.Contains("Mentored two students", page);
    }

    [Fact]
    public async Task AfterASave_TheFlashMessageIsAnnounced_AndTheListScrollsToTheRow()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var create = await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields());
        var id = (await RowsAsync(app)).Single().Id;
        Assert.EndsWith($"/admin/content/experience#row-{id}", create.Headers.Location!.OriginalString);
        var list = await owner.GetStringAsync(ContentFlow.Experience);

        Assert.Contains("role=\"status\" aria-live=\"polite\" tabindex=\"-1\" autofocus=\"autofocus\"", list);
        Assert.Contains("Saved Staff Nurse at Northwind Health.", list);
        Assert.Contains($"id=\"row-{id}\"", list);
        // The message is shown once.
        Assert.DoesNotContain("Saved Staff Nurse", await owner.GetStringAsync(ContentFlow.Experience));
    }

    [Fact]
    public async Task EditingARole_ChangesOnlyThatRole_AndKeepsItsPosition()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields("A Co", "First"));
        await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields("B Co", "Second"));
        var rows = await RowsAsync(app);
        var path = $"{Edit}/{rows[0].Id}";
        var stamp = await ContentFlow.StampAsync(owner, path);

        var fields = ContentFlow.RoleFields("A Co", "First, renamed", "2018", "2021");
        fields["Form.Stamp"] = stamp;
        var save = await ContentFlow.PostAsync(owner, path, fields);

        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var after = await RowsAsync(app);
        Assert.Equal(["First, renamed", "Second"], after.Select(r => r.Title));
        Assert.Equal(rows[1].ConcurrencyStamp, after[1].ConcurrencyStamp);
        Assert.NotEqual(rows[0].ConcurrencyStamp, after[0].ConcurrencyStamp);
        Assert.Equal(rows.Select(r => r.SortOrder), after.Select(r => r.SortOrder));
    }

    [Fact]
    public async Task ACurrentRole_HasNoEndDate_AndShowsAsPresent()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields(end: "2023", current: true));

        var row = Assert.Single(await RowsAsync(app));
        Assert.True(row.Current);
        Assert.Null(row.End);
        Assert.Contains("present", await app.NewClient().GetStringAsync("/experience"));
    }

    [Fact]
    public async Task Reordering_PersistsInTheExistingOrderColumn_AndTheOrderGoesPublic()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        foreach (var name in new[] { "First", "Second", "Third" })
            await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields(name + " Co", name));
        var ids = (await RowsAsync(app)).Select(r => r.Id).ToList();

        var down = await ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}?handler=Move&id={ids[0]}", new Dictionary<string, string> { ["direction"] = "down" }, ContentFlow.Experience);
        Assert.Equal(HttpStatusCode.Redirect, down.StatusCode);
        Assert.Equal(["Second", "First", "Third"], (await RowsAsync(app)).Select(r => r.Title));
        Assert.Equal([1, 2, 3], (await RowsAsync(app)).Select(r => r.SortOrder));

        await ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}?handler=Move&id={ids[2]}", new Dictionary<string, string> { ["direction"] = "up" }, ContentFlow.Experience);
        Assert.Equal(["Second", "Third", "First"], (await RowsAsync(app)).Select(r => r.Title));

        var page = await app.NewClient().GetStringAsync("/experience");
        Assert.True(page.IndexOf("Second Co", StringComparison.Ordinal) < page.IndexOf("Third Co", StringComparison.Ordinal));
        Assert.True(page.IndexOf("Third Co", StringComparison.Ordinal) < page.IndexOf("First Co", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MovingTheFirstRowUp_OrTheLastDown_ChangesNothing()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields("A Co", "First"));
        await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields("B Co", "Second"));
        var rows = await RowsAsync(app);
        var before = await ContentFlow.SnapshotAsync(app);

        await ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}?handler=Move&id={rows[0].Id}", new Dictionary<string, string> { ["direction"] = "up" }, ContentFlow.Experience);
        await ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}?handler=Move&id={rows[1].Id}", new Dictionary<string, string> { ["direction"] = "down" }, ContentFlow.Experience);
        await ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}?handler=Move&id={rows[1].Id}", new Dictionary<string, string> { ["direction"] = "sideways" }, ContentFlow.Experience);
        await ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}?handler=Move&id=99999", new Dictionary<string, string> { ["direction"] = "up" }, ContentFlow.Experience);

        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Fact]
    public async Task ReorderingRowsThatShareAPosition_SeparatesThem()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
        {
            db.Experiences.AddRange(
                new Experience { Company = "A", Title = "One", Start = "2020", Summary = "s", SortOrder = 0 },
                new Experience { Company = "B", Title = "Two", Start = "2020", Summary = "s", SortOrder = 0 },
                new Experience { Company = "C", Title = "Three", Start = "2020", Summary = "s", SortOrder = 0 });
            await db.SaveChangesAsync();
        }
        var ids = (await RowsAsync(app)).Select(r => r.Id).ToList();

        await ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}?handler=Move&id={ids[2]}", new Dictionary<string, string> { ["direction"] = "up" }, ContentFlow.Experience);

        Assert.Equal(["One", "Three", "Two"], (await RowsAsync(app)).Select(r => r.Title));
        Assert.Equal([1, 2, 3], (await RowsAsync(app)).Select(r => r.SortOrder));
    }

    [Fact]
    public async Task Removing_NeedsTheConfirmationPost_ShowsWhatGoes_AndTouchesNothingElse()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seeded = await ContentFlow.SeedRowsAsync(app);
        await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields("Gone Co", "Gone Role"));
        var gone = (await RowsAsync(app)).Single(r => r.Title == "Gone Role");
        var others = (await ContentFlow.SnapshotAsync(app)).Split('\n').Where(l => !l.StartsWith($"E{gone.Id}|")).ToList();

        var confirm = await owner.GetStringAsync($"{ContentFlow.Experience}/delete/{gone.Id}");
        Assert.Contains("Gone Role at Gone Co", confirm);
        Assert.Contains("This cannot be undone", confirm);
        Assert.Contains("Keep it", confirm);
        Assert.Equal(2, (await RowsAsync(app)).Count);

        var remove = await ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}/delete/{gone.Id}", new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Experience}/delete/{gone.Id}") });

        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        Assert.Equal([seeded.ExperienceId], (await RowsAsync(app)).Select(r => r.Id));
        Assert.Equal(others, (await ContentFlow.SnapshotAsync(app)).Split('\n').ToList()); // every other row in every table is untouched
        Assert.Contains("Removed Gone Role at Gone Co.", await owner.GetStringAsync(ContentFlow.Experience));
    }

    [Fact]
    public async Task ARemovedRole_IsGoneFromThePublicPage_AndTheCv()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields("Vanishing Co", "Vanishing Role"));
        Assert.Contains("Vanishing Role", await app.NewClient().GetStringAsync("/experience"));
        var id = (await RowsAsync(app)).Single().Id;

        await ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}/delete/{id}", new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Experience}/delete/{id}") });

        var page = await app.NewClient().GetStringAsync("/experience");
        Assert.DoesNotContain("Vanishing", page);
        Assert.Contains("class=\"mt-10 font-mono text-sm text-muted\"", page); // the template's empty state is back
        Assert.DoesNotContain("Vanishing", await app.NewClient().GetStringAsync("/cv"));
    }

    [Fact]
    public async Task EveryWrite_RefreshesTheGeneratedCvAtOnce()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var anonymous = app.NewClient();
        static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
        var first = Hash(await anonymous.GetByteArrayAsync("/cv.pdf"));
        Assert.Equal(first, Hash(await anonymous.GetByteArrayAsync("/cv.pdf"))); // served from the output cache

        await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields("Fresh Co", "Fresh Role"));
        var afterCreate = Hash(await anonymous.GetByteArrayAsync("/cv.pdf"));
        Assert.NotEqual(first, afterCreate);
        Assert.Equal(afterCreate, Hash(await anonymous.GetByteArrayAsync("/cv.pdf")));

        var id = (await RowsAsync(app)).Single().Id;
        var fields = ContentFlow.RoleFields("Fresh Co", "Fresher Role");
        fields["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{Edit}/{id}");
        await ContentFlow.PostAsync(owner, $"{Edit}/{id}", fields);
        var afterEdit = Hash(await anonymous.GetByteArrayAsync("/cv.pdf"));
        Assert.NotEqual(afterCreate, afterEdit);

        await ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}/delete/{id}", new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Experience}/delete/{id}") });
        Assert.NotEqual(afterEdit, Hash(await anonymous.GetByteArrayAsync("/cv.pdf")));
    }

    [Fact]
    public async Task ASecondTab_WithAStaleForm_IsRefusedWithAFriendlyMessage_AndNothingIsOverwritten()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields());
        var id = (await RowsAsync(app)).Single().Id;
        var path = $"{Edit}/{id}";
        var tabOneStamp = await ContentFlow.StampAsync(owner, path);
        var tabTwoStamp = await ContentFlow.StampAsync(owner, path);

        var one = ContentFlow.RoleFields(title: "Edited in tab one");
        one["Form.Stamp"] = tabOneStamp;
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, one)).StatusCode);

        var two = ContentFlow.RoleFields(title: "Edited in tab two");
        two["Form.Stamp"] = tabTwoStamp;
        var refused = await ContentFlow.PostAsync(owner, path, two);

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        var html = await refused.Content.ReadAsStringAsync();
        Assert.Contains("changed or removed somewhere else", html);
        Assert.Contains("Nothing was saved.", html);
        Assert.Contains("Edited in tab two", html); // the owner's input is still in the form
        Assert.Equal("Edited in tab one", (await RowsAsync(app)).Single().Title);
    }

    [Fact]
    public async Task EditingARoleThatWasRemovedElsewhere_SaysSoAndCreatesNothing()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields());
        var id = (await RowsAsync(app)).Single().Id;
        var path = $"{Edit}/{id}";
        var stamp = await ContentFlow.StampAsync(owner, path);
        await ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}/delete/{id}", new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Experience}/delete/{id}") });

        var fields = ContentFlow.RoleFields(title: "Zombie");
        fields["Form.Stamp"] = stamp;
        var response = await ContentFlow.PostAsync(owner, path, fields, Edit);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("removed in another tab", await owner.GetStringAsync(ContentFlow.Experience));
        Assert.Empty(await RowsAsync(app));
    }

    [Fact]
    public async Task RemovingWithAStaleConfirmation_IsRefused_AndTheRoleStays()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields());
        var id = (await RowsAsync(app)).Single().Id;
        var staleConfirm = await ContentFlow.StampAsync(owner, $"{ContentFlow.Experience}/delete/{id}");
        var fields = ContentFlow.RoleFields(title: "Changed meanwhile");
        fields["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{Edit}/{id}");
        await ContentFlow.PostAsync(owner, $"{Edit}/{id}", fields);

        await ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}/delete/{id}", new Dictionary<string, string> { ["Stamp"] = staleConfirm }, $"{ContentFlow.Experience}/delete/{id}");

        Assert.Equal("Changed meanwhile", (await RowsAsync(app)).Single().Title);
        Assert.Contains("changed or removed somewhere else", await owner.GetStringAsync(ContentFlow.Experience));
    }

    [Theory]
    [InlineData("Form.Company", "", "Company is required")]
    [InlineData("Form.Title", "   ", "Job title is required")]
    [InlineData("Form.Summary", "", "Summary is required")]
    [InlineData("Form.Start", "", "Start is required")]
    [InlineData("Form.Start", "last spring", "must be a year or a month and year")]
    [InlineData("Form.Start", "1801", "must be a year or a month and year")]
    [InlineData("Form.End", "next year-ish", "must be a year or a month and year")]
    [InlineData("Form.End", "2018", "cannot be earlier")]
    [InlineData("Form.Company", "<script>alert(1)</script>", "plain text")]
    [InlineData("Form.Title", "<b>Boss</b>", "plain text")]
    [InlineData("Form.Summary", "Click <a href=x>here</a>", "plain text")]
    [InlineData("Form.HighlightsText", "fine\n<img src=x onerror=alert(1)>", "plain text")]
    [InlineData("Form.TechText", "<svg onload=alert(1)>", "plain text")]
    [InlineData("Form.Title", "bell\u0007char", "not allowed")]
    public async Task InvalidInput_IsRefusedWithAMessage_AndNothingIsSaved(string field, string value, string expected)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = ContentFlow.RoleFields(start: "2019", end: "2022");
        fields[field] = value;

        var response = await ContentFlow.PostAsync(owner, Edit, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(expected, html);
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("Some fields need attention.", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html); // echoed encoded, never raw
        Assert.Empty(await RowsAsync(app));
    }

    [Fact]
    public async Task TooLongValues_AreRefused()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        foreach (var (field, value) in new[] { ("Form.Company", new string('c', 201)), ("Form.Title", new string('t', 201)), ("Form.Summary", new string('s', 1501)),
                                                ("Form.HighlightsText", string.Join("\n", Enumerable.Range(0, 13).Select(i => "line " + i))),
                                                ("Form.TechText", string.Join("\n", Enumerable.Range(0, 21).Select(i => "tool " + i))),
                                                ("Form.HighlightsText", new string('h', 301)) })
        {
            var fields = ContentFlow.RoleFields();
            fields[field] = value;
            var response = await ContentFlow.PostAsync(owner, Edit, fields);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Some fields need attention.", await response.Content.ReadAsStringAsync());
        }
        Assert.Empty(await RowsAsync(app));
    }

    [Fact]
    public async Task APastRoleNeedsAnEndDate_OrTheCurrentTick()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var response = await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields(end: ""));

        Assert.Contains("Give an end date, or tick", await response.Content.ReadAsStringAsync());
        Assert.Empty(await RowsAsync(app));
    }

    [Fact]
    public async Task ADuplicateRole_IsRefusedWithAFriendlyMessage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields());

        var again = await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields("northwind health", "STAFF NURSE"));

        Assert.Contains("already have a role called", await again.Content.ReadAsStringAsync());
        Assert.Single(await RowsAsync(app));
    }

    [Fact]
    public async Task Markup_InStoredValues_IsEncodedOnEveryPageThatShowsIt()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        // A value that reached the database some other way (an older import) must still be output-encoded.
        using (var db = app.CreateDbContext())
        {
            db.Experiences.Add(new Experience { Company = "Evil <script>alert(1)</script> Co", Title = "T & \"Q\"", Start = "2020", End = "2021", Summary = "<img src=x onerror=alert(1)>", SortOrder = 1 });
            await db.SaveChangesAsync();
        }
        var id = (await RowsAsync(app)).Single().Id;

        foreach (var page in new[] { ContentFlow.Experience, $"{Edit}/{id}", $"{ContentFlow.Experience}/delete/{id}" })
        {
            var html = await owner.GetStringAsync(page);
            Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.DoesNotContain("<img src=x", html);
        }
        var publicHtml = await app.NewClient().GetStringAsync("/experience");
        Assert.DoesNotContain("<script>alert(1)</script>", publicHtml);
        Assert.DoesNotContain("<img src=x", publicHtml);
    }

    [Fact]
    public async Task TheListHasAnEmptyState_ACount_AndTableSemantics()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var empty = await owner.GetStringAsync(ContentFlow.Experience);
        Assert.Contains("0 entries in", empty);
        Assert.Contains("No entries yet", empty);
        Assert.Contains("Add role", empty);
        Assert.DoesNotContain("<table", empty);

        await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields());
        var list = await owner.GetStringAsync(ContentFlow.Experience);
        Assert.Contains("1 entry in", list);
        Assert.Contains("<table", list);
        Assert.Contains("<caption>", list);
        Assert.Contains("<th scope=\"col\">", list);
        Assert.Contains("<th scope=\"row\">Staff Nurse</th>", list);
    }
}
