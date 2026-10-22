
namespace Portfolio.Web.Services;

/// <summary>Estimates reading time. Rendering is <see cref="SafeMd"/>: there is deliberately no renderer here that allows raw HTML.</summary>
public static class Md
{
    public static int ReadingMinutes(string? markdown)
    {
        var words = (markdown ?? string.Empty)
            .Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)Math.Round(words / 200.0));
    }
}
