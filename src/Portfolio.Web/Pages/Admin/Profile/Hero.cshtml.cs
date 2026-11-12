using Microsoft.AspNetCore.Mvc;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Profile;

public class HeroForm
{
    public string? Eyebrow { get; set; }
    public string? Headline { get; set; }
    public string? HeadlineAccent { get; set; }
    public string? Lede { get; set; }

    /// <summary>The stamp of the profile when the form was loaded.</summary>
    public string? Stamp { get; set; }
}

// Four short text fields; anything much larger is not from this page.
[BodyLimit(64 * 1024)]
public class HeroModel : ProfilePartModel
{
    public HeroModel(ProfileEditor profile, RequestTemplate template) : base(profile, template) { }

    public override string Part => ProfileParts.Hero;

    [BindProperty]
    public HeroForm Form { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadViewAsync();
        var p = await Writer.LoadAsync(ct);
        Form = new HeroForm { Eyebrow = p.Hero.Eyebrow, Headline = p.Hero.Headline, HeadlineAccent = p.Hero.HeadlineAccent, Lede = p.Hero.Lede, Stamp = p.ConcurrencyStamp };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadViewAsync();
        var c = new FormCheck(ModelState, View);
        var eyebrow = View.Visible("eyebrow") ? c.Text(nameof(Form.Eyebrow), "eyebrow", Form.Eyebrow, ProfileParts.HeroMax) : null;
        var headline = c.Text(nameof(Form.Headline), "headline", Form.Headline, ProfileParts.HeroMax, required: true);
        var accent = View.Visible("headlineAccent") ? c.Text(nameof(Form.HeadlineAccent), "headlineAccent", Form.HeadlineAccent, ProfileParts.HeroMax) : null;
        var lede = View.Visible("lede") ? c.Text(nameof(Form.Lede), "lede", Form.Lede, ProfileParts.LedeMax) : null;
        if (!ModelState.IsValid) return Page();

        var outcome = await Writer.UpdateAsync(p =>
        {
            // Only what the template shows is written; a hidden field keeps whatever it already holds.
            p.Hero = new ProfileHero
            {
                Eyebrow = View.Visible("eyebrow") ? eyebrow : p.Hero.Eyebrow,
                Headline = headline,
                HeadlineAccent = View.Visible("headlineAccent") ? accent : p.Hero.HeadlineAccent,
                Lede = View.Visible("lede") ? lede : p.Hero.Lede,
            };
        }, Form.Stamp, ct);
        if (outcome != SaveOutcome.Saved) return Conflict();

        Flash(SavedMessage);
        // No fragment: a browser skips autofocus when the address has one, and the saved message should take focus.
        return RedirectToPage();
    }
}
