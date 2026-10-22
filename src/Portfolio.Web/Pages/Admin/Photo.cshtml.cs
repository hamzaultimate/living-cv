using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Security;
using Portfolio.Web.Services;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Pages.Admin;

// Applies the upload cap before the antiforgery check reads the body (see BodyLimitAttribute).
[BodyLimit]
public class PhotoModel : AdminPageModel
{
    private readonly ProfileEditor _editor;
    private readonly ImageProcessor _processor;
    private readonly UploadStore _store;

    public PhotoModel(ProfileEditor editor, ImageProcessor processor, UploadStore store)
    {
        _editor = editor;
        _processor = processor;
        _store = store;
    }

    public string? PhotoUrl { get; private set; }

    public string MaxSize => UploadForm.Label(_store.MaxBytes);

    [BindProperty]
    public IFormFile? Photo { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => PhotoUrl = (await _editor.LoadAsync(ct)).PhotoUrl;

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken ct)
    {
        if (Photo is null || Photo.Length == 0)
            return await Reject("Choose an image to upload.", ct);
        if (Photo.Length > _store.MaxBytes)
            return await Reject($"That file is larger than {MaxSize}.", ct);

        ProcessedImage processed;
        try
        {
            processed = await _processor.ProcessAsync(await UploadForm.ReadAsync(Photo, _store.MaxBytes, MaxSize, ct), ct);
        }
        catch (ImageRejectedException ex)
        {
            return await Reject(ex.Message, ct);
        }

        var url = await _store.SaveAsync(processed, ct);
        string? previous;
        try
        {
            previous = await _editor.SetPhotoAsync(url, ct);
        }
        catch
        {
            _store.TryDelete(url);
            throw;
        }

        _store.TryDelete(previous);
        Flash("Photo updated.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(CancellationToken ct)
    {
        _store.TryDelete(await _editor.SetPhotoAsync(null, ct));
        Flash("Photo removed. The site falls back to the monogram.");
        return RedirectToPage();
    }

    private async Task<IActionResult> Reject(string message, CancellationToken ct)
    {
        ModelState.AddModelError(nameof(Photo), message);
        PhotoUrl = (await _editor.LoadAsync(ct)).PhotoUrl;
        return Page();
    }
}
