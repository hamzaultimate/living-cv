using System.Net;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class OnboardingSmokeTests
{
    [Fact]
    public async Task FirstRun_Healthcare_EndToEnd()
    {
        using var app = new AdminApp();
        var owner = await WizardFlow.FirstRunClientAsync(app);

        var step1 = await owner.GetAsync("/admin/welcome");
        Assert.Equal(HttpStatusCode.OK, step1.StatusCode);
        var pick = await WizardFlow.PickTemplateAsync(owner, "healthcare");
        Assert.Equal("/admin/welcome/basics", WizardFlow.Where(pick));
        var basics = await WizardFlow.SaveBasicsAsync(owner);
        Assert.Equal("/admin/welcome/photo", WizardFlow.Where(basics));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/admin/welcome/photo")).StatusCode);
        var review = await owner.GetAsync("/admin/welcome/review");
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var publish = await WizardFlow.PublishAsync(owner);
        Assert.Equal(HttpStatusCode.Redirect, publish.StatusCode);
        var home = await app.NewClient().GetStringAsync("/");
        Assert.Contains("Jordan Alexander", home);
        Assert.Contains("data-template=\"healthcare\"", home);
    }
}
