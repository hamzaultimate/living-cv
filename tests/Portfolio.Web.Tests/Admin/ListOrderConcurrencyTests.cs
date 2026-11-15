using System.Net;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Portfolio.Web.Uploads;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>
/// Writes that change the positions in a list (a move, a new entry, a skill changing group) are serialised by the list stamp. These tests make two
/// real requests overlap: the second one has read the list before the first writes, and the first writes in the middle of it (the test hook runs a
/// complete request right before the second request saves). The second request must be refused, and no two rows may end up in one position.
/// </summary>
public class ListOrderConcurrencyTests
{
    private const string Edit = ContentFlow.Experience + "/edit";
    private const string Refused = "The list was changed somewhere else, so it was not reordered. It is shown again as it is now.";

    private static async Task<List<Experience>> RolesAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return await db.Experiences.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
    }

    private static async Task<List<Skill>> SkillsAsync(AdminApp app)
    {
        using var db = app.CreateDbContext();
        return await db.Skills.AsNoTracking().OrderBy(x => x.Group).ThenBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
    }

    private static void NoSharedPositions<T>(IEnumerable<T> rows, Func<T, string?> scope, Func<T, int> position, string what)
    {
        foreach (var set in rows.GroupBy(scope))
            Assert.True(set.Select(position).Distinct().Count() == set.Count(), $"{what}: two rows share a position in {set.Key ?? "the list"}: {string.Join(",", set.Select(position))}");
    }

    /// <summary>Three roles, listed First, Second, Third, written the way the editor writes them.</summary>
    private static async Task<(AdminApp App, HttpClient Owner, List<Experience> Rows)> ThreeRolesAsync()
    {
        var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        foreach (var name in new[] { "Third", "Second", "First" })
            await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields(name + " Co", name));
        var rows = await RolesAsync(app);
        Assert.Equal(["First", "Second", "Third"], rows.Select(r => r.Title));
        return (app, owner, rows);
    }

    /// <summary>Runs <paramref name="outer"/>; right before it saves, <paramref name="inner"/> runs to completion as another request.</summary>
    private static Task<T> OverlapAsync<T>(AdminApp app, Func<Task<T>> outer, Func<Task> inner) => app.OverlapAsync(outer, inner);

    private static void AssertRefusedMove(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("#row-", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task TwoOverlappingMoves_OnlyTheFirstApplies_AndNoTwoRowsShareAPosition()
    {
        var (app, owner, rows) = await ThreeRolesAsync();
        using var _ = app;
        var seen = await ContentFlow.ListStampAsync(owner, ContentFlow.Experience); // both tabs show the list as it is now

        var outer = await OverlapAsync(app,
            () => ContentFlow.MoveWithStampAsync(owner, ContentFlow.Experience, rows[0].Id, "down", seen),
            () => ContentFlow.MoveWithStampAsync(owner, ContentFlow.Experience, rows[2].Id, "up", seen));

        AssertRefusedMove(outer);
        var after = await RolesAsync(app);
        Assert.Equal(["First", "Third", "Second"], after.Select(r => r.Title)); // the move that committed first, and only that one
        Assert.Equal([1, 2, 3], after.Select(r => r.SortOrder));
        Assert.Contains(Refused, await owner.GetStringAsync(ContentFlow.Experience));
    }

    [Fact]
    public async Task AMove_FromAListPageThatIsOutOfDate_IsRefusedAndChangesNothing()
    {
        var (app, owner, rows) = await ThreeRolesAsync();
        using var _ = app;
        var stale = await ContentFlow.ListStampAsync(owner, ContentFlow.Experience);
        await ContentFlow.MoveAsync(owner, ContentFlow.Experience, rows[2].Id, "up"); // the same list, changed in another tab
        var before = await ContentFlow.SnapshotAsync(app);

        AssertRefusedMove(await ContentFlow.MoveWithStampAsync(owner, ContentFlow.Experience, rows[0].Id, "down", stale));

        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
        Assert.Contains(Refused, await owner.GetStringAsync(ContentFlow.Experience));
        // Pressing the button again from the page that is shown now works.
        var again = await ContentFlow.MoveAsync(owner, ContentFlow.Experience, rows[0].Id, "down");
        Assert.Contains("#row-", again.Headers.Location!.OriginalString);
        Assert.Equal(["Third", "First", "Second"], (await RolesAsync(app)).Select(r => r.Title));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AMoveThatCarriesNoListStamp_IsRefused(string stamp)
    {
        var (app, owner, rows) = await ThreeRolesAsync();
        using var _ = app;
        var before = await ContentFlow.SnapshotAsync(app);

        AssertRefusedMove(await ContentFlow.MoveWithStampAsync(owner, ContentFlow.Experience, rows[0].Id, "down", stamp));

        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AMoveThatCarriesNoListStamp_IsRefused_EvenWhenTheListHasNoStampRowYet(string stamp)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
        {
            db.Experiences.AddRange(
                new Experience { Company = "A", Title = "One", Start = "2020", Summary = "s", SortOrder = 1 },
                new Experience { Company = "B", Title = "Two", Start = "2020", Summary = "s", SortOrder = 2 });
            await db.SaveChangesAsync();
        }
        using (var db = app.CreateDbContext()) Assert.Empty(await db.ListStamps.ToListAsync());
        var rows = await RolesAsync(app);
        var before = await ContentFlow.SnapshotAsync(app);

        AssertRefusedMove(await ContentFlow.MoveWithStampAsync(owner, ContentFlow.Experience, rows[0].Id, "down", stamp));

        Assert.Equal(before, await ContentFlow.SnapshotAsync(app));
        using (var db = app.CreateDbContext()) Assert.Empty(await db.ListStamps.ToListAsync()); // a refused move creates nothing
    }

    [Fact]
    public async Task TheFirstMoveOfAList_ThatHasNeverBeenReordered_Works_AndTheStampMovesOn()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
        {
            db.Experiences.AddRange(
                new Experience { Company = "A", Title = "One", Start = "2020", Summary = "s", SortOrder = 1 },
                new Experience { Company = "B", Title = "Two", Start = "2020", Summary = "s", SortOrder = 2 });
            await db.SaveChangesAsync();
        }
        Assert.Equal(ContentEditor.InitialListStamp, await ContentFlow.ListStampAsync(owner, ContentFlow.Experience));
        using (var db = app.CreateDbContext()) Assert.Empty(await db.ListStamps.ToListAsync()); // a GET never creates the row
        var rows = await RolesAsync(app);

        var move = await ContentFlow.MoveAsync(owner, ContentFlow.Experience, rows[0].Id, "down");

        Assert.Contains("#row-", move.Headers.Location!.OriginalString);
        Assert.Equal(["Two", "One"], (await RolesAsync(app)).Select(r => r.Title));
        Assert.NotEqual(ContentEditor.InitialListStamp, await ContentFlow.ListStampAsync(owner, ContentFlow.Experience));
    }

    [Fact]
    public async Task AMoveRacingAnInsertAtTheTop_IsRefused()
    {
        var (app, owner, rows) = await ThreeRolesAsync();
        using var _ = app;
        var seen = await ContentFlow.ListStampAsync(owner, ContentFlow.Experience);

        var outer = await OverlapAsync(app,
            () => ContentFlow.MoveWithStampAsync(owner, ContentFlow.Experience, rows[1].Id, "up", seen),
            () => ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields("Fresh Co", "Fresh")));

        AssertRefusedMove(outer);
        var after = await RolesAsync(app);
        Assert.Equal(["Fresh", "First", "Second", "Third"], after.Select(r => r.Title));
        NoSharedPositions(after, _ => null, r => r.SortOrder, "after a move raced an insert");
    }

    [Fact]
    public async Task AnInsertAtTheTop_RacingAMove_IsRefused_AndNothingIsAdded()
    {
        var (app, owner, rows) = await ThreeRolesAsync();
        using var _ = app;
        var seen = await ContentFlow.ListStampAsync(owner, ContentFlow.Experience);

        var outer = await OverlapAsync(app,
            () => ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields("Fresh Co", "Fresh")),
            () => ContentFlow.MoveWithStampAsync(owner, ContentFlow.Experience, rows[2].Id, "up", seen));

        // The form is shown again with the owner's input and a message that nothing was saved.
        var page = await ContentFlow.TextOfAsync(outer);
        Assert.Contains(OrderRace, page);
        Assert.Contains("Fresh", page);
        var after = await RolesAsync(app);
        Assert.Equal(["First", "Third", "Second"], after.Select(r => r.Title));
        NoSharedPositions(after, _ => null, r => r.SortOrder, "after an insert raced a move");
    }

    private const string ContentPageModelMessage = "changed or removed somewhere else";
    private const string OrderRace = "The list changed while saving, so nothing was saved. Press Save again.";

    [Fact]
    public async Task TwoOverlappingInserts_AtTheTop_OnlyTheFirstIsSaved_AndNoTwoRowsShareAPosition()
    {
        var (app, owner, _) = await ThreeRolesAsync();
        using var _app = app;

        var outer = await OverlapAsync(app,
            () => ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields("Late Co", "Late")),
            () => ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields("Early Co", "Early")));

        var refusedPage = await ContentFlow.TextOfAsync(outer);
        Assert.Contains(OrderRace, refusedPage);
        Assert.DoesNotContain(ContentPageModelMessage, refusedPage);
        Assert.Contains("value=\"Late\"", refusedPage); // what the owner typed is shown again
        Assert.Contains("value=\"Late Co\"", refusedPage);
        var after = await RolesAsync(app);
        Assert.Equal(["Early", "First", "Second", "Third"], after.Select(r => r.Title));
        NoSharedPositions(after, _ => null, r => r.SortOrder, "after two inserts overlapped");
        // Saving the refused form again (a new page load, as the owner would) works and goes on top.
        await ContentFlow.PostAsync(owner, Edit, ContentFlow.RoleFields("Late Co", "Late"));
        Assert.Equal("Late", (await RolesAsync(app)).First().Title);
    }

    [Fact]
    public async Task TwoOverlappingNewCertifications_OnlyTheFirstIsSaved_AndNoTwoRowsShareAPosition()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var outer = await OverlapAsync(app,
            () => ContentFlow.PostAsync(owner, ContentFlow.Certifications + "/edit", ContentFlow.Fields(("Name", "Late"), ("Issuer", "Board"), ("Status", "Earned"))),
            () => ContentFlow.PostAsync(owner, ContentFlow.Certifications + "/edit", ContentFlow.Fields(("Name", "Early"), ("Issuer", "Board"), ("Status", "Earned"))));

        Assert.Contains(OrderRace, await ContentFlow.TextOfAsync(outer));
        using var db = app.CreateDbContext();
        var rows = await db.Certifications.AsNoTracking().ToListAsync();
        Assert.Equal(["Early"], rows.Select(r => r.Name));
        // And the list still takes the next one at the end.
        await ContentFlow.PostAsync(owner, ContentFlow.Certifications + "/edit", ContentFlow.Fields(("Name", "Late"), ("Issuer", "Board"), ("Status", "Earned")));
        using var db2 = app.CreateDbContext();
        var all = await db2.Certifications.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        Assert.Equal(["Early", "Late"], all.Select(r => r.Name));
        NoSharedPositions(all, _ => null, r => r.SortOrder, "certifications");
    }

    [Fact]
    public async Task TwoOverlappingNewProjects_OnlyTheFirstIsSaved_AndNoTwoRowsShareAPosition()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var outer = await OverlapAsync(app,
            () => ContentFlow.PostAsync(owner, ContentFlow.Projects + "/edit", ContentFlow.Fields(("Title", "Late"), ("Summary", "Second to save."))),
            () => ContentFlow.PostAsync(owner, ContentFlow.Projects + "/edit", ContentFlow.Fields(("Title", "Early"), ("Summary", "First to save."))));

        Assert.Contains(OrderRace, await ContentFlow.TextOfAsync(outer));
        using var db = app.CreateDbContext();
        Assert.Equal(["Early"], (await db.Projects.AsNoTracking().ToListAsync()).Select(r => r.Title));
        await ContentFlow.PostAsync(owner, ContentFlow.Projects + "/edit", ContentFlow.Fields(("Title", "Late"), ("Summary", "Second to save.")));
        using var db2 = app.CreateDbContext();
        var all = await db2.Projects.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        Assert.Equal(["Early", "Late"], all.Select(r => r.Title));
        NoSharedPositions(all, _ => null, r => r.SortOrder, "projects");
    }

    private static async Task<(AdminApp App, HttpClient Owner)> SkillsAppAsync()
    {
        var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using var db = app.CreateDbContext();
        db.Skills.AddRange(
            new Skill { Name = "G1", Group = "Garden", SortOrder = 1, ConcurrencyStamp = ContentEditor.NewStamp() },
            new Skill { Name = "G2", Group = "Garden", SortOrder = 2, ConcurrencyStamp = ContentEditor.NewStamp() },
            new Skill { Name = "G3", Group = "Garden", SortOrder = 3, ConcurrencyStamp = ContentEditor.NewStamp() },
            new Skill { Name = "H1", Group = "Hall", SortOrder = 1, ConcurrencyStamp = ContentEditor.NewStamp() },
            new Skill { Name = "H2", Group = "Hall", SortOrder = 2, ConcurrencyStamp = ContentEditor.NewStamp() });
        await db.SaveChangesAsync();
        return (app, owner);
    }

    private static async Task<HttpResponseMessage> RegroupAsync(AdminApp app, HttpClient owner, string name, string group)
    {
        var id = (await SkillsAsync(app)).Single(s => s.Name == name).Id;
        var path = $"{ContentFlow.Skills}/edit/{id}";
        var fields = ContentFlow.Fields(("Name", name), ("Group", group));
        fields["Form.Stamp"] = await ContentFlow.StampAsync(owner, path);
        return await ContentFlow.PostAsync(owner, path, fields);
    }

    [Fact]
    public async Task AMoveRacingASkillGroupChange_IsRefused_AndNoGroupHasTwoRowsInOnePosition()
    {
        var (app, owner) = await SkillsAppAsync();
        using var _ = app;
        var skills = await SkillsAsync(app);
        var seen = await ContentFlow.ListStampAsync(owner, ContentFlow.Skills);

        var outer = await OverlapAsync(app,
            () => ContentFlow.MoveWithStampAsync(owner, ContentFlow.Skills, skills.Single(s => s.Name == "H1").Id, "down", seen),
            () => RegroupAsync(app, owner, "G3", "Hall"));

        AssertRefusedMove(outer);
        var after = await SkillsAsync(app);
        Assert.Equal(["H1", "H2", "G3"], after.Where(s => s.Group == "Hall").Select(s => s.Name)); // only the group change applied
        Assert.Equal([1, 2, 3], after.Where(s => s.Group == "Hall").Select(s => s.SortOrder));
        NoSharedPositions(after, s => s.Group, s => s.SortOrder, "after a move raced a group change");
    }

    [Fact]
    public async Task ASkillGroupChange_RacingAMove_IsRefused_AndTheSkillStaysWhereItWas()
    {
        var (app, owner) = await SkillsAppAsync();
        using var _ = app;
        var skills = await SkillsAsync(app);
        var seen = await ContentFlow.ListStampAsync(owner, ContentFlow.Skills);
        var g3 = skills.Single(s => s.Name == "G3");

        var outer = await OverlapAsync(app,
            () => RegroupAsync(app, owner, "G3", "Hall"),
            () => ContentFlow.MoveWithStampAsync(owner, ContentFlow.Skills, skills.Single(s => s.Name == "H1").Id, "down", seen));

        Assert.Contains(ContentPageModelMessage, await ContentFlow.TextOfAsync(outer)); // the form is shown again, nothing was saved
        var after = await SkillsAsync(app);
        var stillThere = after.Single(s => s.Name == "G3");
        Assert.Equal(("Garden", g3.SortOrder), (stillThere.Group, stillThere.SortOrder));
        Assert.Equal(["H2", "H1"], after.Where(s => s.Group == "Hall").Select(s => s.Name));
        NoSharedPositions(after, s => s.Group, s => s.SortOrder, "after a group change raced a move");
    }

    [Fact]
    public async Task TwoOverlappingGroupChanges_IntoOneGroup_OnlyTheFirstApplies()
    {
        var (app, owner) = await SkillsAppAsync();
        using var _ = app;

        var outer = await OverlapAsync(app,
            () => RegroupAsync(app, owner, "G2", "Hall"),
            () => RegroupAsync(app, owner, "G3", "Hall"));

        Assert.Contains(ContentPageModelMessage, await ContentFlow.TextOfAsync(outer));
        var after = await SkillsAsync(app);
        Assert.Equal(["H1", "H2", "G3"], after.Where(s => s.Group == "Hall").Select(s => s.Name));
        NoSharedPositions(after, s => s.Group, s => s.SortOrder, "after two group changes overlapped");
    }

    [Fact]
    public async Task TwoOverlappingNewSkills_InOneGroup_OnlyTheFirstIsSaved()
    {
        var (app, owner) = await SkillsAppAsync();
        using var _ = app;

        var outer = await OverlapAsync(app,
            () => ContentFlow.PostAsync(owner, ContentFlow.Skills + "/edit", ContentFlow.Fields(("Name", "Late"), ("Group", "Hall"))),
            () => ContentFlow.PostAsync(owner, ContentFlow.Skills + "/edit", ContentFlow.Fields(("Name", "Early"), ("Group", "Hall"))));

        Assert.Contains(OrderRace, await ContentFlow.TextOfAsync(outer));
        var after = await SkillsAsync(app);
        Assert.Equal(["H1", "H2", "Early"], after.Where(s => s.Group == "Hall").Select(s => s.Name));
        NoSharedPositions(after, s => s.Group, s => s.SortOrder, "after two new skills overlapped");
    }

    [Fact]
    public async Task AnEditThatDoesNotChangeTheOrder_NeedsNoListStamp_AndASkillGroupChangeFromAnOpenFormStillWorks()
    {
        var (app, owner) = await SkillsAppAsync();
        using var _ = app;
        var skills = await SkillsAsync(app);
        var g1 = skills.Single(s => s.Name == "G1");
        var g2 = skills.Single(s => s.Name == "G2");
        var plainPath = $"{ContentFlow.Skills}/edit/{g1.Id}";
        var plainStamp = await ContentFlow.StampAsync(owner, plainPath);
        var regroupPath = $"{ContentFlow.Skills}/edit/{g2.Id}";
        var regroupStamp = await ContentFlow.StampAsync(owner, regroupPath);

        // The order of the list changes after both forms were opened.
        await ContentFlow.MoveAsync(owner, ContentFlow.Skills, skills.Single(s => s.Name == "H1").Id, "down");

        var plain = ContentFlow.Fields(("Name", "G1 renamed"), ("Group", "Garden"));
        plain["Form.Stamp"] = plainStamp;
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, plainPath, plain)).StatusCode);
        var regroup = ContentFlow.Fields(("Name", "G2"), ("Group", "Hall"));
        regroup["Form.Stamp"] = regroupStamp;
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, regroupPath, regroup)).StatusCode);

        var after = await SkillsAsync(app);
        Assert.Contains(after, s => s.Name == "G1 renamed");
        Assert.Equal(["H2", "H1", "G2"], after.Where(s => s.Group == "Hall").Select(s => s.Name));
        NoSharedPositions(after, s => s.Group, s => s.SortOrder, "after edits");
    }

    [Fact]
    public async Task RemovingAnEntry_LeavesTheOtherPositionsAlone_AndTheListStampAsItWas()
    {
        var (app, owner, rows) = await ThreeRolesAsync();
        using var _ = app;
        await ContentFlow.MoveAsync(owner, ContentFlow.Experience, rows[0].Id, "down"); // creates the stamp row
        var seen = await ContentFlow.ListStampAsync(owner, ContentFlow.Experience);
        var beforeRows = await RolesAsync(app);
        var middle = beforeRows[1];
        var path = $"{ContentFlow.Experience}/delete/{middle.Id}";

        await ContentFlow.PostAsync(owner, path, new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, path) });

        var after = await RolesAsync(app);
        Assert.Equal(beforeRows.Where(r => r.Id != middle.Id).Select(r => (r.Id, r.SortOrder)), after.Select(r => (r.Id, r.SortOrder))); // a gap, nothing renumbered
        Assert.Equal(seen, await ContentFlow.ListStampAsync(owner, ContentFlow.Experience)); // a delete cannot create two rows in one position, so it does not take the stamp
        // A page that was open before the removal can still move rows (the pair it names still exists).
        Assert.Contains("#row-", (await ContentFlow.MoveWithStampAsync(owner, ContentFlow.Experience, after[0].Id, "down", seen)).Headers.Location!.OriginalString);
    }

    /// <summary>The same guarantee one level down: two editors over one database, both with the list loaded before either writes.</summary>
    [Fact]
    public async Task TwoContentEditors_OverOneDatabase_BothLoadedBeforeEitherCommits_TheSecondCommitIsRefusedByTheDatabase()
    {
        var (app, owner, _) = await ThreeRolesAsync();
        using var _app = app;
        using var db1 = app.CreateDbContext();
        using var db2 = app.CreateDbContext();
        ContentEditor Editor(PortfolioDbContext db) => new(db,
            new ProfileEditor(db, app.Services.GetRequiredService<ISiteProfileProvider>(), app.Services.GetRequiredService<IOutputCacheStore>()),
            app.Services.GetRequiredService<UploadStore>());
        var one = Editor(db1);
        var two = Editor(db2);
        var seen1 = await one.ListStampAsync("experience", default);
        var seen2 = await two.ListStampAsync("experience", default);
        var scope1 = await db1.Experiences.ToListAsync();
        var scope2 = await db2.Experiences.ToListAsync();
        var ordered = scope1.OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToList();

        // Editor two stages its move (the stamp is read and replaced in memory) ...
        Assert.True(await two.GuardOrderAsync("experience", seen2, default));
        foreach (var (row, i) in scope2.OrderBy(r => r.SortOrder).ThenBy(r => r.Id).Select((r, i) => (r, i))) row.SortOrder = 3 - i;
        // ... and editor one completes a whole move before editor two commits.
        Assert.Equal(SaveOutcome.Saved, await one.MoveAsync("experience", scope1, ordered[2].Id, -1, seen1, default));

        Assert.Equal(SaveOutcome.Conflict, await two.CommitAsync(default));
        Assert.Equal(["First", "Third", "Second"], (await RolesAsync(app)).Select(r => r.Title));
        NoSharedPositions(await RolesAsync(app), _ => null, r => r.SortOrder, "after the refused commit");
    }
}
