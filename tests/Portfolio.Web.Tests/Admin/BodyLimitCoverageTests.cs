using System.Reflection;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Portfolio.Web.Security;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>Every admin page that accepts a POST caps its body before anything reads it, so a new editor cannot forget the cap.</summary>
public class BodyLimitCoverageTests
{
    [Fact]
    public void EveryAdminPageModelWithAPostHandler_CarriesABodyLimit()
    {
        var models = typeof(Program).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(PageModel).IsAssignableFrom(t) && t.Namespace is { } ns && ns.StartsWith("Portfolio.Web.Pages.Admin"))
            .ToList();
        Assert.True(models.Count > 20, "the admin page models were not found");

        var withPost = models.Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance).Any(m => m.Name.StartsWith("OnPost", StringComparison.Ordinal))).ToList();
        Assert.Contains(withPost, t => t.FullName!.EndsWith("Talks.EditModel"));
        Assert.Contains(withPost, t => t.FullName!.EndsWith("Testimonials.EditModel"));
        Assert.Contains(withPost, t => t.FullName!.EndsWith("Blog.EditModel"));
        var missing = withPost.Where(t => t.GetCustomAttribute<BodyLimitAttribute>(inherit: true) is null).Select(t => t.FullName).ToList();

        Assert.Empty(missing);
    }
}
