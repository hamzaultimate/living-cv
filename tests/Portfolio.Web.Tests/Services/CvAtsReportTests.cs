using System.Text.Json;
using Portfolio.Web.Services;
using Xunit;

namespace Portfolio.Web.Tests.Services;

public class CvAtsReportTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void RoundTripsThroughJson()
    {
        var report = new CvAtsReport(
            RuleScore: 82,
            AiScore: 74,
            CombinedScore: 78,
            Findings: new[] { new CvAtsFinding("Contact", true, "Email and phone present.") },
            AiStrengths: new[] { "Strong quantified metrics in recent roles." },
            AiCritiques: new[] { "Summary reads generic." },
            GeneratedAtUtc: new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(report, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<CvAtsReport>(json, JsonOptions);
        var roundTrippedJson = JsonSerializer.Serialize(roundTripped, JsonOptions);

        Assert.Equal(json, roundTrippedJson);
        Assert.NotNull(roundTripped);
        Assert.Equal(82, roundTripped!.RuleScore);
        Assert.Equal("Email and phone present.", roundTripped.Findings[0].Message);
    }

    [Fact]
    public void RoundTripsThroughJsonWithNullAiScore()
    {
        var report = new CvAtsReport(
            RuleScore: 82,
            AiScore: null,
            CombinedScore: 82,
            Findings: new[] { new CvAtsFinding("Contact", true, "Email and phone present.") },
            AiStrengths: Array.Empty<string>(),
            AiCritiques: Array.Empty<string>(),
            GeneratedAtUtc: new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(report, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<CvAtsReport>(json, JsonOptions);
        var roundTrippedJson = JsonSerializer.Serialize(roundTripped, JsonOptions);

        Assert.Equal(json, roundTrippedJson);
        Assert.NotNull(roundTripped);
        Assert.Null(roundTripped!.AiScore);
    }
}
