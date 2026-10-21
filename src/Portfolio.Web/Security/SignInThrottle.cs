using System.Net;

namespace Portfolio.Web.Security;

/// <summary>
/// Limits sign-in attempts per client address, in memory. It runs before the account lockout counter and before the
/// password hash, so one address cannot burn the account's shared failure budget or keep the hash busy. Every attempt
/// counts (not only failures), so parallel guesses cannot slip past a check-then-record gap; a successful sign-in
/// clears the address. State is per process and lost on restart, which only ever makes it more lenient.
/// The table is bounded: expired entries go first, then the oldest.
/// The address comes from the connection, so it is only the visitor's real address where the server sees it directly
/// (IIS in-process) or forwarded headers are configured for the proxy in front.
/// </summary>
public sealed class SignInThrottle
{
    public const int MaxAttempts = 10;
    public const int MaxTracked = 4096;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private readonly Dictionary<string, (int Count, DateTimeOffset Start)> _entries = new();

    public SignInThrottle(TimeProvider time) => _time = time;

    /// <summary>Records an attempt for the client and returns false when it has used up its allowance.</summary>
    public bool TryAcquire(string clientKey)
    {
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            if (_entries.TryGetValue(clientKey, out var entry) && now - entry.Start < Window)
            {
                if (entry.Count >= MaxAttempts) return false;
                _entries[clientKey] = (entry.Count + 1, entry.Start);
                return true;
            }

            if (!_entries.ContainsKey(clientKey) && _entries.Count >= MaxTracked) MakeRoom(now);
            _entries[clientKey] = (1, now);
            return true;
        }
    }

    public void Reset(string clientKey)
    {
        lock (_lock) _entries.Remove(clientKey);
    }

    /// <summary>Groups a connection address into a key. IPv6 clients are grouped by /64, since one customer holds a whole /64.</summary>
    public static string KeyFor(IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return address.ToString();
        return "v6:" + Convert.ToHexString(address.GetAddressBytes().AsSpan(0, 8));
    }

    private void MakeRoom(DateTimeOffset now)
    {
        foreach (var key in _entries.Where(e => now - e.Value.Start >= Window).Select(e => e.Key).ToList())
            _entries.Remove(key);
        if (_entries.Count < MaxTracked) return;
        _entries.Remove(_entries.MinBy(e => e.Value.Start).Key);
    }
}
