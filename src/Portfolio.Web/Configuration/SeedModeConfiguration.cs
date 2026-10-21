using Portfolio.Data.Seeding;

namespace Portfolio.Web.Configuration;

public static class SeedModeConfiguration
{
    /// <summary>Reads <c>Seed:Mode</c>. Anything other than "Authoritative" means seed-if-empty,
    /// the non-destructive default.</summary>
    public static SeedMode GetSeedMode(this IConfiguration config) =>
        Enum.TryParse<SeedMode>(config["Seed:Mode"], ignoreCase: true, out var mode) ? mode : SeedMode.IfEmpty;
}
