using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Data.Entities;

namespace Portfolio.Web.Security;

public enum CreateOwnerOutcome { Created, AlreadyExists }

public enum SignInOutcome { Succeeded, Failed, LockedOut }

public sealed record OwnerSignInResult(SignInOutcome Outcome, OwnerAccount? Account = null);

/// <summary>
/// Creates, resets and authenticates the single owner account.
/// Lockout state lives on the account row, so it survives restarts and cannot be dodged by varying the
/// user name: with exactly one account, every failed attempt counts against it, whatever name was typed.
/// Attempts are serialised in-process, so parallel guesses cannot overshoot the limit and the
/// expensive hash runs one at a time.
/// </summary>
public sealed class OwnerAccountService
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private static readonly SemaphoreSlim SignInGate = new(1, 1);

    private readonly PortfolioDbContext _db;
    private readonly OwnerPasswordHasher _hasher;
    private readonly TimeProvider _time;
    private readonly ILogger<OwnerAccountService> _logger;

    public OwnerAccountService(PortfolioDbContext db, OwnerPasswordHasher hasher, TimeProvider time, ILogger<OwnerAccountService> logger)
    {
        _db = db;
        _hasher = hasher;
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

    public async Task<OwnerSignInResult> SignInAsync(string? userName, string? password, CancellationToken ct = default)
    {
        password ??= "";
        await SignInGate.WaitAsync(ct);
        try
        {
            var account = await _db.OwnerAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == OwnerAccount.SingletonId, ct);
            if (account is null)
            {
                _hasher.SpendVerificationCost(password);
                return new OwnerSignInResult(SignInOutcome.Failed);
            }

            var now = _time.GetUtcNow().UtcDateTime;
            if (account.LockoutEndUtc is { } until && until > now)
            {
                _hasher.SpendVerificationCost(password);
                return new OwnerSignInResult(SignInOutcome.LockedOut);
            }

            // Always verify against the real hash, even when the name is wrong, so timing does not reveal which part failed.
            var verification = _hasher.Verify(account.PasswordHash, password);
            var nameMatches = FixedTimeEquals(NormalizeUserName(userName), NormalizeUserName(account.UserName));
            if (nameMatches && verification != PasswordVerificationResult.Failed)
            {
                await RecordSuccessAsync(verification == PasswordVerificationResult.SuccessRehashNeeded ? password : null, ct);
                return new OwnerSignInResult(SignInOutcome.Succeeded, account);
            }

            await RecordFailureAsync(now, ct);
            return new OwnerSignInResult(SignInOutcome.Failed);
        }
        finally
        {
            SignInGate.Release();
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

    private async Task RecordSuccessAsync(string? rehashPassword, CancellationToken ct)
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
        }
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
