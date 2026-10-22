using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Web.Configuration;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>A new public page cannot skip the section gate unnoticed, and the resolver applies the owner's settings as documented.</summary>
public class SectionGateTests : IClassFixture<AdminApp>
{
    // Pages that are not the face of a section. Adding a public page means either marking it [SectionPage] or listing it here on purpose.
    private static readonly string[] NotSectionPages =
        ["/Pages/Index.cshtml", "/Pages/Cv.cshtml", "/Pages/Error.cshtml", "/Pages/NotFound.cshtml", "/Pages/Privacy.cshtml", "/Pages/Setup.cshtml"];

    private readonly AdminApp _app;

    public SectionGateTests(AdminApp app) => _app = app;

    [Fact]
    public void EveryPublicPage_IsEitherGatedByASection_OrListedAsNotASection()
    {
        var pages = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Select(e => e.Metadata.GetMetadata<PageActionDescriptor>())
            .Where(d => d is not null && !d.RelativePath.StartsWith("/Pages/Admin/", StringComparison.OrdinalIgnoreCase))
            .Select(d => d!)
            .DistinctBy(d => d.RelativePath)
            .ToList();
        Assert.True(pages.Count >= 12, "Expected the public pages to be enumerated.");

        foreach (var page in pages)
        {
            var gate = page.EndpointMetadata.OfType<SectionPageAttribute>().FirstOrDefault();
            if (NotSectionPages.Contains(page.RelativePath))
                Assert.True(gate is null, $"{page.RelativePath} is listed as not a section page but is gated.");
            else
            {
                Assert.True(gate is not null, $"{page.RelativePath} is a public page without [SectionPage]. Mark it, or list it as not a section page.");
                Assert.True(SectionRegistry.IsKnown(gate!.Section), $"{page.RelativePath} names an unknown section '{gate.Section}'.");
            }
        }

        // And every section that owns a route is the face of at least one page.
        var gated = pages.SelectMany(p => p.EndpointMetadata.OfType<SectionPageAttribute>()).Select(a => a.Section).ToHashSet();
        foreach (var key in SectionRegistry.Keys.Where(k => SectionRegistry.PathFor(k) is not null))
            Assert.Contains(key, gated);
    }

    [Fact]
    public void EveryRouteOfASection_MatchesThePageThatServesIt()
    {
        var routes = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<SectionPageAttribute>() is not null)
            .ToDictionary(e => e.RoutePattern.RawText!.TrimStart('/'), e => e.Metadata.GetMetadata<SectionPageAttribute>()!.Section);

        foreach (var key in SectionRegistry.Keys)
        {
            if (SectionRegistry.PathFor(key) is not { } path) continue;
            Assert.Equal(key, routes[path.TrimStart('/')]);
        }
    }

    private static TemplateView Resolve(SiteOptions site, string? preview = null) =>
        new TemplateResolver(new TemplateCatalog(Path.Combine(AppContext.BaseDirectory, "Templates"))).Resolve(site, preview);

    [Fact]
    public void Resolver_UsesTheSavedTemplate_AndAPreviewOnlyWhenItNamesACatalogEntry()
    {
        var site = new SiteOptions { TemplateKey = "healthcare" };

        Assert.Equal("healthcare", Resolve(site).Key);
        Assert.False(Resolve(site).IsPreview);
        Assert.Equal("software", Resolve(site, "software").Key);
        Assert.True(Resolve(site, "software").IsPreview);
        Assert.Equal("healthcare", Resolve(site, "../software").Key);
        Assert.False(Resolve(site, "../software").IsPreview);
        Assert.Equal("software", Resolve(new SiteOptions { TemplateKey = "bogus" }).Key);
    }

    [Fact]
    public void Resolver_AppliesLabelAndHiddenSettings_ButNeverEnablesWhatTheTemplateLacks()
    {
        var site = new SiteOptions
        {
            TemplateKey = "healthcare",
            Sections =
            [
                new SectionOption { Key = "skills", Label = "abilities", Enabled = true },
                new SectionOption { Key = "experience", Enabled = false },
                new SectionOption { Key = "blog", Enabled = true },
                new SectionOption { Key = "unknown-section", Enabled = true },
            ],
        };

        var view = Resolve(site);

        Assert.Equal("abilities", view.Section("skills").Label);
        Assert.Equal("abilities", view.Section("skills").ShortLabel);
        Assert.False(view.IsEnabled("experience"));
        Assert.False(view.IsEnabled("blog"));
        Assert.False(view.IsEnabled("unknown-section"));
        Assert.True(view.IsEnabled("certifications"));
        Assert.DoesNotContain(view.NavLinks(mobile: true), l => l.Path == "/experience" || l.Path == "/blog");
    }

    [Fact]
    public void NavLinks_AreSectionsThenCv_WithContactOnlyInTheMobileMenu()
    {
        var view = Resolve(new SiteOptions());

        Assert.Equal(new[] { "/about", "/experience", "/projects", "/skills", "/certifications", "/blog", "/talks", "/cv" }, view.NavLinks(false).Select(l => l.Path));
        Assert.Equal(new[] { "/about", "/experience", "/projects", "/skills", "/certifications", "/blog", "/talks", "/cv", "/contact" }, view.NavLinks(true).Select(l => l.Path));
        Assert.Equal("certs", view.NavLinks(false).Single(l => l.Path == "/certifications").Label);
        Assert.Equal("certifications", view.NavLinks(true).Single(l => l.Path == "/certifications").Label);
    }

    [Fact]
    public void TemplateText_EscapesMarkupButLeavesReadableCharactersAlone()
    {
        Assert.Equal("Let's talk — a &amp; b &lt;i&gt; &quot;x&quot; →", TemplateView.Text("Let's talk — a & b <i> \"x\" →").ToString());
    }
}
