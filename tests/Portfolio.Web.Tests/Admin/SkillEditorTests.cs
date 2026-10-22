using System.Net;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class SkillEditorTests
{
    private const string Edit = ContentFlow.Skills + "/edit";

    private static Dictionary<string, string> Skill(string name, string group) => ContentFlow.Fields(("Name", name), ("Group", group));

    private static async Task<List<Skill>> RowsAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return await db.Skills.AsNoTracking().OrderBy(x => x.Group).ThenBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
    }

    private static Task MoveAsync(HttpClient owner, int id, string direction) =>
        ContentFlow.PostAsync(owner, $"{ContentFlow.Skills}?handler=Move&id={id}", new Dictionary<string, string> { ["direction"] = direction }, ContentFlow.Skills);

    [Fact]
    public async Task CreatingASkill_Persists_AndShowsOnThePublicPageInItsGroup()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var create = await ContentFlow.PostAsync(owner, Edit, Skill("Distributed tracing", "Backend"));

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var row = Assert.Single(await RowsAsync(app));
        Assert.Equal(("Distributed tracing", "Backend", 1), (row.Name, row.Group, row.SortOrder));
        var page = await app.NewClient().GetStringAsync("/skills");
        Assert.Contains("Distributed tracing", page);
        Assert.Contains("Backend", page);
    }

    [Fact]
    public async Task NewSkills_GoToTheEndOfTheirOwnGroup()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        await ContentFlow.PostAsync(owner, Edit, Skill("One", "Backend"));
        await ContentFlow.PostAsync(owner, Edit, Skill("Two", "Backend"));
        await ContentFlow.PostAsync(owner, Edit, Skill("Other", "Cloud/DevOps"));
        await ContentFlow.PostAsync(owner, Edit, Skill("Three", "Backend"));

        var rows = await RowsAsync(app);
        Assert.Equal([1, 2, 3], rows.Where(r => r.Group == "Backend").Select(r => r.SortOrder));
        Assert.Equal(["One", "Two", "Three"], rows.Where(r => r.Group == "Backend").Select(r => r.Name));
        Assert.Equal([1], rows.Where(r => r.Group == "Cloud/DevOps").Select(r => r.SortOrder));
    }

    [Fact]
    public async Task SaveAndAddAnother_ReturnsToAnEmptyFormInTheSameGroup()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var fields = Skill("First", "Backend");
        fields["AddAnother"] = "true";

        var response = await ContentFlow.PostAsync(owner, Edit, fields);

        Assert.Equal("/admin/content/skills/edit?group=Backend", response.Headers.Location!.OriginalString);
        var next = await owner.GetStringAsync(response.Headers.Location.OriginalString);
        Assert.Contains("value=\"Backend\"", next);
        Assert.Contains("Added First. Add the next one below.", next);
    }

    [Fact]
    public async Task TheGroupSuggestions_ComeFromTheTemplate_AndFromGroupsInUse()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, "healthcare");
        await ContentFlow.PostAsync(owner, Edit, Skill("Phlebotomy", "My own group"));

        var html = await owner.GetStringAsync(Edit);

        Assert.Contains("<datalist id=\"group-options\">", html);
        foreach (var group in new[] { "Clinical", "Procedures", "Documentation", "Languages", "My own group" })
            Assert.Contains($"<option value=\"{group}\">", html);
        Assert.Contains("Suggested for this template: Clinical, Procedures, Documentation, Languages", html);
    }

    [Fact]
    public async Task ASkillCanBeEditedAndMovedToAnotherGroup()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, Skill("Alpha", "Backend"));
        await ContentFlow.PostAsync(owner, Edit, Skill("Beta", "Cloud/DevOps"));
        var alpha = (await RowsAsync(app)).Single(r => r.Name == "Alpha");
        var fields = Skill("Alpha 2", "Cloud/DevOps");
        fields["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{Edit}/{alpha.Id}");

        await ContentFlow.PostAsync(owner, $"{Edit}/{alpha.Id}", fields);

        var moved = (await RowsAsync(app)).Single(r => r.Id == alpha.Id);
        Assert.Equal(("Alpha 2", "Cloud/DevOps", 2), (moved.Name, moved.Group, moved.SortOrder));
    }

    [Fact]
    public async Task Reordering_WorksWithinAGroup_AndNeverTouchesAnotherGroup()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        foreach (var (n, g) in new[] { ("A", "Backend"), ("B", "Backend"), ("C", "Backend"), ("X", "Cloud/DevOps"), ("Y", "Cloud/DevOps") })
            await ContentFlow.PostAsync(owner, Edit, Skill(n, g));
        var rows = await RowsAsync(app);
        var otherGroup = await ContentFlow.SnapshotAsync(app);
        var a = rows.Single(r => r.Name == "A").Id;
        var c = rows.Single(r => r.Name == "C").Id;

        await MoveAsync(owner, a, "down");
        await MoveAsync(owner, c, "up");

        var after = await RowsAsync(app);
        Assert.Equal(["B", "C", "A"], after.Where(r => r.Group == "Backend").Select(r => r.Name));
        Assert.Equal(["X", "Y"], after.Where(r => r.Group == "Cloud/DevOps").Select(r => r.Name));
        var snapshot = await ContentFlow.SnapshotAsync(app);
        foreach (var line in otherGroup.Split('\n').Where(l => l.Contains("|Cloud/DevOps|")))
            Assert.Contains(line, snapshot); // the other group's rows are byte for byte unchanged
        var publicPage = await app.NewClient().GetStringAsync("/skills");
        Assert.True(publicPage.IndexOf(">B</span>", StringComparison.Ordinal) < publicPage.IndexOf(">C</span>", StringComparison.Ordinal));
        Assert.True(publicPage.IndexOf(">C</span>", StringComparison.Ordinal) < publicPage.IndexOf(">A</span>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheListShowsOneTablePerGroup_InTheTemplatesOrder()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        foreach (var (n, g) in new[] { ("Zeta", "Zebra group"), ("Docker", "Cloud/DevOps"), ("C#", "Backend"), ("Alpha", "Aardvark group") })
            await ContentFlow.PostAsync(owner, Edit, Skill(n, g));

        var html = await owner.GetStringAsync(ContentFlow.Skills);

        var order = new[] { "Backend", "Cloud/DevOps", "Aardvark group", "Zebra group" }.Select(g => html.IndexOf($"<caption>{g}</caption>", StringComparison.Ordinal)).ToList();
        Assert.All(order, i => Assert.True(i > 0));
        Assert.Equal(order.OrderBy(i => i), order); // the template's groups first in its order, the rest alphabetically
    }

    [Fact]
    public async Task ADuplicateName_IsRefused_EvenInAnotherGroup_BecauseTheSeederKeysOnTheName()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, Skill("C#", "Backend"));

        var response = await ContentFlow.PostAsync(owner, Edit, Skill("c#", "Cloud/DevOps"));

        Assert.Contains("already have a skill called", await response.Content.ReadAsStringAsync());
        Assert.Single(await RowsAsync(app));
    }

    [Theory]
    [InlineData("", "Backend", "Name is required")]
    [InlineData("Go", "", "Group is required")]
    [InlineData("<b>Go</b>", "Backend", "plain text")]
    [InlineData("Go", "<script>x</script>", "plain text")]
    [InlineData("Go\u0007", "Backend", "not allowed")]
    public async Task InvalidSkills_AreRefused(string name, string group, string expected)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var response = await ContentFlow.PostAsync(owner, Edit, Skill(name, group));

        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
        Assert.Empty(await RowsAsync(app));
    }

    [Fact]
    public async Task OverlongNamesAndGroups_AreRefused()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        Assert.Contains("at most 100", await (await ContentFlow.PostAsync(owner, Edit, Skill(new string('n', 101), "G"))).Content.ReadAsStringAsync());
        Assert.Contains("at most 60", await (await ContentFlow.PostAsync(owner, Edit, Skill("N", new string('g', 61)))).Content.ReadAsStringAsync());
        Assert.Empty(await RowsAsync(app));
    }

    [Fact]
    public async Task RemovingASkill_GoesThroughAConfirmation_AndLeavesTheRest()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, Skill("Keep", "Backend"));
        await ContentFlow.PostAsync(owner, Edit, Skill("Drop", "Backend"));
        var drop = (await RowsAsync(app)).Single(r => r.Name == "Drop");

        var confirm = await owner.GetStringAsync($"{ContentFlow.Skills}/delete/{drop.Id}");
        Assert.Contains("Drop", confirm);
        Assert.Equal(2, (await RowsAsync(app)).Count);
        await ContentFlow.PostAsync(owner, $"{ContentFlow.Skills}/delete/{drop.Id}", new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Skills}/delete/{drop.Id}") });

        Assert.Equal(["Keep"], (await RowsAsync(app)).Select(r => r.Name));
        Assert.DoesNotContain("Drop", await app.NewClient().GetStringAsync("/skills"));
    }

    [Fact]
    public async Task AStaleSkillForm_IsRefused()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ContentFlow.PostAsync(owner, Edit, Skill("Original", "Backend"));
        var id = (await RowsAsync(app)).Single().Id;
        var tabOne = await ContentFlow.StampAsync(owner, $"{Edit}/{id}");
        var tabTwo = await ContentFlow.StampAsync(owner, $"{Edit}/{id}");
        var one = Skill("One", "Backend");
        one["Form.Stamp"] = tabOne;
        await ContentFlow.PostAsync(owner, $"{Edit}/{id}", one);
        var two = Skill("Two", "Backend");
        two["Form.Stamp"] = tabTwo;

        var refused = await ContentFlow.PostAsync(owner, $"{Edit}/{id}", two);

        Assert.Contains("changed or removed somewhere else", await refused.Content.ReadAsStringAsync());
        Assert.Equal("One", (await RowsAsync(app)).Single().Name);
    }
}
