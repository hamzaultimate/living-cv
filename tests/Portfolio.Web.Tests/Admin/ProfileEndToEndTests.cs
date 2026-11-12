using System.Net;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>
/// The whole profile editing story on the real app, for both shipped templates: a wizard-finished blank site, then the hero, the metrics and the About page edited from the
/// admin, what the public pages and the generated output show at once, and that switching template or editing the identity never loses or overwrites any of it.
/// </summary>
public class ProfileEndToEndTests
{
    [Theory]
    [InlineData("software")]
    [InlineData("healthcare")]
    public async Task ABlankSite_GetsItsHeroMetricsAndAboutFromTheAdmin_AndEverySurfaceFollowsAtOnce(string template)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, template);
        var anyone = app.NewClient();
        var sitemapBefore = await anyone.GetStringAsync("/sitemap.xml");
        var ogBefore = await anyone.GetByteArrayAsync("/og.png"); // both generated outputs are cached now

        // What the wizard published: a plain hero from the owner's answers, no metrics, the summary as the only About paragraph and no headline yet.
        var home = await anyone.GetStringAsync("/");
        Assert.Contains("Casey Morgan", home);
        Assert.DoesNotContain("metric-value", home);
        var p = await ProfileFlow.ProfileAsync(app);
        Assert.Empty(p.Metrics);
        Assert.Null(p.About.Headline);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/admin")).StatusCode); // takes the leftover wizard message

        // The dashboard offers all three, then each is edited.
        var dashboard = await owner.GetStringAsync("/admin");
        foreach (var path in ProfileFlow.Parts) Assert.Contains($"href=\"{path}\"", dashboard);

        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Hero, ProfileFlow.HeroFields("Quartz eyebrow", "Granite headline", "basalt accent", "Marble lede."))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, ProfileFlow.MetricsFields(("12", "yrs", "Quartz years"), ("340", "", "Basalt cases")))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields("Granite about", "Quartz paragraph.\n\nBasalt paragraph.", "Marble note", "Quartz focus", ("Basalt card", "Marble card text.")))).StatusCode);

        home = await anyone.GetStringAsync("/");
        foreach (var text in new[] { "Quartz eyebrow", "Granite headline", "Marble lede.", "Quartz years", "Basalt cases", "340" })
            Assert.Contains(text, home);
        // Healthcare does not use the highlighted headline part or the unit, so what the post carried for them was neither read nor stored.
        Assert.Equal(template == "software", home.Contains("basalt accent"));
        Assert.Equal(template == "software", home.Contains("yrs"));
        var about = await anyone.GetStringAsync("/about");
        foreach (var text in new[] { "Granite about", "Quartz paragraph.", "Basalt paragraph.", "Marble note", "Quartz focus", "Basalt card", "Marble card text." })
            Assert.Contains(text, about);
        Assert.NotEqual(ogBefore, await anyone.GetByteArrayAsync("/og.png")); // the share image follows the hero without a restart
        Assert.Equal(sitemapBefore, await anyone.GetStringAsync("/sitemap.xml")); // the page list did not change, and the cached sitemap is still right
        Assert.Equal(HttpStatusCode.OK, (await anyone.GetAsync("/cv.pdf")).StatusCode);

        // The identity page saves the identity and nothing of the three.
        var stamp = await ProfileFlow.StampAsync(owner, ProfileFlow.Identity);
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Identity, new Dictionary<string, string>
            { ["Form.OwnerName"] = "Casey R. Morgan", ["Form.BrandName"] = "Casey", ["Form.Url"] = "https://casey.example" }, stamp)).StatusCode);
        var after = await ProfileFlow.ProfileAsync(app);
        Assert.Equal("Casey R. Morgan", after.OwnerName);
        Assert.Equal("Granite headline", after.Hero.Headline);
        Assert.Equal(2, after.Metrics.Count);
        Assert.Equal("Granite about", after.About.Headline);
        Assert.Contains("Casey R. Morgan", await anyone.GetStringAsync("/"));
        Assert.Contains("Granite headline", await anyone.GetStringAsync("/"));

        // Switching template and back keeps all of it: a template hides and relabels, it never deletes.
        var other = template == "software" ? "healthcare" : "software";
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, "/admin/template", new Dictionary<string, string> { ["Key"] = other })).StatusCode);
        Assert.Contains("Granite headline", await anyone.GetStringAsync("/"));
        Assert.Contains("Quartz paragraph.", await anyone.GetStringAsync("/about"));
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, "/admin/template", new Dictionary<string, string> { ["Key"] = template })).StatusCode);
        var finalRow = await ProfileFlow.ProfileAsync(app);
        Assert.Equal(("Granite headline", 2, "Granite about"), (finalRow.Hero.Headline, finalRow.Metrics.Count, finalRow.About.Headline));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(ProfileFlow.Hero)).StatusCode);
    }

    [Fact]
    public async Task ClearingTheMetricsAndTheCards_RemovesThemFromThePublicPages_AndStoresNothingBehind()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var anyone = app.NewClient();
        await ProfileFlow.SeedTextAsync(app);
        Assert.Contains("Second title", await anyone.GetStringAsync("/about"));

        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.Metrics, ProfileFlow.MetricsFields(("", "", ""), ("", "", "")))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await ProfileFlow.PostAsync(owner, ProfileFlow.About, ProfileFlow.AboutFields(paragraphs: "", note: "", focus: ""))).StatusCode);

        var p = await ProfileFlow.ProfileAsync(app);
        Assert.Empty(p.Metrics);
        Assert.Empty(p.About.ValueProps);
        Assert.Empty(p.About.Paragraphs);
        Assert.Empty(p.About.Focus);
        Assert.Null(p.About.CurrentlyNote);
        var about = await anyone.GetStringAsync("/about");
        Assert.DoesNotContain("Second title", about);
        Assert.DoesNotContain("Seed note", about);
    }
}
