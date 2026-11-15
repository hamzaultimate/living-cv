using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>The profile row carries a concurrency stamp that every write replaces and every form checks, so two tabs (or a tab and the wizard) cannot overwrite each other.</summary>
public class ProfileStampTests
{
    private static async Task<string?> StoredStampAsync(AdminApp app) => (await ProfileFlow.ProfileAsync(app)).ConcurrencyStamp;

    [Fact]
    public async Task EveryKindOfProfileWrite_ReplacesTheStamp()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var seen = new List<string?> { await StoredStampAsync(app) };
        Assert.Matches("^[0-9a-f]{32}$", seen[0]!); // the wizard's publish wrote it

        var steps = new (string Name, Func<Task> Write)[]
        {
            ("identity", async () => Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Identity, new Dictionary<string, string>
                { ["Form.OwnerName"] = "Casey Morgan", ["Form.BrandName"] = "Casey", ["Form.Url"] = "https://casey.example" })).StatusCode)),
            ("hero", async () => Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields())).StatusCode)),
            ("metrics", async () => Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, ProfileFlow.MetricsFields(("9", "x", "nine")))).StatusCode)),
            ("about", async () => Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields())).StatusCode)),
            ("photo", async () => Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, "/admin/photo?handler=Remove", new Dictionary<string, string>())).StatusCode)),
            ("template", async () => Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, "/admin/template", new Dictionary<string, string> { ["Key"] = "healthcare" })).StatusCode)),
            ("wizard", async () =>
            {
                Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await WizardFlow.PickTemplateAsync(owner, "software")));
                Assert.Equal("/admin/welcome/photo", WizardFlow.Where(await WizardFlow.SaveBasicsAsync(owner)));
                Assert.Equal(HttpStatusCode.Redirect, (await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields("https://casey.example"))).StatusCode);
            }),
        };

        foreach (var (name, write) in steps)
        {
            await write();
            var now = await StoredStampAsync(app);
            Assert.Matches("^[0-9a-f]{32}$", now!);
            Assert.False(seen.Contains(now), $"the {name} write did not replace the stamp"); // a new value every time, never one that was used before
            seen.Add(now);
        }
    }

    [Fact]
    public async Task AWizardDraftSave_DoesNotReplaceTheStamp_SoItCannotInvalidateAForm()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var before = await StoredStampAsync(app);

        // The re-run saves its answers as a draft on the same row; nothing public changes, so an open editor stays valid.
        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await WizardFlow.PickTemplateAsync(owner, "software")));
        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(await WizardFlow.SaveBasicsAsync(owner)));

        Assert.NotNull(await WizardFlow.DraftAsync(app));
        Assert.Equal(before, await StoredStampAsync(app));
    }

    [Fact]
    public async Task AWizardDraftSave_IsNotLost_WhenTheStampMovedAfterThePageReadTheProfile()
    {
        using var app = new AdminApp();
        await ContentFlow.BlankSiteAsync(app);
        using var scope = app.Services.CreateScope();
        var onboarding = scope.ServiceProvider.GetRequiredService<OnboardingService>();
        var editor = scope.ServiceProvider.GetRequiredService<ProfileEditor>();
        await editor.LoadAsync(); // the request has read the profile (and its stamp) ...

        using (var other = app.CreateDbContext()) // ... when another request writes it
        {
            (await other.SiteProfiles.SingleAsync()).ConcurrencyStamp = ProfileEditor.NewStamp();
            await other.SaveChangesAsync();
        }
        var movedTo = await StoredStampAsync(app);
        var form = new BasicsForm { OwnerName = "Draft Person", JobTitle = "Draft Title", Email = "draft@owner.example", Location = "Leeds", Summary = "A draft summary." };
        await onboarding.SaveBasicsAsync(form);

        Assert.Equal("Draft Person", (await WizardFlow.DraftAsync(app))!.OwnerName); // the answer was kept
        Assert.Equal(movedTo, await StoredStampAsync(app)); // and the draft write left the stamp alone
    }

    [Fact]
    public async Task APhotoWrite_OverlappingAnEdit_IsAppliedOnTopOfIt_NotRefusedAndNotLost()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var editor = await app.NewClientSignedInAsync();
        using (var db = app.CreateDbContext())
        {
            var p = await db.SiteProfiles.SingleAsync();
            p.PhotoUrl = "/uploads/" + new string('a', 32) + ".jpg";
            await db.SaveChangesAsync();
        }
        var token = await AdminApp.GetTokenAsync(owner, "/admin/photo");

        var response = await app.OverlapAsync(
            () => AdminApp.PostFormAsync(owner, "/admin/photo?handler=Remove", token, new Dictionary<string, string>()),
            async () => Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(editor, ProfileFlow.Hero, ProfileFlow.HeroFields(headline: "Edited meanwhile"))).StatusCode));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var row = await ProfileFlow.ProfileAsync(app);
        Assert.Null(row.PhotoUrl); // the photo write went through
        Assert.Equal("Edited meanwhile", row.Hero.Headline); // and did not undo the edit
    }

    [Theory]
    [InlineData("/admin/profile/hero")]
    [InlineData("/admin/profile/metrics")]
    [InlineData("/admin/profile/about")]
    [InlineData("/admin/profile")]
    public async Task AFormOpenedBeforeAnotherTabSaved_IsRefused_NothingIsWritten_AndTheTypingStaysOnThePage(string path)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ProfileFlow.SeedTextAsync(app);
        var otherTab = await app.NewClientSignedInAsync();
        var opened = await ProfileFlow.StampAsync(owner, path);

        // The other tab saves first (any of the editors writes the same row).
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(otherTab, ProfileFlow.Hero, ProfileFlow.HeroFields(headline: "The other tab"))).StatusCode);
        var afterOther = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));

        var mine = path switch
        {
            ProfileFlow.Hero => ProfileFlow.HeroFields(headline: "My stale headline"),
            ProfileFlow.Metrics => ProfileFlow.MetricsFields(("77", "x", "my stale figure")),
            ProfileFlow.About => ProfileFlow.AboutFields(headline: "My stale about"),
            _ => new Dictionary<string, string> { ["Form.OwnerName"] = "My Stale Name", ["Form.BrandName"] = "Stale", ["Form.Url"] = "https://stale.example" },
        };
        var response = await ProfileFlow.PostAsync(owner, path, mine, stamp: opened);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Nothing was saved.", html);
        Assert.Contains("somewhere else", html);
        Assert.Equal(afterOther, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
        Assert.Matches(@"my stale figure|My stale headline|My stale about|My Stale Name", html); // what was typed is still on the page
    }

    [Theory]
    [InlineData("/admin/profile/hero")]
    [InlineData("/admin/profile/metrics")]
    [InlineData("/admin/profile/about")]
    [InlineData("/admin/profile")]
    public async Task ASaveOverlappingAnotherSave_ThatRealRequestWins_AndTheOtherIsRefusedAtTheDatabase(string path)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ProfileFlow.SeedTextAsync(app);
        var otherTab = await app.NewClientSignedInAsync();
        // Both pages are open with the same stamp; the other tab's request runs to completion between this request's read and its write.
        var stamp = await ProfileFlow.StampAsync(owner, path);
        var mine = path switch
        {
            ProfileFlow.Hero => ProfileFlow.HeroFields(headline: "Loser headline"),
            ProfileFlow.Metrics => ProfileFlow.MetricsFields(("1", "x", "loser")),
            ProfileFlow.About => ProfileFlow.AboutFields(headline: "Loser about"),
            _ => new Dictionary<string, string> { ["Form.OwnerName"] = "Loser Name", ["Form.BrandName"] = "Loser", ["Form.Url"] = "https://loser.example" },
        };

        var response = await app.OverlapAsync(
            () => ProfileFlow.PostAsync(owner, path, mine, stamp: stamp),
            async () => Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(otherTab, ProfileFlow.Hero, ProfileFlow.HeroFields(headline: "Winner headline"), stamp: stamp)).StatusCode));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Nothing was saved.", await response.Content.ReadAsStringAsync());
        var row = await ProfileFlow.ProfileAsync(app);
        Assert.Equal("Winner headline", row.Hero.Headline);
        Assert.DoesNotContain("Loser", ProfileFlow.Fingerprint(row));
        Assert.Contains("Winner headline", await app.NewClient().GetStringAsync("/")); // and the public site shows the winner
    }

    [Theory]
    [InlineData("/admin/profile/hero")]
    [InlineData("/admin/profile/metrics")]
    [InlineData("/admin/profile/about")]
    [InlineData("/admin/profile")]
    public async Task AMissingOrForgedStamp_IsRefused_LikeAStaleOne(string path)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        await ProfileFlow.SeedTextAsync(app);
        var before = ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app));
        var fields = path switch
        {
            ProfileFlow.Hero => ProfileFlow.HeroFields(),
            ProfileFlow.Metrics => ProfileFlow.MetricsFields(("5", "", "five")),
            ProfileFlow.About => ProfileFlow.AboutFields(),
            _ => new Dictionary<string, string> { ["Form.OwnerName"] = "No Stamp", ["Form.BrandName"] = "No", ["Form.Url"] = "https://nostamp.example" },
        };

        foreach (var stamp in new[] { "", "0", new string('f', 32), "not-a-stamp" })
        {
            var response = await ProfileFlow.PostAsync(owner, path, fields, stamp: stamp);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Nothing was saved.", await response.Content.ReadAsStringAsync());
            Assert.Equal(before, ProfileFlow.Fingerprint(await ProfileFlow.ProfileAsync(app)));
        }
    }

    [Fact]
    public async Task ARowThatWasNeverStamped_AcceptsTheFirstSaveFromAFormWithoutOne_ThenWantsTheNewStamp()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        using (var db = app.CreateDbContext())
        {
            var p = await db.SiteProfiles.SingleAsync();
            p.ConcurrencyStamp = null; // a site that existed before the column did
            await db.SaveChangesAsync();
        }
        var stamp = await ProfileFlow.StampAsync(owner, ProfileFlow.Hero);
        Assert.Equal("", stamp);

        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields(headline: "First save"), stamp: "")).StatusCode);
        Assert.Matches("^[0-9a-f]{32}$", await StoredStampAsync(app) ?? "");
        Assert.Equal(HttpStatusCode.OK, (await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields(headline: "Replay"), stamp: "")).StatusCode);
        Assert.Equal("First save", (await ProfileFlow.ProfileAsync(app)).Hero.Headline);
    }

    [Fact]
    public async Task TheStampOnTheForm_IsTheStoredOne_AndAGetNeverChangesIt()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var stored = await StoredStampAsync(app);

        foreach (var path in ProfileFlow.Parts.Append(ProfileFlow.Identity))
        {
            Assert.Equal(stored, await ProfileFlow.StampAsync(owner, path));
            Assert.Equal(stored, await StoredStampAsync(app));
        }
    }

    [Fact]
    public async Task AFormOpenedBeforeTheWizardPublished_IsRefusedAfterIt()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var stamp = await ProfileFlow.StampAsync(owner, ProfileFlow.Hero);
        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(await WizardFlow.PickTemplateAsync(owner, "software")));
        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields("Casey Morgan", "Specialist", "casey@owner.example", "Leeds, UK", "A new summary."))));
        Assert.Equal(HttpStatusCode.Redirect, (await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields("https://casey.example"))).StatusCode);

        var response = await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields(headline: "After the publish"), stamp: stamp);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Nothing was saved.", await response.Content.ReadAsStringAsync());
        Assert.Equal("Casey Morgan", (await ProfileFlow.ProfileAsync(app)).Hero.Headline);
    }
}
