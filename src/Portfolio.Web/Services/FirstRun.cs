using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Data.Seeding;

namespace Portfolio.Web.Services;

/// <summary>
/// Decides whether start-up seeds the demo persona. Content is seeded into empty tables (as always) except where the onboarding wizard owns the
/// decision: (1) a new install that can still be claimed (a Setup:Key is set or an owner exists) whose wizard is not finished waits for the wizard, and
/// (2) a site that went through the wizard (published or skipped) is never seeded at start-up again, so "start blank" stays blank across restarts; only the
/// owner's explicit choices (sample content at publish, or Skip) load demo content. Everything else seeds exactly as before: every site that existed before
/// the wizard (the migration marks it completed but leaves the outcome empty), a development checkout with no Setup:Key, and Seed:Mode=Authoritative.
/// </summary>
public static class FirstRun
{
    public static async Task<bool> ShouldDeferContentSeedAsync(PortfolioDbContext db, bool setupKeyConfigured, SeedMode mode, CancellationToken ct = default)
    {
        if (mode == SeedMode.Authoritative) return false;
        var state = await db.SiteProfiles.Where(p => p.Id == SiteProfile.SingletonId)
            .Select(p => new { p.OnboardingCompletedAt, p.OnboardingOutcome }).FirstOrDefaultAsync(ct);
        if (state?.OnboardingOutcome is not null) return true;

        var claimable = setupKeyConfigured || await db.OwnerAccounts.AnyAsync(ct);
        if (!claimable) return false;
        return state is null || state.OnboardingCompletedAt is null;
    }

    /// <summary>The start-up seeding step: seeds unless <see cref="ShouldDeferContentSeedAsync"/> says the wizard owns the decision.</summary>
    public static async Task<bool> SeedAtStartupAsync(PortfolioDbContext db, string seedDir, SeedMode mode, bool setupKeyConfigured,
        Microsoft.Extensions.Logging.ILogger? log = null, CancellationToken ct = default)
    {
        if (await ShouldDeferContentSeedAsync(db, setupKeyConfigured, mode, ct))
        {
            log?.LogInformation("The onboarding wizard owns the demo content: it is not seeded at start-up.");
            return false;
        }
        await PortfolioSeeder.SeedAsync(db, seedDir, mode, log, ct);
        return true;
    }
}
