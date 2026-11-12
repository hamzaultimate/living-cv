using Microsoft.AspNetCore.Mvc;
using Portfolio.Web.Pages.Admin.Content;
using Portfolio.Web.Pages.Admin.Profile;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin;

public class SectionsForm
{
    public List<SectionRowForm> Rows { get; set; } = new();

    /// <summary>The stamp of the profile when the form was loaded.</summary>
    public string? Stamp { get; set; }
}

/// <summary>
/// Which sections the site shows, what the menu calls them and in which order. The settings live in the profile's <c>Sections</c> JSON and are written through
/// <see cref="ProfileEditor.UpdateAsync"/> with the stamp the form carries, so an overlapping save (another tab, the wizard) is refused with nothing written.
/// One write replaces the whole list, so there is no per-row ordering to race and no separate list stamp is needed. Hiding a section only stops the public site showing it; no row of content is touched.
/// </summary>
// Nine short rows; anything much larger is not from this page.
[BodyLimit(64 * 1024)]
public class SectionsModel : ProfilePartModel
{
    private readonly TemplateResolver _resolver;
    private TemplateDefinition _definition = null!;

    public SectionsModel(ProfileEditor profile, RequestTemplate template, TemplateResolver resolver) : base(profile, template) => _resolver = resolver;

    public override string Part => ProfileParts.Sections;

    public override string Route => "/admin/sections";

    [BindProperty]
    public SectionsForm Form { get; set; } = new();

    public bool LabelShown => View.Visible("label");

    /// <summary>The template's own entry for a row, or null for a key it does not have (a forged post that was refused).</summary>
    public TemplateSection? TemplateRow(SectionRowForm row) => SectionSettings.Find(_definition, row.Key);

    /// <summary>What a row is called on this page: the name typed, or the template's.</summary>
    public string NameOf(SectionRowForm row) => TemplateView.Cap(!string.IsNullOrWhiteSpace(row.Label) ? row.Label.Trim() : TemplateRow(row)?.Label ?? "?");

    public FieldVm Toggle(int index, SectionRowForm row) =>
        FieldVm.For(View, ModelState, "shown", $"Rows[{index}].{nameof(SectionRowForm.Enabled)}", null, FieldKind.Checkbox, isChecked: row.Enabled, withHint: false);

    private async Task<Portfolio.Data.Entities.SiteProfile> LoadAsync(CancellationToken ct)
    {
        // The rows, the template and the stamp all come from the one read of the profile, so they agree with each other.
        var profile = await Writer.LoadAsync(ct);
        var resolved = _resolver.Resolve(SiteProfileMapper.ToOptions(profile));
        _definition = resolved.Definition;
        View = new EditorView(resolved, Part);
        return profile;
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var profile = await LoadAsync(ct);
        Form = new SectionsForm { Rows = SectionSettings.Rows(_definition, _resolver.Resolve(SiteProfileMapper.ToOptions(profile))), Stamp = profile.ConcurrencyStamp };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        var check = new FormCheck(ModelState, View);
        var labelShown = LabelShown;
        if (!SectionSettings.Validate(check, View, _definition, Form.Rows, labelShown, out var labels)) return Page();

        var outcome = await Writer.UpdateAsync(p => p.Sections = SectionSettings.Build(_definition, Form.Rows, labels, labelShown, p.Sections), Form.Stamp, ct);
        if (outcome != SaveOutcome.Saved) return Conflict();

        Flash(SavedMessage);
        return RedirectToPage();
    }

    // The row buttons change the order on the page only; nothing is written until Save (see MetricsModel).
    public async Task<IActionResult> OnPostMoveAsync(int index, string? direction, CancellationToken ct)
    {
        await LoadAsync(ct);
        var target = index + (direction == "up" ? -1 : direction == "down" ? 1 : 0);
        if (index < 0 || index >= Form.Rows.Count || target < 0 || target >= Form.Rows.Count || target == index) return RowChanged("That row cannot move any further.", null);
        var name = NameOf(Form.Rows[index]);
        (Form.Rows[index], Form.Rows[target]) = (Form.Rows[target], Form.Rows[index]);
        var keepGoing = direction == "up" ? target > 0 : target < Form.Rows.Count - 1;
        return RowChanged($"{name} moved {direction} to position {target + 1}. It is not saved until you save.", $"row-{target}-{(keepGoing ? direction : direction == "up" ? "down" : "up")}");
    }
}
