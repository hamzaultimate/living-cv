namespace Portfolio.Web.Services;

/// <summary>The sections a CV can show after its summary, and what a template that says nothing about them gets.</summary>
public static class CvSections
{
    /// <summary>Today's order, and the order of a template whose manifest has no <c>copy.cv.order</c>.</summary>
    public static readonly IReadOnlyList<string> Default =
        [SectionRegistry.Experience, SectionRegistry.Skills, SectionRegistry.Projects, SectionRegistry.Certifications];

    /// <summary>Today's PDF colour, and the colour of a template whose manifest has no <c>copy.cv.accent</c>.</summary>
    public const string DefaultAccent = "#1D4ED8";

    public static bool IsKnown(string? key) => key is not null && Default.Contains(key, StringComparer.Ordinal);
}
