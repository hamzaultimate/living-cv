using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Welcome;

/// <summary>Serves a template's thumbnail to the wizard. The key must name a template in the catalog and the file is the fixed
/// <see cref="TemplateCatalog.PreviewFileName"/> in that template's folder, so no part of the request becomes a path.</summary>
public class WelcomeThumbModel : AdminPageModel
{
    private readonly ITemplateCatalog _catalog;

    public WelcomeThumbModel(ITemplateCatalog catalog) => _catalog = catalog;

    public IActionResult OnGet(string key) =>
        _catalog.TryGet(key, out var template) && _catalog.GetPreviewImagePath(template.Key) is { } path
            ? PhysicalFile(path, "image/png")
            : NotFound();
}
