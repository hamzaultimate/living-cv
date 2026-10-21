using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Portfolio.Data.Entities;

namespace Portfolio.Web.Security;

/// <summary>
/// A signed "this browser has signed in before" cookie, issued after a successful sign-in.
///
/// Why it exists: the account lockout is global (one account, so every failed attempt counts, whatever name was
/// typed), which lets a stranger keep the owner locked out by failing on purpose. A browser that holds a valid
/// cookie is not turned away by that lockout; it still has to give the correct password and is still subject to the
/// per-address throttle. The cookie is useless on its own, and an attacker who never signed in has none.
///
/// The cookie is encrypted and signed with Data Protection and bound to the current password hash, so it stops
/// working when the password is reset (including <c>reset-owner</c>). It is deliberately not bound to the security
/// stamp: signing out rotates the stamp to end sessions, and that must not take the owner's trusted browsers with it.
/// </summary>
public sealed class KnownDevices
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private readonly bool _alwaysSecure;

    public KnownDevices(IDataProtectionProvider provider, TimeProvider time, string cookieName, bool alwaysSecure)
    {
        _protector = provider.CreateProtector("Portfolio.Web.KnownDevice.v1");
        _time = time;
        CookieName = cookieName;
        _alwaysSecure = alwaysSecure;
    }

    public string CookieName { get; }

    public string Issue(OwnerAccount account) =>
        _protector.Protect($"{Binding(account)}|{_time.GetUtcNow().ToUnixTimeSeconds()}");

    public bool IsValid(string? token, OwnerAccount account)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 1024) return false;
        try
        {
            var parts = _protector.Unprotect(token).Split('|');
            if (parts.Length != 2 || !long.TryParse(parts[1], out var issued)) return false;
            var age = _time.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(issued);
            if (age > Lifetime || age < TimeSpan.FromMinutes(-5)) return false;
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(parts[0]), Encoding.UTF8.GetBytes(Binding(account)));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public string? Read(HttpContext http) => http.Request.Cookies[CookieName];

    public void Append(HttpContext http, OwnerAccount account) =>
        http.Response.Cookies.Append(CookieName, Issue(account), new CookieOptions
        {
            HttpOnly = true,
            Secure = _alwaysSecure || http.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = Lifetime,
            IsEssential = true,
        });

    private static string Binding(OwnerAccount account) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("lcv-device|" + account.PasswordHash)));
}
