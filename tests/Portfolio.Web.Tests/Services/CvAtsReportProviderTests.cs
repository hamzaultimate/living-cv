using System.Text.Json;
using Portfolio.Web.Services;
using Xunit;

namespace Portfolio.Web.Tests.Services;

public class CvAtsReportProviderTests
{
    [Fact]
    public void GetReport_ReturnsNull_WhenFileMissing()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var provider = new CvAtsReportProvider(tempDir.FullName);

            Assert.Null(provider.GetReport());
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void GetReport_ReturnsParsedReport_WhenFilePresent()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var report = new CvAtsReport(
                RuleScore: 90,
                AiScore: 80,
                CombinedScore: 85,
                Findings: new[] { new CvAtsFinding("Contact", true, "OK") },
                AiStrengths: new[] { "Clear metrics" },
                AiCritiques: Array.Empty<string>(),
                GeneratedAtUtc: DateTimeOffset.UtcNow);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            File.WriteAllText(Path.Combine(tempDir.FullName, "ats-report.json"), json);

            var provider = new CvAtsReportProvider(tempDir.FullName);
            var loaded = provider.GetReport();

            Assert.NotNull(loaded);
            Assert.Equal(85, loaded!.CombinedScore);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void GetReport_ReturnsNull_WhenJsonMalformed()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDir.FullName, "ats-report.json"), "{ not valid json");

            var provider = new CvAtsReportProvider(tempDir.FullName);
            var result = provider.GetReport();

            Assert.Null(result);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
