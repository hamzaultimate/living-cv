using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data.Entities;
using Portfolio.Data.Seeding;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>What a restart does to a site the wizard has finished, how the photo pages keep the two theme photos in step, and the seed-source sample against existing rows.</summary>
public class OnboardingRestartTests
{
    private static readonly string SeedDir = Path.Combine(AppContext.BaseDirectory, "Seed");

    /// <summary>Runs the start-up seeding step against the app's database, as a restart would.</summary>
    private static async Task<bool> RestartAsync(AdminApp app, bool keyConfigured = true, SeedMode mode = SeedMode.IfEmpty)
    {
        using var db = app.CreateDbContext();
        return await FirstRun.SeedAtStartupAsync(db, SeedDir, mode, keyConfigured);
    }

    private static async Task PublishAsync(AdminApp app, string template, string content)
    {
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, template);
        await WizardFlow.SaveBasicsAsync(owner);
        Assert.Equal("/admin", WizardFlow.Where(await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields(content: content))));
    }

    [Fact]
    public async Task AfterABlankPublish_ARestartDoesNotFillTheEmptyTablesWithTheDemoPersona()
    {
        using var app = new AdminApp();
        await PublishAsync(app, "healthcare", StarterChoice.Blank);
        var before = await DemoSite.RowCountsAsync(app);

        Assert.False(await RestartAsync(app));
        Assert.False(await RestartAsync(app, keyConfigured: false)); // the setup key was removed, as the guide advises
        Assert.False(await RestartAsync(app));

        Assert.Equal(before, await DemoSite.RowCountsAsync(app));
        Assert.All(before.Where(c => c.Key != "profiles"), c => Assert.Equal(0, c.Value));
    }

    [Fact]
    public async Task AfterHealthcareWithSample_ARestartAddsNothing_NotEvenTalksOrBlogPosts()
    {
        using var app = new AdminApp();
        await PublishAsync(app, "healthcare", StarterChoice.Sample);
        var before = await DemoSite.RowCountsAsync(app);
        Assert.Equal(0, before["talks"]);
        Assert.Equal(0, before["posts"]);

        await RestartAsync(app);
        await RestartAsync(app, keyConfigured: false);

        Assert.Equal(before, await DemoSite.RowCountsAsync(app));
    }

    [Fact]
    public async Task AfterASkip_ARestartStillLeavesAnEmptiedTableEmpty()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PostAsync(owner, WizardFlow.Template + "?handler=Skip", new Dictionary<string, string>(), WizardFlow.Template);
        Assert.Equal("skipped", (await WizardFlow.ProfileAsync(app)).OnboardingOutcome);
        using (var db = app.CreateDbContext())
        {
            db.Talks.RemoveRange(db.Talks);
            await db.SaveChangesAsync();
        }
        var before = await DemoSite.RowCountsAsync(app);
        Assert.Equal(0, before["talks"]);
        Assert.True(before["experiences"] > 0); // the skip itself loaded the demo content

        await RestartAsync(app);

        Assert.Equal(before, await DemoSite.RowCountsAsync(app));
    }

    [Fact]
    public async Task ARestartBeforeThePublish_StillWaitsForTheWizard()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");

        Assert.False(await RestartAsync(app));

        Assert.Equal(0, (await DemoSite.RowCountsAsync(app))["experiences"]);
    }

    [Fact]
    public async Task ASiteThatExistedBeforeTheWizard_SeedsAtStartupExactlyAsBefore()
    {
        using var app = new AdminApp();
        _ = app.Services;
        using (var db = app.CreateDbContext())
        {
            // What the migration leaves: completed, no outcome.
            db.SiteProfiles.Add(new SiteProfile { OnboardingCompletedAt = DateTimeOffset.UtcNow, OnboardingOutcome = null });
            await db.SaveChangesAsync();
        }

        Assert.True(await RestartAsync(app));

        Assert.True((await DemoSite.RowCountsAsync(app))["experiences"] > 0);
    }

    [Fact]
    public async Task ReRunningTheWizardOnALegacySite_KeepsTheOutcomeEmpty_AndStartupStillSeedsAsBefore()
    {
        using var app = new AdminApp();
        _ = app.Services;
        using (var db = app.CreateDbContext())
        {
            db.SiteProfiles.Add(new SiteProfile { OnboardingCompletedAt = DateTimeOffset.UtcNow }); // existed before the wizard
            await db.SaveChangesAsync();
        }
        var owner = await app.SignedInClientAsync();
        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        await WizardFlow.SaveBasicsAsync(owner);
        Assert.Equal("/admin", WizardFlow.Where(await WizardFlow.PublishAsync(owner)));

        var profile = await WizardFlow.ProfileAsync(app);
        Assert.Equal("healthcare", profile.TemplateKey); // the re-run really published
        Assert.Null(profile.OnboardingOutcome);
        Assert.True(await RestartAsync(app));
        Assert.True((await DemoSite.RowCountsAsync(app))["experiences"] > 0);
    }

    [Fact]
    public async Task AnUnclaimableLegacyInstall_AndAuthoritativeMode_SeedAsBefore()
    {
        using var app = new AdminApp();
        _ = app.Services;
        Assert.True(await RestartAsync(app, keyConfigured: false)); // no key, no owner: nothing can claim it, so there is no wizard

        using var authoritative = new AdminApp();
        _ = authoritative.Services;
        Assert.True(await RestartAsync(authoritative, mode: SeedMode.Authoritative));
        Assert.True((await DemoSite.RowCountsAsync(authoritative))["experiences"] > 0);
    }

    [Theory]
    [InlineData(true, true, true, "published", "IfEmpty", true)]      // finished through the wizard: never seeded again
    [InlineData(false, true, true, "skipped", "IfEmpty", true)]
    [InlineData(true, true, true, "published", "Authoritative", false)] // authoritative mode is the operator's explicit choice
    [InlineData(true, true, true, null, "IfEmpty", false)]            // finished before the wizard existed
    public async Task TheOutcome_DecidesWhetherStartupSeedingIsSkipped(bool keyConfigured, bool hasRow, bool completed, string? outcome, string mode, bool skipped)
    {
        using var test = new TestDatabase();
        using (var db = test.CreateContext())
        {
            if (hasRow)
            {
                db.SiteProfiles.Add(new SiteProfile { OnboardingCompletedAt = completed ? DateTimeOffset.UtcNow : null, OnboardingOutcome = outcome });
                await db.SaveChangesAsync();
            }
        }
        using var check = test.CreateContext();

        Assert.Equal(skipped, await FirstRun.ShouldDeferContentSeedAsync(check, keyConfigured, Enum.Parse<SeedMode>(mode)));
    }

    // ---------------------------------------------------------------- the seed-source sample against rows that exist

    [Fact]
    public async Task SoftwareSample_OnASiteWithEditedAndEmptiedTables_FillsOnlyTheEmptyOnes_AndRewritesNothing()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        await app.NewClient().GetAsync("/");
        using (var db = app.CreateDbContext())
        {
            (await db.Experiences.OrderBy(e => e.Id).FirstAsync()).Summary = "MY OWN EDIT";
            (await db.Projects.OrderBy(p => p.Id).FirstAsync()).Title = "My renamed project";
            db.Skills.RemoveRange(db.Skills.Where(s => s.Name != "C#")); // a mostly emptied table is still not empty
            db.Talks.RemoveRange(db.Talks); // an empty one is filled
            await db.SaveChangesAsync();
        }
        var before = await DemoSite.RowCountsAsync(app);
        var owner = await app.SignedInClientAsync();
        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner);

        await WizardFlow.PublishAsync(owner, WizardFlow.ReviewFields(content: StarterChoice.Sample));

        var after = await DemoSite.RowCountsAsync(app);
        Assert.Equal(before["skills"], after["skills"]);
        Assert.Equal(before["experiences"], after["experiences"]);
        Assert.Equal(before["projects"], after["projects"]);
        Assert.True(after["talks"] > 0);
        using var check = app.CreateDbContext();
        Assert.Equal("MY OWN EDIT", (await check.Experiences.OrderBy(e => e.Id).FirstAsync()).Summary);
        Assert.Equal("My renamed project", (await check.Projects.OrderBy(p => p.Id).FirstAsync()).Title);
        Assert.Equal(["C#"], await check.Skills.Select(s => s.Name).ToListAsync());
    }

    // ---------------------------------------------------------------- the two theme photos stay in step

    private static async Task<(AdminApp App, HttpClient Owner, string Url)> PublishedWithPhotoAsync()
    {
        var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "healthcare");
        await WizardFlow.SaveBasicsAsync(owner);
        await WizardFlow.UploadPhotoAsync(owner, TestImages.Split(SKEncodedImageFormat.Jpeg, 40, 40));
        await WizardFlow.PublishAsync(owner);
        var p = await WizardFlow.ProfileAsync(app);
        Assert.Equal(p.PhotoUrl, p.PhotoUrlLight);
        return (app, owner, p.PhotoUrl!);
    }

    private static async Task<HttpResponseMessage> PhotoPageUploadAsync(HttpClient owner, byte[] bytes)
    {
        var token = await AdminApp.GetTokenAsync(owner, "/admin/photo");
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "Photo", "x.jpg");
        return await owner.PostAsync("/admin/photo?handler=Upload", form);
    }

    private static bool Exists(AdminApp app, string? url) => url is not null && File.Exists(Path.Combine(app.UploadsPath, url["/uploads/".Length..]));

    [Fact]
    public async Task ReplacingThePhotoOnThePhotoPage_AfterTheWizard_MovesBothThemePhotos_AndNeitherPointsAtADeletedFile()
    {
        var (app, owner, old) = await PublishedWithPhotoAsync();
        using var _ = app;

        Assert.Equal(HttpStatusCode.Redirect, (await PhotoPageUploadAsync(owner, TestImages.Split(SKEncodedImageFormat.Png, 50, 50))).StatusCode);

        var p = await WizardFlow.ProfileAsync(app);
        Assert.NotEqual(old, p.PhotoUrl);
        Assert.Equal(p.PhotoUrl, p.PhotoUrlLight);
        Assert.False(Exists(app, old));
        Assert.True(Exists(app, p.PhotoUrl));
        Assert.True(Exists(app, p.PhotoUrlLight));
        var home = await app.NewClient().GetStringAsync("/");
        Assert.DoesNotContain(old, home);
        Assert.Equal(HttpStatusCode.OK, (await app.NewClient().GetAsync(p.PhotoUrlLight!)).StatusCode);
    }

    [Fact]
    public async Task RemovingThePhoto_AfterTheWizard_ClearsBothThemePhotos()
    {
        var (app, owner, old) = await PublishedWithPhotoAsync();
        using var _ = app;

        var remove = await WizardFlow.PostAsync(owner, "/admin/photo?handler=Remove", new Dictionary<string, string>(), "/admin/photo");

        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        var p = await WizardFlow.ProfileAsync(app);
        Assert.Null(p.PhotoUrl);
        Assert.Null(p.PhotoUrlLight);
        Assert.False(Exists(app, old));
        var home = await app.NewClient().GetStringAsync("/");
        Assert.DoesNotContain("/uploads/", home);
        Assert.Contains(">JA<", home);
    }

    [Fact]
    public async Task ASeparateLightPhotoTheOwnerChose_IsNotTouchedWhenThePhotoIsReplaced()
    {
        var (app, owner, _) = await PublishedWithPhotoAsync();
        using var _app = app;
        using (var db = app.CreateDbContext())
        {
            (await db.SiteProfiles.SingleAsync()).PhotoUrlLight = "/img/light.jpg";
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();

        await PhotoPageUploadAsync(owner, TestImages.Split(SKEncodedImageFormat.Png, 50, 50));

        Assert.Equal("/img/light.jpg", (await WizardFlow.ProfileAsync(app)).PhotoUrlLight);
    }

    // ---------------------------------------------------------------- re-run and the short name

    [Fact]
    public async Task ReRunning_KeepsAnEditedShortName_UnlessTheNameChanges()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);
        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(name: "Jordan Alexander"));
        await WizardFlow.PublishAsync(owner);
        Assert.Equal("Jordan", (await WizardFlow.ProfileAsync(app)).BrandName);
        using (var db = app.CreateDbContext())
        {
            (await db.SiteProfiles.SingleAsync()).BrandName = "JA";
            await db.SaveChangesAsync();
        }

        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(name: "Jordan Alexander", summary: "A new summary."));
        await WizardFlow.PublishAsync(owner);
        Assert.Equal("JA", (await WizardFlow.ProfileAsync(app)).BrandName);

        await WizardFlow.PickTemplateAsync(owner, "software");
        await WizardFlow.SaveBasicsAsync(owner, WizardFlow.BasicsFields(name: "Sam Rivera"));
        await WizardFlow.PublishAsync(owner);
        var p = await WizardFlow.ProfileAsync(app);
        Assert.Equal("Sam", p.BrandName);
        Assert.Equal("Sam Rivera", p.OwnerName);
        Assert.Equal("Jordan Alexander", p.Hero.Headline); // the hero is only built on a first run; a renamed owner edits it in /admin/profile
    }
}
