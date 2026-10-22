namespace Portfolio.Web.Security;

/// <summary>Rules for the owner password, applied at setup and by reset-owner.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    /// <summary>Returns a message describing the first broken rule, or null when the password is acceptable.</summary>
    public static string? Validate(string? password, string? userName = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"The password must be at least {MinLength} characters.";
        if (password.Length > MaxLength)
            return $"The password must be at most {MaxLength} characters.";
        if (string.IsNullOrWhiteSpace(password))
            return "The password cannot be only whitespace.";
        if (!string.IsNullOrEmpty(userName) && string.Equals(password.Trim(), userName.Trim(), StringComparison.OrdinalIgnoreCase))
            return "The password cannot be the same as the user name.";
        return null;
    }
}
