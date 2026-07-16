using Portfolio.Data.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Portfolio.Web.Services;

/// <summary>Data snapshot for the CV PDF — assembled from the same DB content as the site.</summary>
public record CvData(
    string Name,
    string Title,
    string Summary,
    IReadOnlyList<string> Contacts,
    IReadOnlyList<Experience> Experience,
    IReadOnlyList<IGrouping<string, Skill>> SkillGroups,
    IReadOnlyList<Project> Projects,
    IReadOnlyList<Certification> Certifications,
    string? Website = null);

/// <summary>Print-friendly CV rendered with QuestPDF (light background, blue accent).</summary>
public class CvDocument : IDocument
{
    private const string Accent = "#1D4ED8"; // blue, high-contrast on paper
    private const string Ink = "#1A1A1A";
    private const string Muted = "#555555";

    private readonly CvData _d;
    public CvDocument(CvData d) => _d = d;

    public DocumentMetadata GetMetadata() => new() { Title = $"{_d.Name} — CV" };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(Ink).FontFamily("Arial"));

            page.Header().Element(Header);
            page.Content().Element(Body);
            var footer = (_d.Website ?? "").Replace("https://", "").Replace("http://", "").TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(footer))
                page.Footer().PaddingTop(6).AlignCenter()
                    .Text(footer).FontSize(8).FontColor(Accent);
        });
    }

    private void Header(IContainer c) => c.Column(col =>
    {
        col.Item().Text(_d.Name).FontSize(22).Bold().FontColor(Ink);
        col.Item().Text(_d.Title).FontSize(11).FontColor(Accent);
        if (_d.Contacts.Count > 0)
            col.Item().PaddingTop(3).Text(string.Join("   ·   ", _d.Contacts)).FontSize(9).FontColor(Muted);
        col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Accent);
    });

    private void Body(IContainer c) => c.PaddingTop(10).Column(col =>
    {
        col.Spacing(12);

        col.Item().Text(_d.Summary).FontSize(10.5f).FontColor(Muted);

        if (_d.Experience.Count > 0)
        {
            col.Item().Element(x => SectionTitle(x, "Experience"));
            foreach (var r in _d.Experience)
            {
                col.Item().Column(e =>
                {
                    e.Item().Row(row =>
                    {
                        row.RelativeItem().Text(t =>
                        {
                            t.Span(r.Company).Bold();
                            t.Span($"  —  {r.Title}").FontColor(Muted);
                        });
                        row.ConstantItem(130).AlignRight()
                            .Text($"{r.Start} – {(r.Current ? "present" : r.End)}").FontSize(9).FontColor("#888");
                    });
                    if (!string.IsNullOrWhiteSpace(r.Summary))
                        e.Item().PaddingTop(1).Text(r.Summary).FontSize(9.5f).FontColor(Muted);
                    foreach (var h in r.Highlights)
                        e.Item().Text($"▸  {h}").FontSize(9.5f).FontColor(Muted);
                });
            }
        }

        if (_d.SkillGroups.Count > 0)
        {
            col.Item().Element(x => SectionTitle(x, "Skills"));
            foreach (var g in _d.SkillGroups)
            {
                col.Item().Text(t =>
                {
                    t.Span($"{g.Key}:  ").Bold().FontColor(Accent);
                    t.Span(string.Join(", ", g.Select(s => s.Name))).FontSize(9.5f);
                });
            }
        }

        if (_d.Projects.Count > 0)
        {
            col.Item().Element(x => SectionTitle(x, "Selected Projects"));
            foreach (var p in _d.Projects)
            {
                col.Item().Column(pc =>
                {
                    pc.Item().Text(p.Title).Bold();
                    pc.Item().Text(p.Summary).FontSize(9.5f).FontColor(Muted);
                    if (p.Impact.Count > 0)
                        pc.Item().Text($"▸  {string.Join("   ·   ", p.Impact)}").FontSize(9).FontColor(Accent);
                });
            }
        }

        if (_d.Certifications.Count > 0)
        {
            col.Item().Element(x => SectionTitle(x, "Certifications"));
            foreach (var cert in _d.Certifications)
            {
                col.Item().Text(t =>
                {
                    t.Span(cert.Name).FontSize(9.5f);
                    t.Span($"  —  {cert.Issuer} ({Status(cert.Status)})").FontSize(9).FontColor("#888");
                });
            }
        }
    });

    private static void SectionTitle(IContainer c, string title) =>
        c.PaddingTop(6).BorderBottom(1).BorderColor("#DDDDDD").PaddingBottom(2)
            .Text(title.ToUpperInvariant()).FontSize(11).Bold().FontColor(Accent);

    private static string Status(CertificationStatus s) => s switch
    {
        CertificationStatus.Earned => "Earned",
        CertificationStatus.InProgress => "In progress",
        _ => "Planned",
    };
}
