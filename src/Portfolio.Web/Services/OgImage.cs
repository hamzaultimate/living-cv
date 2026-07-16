using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Portfolio.Web.Services;

/// <summary>Generates the 1200×630 Open Graph share image (name + role) on the brand dark background.</summary>
public static class OgImage
{
    public static byte[] Generate(string name, string title, string? domainLabel = null, string? tagline = null)
    {
        var images = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(1200, 630, Unit.Point); // at RasterDpi 72, points map 1:1 to pixels
                page.Content().Background("#0B0F14").PaddingHorizontal(80).PaddingVertical(70).Column(col =>
                {
                    if (!string.IsNullOrWhiteSpace(domainLabel))
                        col.Item().Text(domainLabel).FontFamily("Arial").FontSize(20).FontColor("#60A5FA");
                    col.Item().PaddingTop(150).Text(name).FontFamily("Arial").FontSize(66).Bold().FontColor("#E6EDF3");
                    col.Item().PaddingTop(14).Text(title).FontFamily("Arial").FontSize(30).FontColor("#8896A5");
                    if (!string.IsNullOrWhiteSpace(tagline))
                        col.Item().PaddingTop(40).Text(tagline).FontFamily("Arial").FontSize(22).FontColor("#93C5FD");
                });
            });
        }).GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 72 });

        return images.First();
    }
}
