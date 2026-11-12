using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Data.Seeding;

namespace Portfolio.Web.Services;

/// <summary>
/// Decides whether start-up seeds the demo persona. Content is seeded into empty tables (as always) except where the onboarding wizard owns the
/// decision: (1) a new install that can still be claimed (a Setup:Key is set or an owner exists) whose wizard is not finished waits for the wizard, and
/// (2) a site that went through the wizard (published or skipped) is never seeded at start-up again, so "start blank" stays blank across restarts; only the
/// owner's explicit choices (sample content at publish, or Skip) load demo content. Everything else seeds exactly as before on its first start-up pass:
/// every site that existed before the wizard (the migration marks it completed but leaves the outcome empty), a development checkout with no Setup:Key,
/// and Seed:Mode=Authoritative. Those sites record <see cref="SiteProfile.ContentSeededAt"/> once that first pass has finished, and from then on they are
/// pinned like a wizard site: an emptied table stays empty across restarts. Seed:Mode=Authoritative is the explicit opt-in that still seeds as configured.
/// </summary>
public static class FirstRun
{
    public static async Task<bool> ShouldDeferContentSeedAsync(PortfolioDbContext db, bool setupKeyConfigured, SeedMode mode, CancellationToken ct = default)
    {
        if (mode == SeedMode.Authoritative) return false;
        var state = await db.SiteProfiles.Where(p => p.Id == SiteProfile.SingletonId)
            .Select(p => new { p.OnboardingCompletedAt, p.OnboardingOutcome, p.ContentSeededAt }).FirstOrDefaultAsync(ct);
        if (state?.OnboardingOutcome is not null) return true;
        // A site that has completed a start-up pass keeps what the owner did with the tables afterwards.
        if (state?.ContentSeededAt is not null) return true;

        var claimable = setupKeyConfigured || await db.OwnerAccounts.AnyAsync(ct);
        if (!claimable) return false;
        return state is null || state.OnboardingCompletedAt is null;
    }

    /// <summary>What the start-up seeding step did: whether it seeded at all, and whether every seed file was read (so the pass can be recorded).</summary>
    public readonly record struct StartupSeed(bool Ran, bool Complete);

    /// <summary>The start-up seeding step: seeds unless <see cref="ShouldDeferContentSeedAsync"/> says the wizard owns the decision.</summary>
    public static async Task<bool> SeedAtStartupAsync(PortfolioDbContext db, string seedDir, SeedMode mode, bool setupKeyConfigured,
        Microsoft.Extensions.Logging.ILogger? log = null, CancellationToken ct = default) =>
        (await SeedAtStartupDetailedAsync(db, seedDir, mode, setupKeyConfigured, log, ct)).Ran;

    /// <summary>Like <see cref="SeedAtStartupAsync"/>, and says whether the pass was complete. Records the pass when it was (and the profile row exists).</summary>
    public static async Task<StartupSeed> SeedAtStartupDetailedAsync(PortfolioDbContext db, string seedDir, SeedMode mode, bool setupKeyConfigured,
        Microsoft.Extensions.Logging.ILogger? log = null, CancellationToken ct = default)
    {
        if (await ShouldDeferContentSeedAsync(db, setupKeyConfigured, mode, ct))
        {
            log?.LogInformation("The onboarding wizard owns the demo content: it is not seeded at start-up.");
            return new StartupSeed(false, false);
        }
        var complete = await PortfolioSeeder.SeedAsync(db, seedDir, mode, log, ct);
        // Recorded only after every table has been through the pass, so a crash part-way leaves the site unpinned and the next start finishes it.
        // A missing seed folder, or a seed file that is missing or cannot be read, means the pass did not finish: the next start tries again.
        if (complete) await MarkContentSeededAsync(db, ct);
        return new StartupSeed(true, complete);
    }

    /// <summary>
    /// Records that the first start-up seeding pass has finished, once. Does nothing while the profile row does not exist yet (a new install creates it
    /// right after seeding; Program calls this again then) or when it is already recorded. A single conditional UPDATE, so it needs no tracked row and
    /// two instances starting together cannot disagree.
    /// </summary>
    public static async Task MarkContentSeededAsync(PortfolioDbContext db, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await db.SiteProfiles.Where(p => p.Id == SiteProfile.SingletonId && p.ContentSeededAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ContentSeededAt, now), ct);
    }
}
