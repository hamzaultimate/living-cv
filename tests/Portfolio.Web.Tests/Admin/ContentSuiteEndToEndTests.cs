using System.Net;
using Microsoft.EntityFrameworkCore;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>
/// The whole editing story on the real app, for both shipped templates: a wizard-finished blank site, a talk, a testimonial, a published post
/// and a draft, then what the public pages, the sitemap and the JSON-LD show, then unpublish and delete. Healthcare keeps Talks and the Blog off.
/// </summary>
public class ContentSuiteEndToEndTests
{
    private static async Task<int> IdOfAsync(AdminApp app, string table, string title)
    {
        using var db = app.CreateDbContext();
        return table switch
        {
            "talk" => await db.Talks.Where(t => t.Title == title).Select(t => t.Id).SingleAsync(),
            "word" => await db.Testimonials.Where(t => t.Author == title).Select(t => t.Id).SingleAsync(),
            _ => await db.BlogPosts.Where(t => t.Title == title).Select(t => t.Id).SingleAsync(),
        };
    }

    private static async Task RemoveAsync(HttpClient owner, string section, int id)
    {
        var path = $"{section}/delete/{id}";
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, path, new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, path) })).StatusCode);
    }

    [Theory]
    [InlineData("software", true)]
    [InlineData("healthcare", false)]
    public async Task ATalkATestimonialAPublishedPostAndADraft_ShowOnlyWhatTheyShould_AndGoWhenUnpublishedOrRemoved(string template, bool talksAndBlogOn)
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app, template);
        var anyone = app.NewClient();
        await anyone.GetStringAsync("/sitemap.xml"); // primes the cached sitemap

        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, ContentFlow.Talks + "/edit",
            ContentFlow.Fields(("Title", "Quartz talk"), ("Date", "2025-05-14"), ("YoutubeId", "aqz-KE-bpKQ")))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, ContentFlow.Testimonials + "/edit",
            ContentFlow.Fields(("Quote", "Granite praise that is long enough to be a quote."), ("Author", "Pat Granite")))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, ContentFlow.Blog + "/edit",
            ContentFlow.Fields(("Title", "Marble post"), ("Date", "2025-06-01"), ("Body", "Marble **body**."), ("Draft", "false"), ("Description", "Marble description.")))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, ContentFlow.Blog + "/edit",
            ContentFlow.Fields(("Title", "Basalt draft"), ("Date", "2025-06-02"), ("Body", "Basalt secret."), ("Draft", "true")))).StatusCode);

        // The content exists and the admin lists it, whatever the template shows publicly.
        Assert.Contains("Quartz talk", await owner.GetStringAsync(ContentFlow.Talks));
        Assert.Contains("Pat Granite", await owner.GetStringAsync(ContentFlow.Testimonials));
        var adminBlog = await owner.GetStringAsync(ContentFlow.Blog);
        Assert.Contains("Marble post", adminBlog);
        Assert.Contains("Basalt draft", adminBlog);

        var sitemap = await anyone.GetStringAsync("/sitemap.xml");
        var home = await anyone.GetStringAsync("/");
        Assert.Contains("Granite praise", home);        // testimonials are on in both templates
        Assert.DoesNotContain("Basalt", home);
        Assert.DoesNotContain("basalt", sitemap);
        Assert.DoesNotContain("Basalt", await (await anyone.GetAsync("/blog")).Content.ReadAsStringAsync()); // a 404 page too
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync("/blog/basalt-draft")).StatusCode);

        if (talksAndBlogOn)
        {
            var talks = await anyone.GetStringAsync("/talks");
            Assert.Contains("Quartz talk", talks);
            Assert.Contains("youtube-nocookie.com/embed/aqz-KE-bpKQ", talks);
            Assert.Contains("Marble post", await anyone.GetStringAsync("/blog"));
            var post = await anyone.GetAsync("/blog/marble-post");
            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
            var postHtml = await post.Content.ReadAsStringAsync();
            Assert.Contains("\"@type\":\"BlogPosting\"", postHtml);
            Assert.Contains("\"headline\":\"Marble post\"", postHtml);
            Assert.DoesNotContain("Basalt", postHtml);
            Assert.Contains("/blog/marble-post", sitemap);
            Assert.Contains("<lastmod>2025-06-01</lastmod>", sitemap);
        }
        else
        {
            // Off in this template: the pages answer not found, the sitemap leaves them out, the content is only hidden.
            foreach (var path in new[] { "/talks", "/blog", "/blog/marble-post", "/blog/basalt-draft" })
                Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync(path)).StatusCode);
            Assert.DoesNotContain("/blog", sitemap);
            Assert.DoesNotContain("/talks", sitemap);
            Assert.DoesNotContain("marble-post", sitemap);
            Assert.DoesNotContain("Quartz talk", home);
            Assert.Contains("does not show this section on the public site", await owner.GetStringAsync(ContentFlow.Talks));
            Assert.Contains("does not show this section on the public site", await owner.GetStringAsync(ContentFlow.Blog));
        }

        // Unpublish the post, delete the talk and the testimonial, delete the draft: gone at once.
        var postId = await IdOfAsync(app, "post", "Marble post");
        var edit = $"{ContentFlow.Blog}/edit/{postId}";
        var fields = ContentFlow.Fields(("Title", "Marble post"), ("Slug", "marble-post"), ("Date", "2025-06-01"), ("Body", "Marble **body**."), ("Draft", "true"));
        fields["Form.Stamp"] = await ContentFlow.StampAsync(owner, edit);
        Assert.Equal(HttpStatusCode.Redirect, (await ContentFlow.PostAsync(owner, edit, fields)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync("/blog/marble-post")).StatusCode);
        Assert.DoesNotContain("marble-post", await anyone.GetStringAsync("/sitemap.xml"));
        if (talksAndBlogOn) Assert.DoesNotContain("Marble post", await anyone.GetStringAsync("/blog"));

        await RemoveAsync(owner, ContentFlow.Talks, await IdOfAsync(app, "talk", "Quartz talk"));
        await RemoveAsync(owner, ContentFlow.Testimonials, await IdOfAsync(app, "word", "Pat Granite"));
        await RemoveAsync(owner, ContentFlow.Blog, await IdOfAsync(app, "post", "Basalt draft"));
        await RemoveAsync(owner, ContentFlow.Blog, postId);

        Assert.DoesNotContain("Granite praise", await anyone.GetStringAsync("/"));
        if (talksAndBlogOn) Assert.DoesNotContain("Quartz talk", await anyone.GetStringAsync("/talks"));
        Assert.Equal(0, await ContentFlow.CountAsync(app));
    }
}
