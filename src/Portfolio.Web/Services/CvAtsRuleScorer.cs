using System.Text.RegularExpressions;

namespace Portfolio.Web.Services;

public record CvAtsFinding(string Category, bool Passed, string Message);

public record CvAtsRuleReport(int Score, IReadOnlyList<CvAtsFinding> Findings);

public static partial class CvAtsRuleScorer
{
    public static CvAtsRuleReport Score(CvData data)
    {
        var findings = new List<CvAtsFinding>();
        var score = 0;

        // 1. Contact info — 15 pts
        var hasEmail = data.Contacts.Any(c => c.Contains('@'));
        if (hasEmail && data.Contacts.Count >= 2)
        {
            score += 15;
            findings.Add(new CvAtsFinding("Contact", true, "Email plus at least one other contact method present."));
        }
        else
        {
            findings.Add(new CvAtsFinding("Contact", false, "Missing email or a second contact method (phone/LinkedIn/GitHub)."));
        }

        // 2. Summary — 10 pts
        var summaryLength = data.Summary.Length;
        if (summaryLength is >= 40 and <= 500)
        {
            score += 10;
            findings.Add(new CvAtsFinding("Summary", true, $"Summary is {summaryLength} characters."));
        }
        else
        {
            findings.Add(new CvAtsFinding("Summary", false, $"Summary is {summaryLength} characters (expected 40-500)."));
        }

        // 3. Experience entries complete — 15 pts
        var hasExperience = data.Experience.Count > 0;
        var allComplete = hasExperience && data.Experience.All(e =>
            !string.IsNullOrWhiteSpace(e.Company) && !string.IsNullOrWhiteSpace(e.Title) && !string.IsNullOrWhiteSpace(e.Start));
        if (allComplete)
        {
            score += 15;
            findings.Add(new CvAtsFinding("Experience", true, $"{data.Experience.Count} experience entries, all with Company/Title/Start."));
        }
        else
        {
            findings.Add(new CvAtsFinding("Experience", false, "At least one experience entry is missing Company, Title, or Start."));
        }

        // 4. Quantified impact — 20 pts
        var impactLines = data.Experience.SelectMany(e => e.Highlights)
            .Concat(data.Projects.SelectMany(p => p.Impact))
            .ToList();
        var quantifiedRatio = impactLines.Count == 0
            ? 0d
            : (double)impactLines.Count(QuantifiedRegex().IsMatch) / impactLines.Count;
        if (impactLines.Count > 0 && quantifiedRatio >= 0.5)
        {
            score += 20;
            findings.Add(new CvAtsFinding("Impact", true, $"{quantifiedRatio:P0} of highlight/impact lines are quantified."));
        }
        else
        {
            findings.Add(new CvAtsFinding("Impact", false, $"Only {quantifiedRatio:P0} of highlight/impact lines are quantified (target 50%+)."));
        }

        // 5. Line length — 10 pts
        var longLines = data.Experience.SelectMany(e => e.Highlights)
            .Concat(data.Projects.Select(p => p.Summary))
            .Concat(data.Projects.SelectMany(p => p.Impact))
            .Count(line => line.Length > 220);
        if (longLines == 0)
        {
            score += 10;
            findings.Add(new CvAtsFinding("Length", true, "No highlight, summary, or impact line exceeds 220 characters."));
        }
        else
        {
            findings.Add(new CvAtsFinding("Length", false, $"{longLines} line(s) exceed 220 characters."));
        }

        // 6. Skills grouped — 10 pts
        var hasSkills = data.SkillGroups.Count > 0 && data.SkillGroups.All(g => g.Any());
        if (hasSkills)
        {
            score += 10;
            findings.Add(new CvAtsFinding("Skills", true, $"{data.SkillGroups.Count} skill groups, none empty."));
        }
        else
        {
            findings.Add(new CvAtsFinding("Skills", false, "No skills, or a skill group has no entries."));
        }

        // 7. Consistent date format — 10 pts
        var dateValues = data.Experience.Select(e => e.Start)
            .Concat(data.Experience.Where(e => !e.Current).Select(e => e.End).OfType<string>())
            .ToList();
        var formats = dateValues.Select(ClassifyDateFormat).Distinct().ToList();
        if (dateValues.Count > 0 && formats.Count == 1 && formats[0] != "Other")
        {
            score += 10;
            findings.Add(new CvAtsFinding("Dates", true, $"All experience dates use a consistent '{formats[0]}' format."));
        }
        else
        {
            findings.Add(new CvAtsFinding("Dates", false, "Experience dates are missing or use inconsistent formats."));
        }

        // 8. Certs/projects bonus — 10 pts
        if (data.Certifications.Count > 0 || data.Projects.Count > 0)
        {
            score += 10;
            findings.Add(new CvAtsFinding("Bonus", true, "Certifications and/or projects present."));
        }
        else
        {
            findings.Add(new CvAtsFinding("Bonus", false, "No certifications or projects present."));
        }

        return new CvAtsRuleReport(score, findings);
    }

    private static string ClassifyDateFormat(string value)
    {
        if (MonthYearRegex().IsMatch(value)) return "MonYYYY";
        if (YearOnlyRegex().IsMatch(value)) return "YYYY";
        return "Other";
    }

    [GeneratedRegex(@"[\d%$€£]")]
    private static partial Regex QuantifiedRegex();

    [GeneratedRegex(@"^[A-Za-z]{3,9}\s\d{4}$")]
    private static partial Regex MonthYearRegex();

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex YearOnlyRegex();
}
