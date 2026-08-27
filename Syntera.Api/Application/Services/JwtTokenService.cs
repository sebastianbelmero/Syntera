using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Syntera.Application.Interfaces.Services;

namespace Syntera.Application.Services;
/// <summary>
/// Issues and validates JWTs. Symmetric signing key (HS256) is loaded from
/// configuration. In production, the key MUST be supplied via environment
/// variable or key vault — the startup pipeline fails-fast if the key is
/// missing or shorter than 32 bytes.
///
/// The JWT carries:
/// - sub (user ID)
/// - email, display_name
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
        IEnumerable<string> roles, IEnumerable<string> permissions, long permissionsVersion);

    ClaimsPrincipal? Validate(string token);
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
