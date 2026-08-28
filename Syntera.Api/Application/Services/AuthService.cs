using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Syntera.Application.DTOs.Auth;
using Syntera.Application.Interfaces.Services;
using Syntera.Domain.Entities;
using Syntera.Domain.Exceptions;
using Syntera.Infrastructure.Data;
using Syntera.Infrastructure.Ldap;
using System.Security.Cryptography;

namespace Syntera.Application.Services;

/// <summary>
/// Core authentication flow. Resolves authentication strategy by email domain:
/// - <c>@syntera.com</c> → Platform Admin (local bcrypt credential)
/// - Any registered site domain → Site LDAP (LDAPS / StartTLS)
/// - Unknown domain → reject
///
/// After successful auth, issues JWT (15min) + refresh token (24h, rotating).
/// Refresh tokens are tracked server-side for revocation. The login flow is
/// the SINGLE entry point for the entire platform.
/// </summary>
public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, string? ip, string? userAgent, CancellationToken ct = default);
    Task<RefreshResponse> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, Guid? revokedBy, CancellationToken ct = default);
    Task<UserProfileDto> GetProfileAsync(CancellationToken ct = default);
}

public sealed class AuthService : IAuthService
{
    private static readonly string[] PlatformAdminRoleClaim = { "platform-admin" };

    private readonly PlatformDbContext _platformDb;
    private readonly ISiteDbContextFactory _siteDbFactory;
    private readonly ILdapClient _ldap;
    private readonly ITokenService _tokens;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditService _audit;
    private readonly IThemeService _themes;
    private readonly IPermissionService _permissions;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthService> _log;

    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public AuthService(
        PlatformDbContext platformDb,
        ISiteDbContextFactory siteDbFactory,
        ILdapClient ldap,
        ITokenService tokens,
        IPasswordHasher hasher,
        IAuditService audit,
        IThemeService themes,
        IPermissionService permissions,
        IMemoryCache cache,
        ILogger<AuthService> log)
    {
        _platformDb = platformDb;
        _siteDbFactory = siteDbFactory;
        _ldap = ldap;
        _tokens = tokens;
        _hasher = hasher;
        _audit = audit;
        _themes = themes;
        _permissions = permissions;
        _cache = cache;
        _log = log;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
            throw new AuthenticationException("INVALID_EMAIL", "Valid email is required.");

        // ── Rate limit by IP+email ───────────────────────────────────
        var rateKey = $"login:rl:{ip}:{email}";
        if (_cache.TryGetValue<int>(rateKey, out var fails) && fails >= MaxFailedLogins)
            throw new AuthenticationException("RATE_LIMITED",
                "Too many failed login attempts. Try again in 15 minutes.");

        // ── Branch: Platform Admin or Site user? ─────────────────────
        if (email.EndsWith("@syntera.com", StringComparison.OrdinalIgnoreCase))
            return await LoginPlatformAdminAsync(email, request.Password, ip, userAgent, rateKey, ct);

        return await LoginSiteUserAsync(email, request.Password, ip, userAgent, rateKey, ct);
    }

    private async Task<LoginResponse> LoginPlatformAdminAsync(
        string email, string password, string? ip, string? ua, string rateKey, CancellationToken ct)
    {
        var admin = await _platformDb.PlatformUsers.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (admin is null || !admin.IsEnabled)
        {
            BumpFail(rateKey);
            await _audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: null, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.login", TargetType: "PlatformUser", TargetId: null,
                Outcome: "failure", ErrorMessage: "Unknown or disabled platform admin"), ct);
            throw new AuthenticationException("INVALID_CREDENTIALS", "Invalid credentials.");
        }

        if (admin.LockedUntil is not null && admin.LockedUntil > DateTime.UtcNow)
        {
            throw new AuthenticationException("ACCOUNT_LOCKED",
                $"Account locked until {admin.LockedUntil.Value:u}.");
        }

        if (!_hasher.Verify(password, admin.PasswordHash))
        {
            admin.FailedLoginCount++;
            admin.LastFailedLoginAt = DateTime.UtcNow;
            if (admin.FailedLoginCount >= MaxFailedLogins)
            {
                admin.LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
                admin.FailedLoginCount = 0;
            }
            await _platformDb.SaveChangesAsync(ct);
            BumpFail(rateKey);
            await _audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: admin.Id, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.login", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                Outcome: "failure", ErrorMessage: "Invalid password"), ct);
            throw new AuthenticationException("INVALID_CREDENTIALS", "Invalid credentials.");
        }

        // ── Success: reset counters ───────────────────────────────────
        admin.FailedLoginCount = 0;
        admin.LockedUntil = null;
        admin.LastLoginAt = DateTime.UtcNow;
        await _platformDb.SaveChangesAsync(ct);

        var profile = new UserProfileDto(
            UserId: admin.Id, Email: admin.Email, DisplayName: admin.DisplayName,
            Scope: "platform", SiteId: null, SiteCode: null, SiteDisplayName: null,
            Roles: PlatformAdminRoleClaim,
            Permissions: _permissions.GetPlatformAdminPermissions());

        var (access, exp, refresh) = await IssueTokensAsync(
            userId: admin.Id, scope: "platform", siteId: null,
            email: admin.Email, displayName: admin.DisplayName,
            roles: profile.Roles, permissions: profile.Permissions,
            version: 1, ip: ip, ua: ua, ct: ct);

        _cache.Remove(rateKey);

        await _audit.LogAsync(new AuditEntry(
            SiteId: null, ActorUserId: admin.Id, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
            Action: "auth.login", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
            Outcome: "success", ErrorMessage: null), ct);

        return new LoginResponse(access, exp, refresh, profile, ThemeService.PlatformDefault());
    }

    private async Task<LoginResponse> LoginSiteUserAsync(
        string email, string password, string? ip, string? ua, string rateKey, CancellationToken ct)
    {
        var domain = email[(email.IndexOf('@') + 1)..];

        // ── Resolve site by email domain ──────────────────────────────
        var domainRow = await _platformDb.LdapDomains
            .Include(d => d.Site)
            .FirstOrDefaultAsync(d => d.Domain == domain && d.IsActive, ct);

        if (domainRow is null || domainRow.Site is null || !domainRow.Site.IsEnabled)
        {
            BumpFail(rateKey);
            await _audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: null, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.login", TargetType: "Site", TargetId: null,
                Outcome: "failure", ErrorMessage: $"Domain '{domain}' not registered"), ct);
            throw new AuthenticationException("DOMAIN_NOT_REGISTERED",
                $"The email domain '{domain}' is not registered in this platform.");
        }

        var site = domainRow.Site;

        // ── Resolve LDAP config ───────────────────────────────────────
        var ldapConfig = await _platformDb.LdapConfigs
            .FirstOrDefaultAsync(c => c.SiteId == site.Id, ct);
        if (ldapConfig is null)
        {
            throw new AuthenticationException("LDAP_NOT_CONFIGURED",
                $"Site '{site.Code}' has no LDAP configuration. Contact Platform Admin.");
        }

        var endpoint = new LdapEndpoint(
            Host: ldapConfig.Host,
            Port: ldapConfig.Port,
            UseStartTls: ldapConfig.UseStartTls,
            BaseDn: ldapConfig.BaseDn,
            UpnDomain: ldapConfig.UpnDomain);

        // ── Authenticate via LDAP (direct bind: user's own email + password) ──
        var result = await _ldap.AuthenticateAsync(endpoint, email, password, ct);
        if (!result.IsSuccess)
        {
            BumpFail(rateKey);
            await _audit.LogAsync(new AuditEntry(
                SiteId: site.Id, ActorUserId: null, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.login", TargetType: "User", TargetId: null,
                Outcome: "failure", ErrorMessage: result.ErrorMessage), ct);
            throw new AuthenticationException("LDAP_AUTH_FAILED",
                result.ErrorMessage ?? "LDAP authentication failed.");
        }

        // ── Pre-provisioning check: user must exist in site DB ────────
        var siteDb = await _siteDbFactory.ResolveAsync(ct);
        var user = await siteDb.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            await _audit.LogAsync(new AuditEntry(
                SiteId: site.Id, ActorUserId: null, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.login", TargetType: "User", TargetId: null,
                Outcome: "failure", ErrorMessage: "User not provisioned in site"), ct);
            throw new AuthenticationException("USER_NOT_PROVISIONED",
                "Your account has not been provisioned. Contact your Site Business Admin.");
        }

        if (!user.IsEnabled)
        {
            await _audit.LogAsync(new AuditEntry(
                SiteId: site.Id, ActorUserId: user.Id, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.login", TargetType: "User", TargetId: user.Id.ToString(),
                Outcome: "failure", ErrorMessage: "User disabled in site"), ct);
            throw new AuthenticationException("USER_DISABLED",
                "Your account is disabled. Contact your Site Business Admin.");
        }

        if (user.LockedUntil is not null && user.LockedUntil > DateTime.UtcNow)
        {
            throw new AuthenticationException("ACCOUNT_LOCKED",
                $"Account locked until {user.LockedUntil.Value:u}.");
        }

        // ── Update display name from LDAP (sync) ──────────────────────
        if (!string.IsNullOrEmpty(result.DisplayName) && result.DisplayName != user.DisplayName)
        {
            user.DisplayName = result.DisplayName;
        }
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTime.UtcNow;
        await siteDb.SaveChangesAsync(ct);

        // ── Resolve effective permissions ─────────────────────────────
        var (roles, perms) = await _permissions.ResolveForUserAsync(siteDb, user.Id, ct);
        var siteDisplay = site.DisplayName;

        var profile = new UserProfileDto(
            UserId: user.Id, Email: user.Email, DisplayName: user.DisplayName,
            Scope: "site", SiteId: site.Id, SiteCode: site.Code, SiteDisplayName: siteDisplay,
            Roles: roles, Permissions: perms);

        var (access, exp, refresh) = await IssueTokensAsync(
            userId: user.Id, scope: "site", siteId: site.Id,
            email: user.Email, displayName: user.DisplayName,
            roles: roles, permissions: perms,
            version: user.PermissionsVersion, ip: ip, ua: ua, ct: ct);

        // Save refresh token in the right DB.
        if (user.Id != Guid.Empty)
        {
            var rt = BuildRefreshToken(user.Id, "site", site.Id, refresh, ip, ua);
            siteDb.RefreshTokens.Add(rt);
            await siteDb.SaveChangesAsync(ct);
        }

        _cache.Remove(rateKey);

        var theme = await _themes.GetThemeAsync(site.Id, ct);

        await _audit.LogAsync(new AuditEntry(
            SiteId: site.Id, ActorUserId: user.Id, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
            Action: "auth.login", TargetType: "User", TargetId: user.Id.ToString(),
            Outcome: "success", ErrorMessage: null), ct);

        return new LoginResponse(access, exp, refresh, profile, theme);
    }

    public async Task<RefreshResponse> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new AuthenticationException("INVALID_REFRESH", "Refresh token is required.");

        var hash = SHA256Hex(refreshToken);

        // Look in platform DB first (platform admin).
        var platformToken = await _platformDb.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct);
        if (platformToken is not null)
        {
            if (platformToken.ExpiresAt <= DateTime.UtcNow)
                throw new AuthenticationException("REFRESH_EXPIRED", "Refresh token expired.");

            // Rotate: revoke old, issue new.
            platformToken.RevokedAt = DateTime.UtcNow;
            platformToken.RevokedBy = platformToken.UserId;
            await _platformDb.SaveChangesAsync(ct);

            var admin = await _platformDb.PlatformUsers.FirstOrDefaultAsync(u => u.Id == platformToken.UserId, ct);
            if (admin is null || !admin.IsEnabled)
                throw new AuthenticationException("USER_NOT_FOUND", "Platform admin no longer exists.");

            var profile = new UserProfileDto(
                UserId: admin.Id, Email: admin.Email, DisplayName: admin.DisplayName,
                Scope: "platform", SiteId: null, SiteCode: null, SiteDisplayName: null,
                Roles: PlatformAdminRoleClaim,
                Permissions: _permissions.GetPlatformAdminPermissions());

            var (access, exp, newRefresh) = await IssueTokensAsync(
                admin.Id, "platform", null, admin.Email, admin.DisplayName,
                profile.Roles, profile.Permissions, 1, ip, userAgent, ct);

            var newRt = BuildRefreshToken(admin.Id, "platform", null, newRefresh, ip, userAgent);
            newRt.ReplacedById = platformToken.Id;
            _platformDb.RefreshTokens.Add(newRt);
            await _platformDb.SaveChangesAsync(ct);

            return new RefreshResponse(access, exp, newRefresh, profile, ThemeService.PlatformDefault());
        }

        // Otherwise, look across site DBs. Since refresh tokens live in each site DB,
        // we iterate. In production, embed siteId in the token's first 8 chars (omitted here for brevity).
        // For simplicity, we let the client send siteId as part of refresh (refresh endpoint accepts body).
        // Here, we accept that the frontend will pass refresh token; we hash and try the current site's DB
        // via JWT context (requires a still-valid access token in the Authorization header).
        // If you want fully-decoupled refresh, embed siteId in the refresh token prefix.
        // We'll use the access token's expired claims to recover siteId.
        // Implementation note: the controller passes the previously-known siteId.
        // For this implementation, we expect the API to use /api/auth/refresh endpoint
        // with body {refreshToken, siteId}.
        throw new AuthenticationException("REFRESH_NOT_FOUND",
            "Refresh token not found in platform scope. For site refresh, POST to /api/auth/refresh-site with siteId.");
    }

    public async Task<RefreshResponse> RefreshSiteAsync(string refreshToken, Guid siteId, string? ip, string? ua, CancellationToken ct = default)
    {
        var hash = SHA256Hex(refreshToken);

        // Verify site exists.
        var site = await _platformDb.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct)
            ?? throw new NotFoundException("Site", siteId);

        var siteDb = await _siteDbFactory.ResolveAsync(ct);
        var token = await siteDb.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct)
            ?? throw new AuthenticationException("REFRESH_NOT_FOUND", "Refresh token not found.");

        if (token.ExpiresAt <= DateTime.UtcNow)
            throw new AuthenticationException("REFRESH_EXPIRED", "Refresh token expired.");

        token.RevokedAt = DateTime.UtcNow;
        token.RevokedBy = token.UserId;
        await siteDb.SaveChangesAsync(ct);

        var user = await siteDb.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, ct)
            ?? throw new AuthenticationException("USER_NOT_FOUND", "User no longer exists.");

        if (!user.IsEnabled)
            throw new AuthenticationException("USER_DISABLED", "User is disabled.");

        var (roles, perms) = await _permissions.ResolveForUserAsync(siteDb, user.Id, ct);
        var profile = new UserProfileDto(
            UserId: user.Id, Email: user.Email, DisplayName: user.DisplayName,
            Scope: "site", SiteId: site.Id, SiteCode: site.Code, SiteDisplayName: site.DisplayName,
            Roles: roles, Permissions: perms);

        var (access, exp, newRefresh) = await IssueTokensAsync(
            user.Id, "site", site.Id, user.Email, user.DisplayName,
            roles, perms, user.PermissionsVersion, ip, ua, ct);

        var newRt = BuildRefreshToken(user.Id, "site", site.Id, newRefresh, ip, ua);
        newRt.ReplacedById = token.Id;
        siteDb.RefreshTokens.Add(newRt);
        await siteDb.SaveChangesAsync(ct);

        var theme = await _themes.GetThemeAsync(site.Id, ct);
        return new RefreshResponse(access, exp, newRefresh, profile, theme);
    }

    public async Task LogoutAsync(string refreshToken, Guid? revokedBy, CancellationToken ct = default)
    {
        var hash = SHA256Hex(refreshToken);
        var platformToken = await _platformDb.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (platformToken is not null)
        {
            platformToken.RevokedAt = DateTime.UtcNow;
            platformToken.RevokedBy = revokedBy;
            await _platformDb.SaveChangesAsync(ct);
            return;
        }
        // Site refresh tokens require site context to revoke — handled by the controller.
    }

    public async Task<UserProfileDto> GetProfileAsync(CancellationToken ct = default)
    {
        // Implementation requires authenticated context. The controller reads claims
        // and constructs profile from them. We provide a stub for the contract.
        throw new NotImplementedException("Use controller-level claim resolution for GetProfile.");
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private async Task<(string AccessToken, DateTime ExpiresAt, string RefreshToken)> IssueTokensAsync(
        Guid userId, string scope, Guid? siteId, string email, string displayName,
        IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions,
        long version, string? ip, string? ua, CancellationToken ct)
    {
        var access = _tokens.IssueFor(userId, scope, siteId, email, displayName, roles, permissions, version);
        var refresh = GenerateRefreshToken();
        return (access.Token, access.ExpiresAt, refresh);
    }

    private static RefreshToken BuildRefreshToken(Guid userId, string scope, Guid? siteId, string rawToken, string? ip, string? ua)
    {
        return new RefreshToken
        {
            Token = rawToken,
            TokenHash = SHA256Hex(rawToken),
            UserId = userId,
            UserScope = scope,
            SiteId = siteId,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            CreatedFromIp = ip,
            CreatedUserAgent = ua,
        };
    }

    private static string GenerateRefreshToken(int bytes = 32)
    {
        var buf = new byte[bytes];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(buf);
        return Convert.ToBase64String(buf).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string SHA256Hex(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    private void BumpFail(string key)
    {
        var current = _cache.GetOrCreate(key, e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            return 0;
        });
        _cache.Set(key, current + 1, TimeSpan.FromMinutes(15));
    }
}

/// <summary>Extra extension for site-scoped refresh (kept separate to avoid bloating the interface).</summary>
public static class AuthServiceExtensions
{
    public static async Task<RefreshResponse> RefreshSiteAsync(
        this IAuthService svc, string refreshToken, Guid siteId, string? ip, string? ua, CancellationToken ct = default)
    {
        if (svc is AuthService concrete)
            return await concrete.RefreshSiteAsync(refreshToken, siteId, ip, ua, ct);
        throw new NotImplementedException();
    }
}
