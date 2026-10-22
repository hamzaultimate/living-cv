using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Security;
using Portfolio.Web.Services;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Pages.Admin.Content.Projects;

public class LinkRow
{
    public string? Label { get; set; }
    public string? Url { get; set; }
    public bool IsBlank => string.IsNullOrWhiteSpace(Label) && string.IsNullOrWhiteSpace(Url);
}

public class ProjectForm
{
    public string? Title { get; set; }
    public string? Slug { get; set; }
    public string? Role { get; set; }
    public string? Org { get; set; }
    public string? Period { get; set; }
    public string? Summary { get; set; }
    public string? Category { get; set; }
    public string? Problem { get; set; }
    public string? Approach { get; set; }
    public string? ImpactText { get; set; }
    public string? TechText { get; set; }
    public List<LinkRow> Links { get; set; } = new();
    public bool Featured { get; set; }
    public IFormFile? HeroFile { get; set; }
    public string? HeroAlt { get; set; }
    public bool RemoveHero { get; set; }
    public string? Stamp { get; set; }
}

// Applies the upload cap before the antiforgery check reads the body (see BodyLimitAttribute); the cover image is the only file.
[BodyLimit]
public class EditModel : ContentPageModel
{
    public const int LinkRows = 6;
    public const int MaxLinks = 20;
    private const int TextMax = 3000;

    private readonly ImageProcessor _processor;
    private readonly UploadStore _uploads;

    public EditModel(PortfolioDbContext db, ContentEditor content, RequestTemplate template, ImageProcessor processor, UploadStore uploads) : base(db, content, template)
    {
        _processor = processor;
        _uploads = uploads;
    }

    public override string Section => SectionRegistry.Projects;

    public int? Id { get; private set; }
    public bool IsNew => Id is null;
    public string MaxSize => UploadForm.Label(_uploads.MaxBytes);

    /// <summary>The cover image the project has now, if any (for the preview and the Remove option).</summary>
    public string? CurrentHero { get; private set; }

    [BindProperty]
    public ProjectForm Form { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct)
    {
        await LoadViewAsync();
        if (id is null)
        {
            PadLinks();
            return Page();
        }

        var p = await Db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return ListWithError($"That {View.ItemLabelLower} no longer exists.");
        Id = p.Id;
        CurrentHero = p.HeroImage;
        Form = new ProjectForm
        {
            Title = p.Title, Slug = p.Slug, Role = p.Role, Org = p.Org, Period = p.Period, Summary = p.Summary, Category = p.Category,
            Problem = p.Problem, Approach = p.Approach, ImpactText = string.Join("\n", p.Impact), TechText = string.Join("\n", p.Tech),
            Links = p.Links.Select(l => new LinkRow { Label = l.Label, Url = l.Url }).ToList(), Featured = p.Featured, HeroAlt = p.HeroImageAlt, Stamp = p.ConcurrencyStamp,
        };
        PadLinks();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, CancellationToken ct)
    {
        await LoadViewAsync();
        Id = id;
        var stored = id is null ? null : await Db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (id is not null && stored is null) return ListWithError($"That {View.ItemLabelLower} was removed in another tab, so your changes were not saved.");
        CurrentHero = stored?.HeroImage;
        var c = new FormCheck(ModelState, View);

        var title = c.Text(nameof(Form.Title), "title", Form.Title, Project.MaxLength.Title, required: true);
        var summary = c.Paragraph(nameof(Form.Summary), "summary", Form.Summary, 600, required: true);
        var slug = await CheckSlugAsync(c, title, stored, ct);
        var role = View.Visible("role") ? c.Text(nameof(Form.Role), "role", Form.Role, 200) : null;
        var org = View.Visible("org") ? c.Text(nameof(Form.Org), "org", Form.Org, 200) : null;
        var period = View.Visible("period") ? c.Text(nameof(Form.Period), "period", Form.Period, 60) : null;
        var category = View.Visible("category") ? c.Text(nameof(Form.Category), "category", Form.Category, Project.MaxLength.Category) : null;
        var problem = View.Visible("problem") ? c.Paragraph(nameof(Form.Problem), "problem", Form.Problem, TextMax) : null;
        var approach = View.Visible("approach") ? c.Paragraph(nameof(Form.Approach), "approach", Form.Approach, TextMax) : null;
        var impact = View.Visible("impact") ? c.Lines(nameof(Form.ImpactText), "impact", Form.ImpactText, 8, 200) : [];
        var tech = View.Visible("tech") ? c.Lines(nameof(Form.TechText), "tech", Form.TechText, 20, 60) : [];
        var links = View.Visible("links") ? CheckLinks(c, stored) : [];
        var hero = View.Visible("heroImage") ? await CheckHeroAsync(c, stored, ct) : null;

        PadLinks();
        if (!ModelState.IsValid) return Page();

        // Everything is valid: now the file, then the row. A file stored for a save that does not happen is removed again.
        string? newHeroUrl = null;
        if (hero?.Image is { } image) newHeroUrl = await _uploads.SaveAsync(image, ct);

        try
        {
            Project row;
            if (id is null)
            {
                row = new Project
                {
                    Title = title!, Slug = slug!, Summary = summary!, ConcurrencyStamp = ContentEditor.NewStamp(),
                    SortOrder = ContentEditor.NextOrder(await Db.Projects.Select(x => x.SortOrder).ToListAsync(ct)),
                };
                Db.Projects.Add(row);
            }
            else
            {
                var found = await Db.Projects.FirstOrDefaultAsync(x => x.Id == id, ct);
                if (found is null)
                {
                    Writer.DiscardUpload(newHeroUrl);
                    return ListWithError($"That {View.ItemLabelLower} was removed in another tab, so your changes were not saved.");
                }
                row = found;
                Writer.Stamp(row, Form.Stamp);
            }

            var replacedHero = row.HeroImage;
            row.Title = title!;
            row.Slug = slug!;
            row.Summary = summary!;
            row.Featured = View.Visible("featured") ? Form.Featured : row.Featured;
            if (View.Visible("role")) row.Role = role;
            if (View.Visible("org")) row.Org = org;
            if (View.Visible("period")) row.Period = period;
            if (View.Visible("category")) row.Category = category;
            if (View.Visible("problem")) row.Problem = problem;
            if (View.Visible("approach")) row.Approach = approach;
            if (View.Visible("impact")) row.Impact = impact;
            if (View.Visible("tech")) row.Tech = tech;
            if (View.Visible("links")) row.Links = links;
            if (hero is not null)
            {
                if (newHeroUrl is not null) row.HeroImage = newHeroUrl;
                else if (hero.Remove) row.HeroImage = null;
                row.HeroImageAlt = string.IsNullOrWhiteSpace(row.HeroImage) ? null : hero.Alt;
            }

            var outcome = await Writer.CommitAsync(ct);
            if (outcome != SaveOutcome.Saved)
            {
                Writer.DiscardUpload(newHeroUrl);
                if (outcome == SaveOutcome.Duplicate) c.Error(nameof(Form.Slug), SlugTakenMessage(slug!));
                else AddConflict();
                return Page();
            }

            if (replacedHero != row.HeroImage) await Writer.DeleteUploadsAsync([replacedHero], row.Id, ct);
            Flash(id is null ? $"Added {title}. Add gallery images from its Images page." : $"Saved {title}.");
            return Redirect(ListUrl); // no fragment: a browser skips autofocus when the address has one, and the saved message should take focus
        }
        catch
        {
            Writer.DiscardUpload(newHeroUrl);
            throw;
        }
    }

    private string SlugTakenMessage(string slug) => $"The web address name \"{slug}\" is already used by another {View.ItemLabelLower}. Choose a different one.";

    /// <summary>The address name: typed by the owner (and then checked strictly), built from the title for a new entry, or the one it already has.</summary>
    private async Task<string?> CheckSlugAsync(FormCheck c, string? title, Project? stored, CancellationToken ct)
    {
        var slugs = await Db.Projects.Where(p => p.Id != (stored == null ? 0 : stored.Id)).Select(p => p.Slug).ToListAsync(ct);
        var taken = new HashSet<string>(slugs, StringComparer.OrdinalIgnoreCase);

        var typed = ContentRules.Trim(Form.Slug)?.ToLowerInvariant();
        if (typed is null)
        {
            if (stored is not null) return stored.Slug;
            return title is null ? null : Slugs.Unique(Slugs.From(title), taken.Contains);
        }
        if (typed.Length > Slugs.MaxLength) c.Error(nameof(Form.Slug), $"{View.Label("slug")} can be at most {Slugs.MaxLength} characters.");
        else if (!Slugs.IsValid(typed)) c.Error(nameof(Form.Slug), $"{View.Label("slug")} can only use lower-case letters, digits and single hyphens, for example my-project.");
        else if (Slugs.IsReserved(typed)) c.Error(nameof(Form.Slug), $"\"{typed}\" is reserved. Choose a different {View.Label("slug").ToLowerInvariant()}.");
        else if (taken.Contains(typed)) c.Error(nameof(Form.Slug), SlugTakenMessage(typed));
        return typed;
    }

    private List<Link> CheckLinks(FormCheck c, Project? stored)
    {
        var result = new List<Link>();
        var filled = Form.Links.Select((row, i) => (row, i)).Where(x => !x.row.IsBlank).ToList();
        if (filled.Count > MaxLinks)
        {
            c.Error(nameof(Form.Links), $"{View.Label("links")}: at most {MaxLinks}.");
            return result;
        }
        foreach (var (row, i) in filled)
        {
            var prefix = $"Links[{i}]";
            var label = c.Text($"{prefix}.Label", "links", row.Label, 80);
            var url = c.Url($"{prefix}.Url", "links", row.Url, 300, stored?.Links.Any(l => l.Url == row.Url?.Trim()) == true ? row.Url?.Trim() : null);
            if (label is null) c.Error($"{prefix}.Label", $"Give a label for link {i + 1}.");
            if (url is null) c.Error($"{prefix}.Url", $"Give a web address for link {i + 1}.");
            if (label is not null && url is not null) result.Add(new Link { Label = label, Url = url });
        }
        return result;
    }

    private sealed record HeroChange(ProcessedImage? Image, bool Remove, string? Alt);

    /// <summary>Checks the cover image controls: a new file goes through the image pipeline, alt text is required for an image the owner uploaded.</summary>
    private async Task<HeroChange> CheckHeroAsync(FormCheck c, Project? stored, CancellationToken ct)
    {
        ProcessedImage? image = null;
        if (Form.HeroFile is { Length: > 0 } file)
        {
            if (file.Length > _uploads.MaxBytes) c.Error(nameof(Form.HeroFile), $"That file is larger than {MaxSize}.");
            else
            {
                try
                {
                    image = await _processor.ProcessAsync(await UploadForm.ReadAsync(file, _uploads.MaxBytes, MaxSize, ct), ct);
                }
                catch (ImageRejectedException ex)
                {
                    c.Error(nameof(Form.HeroFile), ex.Message);
                }
            }
        }

        var remove = Form.RemoveHero && image is null;
        var hasHero = image is not null || (!remove && !string.IsNullOrWhiteSpace(stored?.HeroImage));
        var alt = c.Text(nameof(Form.HeroAlt), "heroImage", Form.HeroAlt, Project.MaxLength.HeroImageAlt);
        // An image the owner uploaded needs a description; an older one that ships with the site keeps working without.
        var mustDescribe = hasHero && (image is not null || _uploads.TryResolve(stored?.HeroImage, out _));
        if (mustDescribe && alt is null && c.IsValid(nameof(Form.HeroAlt)))
            c.Error(nameof(Form.HeroAlt), "Describe the cover image in a few words for people who cannot see it (alt text).");
        return new HeroChange(image, remove, hasHero ? alt : null);
    }

    private void PadLinks()
    {
        while (Form.Links.Count < LinkRows) Form.Links.Add(new LinkRow());
    }
}
