using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Web.Configuration;

namespace Portfolio.Web.Services;

/// <summary>
/// Single resilient entry point for reading portfolio content. Every query runs in its own
/// scope and falls back to an empty result if the database isn't configured or is unreachable,
/// so pages render (with empty states) instead of erroring.
/// </summary>
public class PortfolioContentService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly TemplateResolver _templates;
    private readonly ILogger<PortfolioContentService> _logger;

    public PortfolioContentService(IServiceScopeFactory scopeFactory, IConfiguration config, TemplateResolver templates, ILogger<PortfolioContentService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _templates = templates;
        _logger = logger;
    }

    public bool DatabaseConfigured => !string.IsNullOrWhiteSpace(_config.GetConnectionString("Default"));

    /// <summary>True once any query on this (scoped) instance failed and returned its fallback.
    /// Not set when the database simply isn't configured. Callers use it to avoid caching output
    /// built from a transient failure.</summary>
    public bool HadFallback { get; private set; }

    private async Task<T> QueryAsync<T>(Func<PortfolioDbContext, Task<T>> query, T fallback)
    {
        if (!DatabaseConfigured) return fallback;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            return await query(db);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Content query failed; returning fallback.");
            HadFallback = true;
            return fallback;
        }
    }

    public Task<List<Project>> GetProjectsAsync() =>
        QueryAsync(db => db.Projects.OrderBy(p => p.SortOrder).ToListAsync(), new List<Project>());

    public Task<List<Project>> GetFeaturedProjectsAsync() =>
        QueryAsync(db => db.Projects.Where(p => p.Featured).OrderBy(p => p.SortOrder).ToListAsync(), new List<Project>());

    public Task<Project?> GetProjectAsync(string slug) =>
        QueryAsync(db => db.Projects.FirstOrDefaultAsync(p => p.Slug == slug), (Project?)null);

    public Task<List<Experience>> GetExperiencesAsync() =>
        QueryAsync(db => db.Experiences.OrderBy(e => e.SortOrder).ToListAsync(), new List<Experience>());

    public Task<List<Skill>> GetSkillsAsync() =>
        QueryAsync(db => db.Skills.OrderBy(s => s.Group).ThenBy(s => s.SortOrder).ToListAsync(), new List<Skill>());

    public Task<List<Certification>> GetCertificationsAsync() =>
        QueryAsync(db => db.Certifications.OrderBy(c => c.SortOrder).ToListAsync(), new List<Certification>());

    public Task<List<BlogPost>> GetBlogPostsAsync() =>
        QueryAsync(db => db.BlogPosts.Where(b => !b.Draft).OrderByDescending(b => b.Date).ToListAsync(), new List<BlogPost>());

    public Task<BlogPost?> GetBlogPostAsync(string slug) =>
        QueryAsync(db => db.BlogPosts.FirstOrDefaultAsync(b => b.Slug == slug && !b.Draft), (BlogPost?)null);

    public Task<List<Talk>> GetTalksAsync() =>
        QueryAsync(db => db.Talks.OrderByDescending(t => t.Date).ToListAsync(), new List<Talk>());

    public Task<List<Testimonial>> GetTestimonialsAsync() =>
        QueryAsync(db => db.Testimonials.OrderBy(t => t.SortOrder).ToListAsync(), new List<Testimonial>());

    /// <summary>The CV for <paramref name="site"/>: only the sections its template has enabled, in the template's wording.</summary>
    public async Task<CvData> BuildCvDataAsync(SiteOptions site)
    {
        var template = _templates.Resolve(site);
        var roles = template.IsEnabled(SectionRegistry.Experience) ? await GetExperiencesAsync() : new List<Experience>();
        var skills = template.IsEnabled(SectionRegistry.Skills) ? await GetSkillsAsync() : new List<Skill>();
        var certs = template.IsEnabled(SectionRegistry.Certifications) ? await GetCertificationsAsync() : new List<Certification>();
        var projects = template.IsEnabled(SectionRegistry.Projects) ? await GetFeaturedProjectsAsync() : new List<Project>();

        var groups = GroupSkills(skills, template.Definition.SkillGroups);

        var contacts = new[] { site.HasEmail ? site.Email : null, site.HasPhone ? site.Phone : null, site.HasLinkedIn ? site.LinkedIn : null, site.HasGitHub ? site.GitHub : null }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToList();

        var cv = template.Copy.Cv;
        var status = template.Copy.Certifications;
        var headings = new CvHeadings(cv.ExperienceHeading, cv.SkillsHeading, cv.ProjectsHeading, cv.CertificationsHeading,
            status.StatusEarned, status.StatusInProgress, status.StatusPlanned);
        return new CvData(site.OwnerName, site.Role, site.Summary, contacts, roles, groups, projects, certs, site.Url, headings);
    }

    /// <summary>Groups skills for the CV, ordered by <paramref name="groupOrder"/> (unlisted groups last).
    /// Shared with the offline ATS review tool so both build an identical CvData snapshot.</summary>
    public static List<IGrouping<string, Skill>> GroupSkills(IEnumerable<Skill> skills, IList<string> groupOrder) =>
        skills.GroupBy(s => s.Group)
            .OrderBy(g => { var i = groupOrder.IndexOf(g.Key); return i < 0 ? int.MaxValue : i; })
            .ToList();
}
