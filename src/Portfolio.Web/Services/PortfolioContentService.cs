using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;

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
    private readonly ILogger<PortfolioContentService> _logger;

    public PortfolioContentService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<PortfolioContentService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    public bool DatabaseConfigured => !string.IsNullOrWhiteSpace(_config.GetConnectionString("Default"));

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
}
