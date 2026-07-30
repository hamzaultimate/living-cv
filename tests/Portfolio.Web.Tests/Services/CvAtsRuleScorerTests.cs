using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Xunit;

namespace Portfolio.Web.Tests.Services;

public class CvAtsRuleScorerTests
{
    private static CvData FullMarksCvData()
    {
        var experience = new List<Experience>
        {
            new()
            {
                Company = "Acme Corp",
                Title = "Senior Engineer",
                Start = "Jan 2023",
                End = null,
                Current = true,
                Summary = "Led backend platform work.",
                Highlights =
                {
                    "Cut p95 latency by 40% across the payments service",
                    "Scaled the platform to 20k requests per second",
                },
            },
        };
        var skills = new List<Skill>
        {
            new() { Name = "C#", Group = "Backend", SortOrder = 1 },
            new() { Name = "Azure", Group = "Cloud", SortOrder = 1 },
        };
        var projects = new List<Project>
        {
            new()
            {
                Title = "Payments Platform",
                Slug = "payments-platform",
                Summary = "Rebuilt the core payments pipeline for reliability.",
                Impact = { "Reduced failed transactions by 25%" },
            },
        };
        var certifications = new List<Certification>
        {
            new() { Name = "Azure Fundamentals", Issuer = "Microsoft" },
        };

        return new CvData(
            "Test Person",
            "Senior Engineer",
            "A senior engineer with a decade of experience building reliable backend systems at scale.",
            new[] { "test@example.com", "+1 555 0100" },
            experience,
            skills.GroupBy(s => s.Group).ToList(),
            projects,
            certifications);
    }

    [Fact]
    public void FullMarksCvData_ScoresOneHundred()
    {
        var report = CvAtsRuleScorer.Score(FullMarksCvData());

        Assert.Equal(100, report.Score);
        Assert.All(report.Findings, f => Assert.True(f.Passed));
    }

    [Fact]
    public void MissingSecondContact_FailsContactCheck()
    {
        var data = FullMarksCvData() with { Contacts = new[] { "test@example.com" } };

        var report = CvAtsRuleScorer.Score(data);

        var contact = Assert.Single(report.Findings, f => f.Category == "Contact");
        Assert.False(contact.Passed);
        Assert.Equal(85, report.Score);
    }

    [Fact]
    public void UnquantifiedHighlights_FailImpactCheck()
    {
        var experience = new List<Experience>
        {
            new()
            {
                Company = "Acme Corp",
                Title = "Senior Engineer",
                Start = "Jan 2023",
                Current = true,
                Summary = "Led backend platform work.",
                Highlights = { "Helped improve the payments service", "Worked closely with the platform team" },
            },
        };
        var data = FullMarksCvData() with { Experience = experience, Projects = Array.Empty<Project>() };

        var report = CvAtsRuleScorer.Score(data);

        var impact = Assert.Single(report.Findings, f => f.Category == "Impact");
        Assert.False(impact.Passed);
    }

    [Fact]
    public void InconsistentDateFormats_FailDatesCheck()
    {
        var experience = new List<Experience>
        {
            new() { Company = "Acme Corp", Title = "Senior Engineer", Start = "Jan 2023", Current = true, Summary = "s", Highlights = { "Grew revenue by 10%" } },
            new() { Company = "Old Co", Title = "Engineer", Start = "2019", End = "2022", Current = false, Summary = "s", Highlights = { "Shipped 5 releases" } },
        };
        var data = FullMarksCvData() with { Experience = experience };

        var report = CvAtsRuleScorer.Score(data);

        var dates = Assert.Single(report.Findings, f => f.Category == "Dates");
        Assert.False(dates.Passed);
    }
}
