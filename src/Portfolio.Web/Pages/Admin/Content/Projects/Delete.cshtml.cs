using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Pages.Admin.Content.Projects;

public class DeleteModel : ContentDeleteModel<Project>
{
    private readonly UploadStore _uploads;

    public DeleteModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template, UploadStore uploads) : base(db, content, template) => _uploads = uploads;

    public override string Section => SectionRegistry.Projects;
    protected override DbSet<Project> Set => Db.Projects;

    private static IEnumerable<string> Urls(Project p) =>
        new[] { p.HeroImage }.Concat(p.Screenshots.Select(s => s.Url)).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u!);

    protected override DeleteInfo Describe(Project p)
    {
        var uploaded = Urls(p).Count(u => _uploads.TryResolve(u, out _));
        var lines = new List<string> { $"Its page, /projects/{p.Slug}, stops working and answers \"not found\"." };
        lines.Add(uploaded > 0
            ? $"{uploaded} image file(s) you uploaded for it will be deleted from storage."
            : "It has no uploaded image files.");
        if (Urls(p).Count() > uploaded) lines.Add("Images that come with the site rather than from an upload are not touched.");
        return new DeleteInfo(p.Title, lines);
    }

    protected override Task CleanUpAsync(Project removed, CancellationToken ct) => Writer.DeleteUploadsAsync(Urls(removed), removed.Id);
}
