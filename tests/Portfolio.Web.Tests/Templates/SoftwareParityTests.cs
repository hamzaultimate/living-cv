using System.Net;
using Portfolio.Web.Tests.Support;
using Xunit;

namespace Portfolio.Web.Tests.Templates;

/// <summary>
/// The software template must keep rendering exactly what it did before the template engine existed. The snapshots under
/// Snapshots/software were captured from the demo persona on the code that preceded it; only tokens, nonces, asset hashes and
/// the copyright year are normalised. Set UPDATE_SNAPSHOTS=1 to rewrite them after an intentional change.
/// </summary>
public class SoftwareParityTests : IClassFixture<AdminApp>
{
    private readonly AdminApp _app;

    public SoftwareParityTests(AdminApp app) => _app = app;

    private static string SnapshotDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Portfolio.Web.Tests.csproj"))) dir = dir.Parent;
            return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Test project folder not found."), "Snapshots", "software");
        }
    }

    [Fact]
    public async Task DemoPersona_RendersByteForByteLikeTheSnapshots()
    {
        await DemoSite.SeedAsync(_app);
        var (project, post) = DemoSite.FirstSlugs(_app);
        var update = Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1";
        var client = _app.NewClient();
        var mismatches = new List<string>();

        foreach (var (file, pathPattern) in DemoSite.SnapshotPages)
        {
            var path = pathPattern.Replace("{project}", project).Replace("{post}", post);
            var response = await client.GetAsync(path);
            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound, $"{path} returned {(int)response.StatusCode}");
            var actual = DemoSite.Normalise(await response.Content.ReadAsStringAsync());
            Assert.True(actual.Length > 200, $"{path} rendered almost nothing");

            var golden = Path.Combine(SnapshotDir, file);
            if (update)
            {
                Directory.CreateDirectory(SnapshotDir);
                File.WriteAllText(golden, actual);
            }
            else if (!File.Exists(golden) || DemoSite.Normalise(File.ReadAllText(golden)) != actual)
            {
                mismatches.Add($"{path} differs from Snapshots/software/{file}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public async Task Snapshots_AreMeaningful_NotEmptyShells()
    {
        await DemoSite.SeedAsync(_app);
        var html = await _app.NewClient().GetStringAsync("/");

        Assert.Contains("Case studies", html);
        Assert.Contains("Alex Rivera", html);
        Assert.Contains("<article", html);
    }
}
