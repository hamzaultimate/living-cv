using Microsoft.AspNetCore.Mvc;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Profile;

public class CardRowForm
{
    public string? Title { get; set; }
    public string? Body { get; set; }
}

public class AboutForm
{
    public string? Headline { get; set; }
    public string? ParagraphsText { get; set; }
    public List<CardRowForm> Cards { get; set; } = new();
    public string? CurrentlyNote { get; set; }
    public string? FocusText { get; set; }

    /// <summary>The stamp of the profile when the form was loaded.</summary>
    public string? Stamp { get; set; }
}

// Up to ten paragraphs and a few cards; anything much larger is not from this page.
[BodyLimit(128 * 1024)]
public class AboutModel : ProfilePartModel
{
    public AboutModel(ProfileEditor profile, RequestTemplate template) : base(profile, template) { }

    public override string Part => ProfileParts.About;

    [BindProperty]
    public AboutForm Form { get; set; } = new();

    public bool CanAdd => Form.Cards.Count < ProfileParts.CardRows;

    /// <summary>The headline is required once the page has one: a site the wizard published has none yet, and must still be able to save its paragraphs. Emptying a headline that exists is refused.</summary>
    public bool HeadlineRequired { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadViewAsync();
        var p = await Writer.LoadAsync(ct);
        HeadlineRequired = !string.IsNullOrWhiteSpace(p.About.Headline);
        Form = new AboutForm
        {
            Headline = p.About.Headline,
            ParagraphsText = string.Join("\n\n", p.About.Paragraphs),
            Cards = p.About.ValueProps.Select(v => new CardRowForm { Title = v.Title, Body = v.Body }).ToList(),
            CurrentlyNote = p.About.CurrentlyNote,
            FocusText = string.Join("\n", p.About.Focus),
            Stamp = p.ConcurrencyStamp,
        };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadViewAsync();
        var c = new FormCheck(ModelState, View);
        HeadlineRequired = !string.IsNullOrWhiteSpace((await Writer.LoadAsync(ct)).About.Headline);
        var headline = c.Text(nameof(Form.Headline), "headline", Form.Headline, ProfileParts.HeroMax, required: HeadlineRequired);
        var paragraphs = View.Visible("paragraphs") ? c.Paragraphs(nameof(Form.ParagraphsText), "paragraphs", Form.ParagraphsText, ProfileParts.MaxParagraphs, ProfileParts.ParagraphMax) : [];
        var note = View.Visible("currentlyNote") ? c.Text(nameof(Form.CurrentlyNote), "currentlyNote", Form.CurrentlyNote, ProfileParts.HeroMax) : null;
        var focus = View.Visible("focus") ? c.Lines(nameof(Form.FocusText), "focus", Form.FocusText, ProfileParts.MaxFocus, ProfileParts.FocusMax) : [];

        var cards = new List<ProfileValueProp>();
        if (View.Visible("valueProps"))
        {
            var rows = Form.Cards.Select((r, i) => (Row: r, Number: i + 1)).Where(x => !(string.IsNullOrWhiteSpace(x.Row.Title) && string.IsNullOrWhiteSpace(x.Row.Body))).ToList();
            if (rows.Count > ProfileParts.CardRows) ModelState.AddModelError(string.Empty, $"At most {ProfileParts.CardRows} rows in {View.Heading}.");
            foreach (var (row, number) in rows.Take(ProfileParts.CardRows))
            {
                var name = $"{View.ItemLabel} {number}";
                var i = number - 1;
                var title = c.Text($"Cards[{i}].{nameof(row.Title)}", "valuePropTitle", row.Title, ProfileParts.CardTitleMax, required: true, context: name);
                var body = c.Text($"Cards[{i}].{nameof(row.Body)}", "valuePropBody", row.Body, ProfileParts.CardBodyMax, required: true, context: name);
                cards.Add(new ProfileValueProp { Title = title ?? "", Body = body ?? "" });
            }
        }
        if (!ModelState.IsValid) return Page();

        var outcome = await Writer.UpdateAsync(p =>
        {
            // Only what the template shows is written; a hidden field keeps whatever it already holds.
            var old = p.About;
            p.About = new ProfileAbout
            {
                Headline = headline,
                Paragraphs = View.Visible("paragraphs") ? paragraphs : old.Paragraphs,
                ValueProps = View.Visible("valueProps") ? cards : old.ValueProps,
                CurrentlyNote = View.Visible("currentlyNote") ? note : old.CurrentlyNote,
                Focus = View.Visible("focus") ? focus : old.Focus,
            };
        }, Form.Stamp, ct);
        if (outcome != SaveOutcome.Saved) return Conflict();

        Flash(SavedMessage);
        return RedirectToPage();
    }

    // The row buttons change the cards on the page only; nothing is written until Save (see MetricsModel).

    public async Task<IActionResult> OnPostAddAsync()
    {
        await LoadViewAsync();
        if (!CanAdd) return RowChanged($"You can have at most {ProfileParts.CardRows} rows in {View.Heading}.", null);
        Form.Cards.Add(new CardRowForm());
        var i = Form.Cards.Count - 1;
        return RowChanged($"{View.ItemLabel} {i + 1} added. It is not saved until you save.", $"Form_Cards_{i}__Title");
    }

    public async Task<IActionResult> OnPostRemoveAsync(int index)
    {
        await LoadViewAsync();
        if (index < 0 || index >= Form.Cards.Count) return RowChanged("That row is not on the page any more.", "add-row");
        Form.Cards.RemoveAt(index);
        return RowChanged($"{View.ItemLabel} {index + 1} removed. It is not saved until you save.", CanAdd ? "add-row" : null);
    }

    public async Task<IActionResult> OnPostMoveAsync(int index, string? direction)
    {
        await LoadViewAsync();
        var target = index + (direction == "up" ? -1 : direction == "down" ? 1 : 0);
        if (index < 0 || index >= Form.Cards.Count || target < 0 || target >= Form.Cards.Count || target == index) return RowChanged("That row cannot move any further.", null);
        (Form.Cards[index], Form.Cards[target]) = (Form.Cards[target], Form.Cards[index]);
        var keepGoing = direction == "up" ? target > 0 : target < Form.Cards.Count - 1;
        return RowChanged($"{View.ItemLabel} {index + 1} moved {direction} to position {target + 1}. It is not saved until you save.",
            $"row-{target}-{(keepGoing ? direction : direction == "up" ? "down" : "up")}");
    }
}
