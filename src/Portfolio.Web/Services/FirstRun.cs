using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;
using Portfolio.Data.Seeding;

namespace Portfolio.Web.Services;

/// <summary>
/// Decides whether the start-up seeding of the demo persona waits for the onboarding wizard. A new install that can be claimed
/// (a Setup:Key is configured, or an owner already exists) and whose wizard is not finished is not filled with the demo persona
/// behind the owner's back: the wizard loads sample content when it is asked for (or when the wizard is skipped), so "blank" really is blank.
/// Everything else seeds exactly as before: a site whose wizard is done (every site that existed before the wizard, by migration),
/// a development checkout with no Setup:Key, and Seed:Mode=Authoritative.
/// </summary>
public static class FirstRun
{
    public static async Task<bool> ShouldDeferContentSeedAsync(PortfolioDbContext db, bool setupKeyConfigured, SeedMode mode, CancellationToken ct = default)
    {
        if (mode == SeedMode.Authoritative) return false;
        var claimable = setupKeyConfigured || await db.OwnerAccounts.AnyAsync(ct);
        if (!claimable) return false;

        var completedAt = await db.SiteProfiles.Where(p => p.Id == SiteProfile.SingletonId)
            .Select(p => new { p.OnboardingCompletedAt }).FirstOrDefaultAsync(ct);
        return completedAt is null || completedAt.OnboardingCompletedAt is null;
    }
}
