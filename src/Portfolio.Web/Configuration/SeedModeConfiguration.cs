using Portfolio.Data.Seeding;

namespace Portfolio.Web.Configuration;

public static class SeedModeConfiguration
{
    /// <summary>Reads <c>Seed:Mode</c>. Only the exact names "IfEmpty" and "Authoritative" (any casing)
    /// are accepted; anything else, including numbers and comma lists, means seed-if-empty, the
    /// non-destructive default. A non-empty unrecognised value is logged as a warning.</summary>
    public static SeedMode GetSeedMode(this IConfiguration config, ILogger? log = null)
    {
        var raw = config["Seed:Mode"]?.Trim();
        if (string.IsNullOrEmpty(raw)) return SeedMode.IfEmpty;

        foreach (var name in Enum.GetNames<SeedMode>())
            if (string.Equals(name, raw, StringComparison.OrdinalIgnoreCase))
                return Enum.Parse<SeedMode>(name);

        log?.LogWarning("Unrecognised Seed:Mode '{Value}'; using {Mode}.", raw, SeedMode.IfEmpty);
        return SeedMode.IfEmpty;
    }
}
