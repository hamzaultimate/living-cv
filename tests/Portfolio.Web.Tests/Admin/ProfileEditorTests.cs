using System.Net;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Data.Entities;
using Portfolio.Web.Configuration;
using Portfolio.Web.Security;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class ProfileEditorTests
{
    private sealed class RecordingStore : IOutputCacheStore
    {
        public List<string> EvictedTags { get; } = [];
        public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken) { EvictedTags.Add(tag); return ValueTask.CompletedTask; }
        public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<byte[]?>(null);
        public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class CountingProvider : ISiteProfileProvider
    {
        public int Invalidations { get; private set; }
        public Task<SiteOptions> GetAsync(CancellationToken ct = default) => Task.FromResult(new SiteOptions());
        public void Invalidate() => Invalidations++;
    }

    private static ProfileForm ValidForm(Action<ProfileForm>? edit = null)
    {
        var form = ProfileForm.From(new SiteProfile { OwnerName = "Sam Doe", BrandName = "Sam", Url = "https://sam.example" });
        edit?.Invoke(form);
        return form;
    }

    [Fact]
    public async Task Save_UpdatesTheRow_DropsTheProfileCache_AndEvictsTheGeneratedTag()
    {
        using var test = new TestDatabase();
        var store = new RecordingStore();
        var provider = new CountingProvider();
        using var db = test.CreateContext();
        var editor = new ProfileEditor(db, provider, store);

        await editor.SaveAsync(ValidForm(f => f.OwnerName = "Renamed Person"));

        using var check = test.CreateContext();
        Assert.Equal("Renamed Person", (await check.SiteProfiles.SingleAsync()).OwnerName);
        Assert.Equal(1, provider.Invalidations);
        Assert.Equal([OutputCacheTags.Generated], store.EvictedTags);
        Assert.Equal("Generated", OutputCacheTags.Generated);
    }

    [Fact]
    public async Task Save_LeavesPhotoTemplateAndSectionsAlone()
    {
        using var test = new TestDatabase();
        using (var seed = test.CreateContext())
        {
            seed.SiteProfiles.Add(new SiteProfile
            {
                OwnerName = "A", BrandName = "A", Url = "https://a.example", PhotoUrl = "/uploads/keep.jpg", TemplateKey = "software",
                ThemeKey = "dark", AccentColor = "teal", Sections = [new SectionSetting { Key = "blog", Enabled = false }],
            });
            await seed.SaveChangesAsync();
        }
        using var db = test.CreateContext();

        await new ProfileEditor(db, new CountingProvider(), new RecordingStore()).SaveAsync(ValidForm());

        using var check = test.CreateContext();
        var row = await check.SiteProfiles.SingleAsync();
        Assert.Equal("/uploads/keep.jpg", row.PhotoUrl);
        Assert.Equal("dark", row.ThemeKey);
        Assert.Equal("teal", row.AccentColor);
        Assert.False(Assert.Single(row.Sections).Enabled);
    }

    [Fact]
    public async Task SetPhoto_ReturnsThePreviousUrl_AndInvalidatesCaches()
    {
        using var test = new TestDatabase();
        var store = new RecordingStore();
        var provider = new CountingProvider();
        using var db = test.CreateContext();
        var editor = new ProfileEditor(db, provider, store);

        Assert.Null(await editor.SetPhotoAsync("/uploads/a.jpg"));
        Assert.Equal("/uploads/a.jpg", await editor.SetPhotoAsync(null));

        Assert.Equal(2, provider.Invalidations);
        Assert.Equal(2, store.EvictedTags.Count);
    }

    [Fact]
    public async Task EditingTheProfileThroughTheAdmin_ShowsOnPublicPagesAndRefreshesGeneratedOutputImmediately()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();
        var anonymous = app.NewClient();

        // Warm the cached sitemap, then prove the cache is live: a change that bypasses the editor stays invisible.
        Assert.Contains("https://owner.example/about", await anonymous.GetStringAsync("/sitemap.xml"));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Portfolio.Data.PortfolioDbContext>();
            var row = await db.SiteProfiles.SingleAsync();
            row.Url = "https://sneaky.example";
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<ISiteProfileProvider>().Invalidate();
        }
        Assert.Contains("https://owner.example/about", await anonymous.GetStringAsync("/sitemap.xml"));

        var token = await AdminApp.GetTokenAsync(client, "/admin/profile");
        var response = await AdminApp.PostFormAsync(client, "/admin/profile", token, Fields(f =>
        {
            f["Form.OwnerName"] = "Edited Name";
            f["Form.Url"] = "https://edited.example";
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var sitemap = await anonymous.GetStringAsync("/sitemap.xml");
        Assert.Contains("https://edited.example/about", sitemap);
        Assert.DoesNotContain("owner.example", sitemap);
        Assert.Contains("Edited Name", await anonymous.GetStringAsync("/"));
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/og.png")).StatusCode);
        Assert.Contains("Profile saved", await client.GetStringAsync("/admin"));
    }

    [Fact]
    public async Task Form_ShowsTheStoredValues()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();

        var html = await client.GetStringAsync("/admin/profile");

        Assert.Contains("value=\"Alex Rivera\"", html);
        Assert.Contains("name=\"Form.Metrics[0].Value\"", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
    }

    [Theory]
    [InlineData("Form.LinkedIn", "javascript:alert(1)")]
    [InlineData("Form.GitHub", "data:text/html,<script>alert(1)</script>")]
    [InlineData("Form.YouTube", "/relative/path")]
    [InlineData("Form.Url", "ftp://example.com")]
    [InlineData("Form.Url", "")]
    [InlineData("Form.Url", "https://user:pass@example.com")]
    [InlineData("Form.GoogleAnalyticsId", "G-1');alert(1);//")]
    [InlineData("Form.Email", "not-an-email")]
    [InlineData("Form.SecondaryEmailsText", "ok@example.com\nnope")]
    [InlineData("Form.Phone", "<script>")]
    [InlineData("Form.WhatsApp", "abc")]
    [InlineData("Form.OwnerName", "")]
    public async Task InvalidValues_AreRejected_AndNothingIsSaved(string field, string value)
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();
        var token = await AdminApp.GetTokenAsync(client, "/admin/profile");

        var response = await AdminApp.PostFormAsync(client, "/admin/profile", token, Fields(f => f[field] = value));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Some fields need attention", await response.Content.ReadAsStringAsync());
        using var db = app.CreateDbContext();
        Assert.Equal("Alex Rivera", (await db.SiteProfiles.SingleAsync()).OwnerName);
    }

    [Fact]
    public async Task ColumnLengthLimits_AreEnforced()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();
        var token = await AdminApp.GetTokenAsync(client, "/admin/profile");

        var response = await AdminApp.PostFormAsync(client, "/admin/profile", token, Fields(f =>
            f["Form.OwnerName"] = new string('x', SiteProfile.MaxLength.OwnerName + 1)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var db = app.CreateDbContext();
        Assert.Equal("Alex Rivera", (await db.SiteProfiles.SingleAsync()).OwnerName);
    }

    [Fact]
    public async Task Metrics_NeedAValue_AndBlankRowsAreDropped()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();
        var token = await AdminApp.GetTokenAsync(client, "/admin/profile");

        var bad = await AdminApp.PostFormAsync(client, "/admin/profile", token, Fields(f =>
        {
            f["Form.Metrics[0].Value"] = "";
            f["Form.Metrics[0].Label"] = "orphan label";
        }));
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("A metric needs a value", await bad.Content.ReadAsStringAsync());

        token = await AdminApp.GetTokenAsync(client, "/admin/profile");
        var ok = await AdminApp.PostFormAsync(client, "/admin/profile", token, Fields(f =>
        {
            f["Form.Metrics[0].Value"] = "42";
            f["Form.Metrics[0].Unit"] = "ms";
            f["Form.Metrics[0].Label"] = "latency";
            f["Form.Metrics[1].Value"] = "";
        }));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        using var db = app.CreateDbContext();
        var metric = Assert.Single((await db.SiteProfiles.SingleAsync()).Metrics);
        Assert.Equal("42", metric.Value);
    }

    [Fact]
    public async Task Markup_IsStoredAsTextAndEncodedWhenRendered()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();
        var token = await AdminApp.GetTokenAsync(client, "/admin/profile");

        var response = await AdminApp.PostFormAsync(client, "/admin/profile", token, Fields(f =>
            f["Form.HeroHeadline"] = "<script>alert('x')</script>"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var home = await app.NewClient().GetStringAsync("/");
        Assert.DoesNotContain("<script>alert('x')</script>", home);
        Assert.Contains("&lt;script&gt;alert(", home);
    }

    [Fact]
    public async Task Save_RequiresAnAntiforgeryToken()
    {
        using var app = new AdminApp();
        var client = await app.SignedInClientAsync();

        var response = await AdminApp.PostFormAsync(client, "/admin/profile", token: null, Fields(f => f["Form.OwnerName"] = "Forged"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var db = app.CreateDbContext();
        Assert.NotEqual("Forged", (await db.SiteProfiles.SingleOrDefaultAsync())?.OwnerName);
    }

    [Fact]
    public async Task Save_IsRefusedForAnAnonymousVisitor()
    {
        using var app = new AdminApp();
        await app.SignedInClientAsync();
        var stranger = app.NewClient();

        var response = await AdminApp.PostFormAsync(stranger, "/admin/profile", token: null, Fields(f => f["Form.OwnerName"] = "Intruder"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var db = app.CreateDbContext();
        Assert.NotEqual("Intruder", (await db.SiteProfiles.SingleOrDefaultAsync())?.OwnerName);
    }

    /// <summary>A complete valid form post, with one or more fields overridden.</summary>
    private static Dictionary<string, string> Fields(Action<Dictionary<string, string>> edit)
    {
        var fields = new Dictionary<string, string>
        {
            ["Form.OwnerName"] = "Alex Rivera", ["Form.BrandName"] = "Alex", ["Form.Role"] = "Engineer", ["Form.JobTitle"] = "Engineer",
            ["Form.Url"] = "https://owner.example", ["Form.Summary"] = "Summary text.",
            ["Form.AboutParagraphsText"] = "One.\r\n\r\nTwo.",
        };
        edit(fields);
        return fields;
    }
}
