using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Data;

/// <summary>The columns the onboarding wizard adds to the profile row, and the public places the new Location shows up.</summary>
public class OnboardingColumnsTests
{
    [Fact]
    public void TheMigration_AddsTheColumns_AndMarksExistingProfilesAsCompleted()
    {
        using var db = new PortfolioDbContextFactory().CreateDbContext([]);

        var script = db.GetService<IMigrator>().GenerateScript();

        Assert.Contains("ADD [Location] nvarchar(120) NULL", script);
        Assert.Contains("ADD [OnboardingCompletedAt] datetimeoffset NULL", script);
        Assert.Contains("ADD [OnboardingDraft] nvarchar(max) NULL", script);
        // The outcome is left empty for existing rows on purpose: only a site that went through the wizard stops being seeded at start-up.
        Assert.Contains("ADD [OnboardingOutcome] nvarchar(20) NULL", script);
        Assert.DoesNotContain("SET [OnboardingOutcome]", script);
        // Rows that exist when the migration runs belong to sites set up before the wizard: they must not be sent into it.
        Assert.Contains("UPDATE [SiteProfiles] SET [OnboardingCompletedAt] =", script);
        Assert.True(script.IndexOf("ADD [OnboardingCompletedAt]", StringComparison.Ordinal) < script.IndexOf("UPDATE [SiteProfiles] SET [OnboardingCompletedAt]", StringComparison.Ordinal));
    }

    [Fact]
    public void ANewProfile_IsNotCompleted_UntilTheWizardSaysSo()
    {
        var profile = new SiteProfile();

        Assert.Null(profile.OnboardingCompletedAt);
        Assert.Null(profile.OnboardingDraft);
        Assert.Null(profile.Location);
    }

    [Fact]
    public void Location_SurvivesTheMapperBothWays_AndIsFittedToItsColumn()
    {
        var entity = new SiteProfile { Location = new string('x', 500) };
        SiteProfileMapper.FitToColumns(entity);

        Assert.Equal(SiteProfile.MaxLength.Location, entity.Location!.Length);
        var options = SiteProfileMapper.ToOptions(new SiteProfile { Location = "Leeds, UK" });
        Assert.Equal("Leeds, UK", options.Location);
        Assert.True(options.HasLocation);
        Assert.Equal("Leeds, UK", SiteProfileMapper.ToEntity(options).Location);
    }

    [Fact]
    public void TheProfileForm_CarriesTheLocation_SoSavingTheProfileNeverWipesIt()
    {
        var profile = new SiteProfile { OwnerName = "Sam", BrandName = "Sam", Url = "https://sam.example", Location = "Leeds" };
        var form = ProfileForm.From(profile);
        form.Summary = "changed";
        form.ApplyTo(profile);

        Assert.Equal("Leeds", profile.Location);
        form.Location = "  Bristol \n";
        form.ApplyTo(profile);
        Assert.Equal("Bristol", profile.Location);
    }

    [Fact]
    public async Task Location_IsShownOnTheContactPageAndInPersonMarkup_OnlyWhenSet()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var client = app.NewClient();

        var without = await client.GetStringAsync("/contact");
        var withoutHome = await client.GetStringAsync("/");
        Assert.DoesNotContain("Based in", without);
        Assert.DoesNotContain("PostalAddress", withoutHome);

        using (var db = app.CreateDbContext())
        {
            (await db.SiteProfiles.SingleAsync()).Location = "Leeds, UK";
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();

        var with = await client.GetStringAsync("/contact");
        var withHome = await client.GetStringAsync("/");
        Assert.Contains("Based in", with);
        Assert.Contains("Leeds, UK", with);
        Assert.Contains("\"addressLocality\":\"Leeds, UK\"", withHome);
    }

    [Fact]
    public async Task ALocationWithMarkup_IsEncodedOnTheContactPage()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        await app.NewClient().GetAsync("/"); // the profile row is created by the first read
        using (var db = app.CreateDbContext())
        {
            (await db.SiteProfiles.SingleAsync()).Location = "<script>alert(1)</script>";
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();

        var html = await app.NewClient().GetStringAsync("/contact");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
    }

    [Fact]
    public async Task TheOnlineCard_IsHidden_WhenThereAreNoLinks_AndStillShownWithLinks()
    {
        using var app = new AdminApp();
        await DemoSite.SeedAsync(app);
        var client = app.NewClient();
        Assert.Contains("Online", await client.GetStringAsync("/contact"));

        using (var db = app.CreateDbContext())
        {
            var p = await db.SiteProfiles.SingleAsync();
            p.LinkedIn = p.GitHub = p.YouTube = null;
            await db.SaveChangesAsync();
        }
        app.Services.GetRequiredService<ISiteProfileProvider>().Invalidate();

        Assert.DoesNotContain(">Online<", await client.GetStringAsync("/contact"));
    }
}
