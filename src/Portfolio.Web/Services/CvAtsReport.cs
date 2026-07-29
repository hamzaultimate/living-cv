namespace Portfolio.Web.Services;

public record CvAtsReport(
    int RuleScore,
    int? AiScore,
    int CombinedScore,
    IReadOnlyList<CvAtsFinding> Findings,
    IReadOnlyList<string> AiStrengths,
    IReadOnlyList<string> AiCritiques,
    DateTimeOffset GeneratedAtUtc);
