using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Data.Entities;
using Portfolio.Web.Configuration;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>What <see cref="PortfolioContentService.BuildCvDataAsync"/> hands the two CV renderers: section order, accent and status words.</summary>
public class CvDataTests
{
    private static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "Templates");

    private static (PortfolioContentService Service, TemplateResolver Resolver) Build()
    {
        var resolver = new TemplateResolver(new TemplateCatalog(TemplatesDir));
        var service = new PortfolioContentService(null!, new ConfigurationBuilder().Build(), resolver, NullLogger<PortfolioContentService>.Instance);
        return (service, resolver);
    }

    private static SiteOptions Site(string key, string? accent = null, params SectionOption[] sections) =>
        new() { TemplateKey = key, OwnerName = "Casey Morgan", Role = "Specialist", Summary = "A summary.", AccentColor = accent, Sections = sections.ToList() };

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public async Task TheOrder_IsTheTemplatesOwn(string key)
    {
        var (service, _) = Build();

        var data = await service.BuildCvDataAsync(Site(key));

        Assert.Equal(CvManifestTests.Expected[key].Order, data.SectionOrder);
    }

    [Fact]
    public async Task ADisabledSection_IsLeftOutOfTheOrder_AndTheRestKeepTheirPlaces()
    {
        var (service, _) = Build();

        var data = await service.BuildCvDataAsync(Site("finance-accounting", null, new SectionOption { Key = "certifications", Enabled = false }));

        Assert.Equal(new[] { "experience", "projects", "skills" }, data.SectionOrder);
    }

    [Fact]
    public async Task ATemplateWithoutAnOrder_GetsTodaysDefaultOrder()
    {
        var (service, resolver) = Build();
        var site = Site("software");
        var definition = CvManifestTests.Load("software");
        definition.Copy.Cv.Order = null;
        definition.Copy.Cv.Accent = "";
        var view = new TemplateView(site, definition, resolver.Resolve(site).Sections, isPreview: false);

        var data = await service.BuildCvDataAsync(site, view);

        Assert.Equal(CvSections.Default, data.SectionOrder);
        Assert.Equal(CvSections.DefaultAccent, data.Accent);
    }

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public async Task WithNoPickedAccent_TheAccentIsTheTemplatesCvAccent(string key)
    {
        var (service, _) = Build();

        var data = await service.BuildCvDataAsync(Site(key));

        Assert.Equal(CvManifestTests.Expected[key].Accent, data.Accent);
    }

    [Theory]
    [MemberData(nameof(TemplateOnboardingManifestTests.ShippedKeys), MemberType = typeof(TemplateOnboardingManifestTests))]
    public async Task APickedAccent_GivesItsLightStrongTone_ForEveryOptionOfEveryTemplate(string key)
    {
        var (service, _) = Build();
        var options = CvManifestTests.Load(key).Theme.Accents;
        Assert.NotEmpty(options);

        foreach (var option in options)
        {
            var data = await service.BuildCvDataAsync(Site(key, option.Key));
            Assert.Equal(option.Light.Strong, data.Accent);
        }
    }

    [Theory]
    [InlineData("evil")]
    [InlineData("#ff0000")]
    [InlineData("ROSE")]
    [InlineData("rose;}body{display:none")]
    public async Task AnAccentTheTemplateDoesNotOffer_IsIgnored(string stored)
    {
        var (service, _) = Build();

        var data = await service.BuildCvDataAsync(Site("finance-accounting", stored));

        Assert.Equal("#15375c", data.Accent); // finance-accounting offers none of these, so its own CV accent stands
    }

    [Fact]
    public void ABrokenPickedTone_FallsBackToTheTemplatesAccent_NeverToJunk()
    {
        var definition = CvManifestTests.Load("software");
        definition.Theme.Accents[0].Light.Strong = "red";
        var view = new TemplateView(Site("software", definition.Theme.Accents[0].Key), definition, Array.Empty<ResolvedSection>(), isPreview: false);

        Assert.Equal("#1D4ED8", view.CvAccent);
    }

    [Fact]
    public void TheStatusWords_AreTheTemplatesOwn_AndTheDefaultsAreTheEnumWords()
    {
        var healthcare = new CvHeadings("e", "s", "p", "c", "Current", "In progress", "Planned");
        Assert.Equal("Current", healthcare.StatusWord(CertificationStatus.Earned));
        Assert.Equal("In progress", healthcare.StatusWord(CertificationStatus.InProgress));
        Assert.Equal("Planned", healthcare.StatusWord(CertificationStatus.Planned));
        Assert.Equal("In progress", CvHeadings.Default.StatusWord(CertificationStatus.InProgress));
    }
}
