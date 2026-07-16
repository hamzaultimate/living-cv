using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portfolio.Data.Entities;

namespace Portfolio.Data.Seeding;

/// <summary>
/// Idempotent, authoritative seeder: reads JSON files from the Seed folder and makes each
/// table match its seed file, keyed by a natural key (slug / name / company+title). Safe to
/// run on every startup — editing a seed file and redeploying adds new rows, updates matching
/// ones, and removes rows no longer present. The seed files are the single source of truth.
/// Content is data, never hardcoded markup.
/// </summary>
public static class PortfolioSeeder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static async Task SeedAsync(PortfolioDbContext db, string seedDir, ILogger? log = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(seedDir))
        {
            log?.LogWarning("Seed directory not found: {dir}", seedDir);
            return;
        }

        await UpsertAsync(db, seedDir, "skills.json", db.Skills, x => x.Name, log, ct,
            (cur, inc) => { cur.Group = inc.Group; cur.SortOrder = inc.SortOrder; });

        await UpsertAsync(db, seedDir, "experiences.json", db.Experiences, x => $"{x.Company}|{x.Title}", log, ct,
            (cur, inc) => { cur.Start = inc.Start; cur.End = inc.End; cur.Current = inc.Current; cur.Summary = inc.Summary;
                            cur.Highlights = inc.Highlights; cur.Tech = inc.Tech; cur.SortOrder = inc.SortOrder; });

        await UpsertAsync(db, seedDir, "projects.json", db.Projects, x => x.Slug, log, ct,
            (cur, inc) => { cur.Title = inc.Title; cur.Role = inc.Role; cur.Org = inc.Org; cur.Period = inc.Period;
                            cur.Summary = inc.Summary; cur.Category = inc.Category; cur.Problem = inc.Problem; cur.Approach = inc.Approach;
                            cur.HeroImage = inc.HeroImage; cur.Screenshots = inc.Screenshots;
                            cur.Impact = inc.Impact; cur.Tech = inc.Tech; cur.Links = inc.Links; cur.Proofs = inc.Proofs;
                            cur.Featured = inc.Featured; cur.SortOrder = inc.SortOrder; });

        await UpsertAsync(db, seedDir, "certifications.json", db.Certifications, x => x.Name, log, ct,
            (cur, inc) => { cur.Issuer = inc.Issuer; cur.Status = inc.Status; cur.Date = inc.Date;
                            cur.CredentialUrl = inc.CredentialUrl; cur.Category = inc.Category; cur.BadgeImageUrl = inc.BadgeImageUrl;
                            cur.CertificateUrl = inc.CertificateUrl; cur.Proofs = inc.Proofs; cur.SortOrder = inc.SortOrder; });

        await UpsertAsync(db, seedDir, "talks.json", db.Talks, x => x.Title, log, ct,
            (cur, inc) => { cur.Type = inc.Type; cur.YoutubeId = inc.YoutubeId; cur.Event = inc.Event;
                            cur.Date = inc.Date; cur.Url = inc.Url; cur.SortOrder = inc.SortOrder; });

        await UpsertAsync(db, seedDir, "blog.json", db.BlogPosts, x => x.Slug, log, ct,
            (cur, inc) => { cur.Title = inc.Title; cur.Description = inc.Description; cur.Date = inc.Date; cur.Tags = inc.Tags;
                            cur.Draft = inc.Draft; cur.Cover = inc.Cover; cur.BodyMarkdown = inc.BodyMarkdown; });

        await UpsertAsync(db, seedDir, "testimonials.json", db.Testimonials, x => $"{x.Author}|{x.Quote[..Math.Min(40, x.Quote.Length)]}", log, ct,
            (cur, inc) => { cur.Quote = inc.Quote; cur.AuthorTitle = inc.AuthorTitle; cur.SourceUrl = inc.SourceUrl;
                            cur.Relation = inc.Relation; cur.Featured = inc.Featured; cur.SortOrder = inc.SortOrder; });
    }

    private static async Task UpsertAsync<T>(
        PortfolioDbContext db, string dir, string file, DbSet<T> set,
        Func<T, string> key, ILogger? log, CancellationToken ct, Action<T, T> copy) where T : class
    {
        var path = Path.Combine(dir, file);
        if (!File.Exists(path)) { log?.LogInformation("Seed file absent, skipping: {file}", file); return; }

        List<T>? items;
        try
        {
            await using var stream = File.OpenRead(path);
            items = await JsonSerializer.DeserializeAsync<List<T>>(stream, Json, ct);
        }
        catch (Exception ex) { log?.LogError(ex, "Failed to parse seed file {file}", file); return; }
        if (items is null || items.Count == 0) { log?.LogInformation("Seed file empty: {file}", file); return; }

        var byKey = (await set.ToListAsync(ct)).ToDictionary(key, StringComparer.OrdinalIgnoreCase);
        var incomingKeys = new HashSet<string>(items.Select(key), StringComparer.OrdinalIgnoreCase);
        int added = 0, updated = 0;
        foreach (var incoming in items)
        {
            if (byKey.TryGetValue(key(incoming), out var current)) { copy(current, incoming); updated++; }
            else { set.Add(incoming); added++; }
        }

        // Authoritative seed: the seed file is the single source of truth, so rows whose
        // natural key is no longer present get removed. Guarded by the earlier empty-file
        // check (we never reach here with zero items), so an empty/missing file never wipes a table.
        var stale = byKey.Where(kv => !incomingKeys.Contains(kv.Key)).Select(kv => kv.Value).ToList();
        if (stale.Count > 0) set.RemoveRange(stale);

        await db.SaveChangesAsync(ct);
        log?.LogInformation("Seeded {file}: +{added} ~{updated} -{removed}", file, added, updated, stale.Count);
    }
}
