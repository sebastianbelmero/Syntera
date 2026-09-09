using Microsoft.Extensions.Caching.Memory;

namespace Syntera.Backend.Services;

/// <summary>
/// Constants shared across the authentication services (AuthService was split
/// into PlatformAdminAuthService / SiteUserAuthService / RefreshFlowService /
/// RefreshTokenService — these values are needed by more than one of them).
/// </summary>
internal static class AuthConstants
{
    /// <summary>The single role claim carried by platform-scope tokens.</summary>
    public static readonly string[] PlatformAdminRoleClaim = { "platform-admin" };

    /// <summary>Failed login attempts before account lockout (password AND MFA paths).</summary>
    public const int MaxFailedLogins = 5;

    /// <summary>Lockout window applied after <see cref="MaxFailedLogins"/> consecutive failures.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // COMPLIANCE (Sprint 2.3): challenge token scopes — distinct strings
    // ensure a challenge token issued for one flow cannot be replayed
    // against another. The JwtTokenService rejects any mismatch.
    internal const string MfaChallengeScope = "mfa-challenge";
    internal const string PasswordChangeChallengeScope = "password-change-challenge";
}

/// <summary>
/// M4: login rate limiting. Two independent buckets per account protect against
/// different attack patterns:
/// <list type="bullet">
///   <item>(IP, email) — blocks a single attacker at one IP hammering one account.</item>
///   <item>email alone — blocks a botnet spraying one account from many IPs.</item>
/// </list>
/// Failure messages collapse to the same generic "RATE_LIMITED" so attackers
/// can't tell which bucket tripped. Each bucket is a 15-minute window.
/// </summary>
internal static class LoginThrottle
{
    public static string PerIpKey(string? ip, string email) => $"login:rl:ip:{ip}:{email}";
    public static string PerEmailKey(string email) => $"login:rl:email:{email}";

    /// <summary>True when either bucket has reached the failure limit.</summary>
    public static bool IsBlocked(IMemoryCache cache, string? ip, string email, int maxFailedLogins)
        => (cache.TryGetValue<int>(PerIpKey(ip, email), out var ipFails) && ipFails >= maxFailedLogins)
           || (cache.TryGetValue<int>(PerEmailKey(email), out var emailFails) && emailFails >= maxFailedLogins);

    /// <summary>Increment one or more rate-limit buckets (15-minute window each).</summary>
    public static void BumpFail(IMemoryCache cache, params string[] keys)
    {
        foreach (var key in keys)
        {
            var current = cache.GetOrCreate(key, e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
                return 0;
            });
            cache.Set(key, current + 1, TimeSpan.FromMinutes(15));
        }
    }

    /// <summary>Clear both buckets on a successful password verify (M4).</summary>
    public static void Clear(IMemoryCache cache, string? ip, string email)
    {
        cache.Remove(PerIpKey(ip, email));
        cache.Remove(PerEmailKey(email));
    }
}
