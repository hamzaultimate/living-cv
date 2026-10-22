using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Talks;

public class TalkForm
{
    public string? Title { get; set; }
    public string? Type { get; set; } = nameof(TalkType.Video);
    public string? Event { get; set; }
    public string? Date { get; set; }
    public string? YoutubeId { get; set; }
    public string? Url { get; set; }
    public string? Stamp { get; set; }
}

// A few short fields; anything much larger is not from this page.
[BodyLimit(64 * 1024)]
public class EditModel : ContentPageModel
{
    public const int TitleMax = 200;
    public const int EventMax = 200;
    public const int UrlMax = 400;

    public EditModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Talks;

    public int? Id { get; private set; }
    public bool IsNew => Id is null;

    [BindProperty]
    public TalkForm Form { get; set; } = new();

    public IReadOnlyList<(string Value, string Text)> TypeOptions => Enum.GetNames<TalkType>().Select(n => (n, n)).ToList();

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct)
    {
        await LoadViewAsync();
        if (id is null) return Page();

        var t = await Db.Talks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return ListWithError($"That {View.ItemLabelLower} no longer exists.");
        Id = t.Id;
        Form = new TalkForm
        {
            Title = t.Title, Type = t.Type.ToString(), Event = t.Event, Date = t.Date?.ToString("yyyy-MM-dd"),
            YoutubeId = t.YoutubeId, Url = t.Url, Stamp = t.ConcurrencyStamp,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, CancellationToken ct)
    {
        await LoadViewAsync();
        Id = id;
        var stored = id is null ? null : await Db.Talks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        var c = new FormCheck(ModelState, View);

        var title = c.Text(nameof(Form.Title), "title", Form.Title, TitleMax, required: true);
        var type = TalkType.Video;
        if (View.Visible("type"))
        {
            var typeText = c.Choice(nameof(Form.Type), "type", Form.Type, Enum.GetNames<TalkType>(), required: false);
            if (typeText is not null && Enum.TryParse<TalkType>(typeText, out var parsedType)) type = parsedType;
            else if (typeText is null) type = stored?.Type ?? TalkType.Video;
        }
        var ev = View.Visible("event") ? c.Text(nameof(Form.Event), "event", Form.Event, EventMax) : null;

        DateOnly? date = null;
        if (View.Visible("date"))
        {
            var dateText = c.Text(nameof(Form.Date), "date", Form.Date, 30);
            if (dateText is not null && c.IsValid(nameof(Form.Date)))
            {
                if (PartialDate.TryParse(dateText, out var parsedDate)) date = parsedDate.First;
                else c.Error(nameof(Form.Date), $"{View.Label("date")} must be a date, or a month and year, for example 2025-05-14, 2025-05 or 2025.");
            }
        }

        string? youtubeId = null;
        if (View.Visible("youtubeId"))
        {
            var raw = ContentRules.Trim(Form.YoutubeId);
            if (raw is not null)
            {
                youtubeId = ContentRules.YoutubeId(raw);
                // An id that is already stored can be kept as it is, so an older entry can be saved untouched.
                if (youtubeId is null && raw == stored?.YoutubeId) youtubeId = raw;
                else if (youtubeId is null) c.Error(nameof(Form.YoutubeId), $"{View.Label("youtubeId")} must be a YouTube link or the 11 character video id.");
            }
        }
        var url = View.Visible("url") ? c.Url(nameof(Form.Url), "url", Form.Url, UrlMax, stored?.Url) : null;

        // The seeder identifies an entry by its title, so two with one title would break Seed:Mode=Authoritative.
        if (title is not null && c.IsValid(nameof(Form.Title))
            && await Db.Talks.AnyAsync(x => x.Id != id && x.Title.ToLower() == title.ToLower(), ct))
            c.Error(nameof(Form.Title), $"You already have a {View.ItemLabelLower} called \"{title}\". Edit that one instead.");

        if (!ModelState.IsValid) return Page();

        Talk row;
        if (id is null)
        {
            // The public page orders by date, so the position written here only breaks ties and needs no list stamp.
            row = new Talk
            {
                Title = title!, ConcurrencyStamp = ContentEditor.NewStamp(),
                SortOrder = ContentEditor.NextOrder(await Db.Talks.Select(x => x.SortOrder).ToListAsync(ct)),
            };
            Db.Talks.Add(row);
        }
        else
        {
            var found = await Db.Talks.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (found is null) return ListWithError($"That {View.ItemLabelLower} was removed in another tab, so your changes were not saved.");
            row = found;
            Writer.Stamp(row, Form.Stamp);
        }

        // Only what the template shows is written; a hidden field keeps whatever it already holds.
        row.Title = title!;
        if (View.Visible("type")) row.Type = type;
        if (View.Visible("event")) row.Event = ev;
        if (View.Visible("date")) row.Date = date;
        if (View.Visible("youtubeId")) row.YoutubeId = youtubeId;
        if (View.Visible("url")) row.Url = url;

        var outcome = await Writer.CommitAsync(ct);
        if (outcome != SaveOutcome.Saved)
        {
            AddConflict(id is null);
            return Page();
        }

        Flash($"Saved {title}.");
        // No fragment: a browser skips autofocus when the address has one, and the saved message should take focus.
        return Redirect(ListUrl);
    }
}
