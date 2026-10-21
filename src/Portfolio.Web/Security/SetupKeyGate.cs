using System.Security.Cryptography;
using System.Text;

namespace Portfolio.Web.Security;

public enum SetupKeyResult { Disabled, Throttled, Wrong, Correct }

/// <summary>
/// Checks the one-time Setup:Key. An unset or too-short key disables /setup entirely. Wrong guesses are
/// counted across all callers (there is only ever one legitimate user of /setup), and after a few the
/// gate refuses everything for a while, so the key cannot be brute-forced online. The key is never logged or echoed.
/// </summary>
public sealed class SetupKeyGate
{
    public const int MinKeyLength = 16;
    public const int MaxFailures = 5;
    public static readonly TimeSpan ThrottleDuration = TimeSpan.FromMinutes(15);

    private readonly IConfiguration _config;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private int _failures;
    private DateTimeOffset _throttledUntil;

    public SetupKeyGate(IConfiguration config, TimeProvider time, ILogger<SetupKeyGate> logger)
    {
        _config = config;
        _time = time;
        var key = config["Setup:Key"];
        if (!string.IsNullOrWhiteSpace(key) && key.Length < MinKeyLength)
            logger.LogWarning("Setup:Key is shorter than {Min} characters, so /setup is disabled. Use a longer random key.", MinKeyLength);
    }

    /// <summary>True when a usable Setup:Key is configured.</summary>
    public bool IsConfigured => ConfiguredKey() is not null;

    public SetupKeyResult Check(string? submitted)
    {
        var configured = ConfiguredKey();
        if (configured is null) return SetupKeyResult.Disabled;

        lock (_lock)
        {
            if (_time.GetUtcNow() < _throttledUntil) return SetupKeyResult.Throttled;

            var match = CryptographicOperations.FixedTimeEquals(Digest(submitted ?? ""), Digest(configured));
            if (match)
            {
                _failures = 0;
                return SetupKeyResult.Correct;
            }

            if (++_failures >= MaxFailures)
            {
                _failures = 0;
                _throttledUntil = _time.GetUtcNow() + ThrottleDuration;
            }
            return SetupKeyResult.Wrong;
        }
    }

    private string? ConfiguredKey()
    {
        var key = _config["Setup:Key"];
        return string.IsNullOrWhiteSpace(key) || key.Length < MinKeyLength ? null : key;
    }

    private static byte[] Digest(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
