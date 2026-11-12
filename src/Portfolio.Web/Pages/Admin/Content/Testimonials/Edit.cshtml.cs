using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages.Admin.Content.Testimonials;

public class TestimonialForm
{
    public string? Quote { get; set; }
    public string? Author { get; set; }
    public string? AuthorTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string? Stamp { get; set; }
}

// A few short fields; anything much larger is not from this page.
[BodyLimit(64 * 1024)]
public class EditModel : ContentPageModel
{
    public const int QuoteMax = 2000;   // the column length
    public const int AuthorMax = 120;   // the column length
    public const int AuthorTitleMax = 200;
    public const int UrlMax = 400;

    public EditModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template) : base(db, content, template) { }

    public override string Section => SectionRegistry.Testimonials;

    public int? Id { get; private set; }
    public bool IsNew => Id is null;

    [BindProperty]
    public TestimonialForm Form { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct)
    {
        await LoadViewAsync();
        if (id is null) return Page();

        var t = await Db.Testimonials.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return ListWithError($"That {View.ItemLabelLower} no longer exists.");
        Id = t.Id;
        Form = new TestimonialForm { Quote = t.Quote, Author = t.Author, AuthorTitle = t.AuthorTitle, SourceUrl = t.SourceUrl, Stamp = t.ConcurrencyStamp };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, CancellationToken ct)
    {
        await LoadViewAsync();
        Id = id;
        var stored = id is null ? null : await Db.Testimonials.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        var c = new FormCheck(ModelState, View);

        // The page prints the quote as plain text, so markup is refused rather than shown as typed.
        var quote = c.Paragraph(nameof(Form.Quote), "quote", Form.Quote, QuoteMax, required: true);
        var author = c.Text(nameof(Form.Author), "author", Form.Author, AuthorMax, required: true);
        var authorTitle = View.Visible("authorTitle") ? c.Text(nameof(Form.AuthorTitle), "authorTitle", Form.AuthorTitle, AuthorTitleMax) : null;
        var sourceUrl = View.Visible("sourceUrl") ? c.Url(nameof(Form.SourceUrl), "sourceUrl", Form.SourceUrl, UrlMax, stored?.SourceUrl) : null;

        // The seeder identifies an entry by its author and the first 40 characters of the quote, so two with one key would break Seed:Mode=Authoritative.
        if (quote is not null && author is not null && c.IsValid(nameof(Form.Quote)) && c.IsValid(nameof(Form.Author)))
        {
            var key = SeedKey(author, quote);
            var others = await Db.Testimonials.AsNoTracking().Where(x => x.Id != id).Select(x => new { x.Author, x.Quote }).ToListAsync(ct);
            if (others.Any(o => string.Equals(SeedKey(o.Author, o.Quote), key, StringComparison.OrdinalIgnoreCase)))
                c.Error(nameof(Form.Quote), $"You already have a {View.ItemLabelLower} from \"{author}\" that starts the same way. Edit that one instead.");
        }

        if (!ModelState.IsValid) return Page();

        Testimonial row;
        if (id is null)
        {
            await Writer.BeginOrderChangeAsync(Section, ct); // serialises with moves and other new entries (see Experiences/Edit)
            row = new Testimonial
            {
                Quote = quote!, Author = author!, ConcurrencyStamp = ContentEditor.NewStamp(),
                SortOrder = ContentEditor.NextOrder(await Db.Testimonials.Select(x => x.SortOrder).ToListAsync(ct)),
            };
            Db.Testimonials.Add(row);
        }
        else
        {
            var found = await Db.Testimonials.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (found is null) return ListWithError($"That {View.ItemLabelLower} was removed in another tab, so your changes were not saved.");
            row = found;
            Writer.Stamp(row, Form.Stamp);
        }

        row.Quote = quote!;
        row.Author = author!;
        if (View.Visible("authorTitle")) row.AuthorTitle = authorTitle;
        if (View.Visible("sourceUrl")) row.SourceUrl = sourceUrl;

        var outcome = await Writer.CommitAsync(ct);
        if (outcome != SaveOutcome.Saved)
        {
            AddConflict(id is null);
            return Page();
        }

        Flash($"Saved {author}.");
        // No fragment: a browser skips autofocus when the address has one, and the saved message should take focus.
        return Redirect(ListUrl);
    }

    /// <summary>The natural key the seeder uses for a testimonial.</summary>
    private static string SeedKey(string author, string quote) => $"{author}|{quote[..Math.Min(40, quote.Length)]}";
}
