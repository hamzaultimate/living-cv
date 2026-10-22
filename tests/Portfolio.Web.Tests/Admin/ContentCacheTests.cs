using Microsoft.Extensions.DependencyInjection;
using Portfolio.Web.Services;
using Portfolio.Web.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Portfolio.Web.Tests.Admin;

/// <summary>Every successful content write moves the profile version on (the key of the "Generated" output cache) and a refused one does not.</summary>
public class ContentCacheTests
{
    private static int Version(AdminApp app) => app.Services.GetRequiredService<ISiteProfileProvider>().Version;

    [Fact]
    public async Task EveryKindOfWrite_MovesTheVersion_AndEveryRefusedOneLeavesItAlone()
    {
        using var app = new AdminApp();
        var owner = await ContentFlow.BlankSiteAsync(app);
        var step = Version(app);

        async Task Changed(string what, Func<Task> act)
        {
            await act();
            var now = Version(app);
            Assert.True(now > step, $"{what} did not refresh the caches");
            step = now;
        }
        async Task Unchanged(string what, Func<Task> act)
        {
            await act();
            Assert.True(Version(app) == step, $"{what} refreshed the caches although nothing was written");
        }

        await Changed("creating a role", () => ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields("A Co", "A")));
        await Changed("creating a second role", () => ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields("B Co", "B")));
        var ids = await ContentFlow.IdsAsync(app, db => db.Experiences.Select(e => e.Id));
        await Changed("reordering", () => ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}?handler=Move&id={ids[0]}", new Dictionary<string, string> { ["direction"] = "down" }, ContentFlow.Experience));
        await Unchanged("moving past the end", () => ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}?handler=Move&id={ids[0]}", new Dictionary<string, string> { ["direction"] = "down" }, ContentFlow.Experience));
        await Unchanged("an invalid role", () => ContentFlow.PostAsync(owner, ContentFlow.Experience + "/edit", ContentFlow.RoleFields(company: "")));
        await Changed("editing a role", async () =>
        {
            var fields = ContentFlow.RoleFields("A Co", "A2");
            fields["Form.Stamp"] = await ContentFlow.StampAsync(owner, $"{ContentFlow.Experience}/edit/{ids[0]}");
            await ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}/edit/{ids[0]}", fields);
        });
        await Unchanged("a stale edit", () => ContentFlow.PostAsync(owner, $"{ContentFlow.Experience}/edit/{ids[0]}", ContentFlow.RoleFields("A Co", "Stale")));
        await Changed("adding a skill", () => ContentFlow.PostAsync(owner, ContentFlow.Skills + "/edit", ContentFlow.Fields(("Name", "S"), ("Group", "G"))));
        await Changed("adding a certification", () => ContentFlow.PostAsync(owner, ContentFlow.Certifications + "/edit", ContentFlow.Fields(("Name", "C"), ("Issuer", "I"), ("Status", "Planned"))));
        await Changed("adding a project", () => ContentFlow.PostAsync(owner, ContentFlow.Projects + "/edit", ContentFlow.Fields(("Title", "P"), ("Summary", "S"))));
        var projectId = (await ContentFlow.IdsAsync(app, db => db.Projects.Select(p => p.Id))).Single();
        await Changed("adding a gallery image", async () =>
        {
            var path = $"{ContentFlow.Projects}/images/{projectId}";
            await ContentFlow.PostMultipartAsync(owner, path + "?handler=Upload", new Dictionary<string, string> { ["stamp"] = await ContentFlow.StampAsync(owner, path), ["Caption"] = "c" },
                ("Image", TestImages.Split(SKEncodedImageFormat.Jpeg, 80, 60), "a.jpg", "image/jpeg"), path);
        });
        await Changed("removing a role", async () =>
        {
            var path = $"{ContentFlow.Experience}/delete/{ids[1]}";
            await ContentFlow.PostAsync(owner, path, new Dictionary<string, string> { ["Stamp"] = await ContentFlow.StampAsync(owner, path) });
        });
        await Unchanged("viewing every page", async () =>
        {
            foreach (var path in new[] { "/admin/content", ContentFlow.Experience, ContentFlow.Skills, ContentFlow.Projects, ContentFlow.Certifications, $"{ContentFlow.Projects}/images/{projectId}" })
                await owner.GetStringAsync(path);
        });
    }
}
