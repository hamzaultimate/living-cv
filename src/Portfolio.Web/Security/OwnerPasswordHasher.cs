using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Portfolio.Data.Entities;

namespace Portfolio.Web.Security;

/// <summary>
/// Hashes and verifies the owner password with ASP.NET Core's <see cref="PasswordHasher{TUser}"/>
/// (PBKDF2-HMAC-SHA512, random salt, iteration count stored in the hash so it can be raised later).
/// Also holds a throwaway hash so a sign-in for a missing or locked account costs the same as a real check.
/// </summary>
public sealed class OwnerPasswordHasher
{
    /// <summary>Above the framework default (100,000) and in line with current PBKDF2-SHA512 guidance.</summary>
    public const int IterationCount = 210_000;

    private readonly PasswordHasher<OwnerAccount> _hasher =
        new(Options.Create(new PasswordHasherOptions { IterationCount = IterationCount }));
    private readonly string _decoyHash;

    public OwnerPasswordHasher()
    {
        _decoyHash = _hasher.HashPassword(new OwnerAccount(), Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)));
    }

    public string Hash(string password) => _hasher.HashPassword(new OwnerAccount(), password);

    public PasswordVerificationResult Verify(string hash, string password) =>
        _hasher.VerifyHashedPassword(new OwnerAccount(), hash, password);

    /// <summary>Spends the cost of a verification against a hash nobody knows the password for.</summary>
    public void SpendVerificationCost(string password) => _hasher.VerifyHashedPassword(new OwnerAccount(), _decoyHash, password);
}
