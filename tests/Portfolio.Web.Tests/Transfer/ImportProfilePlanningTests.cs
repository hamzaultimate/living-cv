using Portfolio.Web.Tests.Support;
using Portfolio.Web.Transfer;
using Xunit;

namespace Portfolio.Web.Tests.Transfer;

/// <summary>The profile part of a file: opt-in, checked like the profile editors check their forms, and applied whole or not at all.</summary>
[Collection(PlanningCollection.Name)]
public class ImportProfilePlanningTests
{
    private readonly PlanningSite _site;

    public ImportProfilePlanningTests(PlanningSite site) => _site = site;

    private AdminApp App => _site.App;

    private async Task<ImportPlan> PlanAsync(object profile, bool apply = true) => await ImportHarness.PlanOkAsync(App, ImportHarness.File(profile: profile), apply);

    private static string Why(ImportPlan plan) => string.Join(" | ", plan.Profile.Reasons);

    [Fact]
    public async Task WithoutTheTick_TheProfileInTheFileIsIgnored_WhateverItHolds()
    {
        var plan = await PlanAsync(new { ownerName = "<script>", templateKey = "nope", hero = "not an object" }, apply: false);

        Assert.False(plan.Profile.Requested);
        Assert.Null(plan.ProfileChange);
        Assert.Empty(plan.Profile.Reasons);
        Assert.Contains(plan.FileNotes, n => n.Contains("ignored because \"also apply\" was not ticked"));
        Assert.False(plan.HasChanges);
    }

    [Fact]
    public async Task TheTickWithNoProfileInTheFile_SaysThereIsNothingToApply()
    {
        var plan = await ImportHarness.PlanOkAsync(App, ImportHarness.File(), applyProfile: true);

        Assert.Contains(plan.FileNotes, n => n.Contains("no profile") && n.Contains("nothing to apply"));
        Assert.Null(plan.ProfileChange);
    }

    [Fact]
    public async Task AProfileThatSaysWhatIsStored_ChangesNothing_AndAProfileThatDiffersListsEachChange()
    {
        var same = await PlanAsync(new { ownerName = "Casey Morgan", hero = new { headline = "Seed headline" }, metrics = new[] { new { value = "10", unit = "ms", label = "first" }, new { value = "20", unit = "rps", label = "second" } } });
        Assert.Equal(RowStatus.Unchanged, same.Profile.Status);
        Assert.Null(same.ProfileChange);

        var changed = await PlanAsync(new { ownerName = "New Name", summary = "A new summary.", hero = new { lede = "A new lede." }, about = new { currentlyNote = "New note" } });
        Assert.Equal(RowStatus.Update, changed.Profile.Status);
        Assert.NotNull(changed.ProfileChange);
        Assert.Contains(changed.Profile.Changes, c => c.StartsWith("Changed: Name, Summary"));
        Assert.Contains(changed.Profile.Changes, c => c.Contains("Home page hero"));
        Assert.Contains(changed.Profile.Changes, c => c.Contains("About page"));
        Assert.DoesNotContain(changed.Profile.Changes, c => c.Contains("Headline metrics"));
    }

    [Fact]
    public async Task ATemplateThatIsNotInstalled_RefusesTheWholeProfile_AndIsNeverGuessed()
    {
        foreach (var key in new[] { "nope", "../software", "healthcare\0", "Healthcare2", "software/", " soft ware" })
        {
            var plan = await PlanAsync(new { ownerName = "Would Change", templateKey = key });

            Assert.Equal(RowStatus.Refused, plan.Profile.Status);
            Assert.Null(plan.ProfileChange);
            Assert.True(plan.Profile.Reasons.Count > 0, key);
        }
        var named = await PlanAsync(new { templateKey = "nope" });
        Assert.Contains("is not installed on this site", Why(named));
        Assert.Contains("never guessed", Why(named));
    }

    [Fact]
    public async Task ATemplateKeyInAnotherCase_IsTheTemplateItNames()
    {
        var same = await PlanAsync(new { templateKey = "SOFTWARE" });
        var other = await PlanAsync(new { templateKey = "HEALTHCARE" });

        Assert.Equal(RowStatus.Unchanged, same.Profile.Status);
        Assert.Equal(RowStatus.Update, other.Profile.Status);
        Assert.Equal("Healthcare", other.Words.Definition.Name);
    }

    [Fact]
    public async Task ASwitchToAnotherInstalledTemplate_IsPlanned_AndTheMessagesUseItsWords()
    {
        var plan = await PlanAsync(new { templateKey = "healthcare", hero = new { headline = "" } });

        Assert.Equal(RowStatus.Refused, plan.Profile.Status);
        // Healthcare calls the hero block "Home page introduction": the refusal is in the words of the template the profile would have.
        Assert.Contains("Headline", Why(plan));

        var ok = await PlanAsync(new { templateKey = "healthcare" });
        Assert.Equal(RowStatus.Update, ok.Profile.Status);
        Assert.Contains("Template: Software & IT to Healthcare", ok.Profile.Changes[0]);
        Assert.Equal("Healthcare", ok.Words.Definition.Name);
    }

    [Theory]
    [InlineData("ownerName", null, "Name is required")]
    [InlineData("ownerName", "", "Name is required")]
    [InlineData("ownerName", "<b>x</b>", "plain text")]
    [InlineData("brandName", "a\u0000b", "characters that are not allowed")]
    [InlineData("url", "javascript:alert(1)", "full web address")]
    [InlineData("url", "ftp://example.com", "full web address")]
    [InlineData("url", null, "Site address is required")]
    [InlineData("email", "not an address", "email")]
    [InlineData("phone", "call me", "digits")]
    [InlineData("whatsApp", "12", "Digits only")]
    [InlineData("googleAnalyticsId", "UA-123", "GA4")]
    [InlineData("linkedIn", "javascript:alert(1)", "full web address")]
    [InlineData("gitHub", "data:text/html,x", "full web address")]
    [InlineData("youTube", "//evil.example", "full web address")]
    [InlineData("summary", "<img src=x onerror=alert(1)>", "plain text")]
    [InlineData("location", "<!-- x -->", "plain text")]
    [InlineData("role", "line one\nline two", "characters that are not allowed")]
    public async Task IdentityValues_AreHeldToTheIdentityFormsRules(string field, string? value, string expected)
    {
        var plan = await PlanAsync(new Dictionary<string, object?> { [field] = value });

        Assert.Equal(RowStatus.Refused, plan.Profile.Status);
        Assert.Contains(expected, Why(plan), StringComparison.OrdinalIgnoreCase);
        Assert.Null(plan.ProfileChange);
    }

    [Fact]
    public async Task IdentityLimits_AreTheFormsOwn_OneOverIsRefused()
    {
        foreach (var (field, max) in new[] { ("ownerName", 200), ("brandName", 80), ("role", 300), ("jobTitle", 200), ("location", 120), ("summary", 2000) })
        {
            var at = await PlanAsync(new Dictionary<string, object?> { [field] = new string('a', max) });
            var over = await PlanAsync(new Dictionary<string, object?> { [field] = new string('a', max + 1) });

            Assert.True(at.Profile.Status == RowStatus.Update, $"{field} at {max}: {Why(at)}");
            Assert.Equal(RowStatus.Refused, over.Profile.Status);
        }
        Assert.Contains("at most 30", Why(await PlanAsync(new { knowsAbout = Enumerable.Range(0, 31).Select(i => "t" + i).ToArray() })));
        Assert.Contains("at most 5", Why(await PlanAsync(new { secondaryEmails = Enumerable.Range(0, 6).Select(i => $"a{i}@example.com").ToArray() })));
        Assert.Contains("email", Why(await PlanAsync(new { secondaryEmails = new[] { "nope" } })), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must be a list", Why(await PlanAsync(new { knowsAbout = "one, two" })));
    }

    [Fact]
    public async Task OneBadFieldRefusesTheWholeProfile_AndEveryReasonIsListed()
    {
        var plan = await PlanAsync(new { ownerName = "Fine Name", email = "bad", url = "javascript:x", hero = new { headline = "<b>" }, metrics = new object[] { 5 } });

        Assert.Equal(RowStatus.Refused, plan.Profile.Status);
        Assert.True(plan.Profile.Reasons.Count >= 4, Why(plan));
        Assert.Empty(plan.Profile.Changes);
        Assert.Null(plan.ProfileChange);
    }

    [Fact]
    public async Task Hero_Metrics_AndAbout_FollowTheirEditorsRules()
    {
        Assert.Contains("Headline", Why(await PlanAsync(new { hero = new { headline = (string?)null } })));
        Assert.Contains("plain text", Why(await PlanAsync(new { hero = new { eyebrow = "<b>x</b>" } })));
        Assert.Contains("at most 800", Why(await PlanAsync(new { hero = new { lede = new string('l', 801) } })));
        Assert.Contains("must be an object", Why(await PlanAsync(new { hero = "text" })));

        Assert.Contains("At most 8", Why(await PlanAsync(new { metrics = Enumerable.Range(0, 9).Select(i => new { value = "1", label = "l" }).ToArray() })));
        Assert.Contains("is required", Why(await PlanAsync(new { metrics = new[] { new { value = "", unit = "x", label = "l" } } })));
        Assert.Contains("is required", Why(await PlanAsync(new { metrics = new[] { new { value = "1", unit = "x", label = "" } } }))); // the label is shown by this template, so it is needed
        Assert.Contains("at most 20", Why(await PlanAsync(new { metrics = new[] { new { value = new string('9', 21), label = "l" } } })));
        Assert.Contains("must be a list", Why(await PlanAsync(new { metrics = "many" })));
        var dropped = await PlanAsync(new { metrics = new object[] { new { value = "", unit = "", label = "" }, new { value = "3", label = "kept" } } });
        Assert.Equal(RowStatus.Update, dropped.Profile.Status); // the empty row was dropped, the other written

        // The stored About text has a headline, so emptying it is refused (as in the About editor); a missing one is left as it is.
        Assert.Contains("Headline", Why(await PlanAsync(new { about = new { headline = "" } })));
        Assert.Equal(RowStatus.Unchanged, (await PlanAsync(new { about = new { currentlyNote = "Seed note" } })).Profile.Status);
        Assert.Contains("at most 10", Why(await PlanAsync(new { about = new { paragraphs = Enumerable.Range(0, 11).Select(i => "p" + i).ToArray() } })));
        Assert.Contains("plain text", Why(await PlanAsync(new { about = new { paragraphs = new[] { "<b>x</b>" } } })));
        Assert.Contains("at most 2000", Why(await PlanAsync(new { about = new { paragraphs = new[] { new string('p', 2001) } } })));
        Assert.Contains("At most 6", Why(await PlanAsync(new { about = new { valueProps = Enumerable.Range(0, 7).Select(i => new { title = "t", body = "b" }).ToArray() } })));
        Assert.Contains("is required", Why(await PlanAsync(new { about = new { valueProps = new[] { new { title = "t", body = "" } } } })));
        Assert.Contains("at most 30", Why(await PlanAsync(new { about = new { focus = Enumerable.Range(0, 31).Select(i => "f" + i).ToArray() } })));
    }

    [Fact]
    public async Task AFieldTheProfileDoesNotMention_IsKept_ANullClearsAnOptionalOne()
    {
        var absent = await PlanAsync(new { hero = new { lede = "Seed lede." } });
        var cleared = await PlanAsync(new { hero = new { lede = (string?)null, eyebrow = (string?)null } });

        Assert.Equal(RowStatus.Unchanged, absent.Profile.Status);
        Assert.Equal(RowStatus.Update, cleared.Profile.Status);
    }

    [Fact]
    public async Task Photos_AreKeptOnlyIfTheFileIsHere_AnythingElseIsDroppedAndNeverClearsTheStoredPhoto()
    {
        var kept = await PlanAsync(new { photoUrl = PlanningSite.RealUpload });
        Assert.Equal(RowStatus.Update, kept.Profile.Status);
        Assert.Empty(kept.Profile.Warnings);

        using (var db = App.CreateDbContext())
        {
            var p = db.SiteProfiles.Single();
            p.PhotoUrl = PlanningSite.RealUpload;
            p.PhotoUrlLight = PlanningSite.RealUpload;
            await db.SaveChangesAsync();
        }
        try
        {
            foreach (var dropped in new[] { PlanningSite.MissingUpload, "https://example.com/me.jpg", "javascript:alert(1)", "/etc/passwd" })
            {
                var plan = await PlanAsync(new { photoUrl = dropped, photoUrlLight = dropped });
                Assert.Equal(RowStatus.Unchanged, plan.Profile.Status); // the stored photos stay
                Assert.Equal(2, plan.Profile.Warnings.Count(w => w.Contains("the image that is stored was kept")));
                Assert.Empty(plan.Profile.Reasons);
            }
            var cleared = await PlanAsync(new { photoUrl = (string?)null });
            Assert.Equal(RowStatus.Update, cleared.Profile.Status); // only the file saying null clears it
        }
        finally
        {
            using var db = App.CreateDbContext();
            var p = db.SiteProfiles.Single();
            p.PhotoUrl = null;
            p.PhotoUrlLight = null;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task AnAccentTheTemplateDoesNotOffer_IsLeftAsItIs_WithAWarning()
    {
        var plan = await PlanAsync(new { accentColor = "chartreuse" });
        var good = await PlanAsync(new { accentColor = "emerald" });

        Assert.Contains(plan.Profile.Warnings, w => w.Contains("not one of the colours"));
        Assert.Equal(RowStatus.Unchanged, plan.Profile.Status);
        Assert.Equal(RowStatus.Update, good.Profile.Status);
    }

    // ---- sections ----

    [Fact]
    public async Task SectionSettingsForKeysTheTemplateLacks_AreInert_ListedAndNeverStored()
    {
        var plan = await PlanAsync(new { sections = new object[] { new { key = "gallery", label = "Pictures", shown = true }, new { key = "../x", shown = true }, new { key = "blog", label = "Journal" } } });

        Assert.Equal(["gallery", "an entry with an unusable key"], plan.Profile.InertSections.ToArray());
        Assert.Equal(RowStatus.Unchanged, plan.Profile.Status); // "blog" as "Journal" is what is stored; the inert keys changed nothing
        Assert.Null(plan.ProfileChange);

        var withChange = await PlanAsync(new { sections = new object[] { new { key = "gallery", shown = true }, new { key = "blog", label = "Notes" } } });
        Assert.Equal(["gallery"], withChange.Profile.InertSections.ToArray());
        Assert.NotNull(withChange.ProfileChange);
    }

    [Fact]
    public async Task SectionNames_FollowTheSectionsPagesRules()
    {
        Assert.Contains("characters that are not allowed", Why(await PlanAsync(new { sections = new[] { new { key = "blog", label = "Jour​nal" } } })));
        Assert.Contains("characters that are not allowed", Why(await PlanAsync(new { sections = new[] { new { key = "blog", label = "‮lanruoj" } } })));
        Assert.Contains("characters that are not allowed", Why(await PlanAsync(new { sections = new[] { new { key = "blog", label = "⠀⠀" } } })));
        Assert.Contains("plain text", Why(await PlanAsync(new { sections = new[] { new { key = "blog", label = "<b>x</b>" } } })));
        Assert.Contains("at most 40", Why(await PlanAsync(new { sections = new[] { new { key = "blog", label = new string('n', 41) } } })));
        Assert.Contains("own name", Why(await PlanAsync(new { sections = new[] { new { key = "blog", label = "Projects" }, new { key = "talks", label = "projects" } } })));
        Assert.Contains("own name", Why(await PlanAsync(new { sections = new[] { new { key = "blog", label = "Same" }, new { key = "talks", label = "SAME" } } })));
        Assert.Contains("must be a list", Why(await PlanAsync(new { sections = new { key = "blog" } })));
        Assert.Contains("must be an entry", Why(await PlanAsync(new { sections = new object[] { "blog" } })));
        Assert.Contains("at most 40", Why(await PlanAsync(new { sections = Enumerable.Range(0, 41).Select(i => new { key = "k" + i }).ToArray() })));
        Assert.Equal(RowStatus.Update, (await PlanAsync(new { sections = new[] { new { key = "blog", label = "Pro‍ject" } } })).Profile.Status); // an emoji or script joiner between letters is allowed
    }

    [Fact]
    public async Task TheSectionList_NeedsAtLeastOneMenuSection_AndNamesAreComparedAcrossCase()
    {
        var allOff = new[] { "about", "experience", "projects", "skills", "certifications", "blog", "talks", "contact" }.Select(k => new { key = k, shown = false }).ToArray();

        Assert.Contains("at least one", Why(await PlanAsync(new { sections = allOff })));
        var oneOn = allOff.Select(s => s.key == "about" ? new { key = "about", shown = true } : s).ToArray();
        Assert.Equal(RowStatus.Update, (await PlanAsync(new { sections = oneOn })).Profile.Status);
    }

    [Fact]
    public async Task SectionOrderAndShown_ComeFromTheFile_AndAKeyListedTwiceUsesTheFirst()
    {
        var plan = await PlanAsync(new { sections = new object[] { new { key = "skills", sortOrder = 1 }, new { key = "about", sortOrder = 2 }, new { key = "skills", shown = false } } });

        Assert.Equal(RowStatus.Update, plan.Profile.Status);
        Assert.Contains(plan.Profile.Warnings, w => w.Contains("listed twice"));
        Assert.Contains(plan.Profile.Changes, c => c.Contains("Sections") || c.Contains("sections"));
    }

    [Fact]
    public async Task AWrongTypeInASectionEntry_IsRefused()
    {
        Assert.Contains("must be true, false or null", Why(await PlanAsync(new { sections = new[] { new { key = "blog", shown = "yes" } } })));
        Assert.Contains("must be a number", Why(await PlanAsync(new { sections = new[] { new { key = "blog", sortOrder = "first" } } })));
        Assert.Contains("must be text", Why(await PlanAsync(new { sections = new[] { new { key = (object)5 } } })));
    }

    [Fact]
    public async Task ExportingTheSite_ThenPlanningThatFile_FindsNothingToDo_ExceptWhatCouldNotBeKept()
    {
        var text = await TransferFlow.ExportTextAsync(_site.Owner);

        var plan = await ImportHarness.PlanOkAsync(App, text, applyProfile: true);

        Assert.Equal(RowStatus.Unchanged, plan.Profile.Status);
        Assert.False(plan.Count(RowStatus.Refused) > 0, string.Join("\n", plan.Reports.SelectMany(r => r.Rows).Where(r => r.Status == RowStatus.Refused).Select(r => r.Name + ": " + ImportHarness.Why(r))));
        // The seeded gallery images name upload files that were never stored: the file's gallery cannot be kept, so the stored list stays and the row says so. Nothing changes.
        Assert.DoesNotContain(plan.Reports.SelectMany(r => r.Rows), r => r.Status is RowStatus.Create or RowStatus.Update);
        var project = plan.Reports.SelectMany(r => r.Rows).Single(r => r.Warnings.Count > 0);
        Assert.Contains(project.Warnings, w => w.Contains("the stored list was kept as it is"));
    }

    [Fact]
    public async Task ANullForOneOptionalHeroField_ClearsThatFieldAlone()
    {
        var plan = await PlanAsync(new { hero = new { lede = (string?)null } });

        Assert.Equal(RowStatus.Update, plan.Profile.Status);
        Assert.Contains(plan.Profile.Changes, c => c.StartsWith("Changed: "));
    }

    [Fact]
    public async Task ANullForAnOptionalIdentityField_ClearsIt()
    {
        using (var db = App.CreateDbContext())
        {
            var p = db.SiteProfiles.Single();
            p.Location = "Somewhere";
            await db.SaveChangesAsync();
        }
        try
        {
            var plan = await PlanAsync(new { location = (string?)null });

            Assert.Equal(RowStatus.Update, plan.Profile.Status);
            Assert.Contains(plan.Profile.Changes, c => c.Contains("Location"));
        }
        finally
        {
            using var db = App.CreateDbContext();
            db.SiteProfiles.Single().Location = "Leeds, UK";
            await db.SaveChangesAsync();
        }
    }
}
