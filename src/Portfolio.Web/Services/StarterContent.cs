using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Seeding;

namespace Portfolio.Web.Services;

/// <summary>One content table and what sample content would do to it.</summary>
public sealed record StarterTable(string Name, int Existing, int Incoming)
{
    /// <summary>Rows that would be added: sample content only ever goes into a table with no rows.</summary>
    public int WouldAdd => Existing == 0 ? Incoming : 0;

    public bool SkippedBecauseNotEmpty => Existing > 0 && Incoming > 0;
}

/// <summary>
/// Loads a template's sample content into the database with seed-if-empty semantics: a table that already has rows is left
/// exactly as it is, and nothing is ever updated or deleted. The sample comes from the template's own starter file or, when the
/// manifest says so, from the built-in demo persona (the seed files), loaded by the same seeder in its non-destructive mode.
/// </summary>
public class StarterContent
{
    private readonly ITemplateCatalog _catalog;
    private readonly ILogger<StarterContent> _logger;
    private readonly string _seedDir;

    public StarterContent(ITemplateCatalog catalog, ILogger<StarterContent> logger, string? seedDir = null)
    {
        _catalog = catalog;
        _logger = logger;
        _seedDir = seedDir ?? Path.Combine(AppContext.BaseDirectory, "Seed");
    }

    /// <summary>True when the template ships sample content of either kind.</summary>
    public static bool Offers(TemplateDefinition template) => template.Starter is not null;

    private static readonly string[] Tables = ["Experience", "Skills", "Projects", "Certifications", "Talks", "Blog posts", "Testimonials"];

    private static readonly (string Name, string File)[] SeedFiles =
        [("Experience", "experiences.json"), ("Skills", "skills.json"), ("Projects", "projects.json"), ("Certifications", "certifications.json"),
         ("Talks", "talks.json"), ("Blog posts", "blog.json"), ("Testimonials", "testimonials.json")];

    /// <summary>For each content table: how many rows it has and how many the sample would bring. Read-only.</summary>
    public async Task<IReadOnlyList<StarterTable>> DescribeAsync(PortfolioDbContext db, TemplateDefinition template, CancellationToken ct = default)
    {
        var existing = await ExistingAsync(db, ct);
        var incoming = Incoming(template);
        return Tables.Select(t => new StarterTable(t, existing.GetValueOrDefault(t), incoming.GetValueOrDefault(t))).Where(t => t.Incoming > 0 || t.Existing > 0).ToList();
    }

    /// <summary>Adds the template's sample rows to every content table that is empty. Never updates or deletes. The caller owns the
    /// transaction; the seeder saves once per table, so run it inside one to make the whole load atomic.</summary>
    public async Task<IReadOnlyList<StarterTable>> ApplyAsync(PortfolioDbContext db, TemplateDefinition template, CancellationToken ct = default)
    {
        var before = await DescribeAsync(db, template, ct);
        if (template.Starter is null) return before;

        if (template.Starter.IsSeed())
        {
            // Fixed to IfEmpty on purpose: whatever Seed:Mode says, the wizard must never rewrite or remove rows.
            await PortfolioSeeder.SeedAsync(db, _seedDir, SeedMode.IfEmpty, _logger, ct);
        }
        else if (_catalog.GetStarter(template.Key)?.Clone() is { } starter)
        {
            if (!await db.Experiences.AnyAsync(ct)) db.Experiences.AddRange(starter.Experiences);
            if (!await db.Skills.AnyAsync(ct)) db.Skills.AddRange(starter.Skills);
            if (!await db.Projects.AnyAsync(ct)) db.Projects.AddRange(starter.Projects);
            if (!await db.Certifications.AnyAsync(ct)) db.Certifications.AddRange(starter.Certifications);
            if (!await db.Testimonials.AnyAsync(ct)) db.Testimonials.AddRange(starter.Testimonials);
            await db.SaveChangesAsync(ct);
        }
        return before;
    }

    private static async Task<Dictionary<string, int>> ExistingAsync(PortfolioDbContext db, CancellationToken ct) => new()
    {
        ["Experience"] = await db.Experiences.CountAsync(ct),
        ["Skills"] = await db.Skills.CountAsync(ct),
        ["Projects"] = await db.Projects.CountAsync(ct),
        ["Certifications"] = await db.Certifications.CountAsync(ct),
        ["Talks"] = await db.Talks.CountAsync(ct),
        ["Blog posts"] = await db.BlogPosts.CountAsync(ct),
        ["Testimonials"] = await db.Testimonials.CountAsync(ct),
    };

    private Dictionary<string, int> Incoming(TemplateDefinition template)
    {
        var counts = new Dictionary<string, int>();
        if (template.Starter is null) return counts;

        if (template.Starter.IsSeed())
        {
            foreach (var (name, file) in SeedFiles)
            {
                var path = Path.Combine(_seedDir, file);
                if (!File.Exists(path)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    if (doc.RootElement.ValueKind == JsonValueKind.Array) counts[name] = doc.RootElement.GetArrayLength();
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Could not read seed file {File} to describe the sample content.", file);
                }
            }
        }
        else if (_catalog.GetStarter(template.Key) is { } starter)
        {
            counts["Experience"] = starter.Experiences.Count;
            counts["Skills"] = starter.Skills.Count;
            counts["Projects"] = starter.Projects.Count;
            counts["Certifications"] = starter.Certifications.Count;
            counts["Testimonials"] = starter.Testimonials.Count;
        }
        return counts;
    }
}
