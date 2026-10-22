using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;
using Portfolio.Web.Services;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Pages.Admin.Content.Blog;

public class PostForm
{
    public string? Title { get; set; }
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public string? Date { get; set; }
    public string? Tags { get; set; }
    public bool Draft { get; set; } = true;
    public string? Body { get; set; }
    public IFormFile? CoverFile { get; set; }
    public string? CoverAlt { get; set; }
    public bool RemoveCover { get; set; }
    public string? Stamp { get; set; }
}

// Applies the upload cap before the antiforgery check reads the body (see BodyLimitAttribute); the cover image is the only file.
[BodyLimit]
public partial class EditModel : ContentPageModel
{
    public const int DescriptionMax = 300;
    public const int BodyMax = 60_000;
    public const int MaxTags = 8;
    public const int TagMax = 30;
    private static readonly DateOnly EarliestDate = new(1990, 1, 1);
    private static readonly DateOnly LatestDate = new(2100, 12, 31);

    [GeneratedRegex(@"^[\p{L}\p{N}.+#][\p{L}\p{N} .+#&_-]*\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TagPattern();

    private readonly ImageProcessor _processor;
    private readonly UploadStore _uploads;
    private readonly TimeProvider _clock;

    public EditModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template, ImageProcessor processor, UploadStore uploads, TimeProvider clock) : base(db, content, template)
    {
        _processor = processor;
        _uploads = uploads;
        _clock = clock;
    }

    public override string Section => SectionRegistry.Blog;

    public int? Id { get; private set; }
    public bool IsNew => Id is null;
    public string MaxSize => UploadForm.Label(_uploads.MaxBytes);

    /// <summary>The cover image the post has now, if any (for the preview and the Remove option).</summary>
    public string? CurrentCover { get; private set; }

    /// <summary>Set by the Preview button: the body rendered by the same function the public page uses. Nothing is saved.</summary>
    public string? PreviewHtml { get; private set; }

    [BindProperty]
    public PostForm Form { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct)
    {
        await LoadViewAsync();
        if (id is null)
        {
            // A new post starts as a draft, dated today.
            Form = new PostForm { Date = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Draft = true };
            return Page();
        }

        var p = await Db.BlogPosts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return ListWithError($"That {View.ItemLabelLower} no longer exists.");
        Id = p.Id;
        CurrentCover = p.Cover;
        Form = new PostForm
        {
            Title = p.Title, Slug = p.Slug, Description = p.Description, Date = p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Tags = string.Join(", ", p.Tags), Draft = p.Draft, Body = p.BodyMarkdown, CoverAlt = p.CoverAlt, Stamp = p.ConcurrencyStamp,
        };
        return Page();
    }

    /// <summary>Shows the typed text as it will be published. A POST because the text is too long for an address and must not be cached or logged; it reads nothing from the database and writes nothing.</summary>
    public async Task<IActionResult> OnPostPreviewAsync(int? id, CancellationToken ct)
    {
        await LoadViewAsync();
        Id = id;
        CurrentCover = id is null ? null : await Db.BlogPosts.AsNoTracking().Where(x => x.Id == id).Select(x => x.Cover).FirstOrDefaultAsync(ct);
        var body = NormaliseBody(Form.Body);
        if (body.Length > BodyMax) ModelState.AddModelError("Form.Body", $"{View.Label("body")} can be at most {BodyMax:N0} characters.");
        else PreviewHtml = SafeMd.ToHtml(body);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, CancellationToken ct)
    {
        await LoadViewAsync();
        Id = id;
        var stored = id is null ? null : await Db.BlogPosts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (id is not null && stored is null) return ListWithError($"That {View.ItemLabelLower} was removed in another tab, so your changes were not saved.");
        CurrentCover = stored?.Cover;
        var c = new FormCheck(ModelState, View);

        var title = c.Text(nameof(Form.Title), "title", Form.Title, BlogPost.MaxLength.Title, required: true);
        var slug = await CheckSlugAsync(c, title, stored, ct);
        var description = View.Visible("description") ? c.Text(nameof(Form.Description), "description", Form.Description, DescriptionMax) : null;
        var date = CheckDate(c);
        var tags = View.Visible("tags") ? CheckTags(c) : [];
        var body = CheckBody(c);
        var cover = View.Visible("cover") ? await CheckCoverAsync(c, stored, ct) : null;

        if (!ModelState.IsValid) return Page();

        // Everything is valid: now the file, then the row. A file stored for a save that does not happen is removed again.
        string? newCoverUrl = null;
        if (cover?.Image is { } image) newCoverUrl = await _uploads.SaveAsync(image, ct);

        try
        {
            BlogPost row;
            if (id is null)
            {
                row = new BlogPost { Title = title!, Slug = slug!, ConcurrencyStamp = ContentEditor.NewStamp() };
                Db.BlogPosts.Add(row);
            }
            else
            {
                var found = await Db.BlogPosts.FirstOrDefaultAsync(x => x.Id == id, ct);
                if (found is null)
                {
                    Writer.DiscardUpload(newCoverUrl);
                    return ListWithError($"That {View.ItemLabelLower} was removed in another tab, so your changes were not saved.");
                }
                row = found;
                Writer.Stamp(row, Form.Stamp);
            }

            var replacedCover = row.Cover;
            row.Title = title!;
            row.Slug = slug!;
            row.Date = date!.Value;
            row.BodyMarkdown = body!;
            row.Draft = Form.Draft;
            // Only what the template shows is written; a hidden field keeps whatever it already holds.
            if (View.Visible("description")) row.Description = description;
            if (View.Visible("tags")) row.Tags = tags;
            if (cover is not null)
            {
                if (newCoverUrl is not null) row.Cover = newCoverUrl;
                else if (cover.Remove) row.Cover = null;
                row.CoverAlt = string.IsNullOrWhiteSpace(row.Cover) ? null : cover.Alt;
            }

            var outcome = await Writer.CommitAsync(ct);
            if (outcome != SaveOutcome.Saved)
            {
                Writer.DiscardUpload(newCoverUrl);
                if (outcome == SaveOutcome.Duplicate) c.Error(nameof(Form.Slug), SlugTakenMessage(slug!));
                else AddConflict(id is null);
                return Page();
            }

            if (replacedCover != row.Cover) await Writer.DeleteUploadsAsync([replacedCover], null);
            Flash(row.Draft ? $"Saved {title} as a draft. It is not shown on your site." : $"Saved {title}. It is shown on your site.");
            return Redirect(ListUrl); // no fragment: a browser skips autofocus when the address has one, and the saved message should take focus
        }
        catch
        {
            Writer.DiscardUpload(newCoverUrl);
            throw;
        }
    }

    private string SlugTakenMessage(string slug) => $"The web address name \"{slug}\" is already used by another {View.ItemLabelLower}. Choose a different one.";

    private static string NormaliseBody(string? body) => (body ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    /// <summary>The text is Markdown, so tags are legal in it (they are shown as text when published); it only has to be present, within the cap and free of control characters.</summary>
    private string? CheckBody(FormCheck c)
    {
        var body = NormaliseBody(Form.Body);
        var label = View.Label("body");
        if (body.Length == 0) c.Error(nameof(Form.Body), $"{label} is required.");
        else if (body.Length > BodyMax) c.Error(nameof(Form.Body), $"{label} can be at most {BodyMax:N0} characters.");
        else if (ContentRules.HasControlCharacters(body, multiline: true)) c.Error(nameof(Form.Body), $"{label} contains characters that are not allowed.");
        return body;
    }

    private DateOnly? CheckDate(FormCheck c)
    {
        var text = ContentRules.Trim(Form.Date);
        var label = View.Label("date");
        if (text is null) { c.Error(nameof(Form.Date), $"{label} is required."); return null; }
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date < EarliestDate || date > LatestDate)
        {
            c.Error(nameof(Form.Date), $"{label} must be a date written as year-month-day, for example 2025-09-15.");
            return null;
        }
        return date;
    }

    private List<string> CheckTags(FormCheck c)
    {
        var label = View.Label("tags");
        var tags = new List<string>();
        foreach (var raw in (Form.Tags ?? "").Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var tag = ContentRules.Trim(raw);
            if (tag is null || tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) continue;
            if (tag.Length > TagMax) c.Error(nameof(Form.Tags), $"{label}: each can be at most {TagMax} characters.");
            else if (!TagPattern().IsMatch(tag)) c.Error(nameof(Form.Tags), $"{label}: use letters, digits, spaces and . + # & _ - only (\"{tag}\" has something else).");
            tags.Add(tag);
        }
        if (tags.Count > MaxTags) c.Error(nameof(Form.Tags), $"{label}: at most {MaxTags}.");
        return tags;
    }

    /// <summary>The address name: typed by the owner (and then checked strictly), built from the title for a new entry, or the one it already has.</summary>
    private async Task<string?> CheckSlugAsync(FormCheck c, string? title, BlogPost? stored, CancellationToken ct)
    {
        var slugs = await Db.BlogPosts.Where(p => p.Id != (stored == null ? 0 : stored.Id)).Select(p => p.Slug).ToListAsync(ct);
        var taken = new HashSet<string>(slugs, StringComparer.OrdinalIgnoreCase);

        // An address the post already has is kept exactly as stored, however old: it is only checked when the owner changes it.
        if (stored is not null && ContentRules.Trim(Form.Slug) == stored.Slug) return stored.Slug;
        var typed = ContentRules.Trim(Form.Slug)?.ToLowerInvariant();
        if (typed is null)
        {
            if (stored is not null) return stored.Slug;
            return title is null ? null : Slugs.Unique(Slugs.From(title, "post"), taken.Contains);
        }
        if (typed.Length > Slugs.MaxLength) c.Error(nameof(Form.Slug), $"{View.Label("slug")} can be at most {Slugs.MaxLength} characters.");
        else if (!Slugs.IsValid(typed)) c.Error(nameof(Form.Slug), $"{View.Label("slug")} can only use lower-case letters, digits and single hyphens, for example my-first-post.");
        else if (Slugs.IsReserved(typed)) c.Error(nameof(Form.Slug), $"\"{typed}\" is reserved. Choose a different {View.Label("slug").ToLowerInvariant()}.");
        else if (taken.Contains(typed)) c.Error(nameof(Form.Slug), SlugTakenMessage(typed));
        return typed;
    }

    private sealed record CoverChange(ProcessedImage? Image, bool Remove, string? Alt);

    /// <summary>Checks the cover image controls: a new file goes through the image pipeline, alt text is required for an image the owner uploaded.</summary>
    private async Task<CoverChange> CheckCoverAsync(FormCheck c, BlogPost? stored, CancellationToken ct)
    {
        ProcessedImage? image = null;
        if (Form.CoverFile is { Length: > 0 } file)
        {
            if (file.Length > _uploads.MaxBytes) c.Error(nameof(Form.CoverFile), $"That file is larger than {MaxSize}.");
            else
            {
                try
                {
                    image = await _processor.ProcessAsync(await UploadForm.ReadAsync(file, _uploads.MaxBytes, MaxSize, ct), ct);
                }
                catch (ImageRejectedException ex)
                {
                    c.Error(nameof(Form.CoverFile), ex.Message);
                }
            }
        }

        var remove = Form.RemoveCover && image is null;
        var hasCover = image is not null || (!remove && !string.IsNullOrWhiteSpace(stored?.Cover));
        var alt = c.Text(nameof(Form.CoverAlt), "cover", Form.CoverAlt, BlogPost.MaxLength.CoverAlt);
        // An image the owner uploaded needs a description; an older one that ships with the site keeps working without.
        var mustDescribe = hasCover && (image is not null || _uploads.TryResolve(stored?.Cover, out _));
        if (mustDescribe && alt is null && c.IsValid(nameof(Form.CoverAlt)))
            c.Error(nameof(Form.CoverAlt), "Describe the cover image in a few words for people who cannot see it (alt text).");
        return new CoverChange(image, remove, hasCover ? alt : null);
    }
}
