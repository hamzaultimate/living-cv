using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Experiences;

public class ExperienceForm
{
    public string? Company { get; set; }
    public string? Title { get; set; }
    public string? Start { get; set; }
    public string? End { get; set; }
    public bool Current { get; set; }
    public string? Summary { get; set; }
    public string? HighlightsText { get; set; }
    public string? TechText { get; set; }
    public string? Stamp { get; set; }
}

// A few kilobytes of text; anything much larger is not from this page.
[BodyLimit(256 * 1024)]
public class EditModel : ContentPageModel
{
    public const int SummaryMax = 1500;

    public EditModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Experience;

    public int? Id { get; private set; }
    public bool IsNew => Id is null;

    [BindProperty]
    public ExperienceForm Form { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct)
    {
        await LoadViewAsync();
        if (id is null) return Page();

        var e = await Db.Experiences.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return ListWithError("That role no longer exists.");
        Id = e.Id;
        Form = new ExperienceForm
        {
            Company = e.Company, Title = e.Title, Start = e.Start, End = e.End, Current = e.Current, Summary = e.Summary,
            HighlightsText = string.Join("\n", e.Highlights), TechText = string.Join("\n", e.Tech), Stamp = e.ConcurrencyStamp,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, CancellationToken ct)
    {
        await LoadViewAsync();
        Id = id;
        var c = new FormCheck(ModelState, View);

        var company = c.Text(nameof(Form.Company), "company", Form.Company, 200, required: true);
        var title = c.Text(nameof(Form.Title), "title", Form.Title, 200, required: true);
        var start = c.Text(nameof(Form.Start), "start", Form.Start, 30, required: true);
        var summary = c.Paragraph(nameof(Form.Summary), "summary", Form.Summary, SummaryMax, required: true);
        PartialDate? startDate = null;
        if (start is not null && c.IsValid(nameof(Form.Start)))
        {
            if (PartialDate.TryParse(start, out var s)) startDate = s;
            else c.Error(nameof(Form.Start), $"{View.Label("start")} must be a year or a month and year, for example 2021, Mar 2021 or 2021-03.");
        }

        string? end = null;
        var current = Form.Current;
        if (View.Visible("end"))
        {
            end = c.Text(nameof(Form.End), "end", Form.End, 30);
            if (View.Visible("current") && current) end = null;
            else if (end is not null && c.IsValid(nameof(Form.End)))
            {
                if (!PartialDate.TryParse(end, out var e)) c.Error(nameof(Form.End), $"{View.Label("end")} must be a year or a month and year, for example 2023, Mar 2023 or 2023-03.");
                else if (startDate is { } sd && e.Last < sd.First) c.Error(nameof(Form.End), $"{View.Label("end")} cannot be earlier than {View.Label("start").ToLowerInvariant()}.");
            }
            else if (end is null && View.Visible("current") && !current)
                c.Error(nameof(Form.End), $"Give an {View.Label("end").ToLowerInvariant()} date, or tick \"{View.Label("current")}\".");
        }
        var highlights = View.Visible("highlights") ? c.Lines(nameof(Form.HighlightsText), "highlights", Form.HighlightsText, 12, 300) : [];
        var tech = View.Visible("tech") ? c.Lines(nameof(Form.TechText), "tech", Form.TechText, 20, 60) : [];

        if (company is not null && title is not null && await Db.Experiences.AnyAsync(
                x => x.Id != id && x.Company.ToLower() == company.ToLower() && x.Title.ToLower() == title.ToLower(), ct))
            c.Error(nameof(Form.Title), $"You already have a {View.ItemLabelLower} called \"{title}\" at \"{company}\". Edit that one instead.");

        if (!ModelState.IsValid) return Page();

        Experience row;
        if (id is null)
        {
            row = new Experience { Company = company!, Title = title!, Start = start!, Summary = summary!, SortOrder = ContentEditor.NextOrder(await Db.Experiences.Select(x => x.SortOrder).ToListAsync(ct)) };
            row.ConcurrencyStamp = ContentEditor.NewStamp();
            Db.Experiences.Add(row);
        }
        else
        {
            var found = await Db.Experiences.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (found is null) return ListWithError("That role was removed in another tab, so your changes were not saved.");
            row = found;
            Writer.Stamp(row, Form.Stamp);
        }

        // Only what the template shows is written; a hidden field keeps whatever it already holds.
        row.Company = company!;
        row.Title = title!;
        row.Start = start!;
        row.Summary = summary!;
        if (View.Visible("current")) row.Current = current;
        if (View.Visible("end")) row.End = row.Current && View.Visible("current") ? null : end;
        if (View.Visible("highlights")) row.Highlights = highlights;
        if (View.Visible("tech")) row.Tech = tech;

        var outcome = await Writer.CommitAsync(ct);
        if (outcome != SaveOutcome.Saved)
        {
            AddConflict();
            return Page();
        }

        Flash($"Saved {title} at {company}.");
        return Redirect($"{ListUrl}#row-{row.Id}");
    }
}
