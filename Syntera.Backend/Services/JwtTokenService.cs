using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Syntera.Backend.Services;

namespace Syntera.Backend.Services;
/// <summary>
/// Issues and validates JWTs. Symmetric signing key (HS256) is loaded from
/// configuration. In production, the key MUST be supplied via environment
/// variable or key vault — the startup pipeline fails-fast if the key is
/// missing or shorter than 32 bytes.
///
/// The JWT carries:
/// - sub (user ID)
/// - email, display_name, title (title may be absent for platform admins)
/// - scope (platform | site)
/// - site_id, site_code (null for platform)
/// - perm_ver (for stale-perm detection)
/// - role[] claims
/// - perm[] claims (each effective permission)
/// - is_platform_admin / is_site_admin (boolean flags)
/// </summary>
public interface ITokenService
{
    (string Token, DateTime ExpiresAt) IssueFor(
        Guid userId, string scope, Guid? siteId, string email, string displayName,
        string? title,
        IEnumerable<string> roles, IEnumerable<string> permissions, long permissionsVersion);

    ClaimsPrincipal? Validate(string token);

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): Issue a short-lived, single-purpose challenge
    /// token used to continue an in-progress authentication flow without
    /// granting API access. The token carries only the user id and a scope
    /// string (e.g., <c>"mfa-challenge"</c>, <c>"password-change-challenge"</c>)
    /// — it is NOT a bearer token for the API and is rejected by the regular
    /// JWT middleware (no roles, no <c>scope=platform|site</c> claim). The
    /// caller verifies it via <see cref="ValidateChallenge"/> and uses the
    /// returned userId to continue the flow.
    /// </summary>
    (string Token, DateTime ExpiresAt) IssueChallenge(Guid userId, string scope, int lifetimeMinutes = 5);

    /// <summary>
    /// Validate a challenge token issued by <see cref="IssueChallenge"/>.
    /// Returns the userId embedded in the token if the signature is valid,
    /// the token is unexpired, AND the <paramref name="expectedScope"/>
    /// matches the token's scope claim. Returns <c>null</c> on any failure
    /// (invalid signature, wrong scope, expired) — callers should treat
    /// null as "challenge failed, re-authenticate".
    /// </summary>
    Guid? ValidateChallenge(string token, string expectedScope);
}

public sealed class JwtTokenService : ITokenService
{
    private readonly SymmetricSecurityKey _key;
    private readonly int _accessTokenMinutes;

    public JwtTokenService(IConfiguration config)
    {
        var rawKey = config["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is missing from configuration.");

        if (rawKey.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters.");

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(rawKey));
        _accessTokenMinutes = int.TryParse(config["Jwt:AccessTokenMinutes"], out var m) ? m : 15;
    }

    public (string Token, DateTime ExpiresAt) IssueFor(
        Guid userId, string scope, Guid? siteId, string email, string displayName,
        string? title,
        IEnumerable<string> roles, IEnumerable<string> permissions, long permissionsVersion)
    {
        var now = DateTime.UtcNow;
        var exp = now.AddMinutes(_accessTokenMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, email),
            new("email", email),
            new("display_name", displayName),
            new("scope", scope),
            new("perm_ver", permissionsVersion.ToString(CultureInfo.InvariantCulture)),
        };

        // Title is optional — AD may not have it populated for every user.
        // We add the claim only when we actually have a value, so the
        // AuthController.Profile() can tell the difference between "no title
        // set" and "title claim missing" (both surface as null in the DTO).
        if (!string.IsNullOrWhiteSpace(title))
            claims.Add(new Claim("title", title));

        if (siteId is not null)
        {
            claims.Add(new Claim("site_id", siteId.Value.ToString()));
        }

        foreach (var role in roles.Distinct())
            claims.Add(new Claim(ClaimTypes.Role, role));

        foreach (var p in permissions.Distinct())
            claims.Add(new Claim("perm", p));

        // Flag for fast authz middleware checks.
        if (roles.Contains("platform-admin"))
            claims.Add(new Claim("is_platform_admin", "true"));
        if (roles.Contains("site-business-admin"))
            claims.Add(new Claim("is_site_admin", "true"));

        var token = new JwtSecurityToken(
            issuer: "syntera",
            audience: "syntera-api",
            claims: claims,
            notBefore: now,
            expires: exp,
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        var handler = new JwtSecurityTokenHandler();
        return (handler.WriteToken(token), exp);
    }

    public ClaimsPrincipal? Validate(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "syntera",
                ValidateAudience = true,
                ValidAudience = "syntera-api",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _key,
                ClockSkew = TimeSpan.FromSeconds(30),
            };
            return handler.ValidateToken(token, parameters, out _);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): Issue a short-lived challenge token. The token
    /// is signed with the same JWT key but carries a separate audience
    /// (<c>"syntera-challenge"</c>) so the regular API JWT middleware (which
    /// validates audience = <c>"syntera-api"</c>) automatically rejects it.
    /// Only the explicit <see cref="ValidateChallenge"/> call site accepts it.
    ///
    /// <para>The token's <c>scope</c> claim identifies the challenge type
    /// (e.g., <c>"mfa-challenge"</c>, <c>"password-change-challenge"</c>) so
    /// a challenge token issued for one flow cannot be replayed against
    /// another (defense in depth — a stolen MFA challenge cannot be used
    /// to satisfy a forced password change and vice versa).</para>
    /// </summary>
    public (string Token, DateTime ExpiresAt) IssueChallenge(Guid userId, string scope, int lifetimeMinutes = 5)
    {
        if (lifetimeMinutes <= 0) lifetimeMinutes = 5;
        var now = DateTime.UtcNow;
        var exp = now.AddMinutes(lifetimeMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("scope", scope),
            // Mark as a challenge token so audit/forensic tooling can
            // distinguish challenge tokens from API access tokens.
            new("challenge_token", "true"),
        };

        var token = new JwtSecurityToken(
            issuer: "syntera",
            // Distinct audience — API JWT middleware rejects this audience.
            audience: "syntera-challenge",
            claims: claims,
            notBefore: now,
            expires: exp,
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        var handler = new JwtSecurityTokenHandler();
        return (handler.WriteToken(token), exp);
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): Validate a challenge token. Returns the userId
    /// if (and only if) the signature is valid, the token is unexpired, AND
    /// the <paramref name="expectedScope"/> matches the token's scope claim.
    /// Any failure returns <c>null</c> — callers MUST treat null as
    /// "challenge failed, re-authenticate" and surface a generic
    /// authentication error to avoid leaking which check failed.
    /// </summary>
    public Guid? ValidateChallenge(string token, string expectedScope)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(expectedScope))
            return null;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "syntera",
                // Must match the distinct audience used by IssueChallenge —
                // this is what prevents a regular API access token from
                // being misused as a challenge token (and vice versa).
                ValidateAudience = true,
                ValidAudience = "syntera-challenge",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _key,
                ClockSkew = TimeSpan.FromSeconds(30),
            };
            var principal = handler.ValidateToken(token, parameters, out _);
            var scopeClaim = principal.FindFirst("scope")?.Value;
            if (!string.Equals(scopeClaim, expectedScope, StringComparison.Ordinal))
                return null;
            var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(sub, out var uid) ? uid : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>BCrypt-based password hasher for platform admin credentials.</summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    public bool Verify(string password, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(password, hash); }
        catch { return false; }
    }
}
