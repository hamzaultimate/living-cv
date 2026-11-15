using Microsoft.Extensions.DependencyInjection;
using Portfolio.Web.Tests.Support;
using Portfolio.Web.Transfer;
using Xunit;

namespace Portfolio.Web.Tests.Transfer;

/// <summary>Matching file rows to stored rows, the preview's verdicts, and that planning never writes.</summary>
[Collection(PlanningCollection.Name)]
public class ImportPlanningTests
{
    private readonly PlanningSite _site;

    public ImportPlanningTests(PlanningSite site) => _site = site;

    private AdminApp App => _site.App;

    // The seeded rows (ContentFlow.SeedRowsAsync) written as file rows: the stored row, a change to it, a new one.
    public static IEnumerable<object[]> Tables() =>
    [
        ["experience", new { company = "Seed Co", title = "Seed Role" }, new { company = "SEED co", title = "seed ROLE", summary = "Changed summary." }, new { company = "New Co", title = "New Role", start = "2022", end = "2023", summary = "New." }],
        ["skills", new { name = "Seeding" }, new { name = "seeding", group = "Soil" }, new { name = "Brand new", group = "Soil" }],
        ["projects", new { slug = "seed-project" }, new { slug = "SEED-PROJECT", summary = "A changed summary." }, new { title = "A new project", summary = "New summary.", slug = "a-new-project" }],
        ["certifications", new { name = "Seed Certificate" }, new { name = "seed certificate", issuer = "Another Board" }, new { name = "New Certificate", issuer = "Board", status = "Planned" }],
        ["talks", new { title = "Seed Talk" }, new { title = "SEED TALK", @event = "Other Conf" }, new { title = "New Talk" }],
        ["testimonials", new { author = "Seed Person", quote = "Seeded praise that is long enough to be a quote." }, new { author = "seed person", quote = "SEEDED PRAISE THAT IS LONG ENOUGH TO BE A QUOTE.", authorTitle = "Changed" }, new { author = "New Person", quote = "A brand new quote." }],
        ["blog", new { slug = "seed-post" }, new { slug = "seed-post", title = "Changed title" }, new { title = "A new post", date = "2025-02-02", body = "Text." }],
    ];

    [Theory]
    [MemberData(nameof(Tables))]
    public async Task ARowWithTheSameKey_IsMatched_ChangedOrNot_AndANewKeyIsAdded(string table, object same, object changed, object added)
    {
        var unchanged = ImportHarness.OneRow(await ImportHarness.PlanOkAsync(App, ImportHarness.Table(table, same)), table);
        var update = ImportHarness.OneRow(await ImportHarness.PlanOkAsync(App, ImportHarness.Table(table, changed)), table);
        var create = ImportHarness.OneRow(await ImportHarness.PlanOkAsync(App, ImportHarness.Table(table, added)), table);

        Assert.True(unchanged.Status == RowStatus.Unchanged, $"{table}: {ImportHarness.Why(unchanged)}");
        Assert.True(update.Status == RowStatus.Update, $"{table}: {ImportHarness.Why(update)}");
        Assert.True(create.Status == RowStatus.Create, $"{table}: {ImportHarness.Why(create)}");
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public async Task WithinAFile_TheFirstRowOfAKeyWins_AndTheRestAreRefusedWithTheReason(string table, object same, object changed, object added)
    {
        _ = same;
        // The same key twice (the stored one, and a new one): only the first is used.
        foreach (var row in new[] { changed, added })
        {
            var plan = await ImportHarness.PlanOkAsync(App, ImportHarness.Table(table, row, row, row));
            var rows = ImportHarness.Section(plan, table).Rows;

            Assert.Equal(3, rows.Count);
            Assert.NotEqual(RowStatus.Refused, rows[0].Status);
            Assert.All(rows.Skip(1), r => { Assert.Equal(RowStatus.Refused, r.Status); Assert.Contains("earlier", ImportHarness.Why(r)); });
            Assert.Equal(1, rows.Count(r => r.Status != RowStatus.Refused));
        }
    }

    [Fact]
    public async Task APlan_WritesNothing_NotEvenWithEveryKindOfRow_AndAProfile()
    {
        var before = await ContentFlow.SnapshotAsync(App);
        var file = ImportHarness.File(
            profile: new
            {
                ownerName = "Planned Name", hero = new { headline = "Planned hero" }, metrics = new[] { new { value = "1", label = "x" } }, about = new { headline = "Planned about" },
                sections = new[] { new { key = "blog", label = "Planned journal" } }, summary = "Planned summary",
            },
            content: new
            {
                experience = new object[] { new { company = "Seed Co", title = "Seed Role", summary = "Changed." }, new { company = "Added Co", title = "Added", start = "2020", end = "2021", summary = "Added." }, new { company = "" } },
                skills = new object[] { new { name = "Seeding", group = "Changed" }, new { name = "Added skill", group = "G" }, "not a row" },
                projects = new object[] { new { slug = "seed-project", summary = "Changed." }, new { title = "Added project", summary = "S." } },
                certifications = new object[] { new { name = "Seed Certificate", issuer = "Changed" }, new { name = "Added cert", issuer = "I", status = "Earned" } },
                talks = new object[] { new { title = "Seed Talk", @event = "Changed" }, new { title = "Added talk" } },
                testimonials = new object[] { new { author = "Added Person", quote = "An added quote." } },
                blog = new object[] { new { slug = "seed-post", title = "Changed" }, new { title = "Added post", date = "2025-01-01", body = "Body." }, new { title = "Bad", date = "x", body = "b" } },
            });

        var plan = await ImportHarness.PlanOkAsync(App, file, applyProfile: true);

        Assert.True(plan.Count(RowStatus.Create) >= 6 && plan.Count(RowStatus.Update) >= 6 && plan.Count(RowStatus.Refused) >= 3, "every verdict was produced");
        Assert.Equal(RowStatus.Update, plan.Profile.Status);
        Assert.Equal(before, await ContentFlow.SnapshotAsync(App));
    }

    [Fact]
    public async Task APreviewThroughTheService_HoldsThePlan_AndStillWritesNothing()
    {
        var before = await ContentFlow.SnapshotAsync(App);
        using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(App.Services);
        var service = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ImportService>(scope.ServiceProvider);

        var outcome = await service.PreviewAsync(TransferFlow.Utf8(ImportHarness.Table("skills", new { name = "Held", group = "G" })), "held.json", false, "session-a", CancellationToken.None);

        Assert.NotNull(outcome.Plan);
        Assert.Equal(32, outcome.Plan!.Token.Length);
        Assert.Equal(before, await ContentFlow.SnapshotAsync(App));
    }

    [Fact]
    public async Task APartlyBadFile_PlansEveryRowOnItsOwn_ABadRowNeverStopsTheOthers()
    {
        var plan = await ImportHarness.PlanOkAsync(App, ImportHarness.Table("skills",
            new { name = "Good one", group = "G" }, new { name = "", group = "G" }, new { name = "Good two", group = "G" }, new { name = "No group" }, 7, "text", new { name = "Good three", group = "G" }));

        var statuses = ImportHarness.Section(plan, "skills").Rows.Select(r => r.Status).ToArray();

        Assert.Equal([RowStatus.Create, RowStatus.Refused, RowStatus.Create, RowStatus.Refused, RowStatus.Refused, RowStatus.Refused, RowStatus.Create], statuses);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], ImportHarness.Section(plan, "skills").Rows.Select(r => r.Position).ToArray());
    }

    [Fact]
    public async Task AProjectWithoutAnAddressName_IsMatchedByTheNameBuiltFromItsTitle_SoTheSameFileTwiceAddsNothingNew()
    {
        // "Seed Project" builds the name "seed-project", which the stored project has: it is that project, not a second one.
        var plan = await ImportHarness.PlanOkAsync(App, ImportHarness.Table("projects", new { title = "Seed Project", summary = "A seeded project." }));

        Assert.Equal(RowStatus.Unchanged, ImportHarness.OneRow(plan, "projects").Status);
    }

    [Fact]
    public async Task AFieldTheFileDoesNotMention_IsKept_AndANullClearsIt()
    {
        using (var db = App.CreateDbContext())
        {
            var p = db.Projects.First(x => x.Slug == "seed-project");
            p.Role = "Lead";
            await db.SaveChangesAsync();
        }
        try
        {
            var absent = ImportHarness.OneRow(await ImportHarness.PlanOkAsync(App, ImportHarness.Table("projects", new { slug = "seed-project" })), "projects");
            var cleared = ImportHarness.OneRow(await ImportHarness.PlanOkAsync(App, ImportHarness.Table("projects", new { slug = "seed-project", role = (string?)null })), "projects");
            var same = ImportHarness.OneRow(await ImportHarness.PlanOkAsync(App, ImportHarness.Table("projects", new { slug = "seed-project", role = "Lead" })), "projects");

            Assert.Equal(RowStatus.Unchanged, absent.Status);
            Assert.Equal(RowStatus.Update, cleared.Status);
            Assert.Equal(RowStatus.Unchanged, same.Status);
        }
        finally
        {
            using var db = App.CreateDbContext();
            db.Projects.First(x => x.Slug == "seed-project").Role = null;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task AnEmptyOrMissingContent_PlansNothing_AndNothingIsDeleted()
    {
        var plan = await ImportHarness.PlanOkAsync(App, ImportHarness.File());
        var emptyLists = await ImportHarness.PlanOkAsync(App, ImportHarness.File(content: new { skills = Array.Empty<object>(), blog = Array.Empty<object>() }));

        Assert.Equal(0, plan.Count(RowStatus.Create) + plan.Count(RowStatus.Update) + plan.Count(RowStatus.Refused));
        Assert.False(plan.HasChanges);
        Assert.False(emptyLists.HasChanges);
        Assert.All(plan.Reports, r => Assert.Empty(r.Rows));
    }

    [Fact]
    public async Task EveryTableCap_RefusesTheWholeFile_OneRowOver_AndAcceptsTheCapItself()
    {
        foreach (var (table, cap, make) in new (string, int, Func<int, object>)[]
                 {
                     ("experience", TransferLimits.MaxExperience, i => new { company = "C" + i, title = "T", start = "2020", end = "2021", summary = "S" }),
                     ("skills", TransferLimits.MaxSkills, i => new { name = "S" + i, group = "G" }),
                     ("projects", TransferLimits.MaxProjects, i => new { title = "P" + i, summary = "S" }),
                     ("certifications", TransferLimits.MaxCertifications, i => new { name = "C" + i, issuer = "I", status = "Earned" }),
                     ("talks", TransferLimits.MaxTalks, i => new { title = "T" + i }),
                     ("testimonials", TransferLimits.MaxTestimonials, i => new { author = "A" + i, quote = "Quote number " + i }),
                     ("blog", TransferLimits.MaxPosts, i => new { title = "B" + i, date = "2025-01-01", body = "Body." }),
                 })
        {
            var atCap = await ImportHarness.PlanAsync(App, ImportHarness.Table(table, Enumerable.Range(0, cap).Select(make).ToArray()));
            var over = await ImportHarness.PlanAsync(App, ImportHarness.Table(table, Enumerable.Range(0, cap + 1).Select(make).ToArray()));

            Assert.NotNull(atCap.Plan);
            Assert.Equal(cap, ImportHarness.Section(atCap.Plan!, table).Rows.Count);
            Assert.Null(over.Plan);
            Assert.Contains($"at most {cap:N0}", over.Refusal);
            Assert.Contains(table, over.Refusal);
        }
    }

    [Fact]
    public async Task TheStructureOfTheFile_IsChecked_ATableThatIsNotAListRefusesTheFile()
    {
        foreach (var bad in new[]
                 {
                     ImportHarness.File(content: new { skills = new { name = "x" } }),
                     ImportHarness.File(content: new { blog = "text" }),
                     ImportHarness.File(content: new[] { 1 }),
                     ImportHarness.File(profile: new[] { 1 }),
                     ImportHarness.File(extra: new Dictionary<string, object?> { ["uploads"] = new { } }),
                 })
        {
            var outcome = await ImportHarness.PlanAsync(App, bad);

            Assert.Null(outcome.Plan);
            Assert.False(string.IsNullOrWhiteSpace(outcome.Refusal));
        }
    }

    [Fact]
    public async Task PropertiesNothingReads_AreIgnoredAndCounted_WithoutBeingQuoted()
    {
        var file = ImportHarness.File(
            profile: new { ownerName = "Seed Owner", surprise = 1, hero = new { headline = "Seed headline", other = true } },
            content: new Dictionary<string, object> { ["skills"] = new object[] { new { name = "Seeding", group = "Garden", colour = "red", extra2 = 2 } }, ["unknown"] = new object[0] },
            extra: new Dictionary<string, object?> { ["<script>x</script>"] = 1, ["exportedAt"] = "2025-01-01T00:00:00Z", ["uploads"] = new object[] { new { path = "/uploads/x.png", exists = true } } });

        var plan = await ImportHarness.PlanOkAsync(App, file, applyProfile: true);

        Assert.Equal(6, plan.UnknownProperties); // the stray root key, the stray content key, the row's two, the profile's one and the hero's one
        var notes = string.Join("\n", plan.FileNotes);
        Assert.Contains("6 properties", notes);
        Assert.DoesNotContain("script", notes);
        Assert.DoesNotContain("colour", notes);
        Assert.Equal(RowStatus.Unchanged, ImportHarness.OneRow(plan, "skills").Status);
    }

    [Fact]
    public async Task TheUploadsManifest_IsNotImported_ItIsOnlyReportedOn()
    {
        var file = ImportHarness.File(extra: new Dictionary<string, object?> { ["uploads"] = new object[] { new { path = "/uploads/x.png", exists = true }, new { path = "/uploads/y.png", exists = false } } });

        var plan = await ImportHarness.PlanOkAsync(App, file);

        Assert.Contains(plan.FileNotes, n => n.Contains("2 image files") && n.Contains("not part of an export"));
        Assert.Equal(0, plan.UnknownProperties);
    }

    private async Task<RowReport> PlanOneAsync(string table, object row) => ImportHarness.OneRow(await ImportHarness.PlanOkAsync(App, ImportHarness.Table(table, row)), table);

    [Fact]
    public async Task ATestimonialKey_IsTheAuthorAndTheFirst40CharactersOfTheQuote()
    {
        // Differing only after character 40: the same entry. Differing within the first 40, or by author, or entirely: another one.
        Assert.Equal(RowStatus.Update, (await PlanOneAsync("testimonials", new { author = "Seed Person", quote = "Seeded praise that is long enough to be rewritten at the end." })).Status);
        Assert.Equal(RowStatus.Create, (await PlanOneAsync("testimonials", new { author = "Seed Person", quote = "Seeded praise that is entirely different from the stored one." })).Status);
        Assert.Equal(RowStatus.Create, (await PlanOneAsync("testimonials", new { author = "Seed Person", quote = "A completely different quote." })).Status);
        Assert.Equal(RowStatus.Create, (await PlanOneAsync("testimonials", new { author = "Another Person", quote = "Seeded praise that is long enough to be a quote." })).Status);
    }

    [Fact]
    public async Task ARoleKey_IsTheCompanyAndTheTitle_NotTheCompanyAlone()
    {
        var row = await PlanOneAsync("experience", new { company = "Seed Co", title = "Another Role", start = "2022", end = "2023", summary = "New." });

        Assert.Equal(RowStatus.Create, row.Status);
    }

    [Fact]
    public async Task APostWithoutAnAddressName_IsMatchedByTheNameBuiltFromItsTitle()
    {
        var row = await PlanOneAsync("blog", new { title = "Seed Post" });

        Assert.Equal(RowStatus.Unchanged, row.Status);
    }

    [Fact]
    public async Task TwoRowsThatDifferOnlyByTheCaseOfTheirKey_AreTheSameEntry_TheSecondIsRefused()
    {
        var plan = await ImportHarness.PlanOkAsync(App, ImportHarness.Table("skills", new { name = "CaseKey", group = "G" }, new { name = "casekey", group = "G" }, new { name = "CASEKEY", group = "G" }));

        var rows = ImportHarness.Section(plan, "skills").Rows;

        Assert.Equal([RowStatus.Create, RowStatus.Refused, RowStatus.Refused], rows.Select(r => r.Status).ToArray());
        Assert.All(rows.Skip(1), r => Assert.Contains("earlier", ImportHarness.Why(r)));
    }

    [Fact]
    public async Task ADroppedGalleryPicture_OfAnExistingProject_SaysTheStoredImageIsKept()
    {
        var row = await PlanOneAsync("projects", new { slug = "seed-project", screenshots = new object[] { new { url = "https://example.com/x.png", caption = "Web" } } });

        Assert.Contains(row.Warnings, w => w.Contains("the image that is stored was kept"));
        Assert.DoesNotContain(row.Warnings, w => w.Contains("the image was left out"));
    }

    [Fact]
    public async Task AnUploadsListOverTheCap_RefusesTheFile()
    {
        var uploads = Enumerable.Range(0, TransferLimits.MaxUploadEntries + 1).Select(_ => new { path = "/uploads/x.png" }).ToArray();

        var outcome = await ImportHarness.PlanAsync(App, ImportHarness.File(extra: new Dictionary<string, object?> { ["uploads"] = uploads }));

        Assert.Null(outcome.Plan);
        Assert.Contains("more than", outcome.Refusal);
        var atCap = await ImportHarness.PlanAsync(App, ImportHarness.File(extra: new Dictionary<string, object?> { ["uploads"] = uploads.Take(TransferLimits.MaxUploadEntries).ToArray() }));
        Assert.NotNull(atCap.Plan);
    }

    [Fact]
    public async Task BytesOverTheFileCap_AreRefusedByTheServiceToo_NotOnlyByTheUploadPath()
    {
        using var scope = App.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ImportService>();
        var json = ImportHarness.Table("skills", new { name = "Big", group = "G" });
        var bytes = TransferFlow.Utf8(json + new string(' ', TransferLimits.MaxFileBytes - json.Length + 1));

        var outcome = await service.PreviewAsync(bytes, "big.json", false, "cap-session", CancellationToken.None);

        Assert.Null(outcome.Plan);
        Assert.Contains("larger than", outcome.Refusal);
    }
}
