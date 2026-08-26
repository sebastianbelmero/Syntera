using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Syntera.Application.DTOs.Auth;
using Syntera.Application.Interfaces.Services;
using Syntera.Application.Logging;
using Syntera.Domain.Exceptions;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Syntera.Application.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest req, CancellationToken ct = default);
    Task<LoginResponse> RefreshAsync(string refreshToken, CancellationToken ct = default);
}

public sealed class AuthService : IAuthService
{
    private readonly SignInManager<IdentityUser> _signIn;
    private readonly UserManager<IdentityUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly IConfiguration _cfg;
    private readonly ILogger<AuthService> _log;

    public AuthService(
        SignInManager<IdentityUser> signIn,
        UserManager<IdentityUser> users,
        RoleManager<IdentityRole> roles,
        IConfiguration cfg,
        ILogger<AuthService> log)
    {
        _signIn = signIn; _users = users; _roles = roles;
        _cfg = cfg; _log = log;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest req, CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(req.Email)
            ?? throw new BusinessRuleException("INVALID_CREDENTIALS", "Email or password is incorrect.");

        var result = await _signIn.PasswordSignInAsync(user, req.Password, false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            AuthLogger.LogFailedLogin(_log, req.Email);
            throw new BusinessRuleException("INVALID_CREDENTIALS",
                result.IsLockedOut ? "Account is locked. Try again later." : "Email or password is incorrect.");
        }

        var roles = await _users.GetRolesAsync(user);
        var tokens = IssueTokens(user, roles);
        return new LoginResponse(
            tokens.AccessToken,
            "Bearer",
            tokens.ExpiresAt,
            tokens.RefreshToken ?? string.Empty,
            new UserProfile(
                Guid.Parse(user.Id),
                user.Email ?? string.Empty,
                user.UserName,
                roles.ToList()));
    }

    public async Task<LoginResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var (principal, _) = ValidateRefreshTokenPrincipal(refreshToken)
            ?? throw new BusinessRuleException("INVALID_REFRESH", "Refresh token is invalid or expired.");

        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new BusinessRuleException("INVALID_REFRESH", "Refresh token subject missing.");
        var user = await _users.FindByIdAsync(userId)
            ?? throw new BusinessRuleException("INVALID_REFRESH", "User not found.");

        var roles = await _users.GetRolesAsync(user);
        var tokens = IssueTokens(user, roles);
        return new LoginResponse(
            tokens.AccessToken, "Bearer", tokens.ExpiresAt,
            tokens.RefreshToken ?? string.Empty,
            new UserProfile(
                Guid.Parse(user.Id), user.Email ?? string.Empty,
                user.UserName, roles.ToList()));
    }

    // ── Helpers ──────────────────────────────────────────────────
    private (string AccessToken, DateTime ExpiresAt, string RefreshToken) IssueTokens(
        IdentityUser user, IEnumerable<string> roles)
    {
        var key = GetSigningKey();
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var accessExpires = DateTime.UtcNow.AddMinutes(
            _cfg.GetValue("Jwt:AccessMinutes", 15));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var access = new JwtSecurityToken(
            issuer: _cfg["Jwt:Issuer"],
            audience: _cfg["Jwt:Audience"],
            claims: claims,
            expires: accessExpires,
            signingCredentials: creds);
        var accessStr = new JwtSecurityTokenHandler().WriteToken(access);

        // Refresh — same key, longer lifetime, separate audience.
        var refreshExpires = DateTime.UtcNow.AddDays(
            _cfg.GetValue("Jwt:RefreshDays", 7));
        var refreshClaims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new("type", "refresh"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        var refresh = new JwtSecurityToken(
            issuer: _cfg["Jwt:Issuer"],
            audience: _cfg["Jwt:Audience"] + "-refresh",
            claims: refreshClaims,
            expires: refreshExpires,
            signingCredentials: creds);
        var refreshStr = new JwtSecurityTokenHandler().WriteToken(refresh);

        return (accessStr, accessExpires, refreshStr);
    }

    private (ClaimsPrincipal principal, JwtSecurityToken token)? ValidateRefreshTokenPrincipal(string token)
    {
        try
        {
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _cfg["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = _cfg["Jwt:Audience"] + "-refresh",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = GetSigningKey(),
                ClockSkew = TimeSpan.FromSeconds(30),
            };
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, parameters, out var raw);
            return (principal, (JwtSecurityToken)raw);
        }
        catch (Exception ex)
        {
            AuthLogger.LogRefreshValidationFailed(_log, ex);
            return null;
        }
    }

    private SymmetricSecurityKey GetSigningKey()
    {
        var raw = _cfg["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey missing. Set via User Secrets / env var.");
        if (raw.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters for HS256.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(raw));
    }
}
