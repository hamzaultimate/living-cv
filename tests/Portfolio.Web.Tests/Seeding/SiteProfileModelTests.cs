using Microsoft.EntityFrameworkCore;
using Portfolio.Data.Entities;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Seeding;

public class SiteProfileModelTests
{
    [Fact]
    public async Task Profile_RoundTripsOwnedJsonContent()
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.SiteProfiles.Add(new SiteProfile
            {
                OwnerName = "Sam Doe",
                KnowsAbout = { "Nursing", "Triage" },
                Hero = new ProfileHero { Headline = "Hello", Lede = "World" },
                Metrics = { new ProfileMetric { Value = "10", Unit = "yrs", Label = "practice" } },
                About = new ProfileAbout
                {
                    Paragraphs = { "One", "Two" },
                    ValueProps = { new ProfileValueProp { Title = "T", Body = "B" } },
                    Focus = { "Care" },
                },
                Sections = { new SectionSetting { Key = "projects", Enabled = false, SortOrder = 3 } },
            });
            await db.SaveChangesAsync();
        }

        using var read = database.CreateContext();
        var p = await read.SiteProfiles.SingleAsync();
        Assert.Equal(SiteProfile.SingletonId, p.Id);
        Assert.Equal("software", p.TemplateKey);
        Assert.Equal(new[] { "Nursing", "Triage" }, p.KnowsAbout);
        Assert.Equal("World", p.Hero.Lede);
        Assert.Equal("yrs", Assert.Single(p.Metrics).Unit);
        Assert.Equal(new[] { "One", "Two" }, p.About.Paragraphs);
        Assert.Equal("B", Assert.Single(p.About.ValueProps).Body);
        var section = Assert.Single(p.Sections);
        Assert.False(section.Enabled);
        Assert.Equal(3, section.SortOrder);
    }

    [Fact]
    public async Task Profile_RejectsASecondRow()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        db.SiteProfiles.Add(new SiteProfile());
        await db.SaveChangesAsync();

        db.SiteProfiles.Add(new SiteProfile { Id = 2 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
