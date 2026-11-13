using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using BlogEditor = Portfolio.Web.Pages.Admin.Content.Blog.EditModel;
using ExperienceEditor = Portfolio.Web.Pages.Admin.Content.Experiences.EditModel;
using ProjectEditor = Portfolio.Web.Pages.Admin.Content.Projects.EditModel;
using ImagesEditor = Portfolio.Web.Pages.Admin.Content.Projects.ImagesModel;
using TalkEditor = Portfolio.Web.Pages.Admin.Content.Talks.EditModel;
using TestimonialEditor = Portfolio.Web.Pages.Admin.Content.Testimonials.EditModel;

namespace Portfolio.Web.Transfer;

// One spec per content table. Each reads a file row with the rules of the editor for the same fields (same limits, same plain-text and address checks, same
// messages through FormCheck), so a value the editor would refuse is refused here too. Where an editor takes a limit from a number inside its own method, the
// number is repeated here, and the parity tests in ImportEditorParityTests fail if the two ever disagree.

/// <summary>The checks that the proofs of a project or a certification share (they have no editor yet).</summary>
internal static class ProofRows
{
    public static void Read<T>(RowReader r, Patch<T> patch, Action<T, List<Proof>> write)
    {
        if (!r.TryObjects("proofs", "proofs", out var objects)) return;
        if (objects.Count > ImportRules.MaxProofs) { r.Check.Error("proofs", $"{r.View.Label("proofs")}: at most {ImportRules.MaxProofs}."); return; }
        var proofs = new List<Proof>();
        for (var i = 0; i < objects.Count; i++)
        {
            var row = r.Nested(objects[i], $"proofs[{i}]", $"Proof {i + 1}", ["label", "url", "kind"]);
            var label = row.Text("label", "proofLabel", ImportRules.ProofLabelMax, required: true);
            var url = row.Url("url", "proofUrl", ImportRules.ProofUrlMax);
            if (url.Value is null && row.Check.IsValid($"proofs[{i}].url")) r.Check.Error($"proofs[{i}].url", $"Proof {i + 1}: \"{r.View.Label("proofUrl")}\" is required.");
            var kind = ProofKind.Link;
            if (row.Has("kind"))
            {
                var parsed = row.Text("kind", "proofKind", 20);
                if (ImportRules.EnumValue<ProofKind>(parsed.Value) is { } k) kind = k;
                else if (row.Check.IsValid($"proofs[{i}].kind")) r.Check.Error($"proofs[{i}].kind", $"Proof {i + 1}: choose one of {string.Join(", ", Enum.GetNames<ProofKind>())} for \"{r.View.Label("proofKind")}\".");
            }
            if (label.Value is not null && url.Value is not null) proofs.Add(new Proof { Label = label.Value, Url = url.Value, Kind = kind });
        }
        patch.Add(row => write(row, proofs));
    }
}

public sealed class ExperienceSpec : TableSpec<Experience>
{
    private static readonly string[] Names = ["company", "title", "start", "end", "current", "summary", "highlights", "tech"];

    public override string Section => SectionRegistry.Experience;
    public override IReadOnlyCollection<string> Known => Names;
    public override string? KeyOf(JsonElement row) => Peek(row, "company") is { } c && Peek(row, "title") is { } t ? Join(c, t) : null;
    public override string KeyOf(Experience e) => Join(e.Company.Trim(), e.Title.Trim());
    public override string? NameOf(JsonElement row) => Peek(row, "title") is { } t ? Peek(row, "company") is { } c ? $"{t} at {c}" : t : null;
    public override string KeyWords(EditorView view) => $"{view.Label("company").ToLowerInvariant()} and {view.Label("title").ToLowerInvariant()}";
    public override Experience NewRow() => new() { Company = "", Title = "", Start = "", Summary = "" };
    public override IQueryable<Experience> Stored(PortfolioDbContext db) => db.Experiences.AsNoTracking();
    public override DbSet<Experience> Set(PortfolioDbContext db) => db.Experiences;
    public override string Fingerprint(Experience row) => Print(ExportMap.Role(row));

    public override void Fill(RowReader r, Experience? stored, Patch<Experience> p)
    {
        var c = r.Check;
        var view = r.View;
        var company = r.Text("company", "company", 200, required: true);
        var title = r.Text("title", "title", 200, required: true);
        // The seeder identifies a role by "company|title", so a bar in either would let two different roles share a key. A value the row already has is kept.
        if (company.Value?.Contains('|') == true && company.Value != stored?.Company) c.Error("company", $"{view.Label("company")} cannot contain the | character.");
        if (title.Value?.Contains('|') == true && title.Value != stored?.Title) c.Error("title", $"{view.Label("title")} cannot contain the | character.");
        var start = r.Text("start", "start", 30, required: true);
        var summary = r.Paragraph("summary", "summary", ExperienceEditor.SummaryMax, required: true);
        p.Set(company, (row, v) => row.Company = v!);
        p.Set(title, (row, v) => row.Title = v!);
        p.Set(start, (row, v) => row.Start = v!);
        p.Set(summary, (row, v) => row.Summary = v!);

        PartialDate? startDate = null;
        var startText = start.Present ? start.Value : stored?.Start;
        if (startText is not null && (!start.Present || c.IsValid("start")))
        {
            if (PartialDate.TryParse(startText, out var parsed)) startDate = parsed;
            else if (start.Present) c.Error("start", $"{view.Label("start")} must be a year or a month and year, for example 2021, Mar 2021 or 2021-03.");
        }

        var end = r.Text("end", "end", 30);
        var current = r.Flag("current", "current");
        var currentNow = current.Present ? current.Value : stored?.Current ?? false;
        var endNow = end.Present ? end.Value : stored?.End;
        if (end.Present || current.Present || r.Create)
        {
            if (currentNow) endNow = null;
            else if (endNow is not null)
            {
                if (end.Present && c.IsValid("end"))
                {
                    if (!PartialDate.TryParse(endNow, out var endDate)) c.Error("end", $"{view.Label("end")} must be a year or a month and year, for example 2023, Mar 2023 or 2023-03.");
                    else if (startDate is { } sd && endDate.Last < sd.First) c.Error("end", $"{view.Label("end")} cannot be earlier than {view.Label("start").ToLowerInvariant()}.");
                }
            }
            else if (view.Visible("end") && view.Visible("current"))
                c.Error("end", $"Give an {view.Label("end").ToLowerInvariant()} date, or tick \"{view.Label("current")}\".");
            p.Add(row => { row.Current = currentNow; row.End = endNow; });
        }

        p.Set(r.Lines("highlights", "highlights", 12, 300), (row, v) => row.Highlights = v!);
        p.Set(r.Lines("tech", "tech", 20, 60), (row, v) => row.Tech = v!);
    }
}

public sealed class SkillSpec : TableSpec<Skill>
{
    private static readonly string[] Names = ["name", "group"];

    public override string Section => SectionRegistry.Skills;
    public override IReadOnlyCollection<string> Known => Names;
    public override string? KeyOf(JsonElement row) => Peek(row, "name");
    public override string KeyOf(Skill s) => s.Name.Trim();
    public override string? NameOf(JsonElement row) => Peek(row, "name");
    public override string KeyWords(EditorView view) => view.Label("name").ToLowerInvariant();
    public override string? GroupOf(Skill row) => row.Group;
    public override Skill NewRow() => new() { Name = "", Group = "" };
    public override IQueryable<Skill> Stored(PortfolioDbContext db) => db.Skills.AsNoTracking();
    public override DbSet<Skill> Set(PortfolioDbContext db) => db.Skills;
    public override string Fingerprint(Skill row) => Print(ExportMap.Skill(row));

    public override void Fill(RowReader r, Skill? stored, Patch<Skill> p)
    {
        var name = r.Text("name", "name", 100, required: true);
        var group = r.Text("group", "group", 60, required: true);
        p.Set(name, (row, v) => row.Name = v!);
        p.Set(group, (row, v) => row.Group = v!);
        p.Group = group.Present ? group.Value : stored?.Group;
    }
}

public sealed class ProjectSpec : TableSpec<Project>
{
    private static readonly string[] Names =
        ["title", "slug", "role", "org", "period", "summary", "category", "problem", "approach", "impact", "tech", "links", "heroImage", "heroImageAlt", "screenshots", "proofs", "featured"];

    public override string Section => SectionRegistry.Projects;
    public override IReadOnlyCollection<string> Known => Names;

    // A project is the same project when its address name is. A file that gives none is matched by the name the editor would build from the title, so importing the same file twice updates rather than adds.
    public override string? KeyOf(JsonElement row) => Peek(row, "slug")?.ToLowerInvariant() ?? (Peek(row, "title") is { } t ? Slugs.From(t) : null);
    public override string KeyOf(Project p) => p.Slug;
    public override string? NameOf(JsonElement row) => Peek(row, "title");
    public override string KeyWords(EditorView view) => view.Label("slug").ToLowerInvariant();
    public override Project NewRow() => new() { Title = "", Slug = "", Summary = "" };
    public override IQueryable<Project> Stored(PortfolioDbContext db) => db.Projects.AsNoTracking();
    public override DbSet<Project> Set(PortfolioDbContext db) => db.Projects;
    public override string Fingerprint(Project row) => Print(ExportMap.Project(row));

    public override void Fill(RowReader r, Project? stored, Patch<Project> p)
    {
        var c = r.Check;
        var view = r.View;
        var title = r.Text("title", "title", Project.MaxLength.Title, required: true);
        var summary = r.Paragraph("summary", "summary", 600, required: true);
        p.Set(title, (row, v) => row.Title = v!);
        p.Set(summary, (row, v) => row.Summary = v!);

        var slug = Slug(r, stored, title.Value);
        if (slug is not null) p.Add(row => row.Slug = slug);

        p.Set(r.Text("role", "role", 200), (row, v) => row.Role = v);
        p.Set(r.Text("org", "org", 200), (row, v) => row.Org = v);
        p.Set(r.Text("period", "period", 60), (row, v) => row.Period = v);
        p.Set(r.Text("category", "category", Project.MaxLength.Category), (row, v) => row.Category = v);
        p.Set(r.Paragraph("problem", "problem", 3000), (row, v) => row.Problem = v);
        p.Set(r.Paragraph("approach", "approach", 3000), (row, v) => row.Approach = v);
        p.Set(r.Lines("impact", "impact", 8, 200), (row, v) => row.Impact = v!);
        p.Set(r.Lines("tech", "tech", 20, 60), (row, v) => row.Tech = v!);
        p.Set(r.Flag("featured", "featured"), (row, v) => row.Featured = v);

        if (r.TryObjects("links", "links", out var links))
        {
            if (links.Count > ProjectEditor.MaxLinks) c.Error("links", $"{view.Label("links")}: at most {ProjectEditor.MaxLinks}.");
            else
            {
                var result = new List<Link>();
                for (var i = 0; i < links.Count; i++)
                {
                    var row = r.Nested(links[i], $"links[{i}]", $"Link {i + 1}", ["label", "url"]);
                    var label = row.Text("label", "links", 80);
                    var url = row.Url("url", "links", 300);
                    if (label.Value is null) c.Error($"links[{i}].label", $"Give a label for link {i + 1}.");
                    if (url.Value is null) c.Error($"links[{i}].url", $"Give a web address for link {i + 1}.");
                    if (label.Value is not null && url.Value is not null) result.Add(new Link { Label = label.Value, Url = url.Value });
                }
                p.Add(row => row.Links = result);
            }
        }

        // The cover image: a web address, a bundled picture, or an upload that is already here. A reference that cannot be kept is dropped, and its description with it.
        var hero = r.Image("heroImage", "heroImage", Project.MaxLength.HeroImage);
        var heroAlt = r.Text("heroImageAlt", "heroImage", Project.MaxLength.HeroImageAlt);
        p.Set(hero, (row, v) => row.HeroImage = v);
        p.Set(heroAlt, (row, v) => row.HeroImageAlt = v);
        if (hero.Present || heroAlt.Present) p.Add(row => { if (string.IsNullOrWhiteSpace(row.HeroImage)) row.HeroImageAlt = null; });

        if (r.TryObjects("screenshots", "screenshots", out var shots))
        {
            if (shots.Count > ImagesEditor.MaxImages) c.Error("screenshots", $"A {view.ItemLabelLower} can have at most {ImagesEditor.MaxImages} gallery images.");
            else
            {
                var images = new List<ProjectImage>();
                for (var i = 0; i < shots.Count; i++)
                {
                    var row = r.Nested(shots[i], $"screenshots[{i}]", $"Image {i + 1}", ["url", "caption"]);
                    var url = row.Image("url", "screenshots", 400);
                    var caption = row.Text("caption", "screenshots", ImagesEditor.CaptionMax);
                    if (caption.Value is null && c.IsValid($"screenshots[{i}].caption")) c.Error($"screenshots[{i}].caption", $"Image {i + 1} needs a description in a few words (alt text).");
                    if (url.Value is not null && caption.Value is not null) images.Add(new ProjectImage { Url = url.Value, Caption = caption.Value });
                }
                p.Add(row => row.Screenshots = images);
            }
        }

        ProofRows.Read(r, p, (row, proofs) => row.Proofs = proofs);
    }

    /// <summary>The address name, with the editor's rules: kept as stored when the file says the same, otherwise checked; built from the title when the file gives none.</summary>
    private static string? Slug(RowReader r, Project? stored, string? title)
    {
        var typedRaw = r.Has("slug") && r.Raw("slug") is { ValueKind: JsonValueKind.String } e ? ContentRules.Trim(e.GetString()) : null;
        if (stored is not null && typedRaw == stored.Slug) return stored.Slug;
        var typed = typedRaw?.ToLowerInvariant();
        if (typed is null) return stored?.Slug ?? (title is null ? null : Slugs.From(title));
        if (typed.Length > Slugs.MaxLength) r.Check.Error("slug", $"{r.View.Label("slug")} can be at most {Slugs.MaxLength} characters.");
        else if (!Slugs.IsValid(typed)) r.Check.Error("slug", $"{r.View.Label("slug")} can only use lower-case letters, digits and single hyphens, for example my-project.");
        else if (Slugs.IsReserved(typed)) r.Check.Error("slug", $"\"{typed}\" is reserved. Choose a different {r.View.Label("slug").ToLowerInvariant()}.");
        return typed;
    }
}

public sealed class CertificationSpec : TableSpec<Certification>
{
    private static readonly string[] Names = ["name", "issuer", "status", "date", "category", "credentialUrl", "certificateUrl", "badgeImageUrl", "proofs"];

    public override string Section => SectionRegistry.Certifications;
    public override IReadOnlyCollection<string> Known => Names;
    public override string? KeyOf(JsonElement row) => Peek(row, "name");
    public override string KeyOf(Certification c) => c.Name.Trim();
    public override string? NameOf(JsonElement row) => Peek(row, "name");
    public override string KeyWords(EditorView view) => view.Label("name").ToLowerInvariant();
    public override Certification NewRow() => new() { Name = "", Issuer = "" };
    public override IQueryable<Certification> Stored(PortfolioDbContext db) => db.Certifications.AsNoTracking();
    public override DbSet<Certification> Set(PortfolioDbContext db) => db.Certifications;
    public override string Fingerprint(Certification row) => Print(ExportMap.Certification(row));

    public override void Fill(RowReader r, Certification? stored, Patch<Certification> p)
    {
        var c = r.Check;
        var view = r.View;
        var name = r.Text("name", "name", 200, required: true);
        var issuer = r.Text("issuer", "issuer", 160, required: true);
        p.Set(name, (row, v) => row.Name = v!);
        p.Set(issuer, (row, v) => row.Issuer = v!);

        var statusText = r.Text("status", "status", 20, required: true);
        CertificationStatus? status = null;
        if (statusText.Present)
        {
            status = ImportRules.EnumValue<CertificationStatus>(statusText.Value);
            c.Choice("status", "status", status?.ToString() ?? statusText.Value, Enum.GetNames<CertificationStatus>());
            if (status is { } s) p.Add(row => row.Status = s);
        }

        var date = r.Text("date", "date", 30);
        p.Set(date, (row, v) => row.Date = v);
        var effectiveStatus = status ?? stored?.Status ?? CertificationStatus.Earned;
        if (date.Present && date.Value is not null && c.IsValid("date"))
        {
            if (!PartialDate.TryParse(date.Value, out var parsed)) c.Error("date", $"{view.Label("date")} must be a year or a month and year, for example 2024, Mar 2024 or 2024-03.");
            else if (effectiveStatus == CertificationStatus.Earned && parsed.First > DateOnly.FromDateTime(r.Context.Clock.GetUtcNow().UtcDateTime))
                c.Error("date", $"{view.Label("date")} is in the future, so this cannot be marked {view.StatusLabel(CertificationStatus.Earned).ToLowerInvariant()}. Choose another status or the date it was awarded.");
        }
        else if (!date.Present && status is not null && stored?.Date is { } storedDate && effectiveStatus == CertificationStatus.Earned
                 && PartialDate.TryParse(storedDate, out var storedParsed) && storedParsed.First > DateOnly.FromDateTime(r.Context.Clock.GetUtcNow().UtcDateTime))
            c.Error("status", $"{view.Label("date")} is in the future, so this cannot be marked {view.StatusLabel(CertificationStatus.Earned).ToLowerInvariant()}. Choose another status or the date it was awarded.");

        p.Set(r.Text("category", "category", 80), (row, v) => row.Category = v);
        p.Set(r.Url("credentialUrl", "credentialUrl", 400), (row, v) => row.CredentialUrl = v);
        p.Set(r.Url("certificateUrl", "certificateUrl", 400), (row, v) => row.CertificateUrl = v);
        p.Set(r.Url("badgeImageUrl", "badgeImageUrl", 400), (row, v) => row.BadgeImageUrl = v);
        ProofRows.Read(r, p, (row, proofs) => row.Proofs = proofs);
    }
}

public sealed class TalkSpec : TableSpec<Talk>
{
    private static readonly string[] Names = ["title", "type", "event", "date", "youtubeId", "url"];

    public override string Section => SectionRegistry.Talks;
    public override IReadOnlyCollection<string> Known => Names;
    public override bool FollowsFileOrder => false;
    public override string? KeyOf(JsonElement row) => Peek(row, "title");
    public override string KeyOf(Talk t) => t.Title.Trim();
    public override string? NameOf(JsonElement row) => Peek(row, "title");
    public override string KeyWords(EditorView view) => view.Label("title").ToLowerInvariant();
    public override Talk NewRow() => new() { Title = "" };
    public override IQueryable<Talk> Stored(PortfolioDbContext db) => db.Talks.AsNoTracking();
    public override DbSet<Talk> Set(PortfolioDbContext db) => db.Talks;
    public override string Fingerprint(Talk row) => Print(ExportMap.Talk(row));

    public override void Fill(RowReader r, Talk? stored, Patch<Talk> p)
    {
        var c = r.Check;
        var view = r.View;
        p.Set(r.Text("title", "title", TalkEditor.TitleMax, required: true), (row, v) => row.Title = v!);

        if (r.Has("type"))
        {
            var typeText = r.Text("type", "type", 20);
            var parsed = ImportRules.EnumValue<TalkType>(typeText.Value);
            if (typeText.Value is not null) c.Choice("type", "type", parsed?.ToString() ?? typeText.Value, Enum.GetNames<TalkType>(), required: false);
            if (parsed is { } t) p.Add(row => row.Type = t);
            else if (typeText.Present && typeText.Value is null) p.Add(row => row.Type = stored?.Type ?? TalkType.Video);
        }

        p.Set(r.Text("event", "event", TalkEditor.EventMax), (row, v) => row.Event = v);

        var date = r.Text("date", "date", 30);
        if (date.Present)
        {
            DateOnly? parsedDate = null;
            if (date.Value is not null && c.IsValid("date"))
            {
                if (PartialDate.TryParse(date.Value, out var parsed)) parsedDate = parsed.First;
                else c.Error("date", $"{view.Label("date")} must be a date, or a month and year, for example 2025-05-14, 2025-05 or 2025.");
            }
            p.Add(row => row.Date = parsedDate);
        }

        if (r.Has("youtubeId"))
        {
            var raw = r.Text("youtubeId", "youtubeId", 200);
            if (raw.Present)
            {
                var id = raw.Value is null ? null : ContentRules.YoutubeId(raw.Value);
                // An id that is already stored can be kept as it is, so an older entry can be written back untouched.
                if (id is null && raw.Value is not null && raw.Value == stored?.YoutubeId) id = raw.Value;
                else if (id is null && raw.Value is not null && c.IsValid("youtubeId")) c.Error("youtubeId", $"{view.Label("youtubeId")} must be a YouTube link or the 11 character video id.");
                p.Add(row => row.YoutubeId = id);
            }
        }

        p.Set(r.Url("url", "url", TalkEditor.UrlMax), (row, v) => row.Url = v);
    }
}

public sealed class TestimonialSpec : TableSpec<Testimonial>
{
    private static readonly string[] Names = ["quote", "author", "authorTitle", "sourceUrl", "relation", "featured"];

    public override string Section => SectionRegistry.Testimonials;
    public override IReadOnlyCollection<string> Known => Names;

    // The seeder's key: the author and the first 40 characters of the quote.
    private static string Key(string author, string quote) => Join(author, quote[..Math.Min(40, quote.Length)]);

    public override string? KeyOf(JsonElement row) =>
        Peek(row, "author") is { } a && row.TryGetProperty("quote", out var q) && q.ValueKind == JsonValueKind.String && ContentRules.Paragraph(q.GetString()) is { } quote ? Key(a, quote) : null;
    public override string KeyOf(Testimonial t) => Key(t.Author.Trim(), t.Quote);
    public override string? NameOf(JsonElement row) => Peek(row, "author");
    public override string KeyWords(EditorView view) => $"{view.Label("author").ToLowerInvariant()} and the start of the {view.Label("quote").ToLowerInvariant()}";
    public override Testimonial NewRow() => new() { Quote = "", Author = "" };
    public override IQueryable<Testimonial> Stored(PortfolioDbContext db) => db.Testimonials.AsNoTracking();
    public override DbSet<Testimonial> Set(PortfolioDbContext db) => db.Testimonials;
    public override string Fingerprint(Testimonial row) => Print(ExportMap.Testimonial(row));

    public override void Fill(RowReader r, Testimonial? stored, Patch<Testimonial> p)
    {
        p.Set(r.Paragraph("quote", "quote", TestimonialEditor.QuoteMax, required: true), (row, v) => row.Quote = v!);
        p.Set(r.Text("author", "author", TestimonialEditor.AuthorMax, required: true), (row, v) => row.Author = v!);
        p.Set(r.Text("authorTitle", "authorTitle", TestimonialEditor.AuthorTitleMax), (row, v) => row.AuthorTitle = v);
        p.Set(r.Url("sourceUrl", "sourceUrl", TestimonialEditor.UrlMax), (row, v) => row.SourceUrl = v);
        p.Set(r.Text("relation", "relation", 200), (row, v) => row.Relation = v);
        p.Set(r.Flag("featured", "featured"), (row, v) => row.Featured = v);
    }
}

public sealed class PostSpec : TableSpec<BlogPost>
{
    private static readonly string[] Names = ["title", "slug", "description", "date", "tags", "draft", "cover", "coverAlt", "body"];
    private static readonly DateOnly Earliest = new(1990, 1, 1);
    private static readonly DateOnly Latest = new(2100, 12, 31);

    public override string Section => SectionRegistry.Blog;
    public override IReadOnlyCollection<string> Known => Names;
    public override bool FollowsFileOrder => false;
    public override string? KeyOf(JsonElement row) => Peek(row, "slug")?.ToLowerInvariant() ?? (Peek(row, "title") is { } t ? Slugs.From(t, "post") : null);
    public override string KeyOf(BlogPost b) => b.Slug;
    public override string? NameOf(JsonElement row) => Peek(row, "title");
    public override string KeyWords(EditorView view) => view.Label("slug").ToLowerInvariant();
    public override BlogPost NewRow() => new() { Title = "", Slug = "" };
    public override IQueryable<BlogPost> Stored(PortfolioDbContext db) => db.BlogPosts.AsNoTracking();
    public override DbSet<BlogPost> Set(PortfolioDbContext db) => db.BlogPosts;
    public override string Fingerprint(BlogPost row) => Print(ExportMap.Post(row));

    public override void Fill(RowReader r, BlogPost? stored, Patch<BlogPost> p)
    {
        var c = r.Check;
        var view = r.View;
        var title = r.Text("title", "title", BlogPost.MaxLength.Title, required: true);
        p.Set(title, (row, v) => row.Title = v!);

        var typedRaw = r.Raw("slug") is { ValueKind: JsonValueKind.String } e ? ContentRules.Trim(e.GetString()) : null;
        string? slug;
        if (stored is not null && typedRaw == stored.Slug) slug = stored.Slug;
        else
        {
            var typed = typedRaw?.ToLowerInvariant();
            if (typed is null) slug = stored?.Slug ?? (title.Value is null ? null : Slugs.From(title.Value, "post"));
            else
            {
                if (typed.Length > Slugs.MaxLength) c.Error("slug", $"{view.Label("slug")} can be at most {Slugs.MaxLength} characters.");
                else if (!Slugs.IsValid(typed)) c.Error("slug", $"{view.Label("slug")} can only use lower-case letters, digits and single hyphens, for example my-first-post.");
                else if (Slugs.IsReserved(typed)) c.Error("slug", $"\"{typed}\" is reserved. Choose a different {view.Label("slug").ToLowerInvariant()}.");
                slug = typed;
            }
        }
        if (slug is not null) p.Add(row => row.Slug = slug);

        p.Set(r.Text("description", "description", BlogEditor.DescriptionMax), (row, v) => row.Description = v);

        var date = r.Text("date", "date", 30, required: true);
        if (date.Present && date.Value is not null && c.IsValid("date"))
        {
            if (DateOnly.TryParseExact(date.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) && parsed >= Earliest && parsed <= Latest) p.Add(row => row.Date = parsed);
            else c.Error("date", $"{view.Label("date")} must be a date written as year-month-day, for example 2025-09-15.");
        }

        if (r.TryStrings("tags", "tags", out var rawTags))
        {
            var label = view.Label("tags");
            var tags = new List<string>();
            foreach (var raw in rawTags)
            {
                var tag = ContentRules.Trim(raw);
                if (tag is null || tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) continue;
                if (tag.Length > BlogEditor.TagMax) c.Error("tags", $"{label}: each can be at most {BlogEditor.TagMax} characters.");
                else if (!ContentRules.IsTag(tag)) c.Error("tags", $"{label}: use letters, digits, spaces and . + # & _ - only (\"{tag}\" has something else).");
                tags.Add(tag);
            }
            if (tags.Count > BlogEditor.MaxTags) c.Error("tags", $"{label}: at most {BlogEditor.MaxTags}.");
            p.Add(row => row.Tags = tags);
        }

        // A new post that does not say is a draft (as in the editor); an existing one keeps what it is.
        var draft = r.Flag("draft", "draft");
        if (draft.Present) p.Add(row => row.Draft = draft.Value);
        else if (r.Create) p.Add(row => row.Draft = true);

        var cover = r.Image("cover", "cover", 400);
        var coverAlt = r.Text("coverAlt", "cover", BlogPost.MaxLength.CoverAlt);
        p.Set(cover, (row, v) => row.Cover = v);
        p.Set(coverAlt, (row, v) => row.CoverAlt = v);
        if (cover.Present || coverAlt.Present) p.Add(row => { if (string.IsNullOrWhiteSpace(row.Cover)) row.CoverAlt = null; });

        Body(r, p);
    }

    /// <summary>The text is Markdown, so tags are legal in it (they are shown as text when published). It must be present, within the cap, free of control characters and one the safe renderer accepts.</summary>
    private static void Body(RowReader r, Patch<BlogPost> p)
    {
        var view = r.View;
        var label = view.Label("body");
        if (!r.Has("body"))
        {
            if (r.Create) r.Check.Error("body", $"{label} is required.");
            return;
        }
        if (r.Raw("body") is not { } element || element.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            r.Check.Error("body", $"{label} must be text.");
            return;
        }
        var body = (element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (body.Length == 0) r.Check.Error("body", $"{label} is required.");
        else if (body.Length > BlogEditor.BodyMax) r.Check.Error("body", $"{label} can be at most {BlogEditor.BodyMax:N0} characters.");
        else if (ContentRules.HasControlCharacters(body, multiline: true)) r.Check.Error("body", $"{label} contains characters that are not allowed.");
        else
        {
            var problem = SafeMd.TryRender(body, out _, remember: false);
            if (problem != SafeMd.Problem.None) r.Check.Error("body", Problem(label, problem));
            else p.Add(row => { row.BodyMarkdown = body; });
        }
    }

    private static string Problem(string label, SafeMd.Problem problem) => problem switch
    {
        SafeMd.Problem.TooManyHeadings => $"{label} has more than {SafeMd.MaxHeadings} headings. Remove some of them.",
        SafeMd.Problem.TooManyBrackets => $"{label} has more than {SafeMd.MaxBrackets:N0} opening square brackets (links, images and references). Remove some of them.",
        SafeMd.Problem.TooDeeplyNested => $"{label} is nested too deeply: lists, quotes or emphasis inside one another many levels down. Flatten it and try again.",
        _ => $"{label} could not be turned into a page. Simplify it and try again.",
    };
}
