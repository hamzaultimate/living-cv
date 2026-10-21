using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;

namespace Portfolio.Web.Security;

public enum CreateOwnerOutcome { Created, AlreadyExists }

public enum SignInOutcome { Succeeded, Failed, LockedOut, Throttled }

public sealed record OwnerSignInResult(SignInOutcome Outcome, OwnerAccount? Account = null);

/// <summary>What the caller knows about where an attempt came from. Both parts are optional (the CLI and unit tests have neither).</summary>
/// <param name="ClientKey">Throttle key for the client address, from <see cref="SignInThrottle.KeyFor"/>.</param>
/// <param name="DeviceToken">The value of the known-device cookie, if the browser sent one.</param>
public readonly record struct SignInContext(string? ClientKey = null, string? DeviceToken = null);

/// <summary>
/// Creates, resets and authenticates the single owner account.
/// Lockout state lives on the account row, so it survives restarts and cannot be dodged by varying the
/// user name: with exactly one account, every failed attempt counts against it, whatever name was typed.
/// Attempts are serialised in-process, so parallel guesses cannot overshoot the limit and the
/// expensive hash runs one at a time.
/// A stranger could use the shared lockout to keep the owner out, so two things sit around it: a per-address
/// throttle that turns a flood away before it reaches the account counter or the hash, and the known-device cookie
/// (see <see cref="KnownDevices"/>), which lets a browser that has signed in before skip the lockout (not the password).
/// </summary>
public sealed class OwnerAccountService
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly PortfolioDbContext _db;
    private readonly OwnerPasswordHasher _hasher;
    private readonly SignInGate _gate;
    private readonly SignInThrottle _throttle;
    private readonly KnownDevices _devices;
    private readonly TimeProvider _time;
    private readonly ILogger<OwnerAccountService> _logger;

    public OwnerAccountService(PortfolioDbContext db, OwnerPasswordHasher hasher, SignInGate gate, SignInThrottle throttle,
        KnownDevices devices, TimeProvider time, ILogger<OwnerAccountService> logger)
    {
        _db = db;
        _hasher = hasher;
        _gate = gate;
        _throttle = throttle;
        _devices = devices;
        _time = time;
        _logger = logger;
    }

    /// <summary>Case-insensitive, width- and whitespace-insensitive form used to compare user names.</summary>
    public static string NormalizeUserName(string? userName) =>
        (userName ?? "").Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();

    public Task<bool> AnyAsync(CancellationToken ct = default) => _db.OwnerAccounts.AnyAsync(ct);

    /// <summary>
    /// Inserts the owner. There is no read-then-write check to race: the primary key and the Id = 1 check
    /// constraint make a second insert fail in the database, which is reported as <see cref="CreateOwnerOutcome.AlreadyExists"/>.
    /// </summary>
    public async Task<CreateOwnerOutcome> CreateAsync(string userName, string password, CancellationToken ct = default)
    {
        var account = new OwnerAccount
        {
            UserName = userName.Trim(),
            SecurityStamp = NewStamp(),
            CreatedUtc = _time.GetUtcNow().UtcDateTime,
        };
        account.PasswordHash = _hasher.Hash(password);
        _db.OwnerAccounts.Add(account);
        try
        {
            await _db.SaveChangesAsync(ct);
            return CreateOwnerOutcome.Created;
        }
        catch (DbUpdateException ex) when (Services.SiteProfileProvider.IsDuplicateKey(ex))
        {
            _db.Entry(account).State = EntityState.Detached;
            return CreateOwnerOutcome.AlreadyExists;
        }
    }

    /// <summary>Sets a new password, signs out every existing session and clears any lockout. False when no owner exists.</summary>
    public async Task<bool> ResetPasswordAsync(string newPassword, CancellationToken ct = default)
    {
        var hash = _hasher.Hash(newPassword);
        var stamp = NewStamp();
        var changed = await _db.OwnerAccounts.Where(a => a.Id == OwnerAccount.SingletonId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.PasswordHash, hash)
                .SetProperty(a => a.SecurityStamp, stamp)
                .SetProperty(a => a.FailedAccessCount, 0)
                .SetProperty(a => a.LockoutEndUtc, (DateTime?)null), ct);
        return changed > 0;
    }

    /// <summary>The user name stored for the account, or null when there is none. Shown by <c>reset-owner</c>.</summary>
    public Task<string?> GetUserNameAsync(CancellationToken ct = default) =>
        _db.OwnerAccounts.AsNoTracking().Where(a => a.Id == OwnerAccount.SingletonId).Select(a => (string?)a.UserName).FirstOrDefaultAsync(ct);

    /// <summary>Changes the security stamp, which ends every sign-in cookie (used on sign-out).</summary>
    public Task RotateStampAsync(CancellationToken ct = default)
    {
        var stamp = NewStamp();
        return _db.OwnerAccounts.Where(a => a.Id == OwnerAccount.SingletonId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.SecurityStamp, stamp), ct);
    }

    public async Task<OwnerSignInResult> SignInAsync(string? userName, string? password, SignInContext context = default, CancellationToken ct = default)
    {
        password ??= "";
        // Per-address allowance first: a flood from one place is refused here, before it can queue for the gate,
        // cost a hash, or touch the account's failure count.
        if (context.ClientKey is { } client && !_throttle.TryAcquire(client))
            return new OwnerSignInResult(SignInOutcome.Throttled);

        // Sign-ins queue one at a time; a flood gets turned away rather than piling up behind the hash.
        if (!await _gate.EnterAsync(ct))
            return new OwnerSignInResult(SignInOutcome.Failed);
        try
        {
            var account = await _db.OwnerAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == OwnerAccount.SingletonId, ct);
            if (account is null)
            {
                _hasher.SpendVerificationCost(password);
                return new OwnerSignInResult(SignInOutcome.Failed);
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var knownDevice = _devices.IsValid(context.DeviceToken, account);
            // No password check while locked: it would only give a flood something expensive to ask for.
            // A known device is the exception: it is let through to the password check, so a lockout caused by
            // someone else's guesses cannot keep the owner out of their own browser.
            if (account.LockoutEndUtc is { } until && until > now && !knownDevice)
                return new OwnerSignInResult(SignInOutcome.LockedOut);

            // Always verify against the real hash, even when the name is wrong, so timing does not reveal which part failed.
            var verification = _hasher.Verify(account.PasswordHash, password);
            var nameMatches = FixedTimeEquals(NormalizeUserName(userName), NormalizeUserName(account.UserName));
            if (nameMatches && verification != PasswordVerificationResult.Failed)
            {
                var newHash = await RecordSuccessAsync(verification == PasswordVerificationResult.SuccessRehashNeeded ? password : null, ct);
                // Keep the returned account current: the known-device cookie issued from it is bound to the hash.
                if (newHash is not null) account.PasswordHash = newHash;
                if (context.ClientKey is { } ok) _throttle.Reset(ok);
                return new OwnerSignInResult(SignInOutcome.Succeeded, account);
            }

            // A wrong guess from a known device does not add to the shared failure count. Such a browser is not
            // subject to the lockout anyway, so counting would only lock out the owner's other devices after their
            // own typos. Guessing from it is still bounded by the per-address throttle, and the password is still required.
            if (!knownDevice)
                await RecordFailureAsync(now, ct);
            return new OwnerSignInResult(SignInOutcome.Failed);
        }
        finally
        {
            _gate.Exit();
        }
    }

    /// <summary>True when the stamp in a sign-in cookie still matches the account (it changes on password reset).</summary>
    public async Task<bool> IsStampCurrentAsync(string? stamp, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(stamp)) return false;
        var current = await _db.OwnerAccounts.AsNoTracking()
            .Where(a => a.Id == OwnerAccount.SingletonId)
            .Select(a => a.SecurityStamp)
            .FirstOrDefaultAsync(ct);
        return current is not null && FixedTimeEquals(stamp, current);
    }

    /// <returns>The upgraded password hash when one was stored, otherwise null.</returns>
    private async Task<string?> RecordSuccessAsync(string? rehashPassword, CancellationToken ct)
    {
        var owner = _db.OwnerAccounts.Where(a => a.Id == OwnerAccount.SingletonId);
        await owner.ExecuteUpdateAsync(s => s
            .SetProperty(a => a.FailedAccessCount, 0)
            .SetProperty(a => a.LockoutEndUtc, (DateTime?)null), ct);
        if (rehashPassword is not null)
        {
            // The stored hash used older parameters; upgrade it now that the password is known.
            var newHash = _hasher.Hash(rehashPassword);
            await owner.ExecuteUpdateAsync(s => s.SetProperty(a => a.PasswordHash, newHash), ct);
            return newHash;
        }
        return null;
    }

    private async Task RecordFailureAsync(DateTime now, CancellationToken ct)
    {
        var owner = _db.OwnerAccounts.Where(a => a.Id == OwnerAccount.SingletonId);
        await owner.ExecuteUpdateAsync(s => s.SetProperty(a => a.FailedAccessCount, a => a.FailedAccessCount + 1), ct);
        var lockedUntil = now + LockoutDuration;
        var locked = await owner.Where(a => a.FailedAccessCount >= MaxFailedAttempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.LockoutEndUtc, (DateTime?)lockedUntil)
                .SetProperty(a => a.FailedAccessCount, 0), ct);
        if (locked > 0)
            _logger.LogWarning("Owner sign-in locked for {Minutes} minutes after {Attempts} failed attempts.", LockoutDuration.TotalMinutes, MaxFailedAttempts);
    }

    private static string NewStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}
