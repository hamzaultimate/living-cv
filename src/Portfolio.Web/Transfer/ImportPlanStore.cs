using System.Security.Cryptography;

namespace Portfolio.Web.Transfer;

/// <summary>
/// Holds previews until they are confirmed. The confirm request carries only a random 128-bit token, so what is written is exactly what the preview showed.
/// A token belongs to the owner session that made the preview, works once, and expires after <see cref="TransferLimits.PreviewLifetime"/>. At most one preview is held per
/// session and <see cref="TransferLimits.MaxHeldPreviews"/> overall (a newer one pushes the oldest out), and a plan is bounded by the size of the file it came from, so memory is bounded.
/// A restart forgets every preview, which only means the owner previews again.
/// </summary>
public sealed class ImportPlanStore
{
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly List<ImportPlan> _plans = [];

    public ImportPlanStore(TimeProvider clock) => _clock = clock;

    /// <summary>Gives <paramref name="plan"/> its token and expiry and holds it, replacing the session's earlier preview.</summary>
    public void Hold(ImportPlan plan)
    {
        plan.Token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        plan.Expires = _clock.GetUtcNow() + TransferLimits.PreviewLifetime;
        lock (_gate)
        {
            Purge();
            _plans.RemoveAll(p => string.Equals(p.SessionKey, plan.SessionKey, StringComparison.Ordinal));
            _plans.Add(plan);
            while (_plans.Count > TransferLimits.MaxHeldPreviews) _plans.RemoveAt(0);
        }
    }

    /// <summary>The plan for <paramref name="token"/>, removed so it cannot be used again; null for an unknown, expired or used token, or one from another session (which is left alone).</summary>
    public ImportPlan? Take(string? token, string sessionKey)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 32) return null;
        lock (_gate)
        {
            Purge();
            var plan = _plans.FirstOrDefault(p => string.Equals(p.Token, token, StringComparison.Ordinal));
            if (plan is null || !string.Equals(plan.SessionKey, sessionKey, StringComparison.Ordinal)) return null;
            _plans.Remove(plan);
            return plan;
        }
    }

    /// <summary>Drops the session's preview, if it has one with this token.</summary>
    public bool Discard(string? token, string sessionKey) => Take(token, sessionKey) is not null;

    /// <summary>How many previews are held now (expired ones are dropped first).</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                Purge();
                return _plans.Count;
            }
        }
    }

    private void Purge()
    {
        var now = _clock.GetUtcNow();
        _plans.RemoveAll(p => p.Expires <= now);
    }
}
