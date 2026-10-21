namespace Portfolio.Data.Entities;

/// <summary>
/// The site owner's login: one row, always <see cref="SingletonId"/>, enforced by a check constraint
/// so the database itself can never hold a second account. Created once by /setup and changed only
/// by the reset-owner command.
/// </summary>
public class OwnerAccount
{
    /// <summary>Column maximum lengths, shared by the model configuration and anything that writes the row.</summary>
    public static class MaxLength
    {
        public const int UserName = 64;
        public const int PasswordHash = 512;
        public const int SecurityStamp = 64;
    }

    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Login name as entered at setup. Compared case-insensitively and ignoring surrounding whitespace.</summary>
    public string UserName { get; set; } = "";

    /// <summary>Salted, iterated password hash (ASP.NET Core PasswordHasher format). Never the password.</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>Random value baked into the sign-in cookie; rotating it signs out every existing session.</summary>
    public string SecurityStamp { get; set; } = "";

    /// <summary>Consecutive failed sign-ins since the last success or lockout.</summary>
    public int FailedAccessCount { get; set; }

    /// <summary>While in the future, sign-in is refused regardless of the password.</summary>
    public DateTime? LockoutEndUtc { get; set; }

    public DateTime CreatedUtc { get; set; }
}
