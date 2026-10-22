using System.Net;
using Microsoft.EntityFrameworkCore;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

public class ExperienceEditorTests
{
    [Fact]
    public async Task CreatingARole_Persists_AndShowsOnThePublicPage()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);

        var create = await ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields());

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        using var db = app.CreateDbContext();
        var row = await db.Experiences.SingleAsync();
        Assert.Equal("Staff Nurse", row.Title);
        Assert.NotNull(row.ConcurrencyStamp);
        Assert.Contains("Staff Nurse", await app.NewClient().GetStringAsync("/experience"));
    }
}
