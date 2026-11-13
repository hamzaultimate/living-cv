using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Services;
using Portfolio.Web.Uploads;

namespace Portfolio.Web.Transfer;

/// <summary>
/// Reads the profile and every content table into an <see cref="ExportFile"/>. It only reads: nothing is tracked, no row is created (a profile that was never
/// stored is taken from the provider), so a GET that exports never changes the site. Rows come out in a fixed order (the public order of each list, ties by
/// id), so two exports of the same data are the same file apart from its timestamp.
/// </summary>
public sealed partial class ExportService
{
    [GeneratedRegex(@"/uploads/[0-9a-f]{32}\.(?:jpg|png|webp)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UploadInText();

    private readonly PortfolioDbContext _db;
    private readonly ISiteProfileProvider _provider;
    private readonly UploadStore _uploads;
    private readonly TimeProvider _clock;

    public ExportService(PortfolioDbContext db, ISiteProfileProvider provider, UploadStore uploads, TimeProvider clock)
    {
        _db = db;
        _provider = provider;
        _uploads = uploads;
        _clock = clock;
    }

    public async Task<ExportFile> BuildAsync(CancellationToken ct)
    {
        var profile = await _db.SiteProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == SiteProfile.SingletonId, ct)
                      ?? SiteProfileMapper.ToEntity(await _provider.GetAsync(ct));

        var experience = await _db.Experiences.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(ct);
        // Skills are ordered within their group; the groups themselves have no stored order, so they are listed alphabetically (ordinal, whatever the database collation).
        var skills = (await _db.Skills.AsNoTracking().ToListAsync(ct)).OrderBy(x => x.Group, StringComparer.Ordinal).ThenBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();
        var projects = await _db.Projects.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(ct);
        var certifications = await _db.Certifications.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(ct);
        // The public pages list talks and posts newest first; the date is the order.
        var talks = (await _db.Talks.AsNoTracking().ToListAsync(ct)).OrderByDescending(x => x.Date ?? DateOnly.MinValue).ThenBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();
        var testimonials = await _db.Testimonials.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(ct);
        var posts = (await _db.BlogPosts.AsNoTracking().ToListAsync(ct)).OrderByDescending(x => x.Date).ThenBy(x => x.Id).ToList();

        var content = new ExportContent(
            experience.Select(ExportMap.Role).ToList(), skills.Select(ExportMap.Skill).ToList(), projects.Select(ExportMap.Project).ToList(),
            certifications.Select(ExportMap.Certification).ToList(), talks.Select(ExportMap.Talk).ToList(), testimonials.Select(ExportMap.Testimonial).ToList(),
            posts.Select(ExportMap.Post).ToList());

        var exported = new ExportProfile(
            profile.OwnerName, profile.BrandName, profile.Role, profile.JobTitle, profile.Url, ExportMap.Opt(profile.GoogleAnalyticsId), profile.Summary, ExportMap.Opt(profile.Location), profile.KnowsAbout.ToList(),
            ExportMap.Opt(profile.PhotoUrl), ExportMap.Opt(profile.PhotoUrlLight), ExportMap.Opt(profile.Phone), ExportMap.Opt(profile.WhatsApp), ExportMap.Opt(profile.Email), profile.SecondaryEmails.ToList(),
            ExportMap.Opt(profile.LinkedIn), ExportMap.Opt(profile.GitHub), ExportMap.Opt(profile.YouTube), profile.TemplateKey, ExportMap.Opt(profile.AccentColor),
            new ExportHero(ExportMap.Opt(profile.Hero.Eyebrow), ExportMap.Opt(profile.Hero.Headline), ExportMap.Opt(profile.Hero.HeadlineAccent), ExportMap.Opt(profile.Hero.Lede)),
            profile.Metrics.Select(m => new ExportMetric(m.Value, ExportMap.Opt(m.Unit), ExportMap.Opt(m.Label))).ToList(),
            new ExportAbout(ExportMap.Opt(profile.About.Headline), profile.About.Paragraphs.ToList(), profile.About.ValueProps.Select(v => new ExportCard(v.Title, v.Body)).ToList(), ExportMap.Opt(profile.About.CurrentlyNote), profile.About.Focus.ToList()),
            // Every stored setting is written, the ones for keys the template does not list included: they are inert, and an import keeps them that way.
            profile.Sections.Select(s => new ExportSection(s.Key, ExportMap.Opt(s.Label), s.Enabled, s.Shown, s.SortOrder)).ToList());

        return new ExportFile(TransferLimits.SchemaVersion, TransferLimits.Format, _clock.GetUtcNow(), exported, content, Manifest(profile, projects, posts));
    }

    /// <summary>The uploaded files the data points at (ordered by path, with where each is used), and whether each is in the uploads folder now.</summary>
    private IReadOnlyList<ExportUpload> Manifest(SiteProfile profile, IEnumerable<Project> projects, IEnumerable<BlogPost> posts)
    {
        var uses = new List<(string Path, ExportUse Use)>();
        void Add(string? url, string table, string? entry, string field)
        {
            if (url is not null && _uploads.TryResolve(url, out _)) uses.Add((url, new ExportUse(table, entry, field)));
        }
        Add(profile.PhotoUrl, "profile", null, "photoUrl");
        Add(profile.PhotoUrlLight, "profile", null, "photoUrlLight");
        foreach (var p in projects)
        {
            Add(p.HeroImage, "projects", p.Slug, "heroImage");
            foreach (var (shot, i) in p.Screenshots.Select((s, i) => (s, i))) Add(shot.Url, "projects", p.Slug, $"screenshots[{i}]");
        }
        foreach (var b in posts)
        {
            Add(b.Cover, "blog", b.Slug, "cover");
            foreach (var match in UploadInText().Matches(b.BodyMarkdown).Select(m => m.Value).Distinct()) Add(match, "blog", b.Slug, "body");
        }

        return uses.GroupBy(u => u.Path, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ExportUpload(g.Key, _uploads.TryResolve(g.Key, out var full) && File.Exists(full), g.Select(u => u.Use).ToList())).ToList();
    }
}

/// <summary>Row to file shape. Shared by the export and by the import, which compares a row with what the file would make of it.</summary>
public static class ExportMap
{
    /// <summary>Empty text and no text are the same thing to the editors (they store a field they clear as null), so an optional field is written as null when it is empty: the file says one thing, whichever way the row stores it.</summary>
    public static string? Opt(string? value) => string.IsNullOrEmpty(value) ? null : value;

    public static ExportRole Role(Experience e) => new(e.Company, e.Title, e.Start, Opt(e.End), e.Current, e.Summary, e.Highlights.ToList(), e.Tech.ToList());

    public static ExportSkill Skill(Skill s) => new(s.Name, s.Group);

    private static List<ExportProof> Proofs(IEnumerable<Proof> proofs) => proofs.Select(x => new ExportProof(x.Label, x.Url, x.Kind)).ToList();

    public static ExportProject Project(Project p) => new(p.Title, p.Slug, Opt(p.Role), Opt(p.Org), Opt(p.Period), p.Summary, Opt(p.Category), Opt(p.Problem), Opt(p.Approach), p.Impact.ToList(), p.Tech.ToList(),
        p.Links.Select(l => new ExportLink(l.Label, l.Url)).ToList(), Opt(p.HeroImage), Opt(p.HeroImageAlt), p.Screenshots.Select(i => new ExportImage(i.Url, Opt(i.Caption))).ToList(), Proofs(p.Proofs), p.Featured);

    public static ExportCertification Certification(Certification c) => new(c.Name, c.Issuer, c.Status, Opt(c.Date), Opt(c.Category), Opt(c.CredentialUrl), Opt(c.CertificateUrl), Opt(c.BadgeImageUrl), Proofs(c.Proofs));

    public static ExportTalk Talk(Talk t) => new(t.Title, t.Type, Opt(t.Event), t.Date, Opt(t.YoutubeId), Opt(t.Url));

    public static ExportTestimonial Testimonial(Testimonial t) => new(t.Quote, t.Author, Opt(t.AuthorTitle), Opt(t.SourceUrl), Opt(t.Relation), t.Featured);

    public static ExportPost Post(BlogPost b) => new(b.Title, b.Slug, Opt(b.Description), b.Date, b.Tags.ToList(), b.Draft, Opt(b.Cover), Opt(b.CoverAlt), b.BodyMarkdown);
}
