using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;
using Portfolio.Web.Services;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Pages.Admin.Content.Projects;

/// <summary>The gallery images of one project: add (one at a time, with a description), reorder and remove. Same upload pipeline as the profile photo.</summary>
// Applies the upload cap before the antiforgery check reads the body (see BodyLimitAttribute); one image per request.
[BodyLimit]
public class ImagesModel : ContentPageModel
{
    public const int MaxImages = 12;
    public const int CaptionMax = 200;

    private readonly ImageProcessor _processor;
    private readonly UploadStore _uploads;

    public ImagesModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template, ImageProcessor processor, UploadStore uploads) : base(db, content, template)
    {
        _processor = processor;
        _uploads = uploads;
    }

    public override string Section => SectionRegistry.Projects;

    public int Id { get; private set; }
    public string ProjectTitle { get; private set; } = "";
    public string? Stamp { get; set; }
    public IReadOnlyList<ProjectImage> Images { get; private set; } = [];
    public string MaxSize => UploadForm.Label(_uploads.MaxBytes);

    [BindProperty]
    public IFormFile? Image { get; set; }

    [BindProperty]
    public string? Caption { get; set; }

    public string Heading => View.Label("screenshots");

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        await LoadViewAsync();
        if (!View.Visible("screenshots")) return ListWithError("Gallery images are not used by your template.");
        return await LoadAsync(id, ct) ? Page() : ListWithError($"That {View.ItemLabelLower} no longer exists.");
    }

    public async Task<IActionResult> OnPostUploadAsync(int id, string? stamp, CancellationToken ct)
    {
        await LoadViewAsync();
        if (!View.Visible("screenshots")) return ListWithError("Gallery images are not used by your template.");
        var project = await Db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return ListWithError($"That {View.ItemLabelLower} no longer exists.");

        var caption = ContentRules.Trim(Caption);
        if (caption is null) ModelState.AddModelError(nameof(Caption), "Describe the image in a few words (alt text). It is read to people who cannot see it and shown as the caption.");
        else if (caption.Length > CaptionMax) ModelState.AddModelError(nameof(Caption), $"The description can be at most {CaptionMax} characters.");
        else if (ContentRules.HasControlCharacters(caption) || ContentRules.HasMarkup(caption)) ModelState.AddModelError(nameof(Caption), "The description is plain text: remove anything that looks like a tag, such as <b>.");

        if (project.Screenshots.Count >= MaxImages) ModelState.AddModelError(nameof(Image), $"A {View.ItemLabelLower} can have at most {MaxImages} gallery images. Remove one first.");
        ProcessedImage? processed = null;
        if (Image is null || Image.Length == 0) ModelState.AddModelError(nameof(Image), "Choose an image to upload.");
        else if (Image.Length > _uploads.MaxBytes) ModelState.AddModelError(nameof(Image), $"That file is larger than {MaxSize}.");
        else
        {
            try
            {
                processed = await _processor.ProcessAsync(await UploadForm.ReadAsync(Image, _uploads.MaxBytes, MaxSize, ct), ct);
            }
            catch (ImageRejectedException ex)
            {
                ModelState.AddModelError(nameof(Image), ex.Message);
            }
        }
        if (!ModelState.IsValid || processed is null)
        {
            Db.ChangeTracker.Clear();
            await LoadAsync(id, ct);
            return Page();
        }

        var url = await _uploads.SaveAsync(processed, ct);
        try
        {
            Writer.Stamp(project, stamp);
            project.Screenshots = project.Screenshots.Append(new ProjectImage { Url = url, Caption = caption }).ToList();
            var outcome = await Writer.CommitAsync(ct);
            if (outcome != SaveOutcome.Saved)
            {
                Writer.DiscardUpload(url);
                return ListWithError(ConflictMessage);
            }
        }
        catch
        {
            Writer.DiscardUpload(url);
            throw;
        }

        Flash("Image added.");
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id, string? stamp, string? url, CancellationToken ct)
    {
        await LoadViewAsync();
        if (!View.Visible("screenshots")) return ListWithError("Gallery images are not used by your template.");
        var project = await Db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        var image = project?.Screenshots.FirstOrDefault(s => s.Url == url);
        if (project is null || image is null) return ListWithError("That image was already removed.");

        Writer.Stamp(project, stamp);
        project.Screenshots = project.Screenshots.Where(s => s.Url != url).ToList();
        if (await Writer.CommitAsync(ct) != SaveOutcome.Saved) return ListWithError(ConflictMessage);

        await Writer.DeleteUploadsAsync([url], id);
        Flash("Image removed.");
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, string? stamp, string? url, string? direction, CancellationToken ct)
    {
        await LoadViewAsync();
        if (!View.Visible("screenshots")) return ListWithError("Gallery images are not used by your template.");
        var project = await Db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        var list = project?.Screenshots.ToList();
        var index = list?.FindIndex(s => s.Url == url) ?? -1;
        var target = index + (direction == "up" ? -1 : direction == "down" ? 1 : 0);
        if (project is null || list is null || index < 0) return ListWithError("That image no longer exists.");
        if (target < 0 || target >= list.Count || target == index) return RedirectToPage(new { id });

        (list[index], list[target]) = (list[target], list[index]);
        Writer.Stamp(project, stamp);
        // New instances: EF tracks the owned JSON items by identity, so the same items in another order would not be written.
        project.Screenshots = list.Select(i => new ProjectImage { Url = i.Url, Caption = i.Caption }).ToList();
        if (await Writer.CommitAsync(ct) != SaveOutcome.Saved) return ListWithError(ConflictMessage);

        Flash("Image moved.");
        return Redirect($"{ListUrl}/images/{id}");
    }

    private async Task<bool> LoadAsync(int id, CancellationToken ct)
    {
        var project = await Db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return false;
        Id = project.Id;
        ProjectTitle = project.Title;
        Stamp = project.ConcurrencyStamp;
        Images = project.Screenshots;
        return true;
    }
}
