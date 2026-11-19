using Microsoft.EntityFrameworkCore;
using Portfolio.Data;

namespace Portfolio.Web.Services;

/// <summary>How many entries each content section holds, and how many blog posts are drafts.</summary>
public sealed record ContentCounts(IReadOnlyDictionary<string, int> Entries, int BlogDrafts);

/// <summary>
/// The counts the menu and the dashboard show. Scoped, so a request asks the database once however many blocks want the numbers. A failed read is
/// logged and answered with null: the counts are a convenience and never break a page.
/// </summary>
public sealed class AdminContentCounts
{
    private readonly PortfolioDbContext _db;
    private readonly ILogger<AdminContentCounts> _logger;
    private Task<ContentCounts?>? _counts;

    public AdminContentCounts(PortfolioDbContext db, ILogger<AdminContentCounts> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<ContentCounts?> GetAsync(CancellationToken ct = default) => _counts ??= LoadAsync(ct);

    private async Task<ContentCounts?> LoadAsync(CancellationToken ct)
    {
        try
        {
            var entries = new Dictionary<string, int>
            {
                [SectionRegistry.Experience] = await _db.Experiences.CountAsync(ct),
                [SectionRegistry.Skills] = await _db.Skills.CountAsync(ct),
                [SectionRegistry.Projects] = await _db.Projects.CountAsync(ct),
                [SectionRegistry.Certifications] = await _db.Certifications.CountAsync(ct),
                [SectionRegistry.Talks] = await _db.Talks.CountAsync(ct),
                [SectionRegistry.Testimonials] = await _db.Testimonials.CountAsync(ct),
                [SectionRegistry.Blog] = await _db.BlogPosts.CountAsync(ct),
            };
            return new ContentCounts(entries, await _db.BlogPosts.CountAsync(b => b.Draft, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not count the entries for the admin menu and dashboard.");
            return null;
        }
    }
}
