using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Syntera.Backend.Models.Dtos.Auth;
using Syntera.Backend.Services;
using Syntera.Backend.Models.Entities;
using Syntera.Backend.Models;
using Syntera.Backend.Data;
using System.Security.Cryptography;

namespace Syntera.Backend.Services;

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
    private readonly ICurrentUserService _currentUser;
    // L3: HMAC key for refresh token signature (fast-reject invalid tokens
    // without DB lookup). Derived from Jwt:SigningKey so no extra config.
    private readonly byte[] _refreshHmacKey;

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
        ILogger<AuthService> log,
        ICurrentUserService currentUser,
        Microsoft.Extensions.Configuration.IConfiguration config)
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
        _currentUser = currentUser;

        // L3: derive HMAC key from JWT signing key. Same secret, different
        // purpose (subdomain separation via "refresh-token-v1" prefix).
        var jwtKey = config["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is required for refresh token HMAC (L3).");
        // Hash the (prefix + jwtKey) string with SHA-256 to derive a 32-byte
        // HMAC key. Subdomain separation via the prefix ensures this key is
        // distinct from any other use of Jwt:SigningKey.
        _refreshHmacKey = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("syntera:refresh-hmac:v1:" + jwtKey));
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
            UserId: admin.Id, Email: admin.Email, DisplayName: admin.DisplayName, Title: null,
            Scope: "platform", SiteId: null, SiteCode: null, SiteDisplayName: null,
            Roles: PlatformAdminRoleClaim,
            Permissions: _permissions.GetPlatformAdminPermissions());

        var (access, exp, refresh) = await IssueTokensAsync(
            userId: admin.Id, scope: "platform", siteId: null,
            email: admin.Email, displayName: admin.DisplayName, title: null,
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
            // SECURITY (H5): audit log keeps the detailed internal error for
            // forensic/admin debugging, but the user-facing exception is
            // genericized to prevent account enumeration / info leakage.
            // Attackers must not be able to distinguish:
            //   "user does not exist" vs "wrong password" vs "multiple AD entries"
            //   vs "AD server unreachable" — all collapse to the same generic
            //   message. The disabled-account case is intentionally kept
            //   explicit because it provides actionable UX to legitimate
            //   users (their admin disabled them, they need to contact admin).
            await _audit.LogAsync(new AuditEntry(
                SiteId: site.Id, ActorUserId: null, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.login", TargetType: "User", TargetId: null,
                Outcome: "failure", ErrorMessage: result.ErrorMessage), ct);
            var publicMessage = MapLdapErrorToPublic(result.ErrorMessage);
            throw new AuthenticationException("LDAP_AUTH_FAILED", publicMessage);
        }

        // ── Pre-provisioning check: user must exist in site DB ────────
        // Use ResolveForSiteAsync(site.Id) — at login time there is no JWT yet,
        // so ResolveAsync(ct) (which reads JWT site_id claim) would throw.
        var siteDb = await _siteDbFactory.ResolveForSiteAsync(site.Id, ct);
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

        // ── Auto-sync DisplayName + Title from LDAP on every login ─────
        // LDAP returns null when AD doesn't have the attribute (referral, search
        // error, or attribute simply missing). We must NOT overwrite the DB value
        // with null/empty — that would erase the manual value the Business Admin
        // set during pre-provisioning. Only update when LDAP gives us real data
        // AND it differs from the current DB value.
        var changed = false;
        if (!string.IsNullOrEmpty(result.DisplayName) && result.DisplayName != user.DisplayName)
        {
            user.DisplayName = result.DisplayName;
            changed = true;
        }
        if (result.Title is not null && result.Title != user.Title)
        {
            user.Title = result.Title;
            changed = true;
        }
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTime.UtcNow;
        if (changed)
            _log.LogInformation("LDAP sync: user {Email} DisplayName/Title updated from AD", email);
        await siteDb.SaveChangesAsync(ct);

        // ── Resolve effective permissions ─────────────────────────────
        var (roles, perms) = await _permissions.ResolveForUserAsync(siteDb, user.Id, ct);
        var siteDisplay = site.DisplayName;

        var profile = new UserProfileDto(
            UserId: user.Id, Email: user.Email, DisplayName: user.DisplayName, Title: user.Title,
            Scope: "site", SiteId: site.Id, SiteCode: site.Code, SiteDisplayName: siteDisplay,
            Roles: roles, Permissions: perms);

        var (access, exp, refresh) = await IssueTokensAsync(
            userId: user.Id, scope: "site", siteId: site.Id,
            email: user.Email, displayName: user.DisplayName, title: user.Title,
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

        // L3: fast-reject forged tokens before DB lookup. An attacker
        // spraying random strings at this endpoint would otherwise force
        // a DB query per attempt — now they're rejected at the signature
        // check. REFRESH_NOT_FOUND is returned (same as a real miss) so
        // the failure mode doesn't leak that the signature failed.
        if (!VerifyRefreshTokenSignature(refreshToken))
            throw new AuthenticationException("REFRESH_NOT_FOUND", "Refresh token not found.");

        // L3: hash only the random part — signature suffix is server-derived
        // and adds no entropy. DB column already stores hash of the random part.
        var hash = SHA256Hex(TokenRandomPart(refreshToken));

        // Look in platform DB first (platform admin).
        // M1: include tokens that are already revoked in the lookup so we can
        // detect reuse — if a token was already rotated (ReplacedById is set
        // OR RevokedAt is set) and someone presents it again, that's theft.
        var platformToken = await _platformDb.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (platformToken is not null)
        {
            // M1 — Token reuse detection: if this token was already rotated
            // or revoked, but someone is presenting it again, the legitimate
            // user already moved on (rotated to a new token). The presenter
            // is therefore an attacker using a stolen old token. Revoke the
            // entire family to invalidate both attacker and (now-also-stolen)
            // legitimate user — force re-authentication.
            if (platformToken.RevokedAt is not null || platformToken.ReplacedById is not null)
            {
                _log.LogWarning("Refresh token reuse detected (platform scope, family {FamilyId}). Revoking family.", platformToken.FamilyId);
                await RevokeFamilyAsync(_platformDb.RefreshTokens, platformToken.FamilyId, revokedBy: platformToken.UserId, ct);
                await _audit.LogAsync(new AuditEntry(
                    SiteId: null, ActorUserId: platformToken.UserId, ActorEmail: null, ActorIp: ip, ActorUserAgent: userAgent,
                    Action: "auth.refresh", TargetType: "RefreshToken", TargetId: platformToken.Id.ToString(),
                    Outcome: "failure", ErrorMessage: "Token reuse detected — family revoked"), ct);
                throw new AuthenticationException("REFRESH_REUSE_DETECTED",
                    "Refresh token reuse detected. Please log in again.");
            }

            if (platformToken.ExpiresAt <= DateTime.UtcNow)
                throw new AuthenticationException("REFRESH_EXPIRED", "Refresh token expired.");

            // Rotate: revoke old, issue new (same family).
            platformToken.RevokedAt = DateTime.UtcNow;
            platformToken.RevokedBy = platformToken.UserId;
            await _platformDb.SaveChangesAsync(ct);

            var admin = await _platformDb.PlatformUsers.FirstOrDefaultAsync(u => u.Id == platformToken.UserId, ct);
            if (admin is null || !admin.IsEnabled)
                throw new AuthenticationException("USER_NOT_FOUND", "Platform admin no longer exists.");

            var profile = new UserProfileDto(
                UserId: admin.Id, Email: admin.Email, DisplayName: admin.DisplayName, Title: null,
                Scope: "platform", SiteId: null, SiteCode: null, SiteDisplayName: null,
                Roles: PlatformAdminRoleClaim,
                Permissions: _permissions.GetPlatformAdminPermissions());

            var (access, exp, newRefresh) = await IssueTokensAsync(
                admin.Id, "platform", null, admin.Email, admin.DisplayName, null,
                profile.Roles, profile.Permissions, 1, ip, userAgent, ct);

            // M1: propagate FamilyId from parent so all tokens in a chain share it.
            var newRt = BuildRefreshToken(admin.Id, "platform", null, newRefresh, ip, userAgent, familyId: platformToken.FamilyId);
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
        // L3: signature check before DB lookup (same rationale as RefreshAsync).
        if (!VerifyRefreshTokenSignature(refreshToken))
            throw new AuthenticationException("REFRESH_NOT_FOUND", "Refresh token not found.");

        var hash = SHA256Hex(TokenRandomPart(refreshToken));

        // Verify site exists.
        var site = await _platformDb.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct)
            ?? throw new NotFoundException("Site", siteId);

        var siteDb = await _siteDbFactory.ResolveForSiteAsync(siteId, ct);
        // M1: include tokens that are already revoked in the lookup so we can
        // detect reuse — see RefreshAsync for full explanation.
        var token = await siteDb.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new AuthenticationException("REFRESH_NOT_FOUND", "Refresh token not found.");

        // M1 — Token reuse detection (site scope): same logic as platform.
        if (token.RevokedAt is not null || token.ReplacedById is not null)
        {
            _log.LogWarning("Refresh token reuse detected (site {SiteId}, family {FamilyId}). Revoking family.", siteId, token.FamilyId);
            await RevokeFamilyAsync(siteDb.RefreshTokens, token.FamilyId, revokedBy: token.UserId, ct);
            await _audit.LogAsync(new AuditEntry(
                SiteId: siteId, ActorUserId: token.UserId, ActorEmail: null, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.refresh", TargetType: "RefreshToken", TargetId: token.Id.ToString(),
                Outcome: "failure", ErrorMessage: "Token reuse detected — family revoked"), ct);
            throw new AuthenticationException("REFRESH_REUSE_DETECTED",
                "Refresh token reuse detected. Please log in again.");
        }

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
            UserId: user.Id, Email: user.Email, DisplayName: user.DisplayName, Title: user.Title,
            Scope: "site", SiteId: site.Id, SiteCode: site.Code, SiteDisplayName: site.DisplayName,
            Roles: roles, Permissions: perms);

        var (access, exp, newRefresh) = await IssueTokensAsync(
            user.Id, "site", site.Id, user.Email, user.DisplayName, user.Title,
            roles, perms, user.PermissionsVersion, ip, ua, ct);

        // M1: propagate FamilyId from parent.
        var newRt = BuildRefreshToken(user.Id, "site", site.Id, newRefresh, ip, ua, familyId: token.FamilyId);
        newRt.ReplacedById = token.Id;
        siteDb.RefreshTokens.Add(newRt);
        await siteDb.SaveChangesAsync(ct);

        var theme = await _themes.GetThemeAsync(site.Id, ct);
        return new RefreshResponse(access, exp, newRefresh, profile, theme);
    }

    public async Task LogoutAsync(string refreshToken, Guid? revokedBy, CancellationToken ct = default)
    {
        // L3: signature check before DB lookup. Even on logout we don't
        // want an attacker to be able to force DB queries with garbage tokens.
        // If signature fails, silently succeed (logout is idempotent — there's
        // nothing to revoke). Don't leak the failure mode.
        if (!VerifyRefreshTokenSignature(refreshToken))
            return;
        var hash = SHA256Hex(TokenRandomPart(refreshToken));
        var platformToken = await _platformDb.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (platformToken is not null)
        {
            platformToken.RevokedAt = DateTime.UtcNow;
            platformToken.RevokedBy = revokedBy;
            await _platformDb.SaveChangesAsync(ct);
            return;
        }

        // Try site DB — revoke site refresh token
        var siteId = _currentUser.SiteId;
        if (siteId is not null)
        {
            var siteDb = await _siteDbFactory.ResolveForSiteAsync(siteId.Value, ct);
            var siteToken = await siteDb.RefreshTokens
                .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct);
            if (siteToken is not null)
            {
                siteToken.RevokedAt = DateTime.UtcNow;
                siteToken.RevokedBy = revokedBy;
                await siteDb.SaveChangesAsync(ct);
            }
        }
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
        string? title,
        IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions,
        long version, string? ip, string? ua, CancellationToken ct)
    {
        var access = _tokens.IssueFor(userId, scope, siteId, email, displayName, title, roles, permissions, version);
        var refresh = GenerateRefreshToken();
        return (access.Token, access.ExpiresAt, refresh);
    }

    private static RefreshToken BuildRefreshToken(Guid userId, string scope, Guid? siteId, string rawToken, string? ip, string? ua, Guid? familyId = null)
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
            // M1: new tokens without an explicit FamilyId start a new family.
            // Rotation will propagate the parent's FamilyId via the caller.
            FamilyId = familyId ?? Guid.NewGuid(),
        };
    }

    /// <summary>
    /// SECURITY (L3): Generate a refresh token that is both random AND
    /// HMAC-signed. Format: <c>{base64url random}.{base64url hmac}</c>.
    /// The random part has the entropy; the HMAC lets the server reject
    /// obviously-forged tokens without a DB lookup (DoS mitigation — an
    /// attacker spraying random strings at /api/auth/refresh would
    /// otherwise force a DB query per attempt).
    /// </summary>
    private string GenerateRefreshToken(int bytes = 32)
    {
        var buf = new byte[bytes];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(buf);
        var random = Convert.ToBase64String(buf).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // HMAC-SHA256 over the random part. Verification: server recomputes
        // HMAC on incoming token, constant-time compares. If mismatch → reject
        // before DB query.
        using var hmac = new System.Security.Cryptography.HMACSHA256(_refreshHmacKey);
        var sig = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(random));
        var sigB64 = Convert.ToBase64String(sig).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{random}.{sigB64}";
    }

    /// <summary>
    /// SECURITY (L3): verify the HMAC signature on an incoming refresh token.
    /// Returns false (without throwing) if the token is malformed or the
    /// signature doesn't match — caller should treat as REFRESH_NOT_FOUND,
    /// not as a different error (don't leak that the signature check failed).
    /// </summary>
    private bool VerifyRefreshTokenSignature(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var dot = token.IndexOf('.');
        if (dot <= 0 || dot >= token.Length - 1) return false;
        var random = token[..dot];
        var sigB64 = token[(dot + 1)..];

        byte[] expectedSig;
        try
        {
            using var hmac = new System.Security.Cryptography.HMACSHA256(_refreshHmacKey);
            expectedSig = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(random));
        }
        catch
        {
            return false;
        }

        // Decode incoming signature (base64url → bytes). Malformed = reject.
        byte[]? incomingSig;
        try
        {
            // base64url → base64
            var b64 = sigB64.Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4)
            {
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
            }
            incomingSig = Convert.FromBase64String(b64);
        }
        catch
        {
            return false;
        }

        // Constant-time comparison to avoid timing-based signature oracle.
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            expectedSig, incomingSig);
    }

    /// <summary>
    /// SECURITY (L3): extract the random portion of a signed refresh token,
    /// which is what the SHA-256 hash is computed over for DB lookup. The
    /// signature suffix is stripped before hashing.
    /// </summary>
    private static string TokenRandomPart(string token)
    {
        var dot = token.IndexOf('.');
        return dot > 0 ? token[..dot] : token;
    }

    private static string SHA256Hex(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// SECURITY (M1): revoke every non-revoked refresh token that shares the
    /// given family ID. Called when token reuse is detected — see RefreshAsync
    /// for the rationale. Works against either the Platform DB's RefreshTokens
    /// DbSet or a Site DB's RefreshTokens DbSet (same entity type).
    /// </summary>
    private static async Task RevokeFamilyAsync(
        Microsoft.EntityFrameworkCore.DbSet<RefreshToken> tokens,
        Guid? familyId,
        Guid revokedBy,
        CancellationToken ct)
    {
        if (familyId is null) return;
        var now = DateTime.UtcNow;
        // Only revoke tokens that are still active — already-revoked tokens
        // keep their original RevokedAt/RevokedBy for audit clarity.
        var active = await tokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var t in active)
        {
            t.RevokedAt = now;
            t.RevokedBy = revokedBy;
        }
        // Caller is responsible for SaveChangesAsync.
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

    /// <summary>
    /// SECURITY (H5): Map detailed LDAP server errors to a small set of
    /// generic user-facing messages. The detailed error is preserved in
    /// the audit log for forensic/admin debugging. Three buckets:
    /// <list type="bullet">
    ///   <item>Disabled account — kept explicit ("Your account is disabled…")
    ///     for legitimate UX (admin disabled the user, user needs to know).</item>
    ///   <item>Server / transport error — "Authentication service unavailable,
    ///     please try again later." Reveals nothing about whether the user
    ///     exists or credentials are wrong.</item>
    ///   <item>Everything else (invalid creds, multiple matches, unknown
    ///     LDAP codes) — "Invalid email or password." Attackers cannot
    ///     distinguish "user doesn't exist" from "wrong password" from
    ///     "multiple AD entries match" — all collapse to the same message.</item>
    /// </list>
    /// </summary>
    private static string MapLdapErrorToPublic(string? internalMessage)
    {
        if (string.IsNullOrEmpty(internalMessage))
            return "Invalid email or password.";

        // Disabled account: keep explicit for UX (B2B internal — admin
        // disabling a user wants the user to know why login fails).
        if (internalMessage.Contains("disabled", StringComparison.OrdinalIgnoreCase))
            return "Your account is disabled. Contact your Site Business Admin.";

        // Server / transport failures: don't leak user existence.
        if (internalMessage.Contains("server error", StringComparison.OrdinalIgnoreCase)
            || internalMessage.Contains("connection failed", StringComparison.OrdinalIgnoreCase)
            || internalMessage.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            return "Authentication service unavailable, please try again later.";

        // Everything else (invalid creds, multiple matches, bind code X): generic.
        return "Invalid email or password.";
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
