using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Certifications;

public class CertificationForm
{
    public string? Name { get; set; }
    public string? Issuer { get; set; }
    public string? Status { get; set; } = nameof(CertificationStatus.Earned);
    public string? Date { get; set; }
    public string? Category { get; set; }
    public string? CredentialUrl { get; set; }
    public string? CertificateUrl { get; set; }
    public string? BadgeImageUrl { get; set; }
    public string? Stamp { get; set; }
}

[BodyLimit(64 * 1024)]
public class EditModel : ContentPageModel
{
    private const int UrlMax = 400;
    private readonly TimeProvider _clock;

    public EditModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template, TimeProvider clock) : base(db, content, template) => _clock = clock;

    public override string Section => SectionRegistry.Certifications;

    public int? Id { get; private set; }
    public bool IsNew => Id is null;

    [BindProperty]
    public CertificationForm Form { get; set; } = new();

    /// <summary>The statuses in the template's words; the stored value is the enum name, whatever the wording.</summary>
    public IReadOnlyList<(string Value, string Text)> StatusOptions =>
        Enum.GetValues<CertificationStatus>().Select(s => (s.ToString(), View.StatusLabel(s))).ToList();

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct)
    {
        await LoadViewAsync();
        if (id is null) return Page();

        var c = await Db.Certifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return ListWithError($"That {View.ItemLabelLower} no longer exists.");
        Id = c.Id;
        Form = new CertificationForm
        {
            Name = c.Name, Issuer = c.Issuer, Status = c.Status.ToString(), Date = c.Date, Category = c.Category,
            CredentialUrl = c.CredentialUrl, CertificateUrl = c.CertificateUrl, BadgeImageUrl = c.BadgeImageUrl, Stamp = c.ConcurrencyStamp,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, CancellationToken ct)
    {
        await LoadViewAsync();
        Id = id;
        var stored = id is null ? null : await Db.Certifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        var c = new FormCheck(ModelState, View);

        var name = c.Text(nameof(Form.Name), "name", Form.Name, 200, required: true);
        var issuer = c.Text(nameof(Form.Issuer), "issuer", Form.Issuer, 160, required: true);
        var statusText = c.Choice(nameof(Form.Status), "status", Form.Status, Enum.GetNames<CertificationStatus>());
        var status = Enum.TryParse<CertificationStatus>(statusText, out var parsed) ? parsed : CertificationStatus.Earned;

        string? date = null;
        if (View.Visible("date"))
        {
            date = c.Text(nameof(Form.Date), "date", Form.Date, 30);
            if (date is not null && c.IsValid(nameof(Form.Date)))
            {
                if (!PartialDate.TryParse(date, out var parsedDate)) c.Error(nameof(Form.Date), $"{View.Label("date")} must be a year or a month and year, for example 2024, Mar 2024 or 2024-03.");
                else if (statusText == nameof(CertificationStatus.Earned) && parsedDate.First > DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime))
                    c.Error(nameof(Form.Date), $"{View.Label("date")} is in the future, so this cannot be marked {View.StatusLabel(CertificationStatus.Earned).ToLowerInvariant()}. Choose another status or the date it was awarded.");
            }
        }
        var category = View.Visible("category") ? c.Text(nameof(Form.Category), "category", Form.Category, 80) : null;
        var credentialUrl = View.Visible("credentialUrl") ? c.Url(nameof(Form.CredentialUrl), "credentialUrl", Form.CredentialUrl, UrlMax, stored?.CredentialUrl) : null;
        var certificateUrl = View.Visible("certificateUrl") ? c.Url(nameof(Form.CertificateUrl), "certificateUrl", Form.CertificateUrl, UrlMax, stored?.CertificateUrl) : null;
        var badgeUrl = View.Visible("badgeImageUrl") ? c.Url(nameof(Form.BadgeImageUrl), "badgeImageUrl", Form.BadgeImageUrl, UrlMax, stored?.BadgeImageUrl) : null;

        // The seeder identifies a certification by its name, so two with one name would break Seed:Mode=Authoritative.
        if (name is not null && c.IsValid(nameof(Form.Name))
            && await Db.Certifications.AnyAsync(x => x.Id != id && x.Name.ToLower() == name.ToLower(), ct))
            c.Error(nameof(Form.Name), $"You already have a {View.ItemLabelLower} called \"{name}\". Edit that one instead.");

        if (!ModelState.IsValid) return Page();

        Certification row;
        if (id is null)
        {
            row = new Certification
            {
                Name = name!, Issuer = issuer!, Status = status, ConcurrencyStamp = ContentEditor.NewStamp(),
                SortOrder = ContentEditor.NextOrder(await Db.Certifications.Select(x => x.SortOrder).ToListAsync(ct)),
            };
            Db.Certifications.Add(row);
        }
        else
        {
            var found = await Db.Certifications.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (found is null) return ListWithError($"That {View.ItemLabelLower} was removed in another tab, so your changes were not saved.");
            row = found;
            Writer.Stamp(row, Form.Stamp);
        }

        row.Name = name!;
        row.Issuer = issuer!;
        row.Status = status;
        if (View.Visible("date")) row.Date = date;
        if (View.Visible("category")) row.Category = category;
        if (View.Visible("credentialUrl")) row.CredentialUrl = credentialUrl;
        if (View.Visible("certificateUrl")) row.CertificateUrl = certificateUrl;
        if (View.Visible("badgeImageUrl")) row.BadgeImageUrl = badgeUrl;

        var outcome = await Writer.CommitAsync(ct);
        if (outcome != SaveOutcome.Saved)
        {
            AddConflict();
            return Page();
        }

        Flash($"Saved {name}.");
        // No fragment: a browser skips autofocus when the address has one, and the saved message should take focus.
        return Redirect(ListUrl);
    }
}
