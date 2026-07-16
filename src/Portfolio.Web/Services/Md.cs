using Markdig;

namespace Portfolio.Web.Services;

/// <summary>Renders trusted (seed-authored) Markdown to HTML and estimates reading time.</summary>
public static class Md
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static string ToHtml(string? markdown) =>
        Markdig.Markdown.ToHtml(markdown ?? string.Empty, Pipeline);

    public static int ReadingMinutes(string? markdown)
    {
        var words = (markdown ?? string.Empty)
            .Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)Math.Round(words / 200.0));
    }
}
