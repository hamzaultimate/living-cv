using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Data.Seeding;
using Portfolio.Web.Configuration;
using Portfolio.Web.Security;
using Portfolio.Web.Services;
using Portfolio.Web.Transfer;

namespace Portfolio.Web.Pages.Admin.Transfer;

/// <summary>What a preview or a result page draws: the report of every table, and whether the words are past or future.</summary>
public sealed record ReportVm(IReadOnlyList<SectionReport> Sections, ProfileReport Profile, bool Done);

/// <summary>Ties a held preview to the owner session that made it.</summary>
public static class ImportSession
{
    /// <summary>The session's key: the random id of the sign-in, with the account stamp (which a password reset or sign-out replaces) and when it began. A sign-in made before the id existed has none; its previews then share one key, which only lasts until that cookie expires.</summary>
    public static string KeyOf(ClaimsPrincipal user) =>
        $"{user.FindFirstValue(AdminAuth.SessionClaim)}|{user.FindFirstValue(AdminAuth.StampClaim)}|{user.FindFirstValue(AdminAuth.SignedInClaim)}";
}

/// <summary>
/// Export and import. The page shows the export link and the import form; a preview is the answer to the form's post (it reads the file and writes nothing), and the
/// confirm is a separate post on <see cref="ConfirmModel"/>. The words come from the template's <c>editor.transfer</c> block; what each entry is called comes from the
/// other editors' words, so nothing here names a profession.
/// </summary>
// The file plus the multipart boundaries and the other form fields; anything larger is not from this page.
[BodyLimit(TransferLimits.MaxRequestBytes)]
public class IndexModel : AdminPageModel
{
    private readonly ImportService _import;
    private readonly RequestTemplate _template;
    private readonly IConfiguration _config;

    public IndexModel(ImportService import, RequestTemplate template, IConfiguration config)
    {
        _import = import;
        _template = template;
        _config = config;
    }

    public EditorView View { get; private set; } = null!;

    /// <summary>The message after a discard takes keyboard focus, so it is announced (a preview or a refusal has its own focus target).</summary>
    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context) => ViewData["FocusFlash"] = true;

    /// <summary>The file chosen on the form.</summary>
    [BindProperty]
    public IFormFile? Upload { get; set; }

    /// <summary>Whether the profile in the file is applied too.</summary>
    [BindProperty]
    public bool ApplyProfile { get; set; }

    /// <summary>The preview of the file just posted, held until it is confirmed.</summary>
    public ImportPlan? Plan { get; private set; }

    /// <summary>Why the file was not read, when it was not.</summary>
    public string? Refusal { get; private set; }

    /// <summary>True when start-up seeding replaces browser-written content, so an import would be overwritten at the next start.</summary>
    public bool Authoritative => _config.GetSeedMode() == SeedMode.Authoritative;

    public string MaxSize => ImportFileReader.SizeLabel;

    private async Task LoadViewAsync() => View = new EditorView(await _template.GetAsync(), ProfileParts.Transfer);

    public async Task OnGetAsync() => await LoadViewAsync();

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken ct)
    {
        await LoadViewAsync();
        var outcome = await _import.PreviewAsync(Upload, ApplyProfile, ImportSession.KeyOf(User), ct);
        Plan = outcome.Plan;
        Refusal = outcome.Refusal;
        return Page();
    }
}
