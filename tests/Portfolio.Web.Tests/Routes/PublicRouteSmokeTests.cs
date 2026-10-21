using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Portfolio.Web.Tests.Routes;

/// <summary>Boots the real app with no database configured and requests every public route.</summary>
public class PublicRouteSmokeTests : IClassFixture<PublicRouteSmokeTests.NoDatabaseFactory>
{
    public class NoDatabaseFactory : WebApplicationFactory<Program>
    {
        public NoDatabaseFactory()
        {
            // The PDF and OG layouts name Arial; hosts without it (e.g. Linux CI images) should
            // fall back to another font rather than fail the render.
            QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "" }));
    }

    private readonly NoDatabaseFactory _factory;

    public PublicRouteSmokeTests(NoDatabaseFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/", "text/html")]
    [InlineData("/about", "text/html")]
    [InlineData("/experience", "text/html")]
    [InlineData("/projects", "text/html")]
    [InlineData("/skills", "text/html")]
    [InlineData("/certifications", "text/html")]
    [InlineData("/blog", "text/html")]
    [InlineData("/talks", "text/html")]
    [InlineData("/cv", "text/html")]
    [InlineData("/contact", "text/html")]
    [InlineData("/cv.pdf", "application/pdf")]
    [InlineData("/og.png", "image/png")]
    [InlineData("/sitemap.xml", "application/xml")]
    [InlineData("/robots.txt", "text/plain")]
    [InlineData("/health", "text/plain")]
    public async Task Route_Returns200(string path, string contentType)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Home_RendersTheConfiguredIdentityAndSoftwarePresetCopy()
    {
        var client = _factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/"));

        Assert.Contains("<title>Alex Rivera — Senior Software Engineer, .NET & AI</title>", html);
        Assert.Contains(
            "content=\"Alex Rivera is a Senior Software Engineer with 9+ years building high-performance .NET systems, now building agentic AI products in production.\"",
            html);
    }

    [Fact]
    public async Task About_RendersThePresetDescription()
    {
        var client = _factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/about"));

        Assert.Contains(
            "content=\"Alex Rivera — Senior Software Engineer with 9+ years building reliable, high-performance .NET systems, now bringing applied AI into production.\"",
            html);
    }
}
