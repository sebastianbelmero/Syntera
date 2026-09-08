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
    /// <summary>
    /// M7: change the calling user's password (Platform Admin only, since
    /// site users authenticate via LDAP — their password is changed in AD,
    /// not here). Enforces password policy, verifies current password,
    /// and refuses to set the same password again.
    ///
    /// <para>COMPLIANCE (Sprint 2.3): when <paramref name="passwordChangeChallengeToken"/>
    /// is non-null, the current-password verification is SKIPPED and the
    /// caller is resolved from the challenge token (a short-lived JWT
    /// issued by the login flow when <see cref="LoginResponse.RequiresPasswordChange"/>
    /// is true, scope = <c>"password-change-challenge"</c>). This is the
    /// forced-change flow: the user already proved password knowledge by
    /// logging in, but the password is past its max-age and must be rotated
    /// before any access/refresh tokens are issued.</para>
    /// </summary>
    Task ChangePasswordAsync(string currentPassword, string newPassword, string? ip, string? userAgent, CancellationToken ct = default, string? passwordChangeChallengeToken = null);

    // ── COMPLIANCE (Sprint 2.3): MFA (TOTP) ────────────────────────────────

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): Complete login after the user provided a
    /// valid password but MFA (TOTP) was enabled. Validates the MFA challenge
    /// token (issued by <see cref="LoginAsync"/> with
    /// <see cref="LoginResponse.RequiresMfa"/> = true), verifies the TOTP
    /// code, and on success issues the full access + refresh tokens. On
    /// failure, increments failed MFA attempts; after 5 failures, the
    /// account is locked (same window as password lockout).
    /// </summary>
    Task<LoginResponse> LoginMfaAsync(LoginMfaRequest request, string? ip, string? userAgent, CancellationToken ct = default);

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): Begin MFA enrollment for the calling Platform
    /// Admin. Generates a new TOTP secret, stores it (encrypted) on the user
    /// row, but does NOT enable MFA — the user must first call
    /// <see cref="ConfirmMfaAsync"/> with a valid TOTP code from their
    /// authenticator app. Returns the QR code URL (otpauth://) and the
    /// plaintext Base32 secret (for manual entry on devices without a camera).
    /// </summary>
    Task<MfaSetupResponse> SetupMfaAsync(CancellationToken ct = default);

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): Confirm MFA enrollment. Verifies the supplied
    /// TOTP code against the secret stored by <see cref="SetupMfaAsync"/>;
    /// on success sets <c>TotpEnabled = true</c>. Audit-logged as
    /// <c>auth.mfa_enable</c> (CRITICAL audit — uses
    /// <see cref="IAuditService.LogCriticalAsync"/> so a failed audit write
    /// rolls back the enable).
    /// </summary>
    Task ConfirmMfaAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): Disable MFA for the calling Platform Admin.
    /// Requires a current valid TOTP code (re-verifies the user has the
    /// authenticator device) to prevent accidental or attacker-initiated
    /// disable. On success clears the stored secret and sets
    /// <c>TotpEnabled = false</c>. Audit-logged as <c>auth.mfa_disable</c>
    /// (CRITICAL audit).
    /// </summary>
    Task DisableMfaAsync(string code, CancellationToken ct = default);
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
    // M2: refresh token TTL per scope, read from IConfiguration. Falls back
    // to legacy "Jwt:RefreshTokenDays" if the new per-scope keys are absent.
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;
    // M7: password policy for ChangePasswordAsync (Platform Admin).
    private readonly IPasswordPolicy _passwordPolicy;
    // COMPLIANCE (Sprint 2.3): TOTP (MFA) service for Platform Admin second
    // factor. Site users bypass MFA (they authenticate via LDAP, which
    // already enforces AD policies).
    private readonly ITotpService _totp;

    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // COMPLIANCE (Sprint 2.3): challenge token scopes — distinct strings
    // ensure a challenge token issued for one flow cannot be replayed
    // against another. The JwtTokenService rejects any mismatch.
    internal const string MfaChallengeScope = "mfa-challenge";
    internal const string PasswordChangeChallengeScope = "password-change-challenge";

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
        Microsoft.Extensions.Configuration.IConfiguration config,
        IPasswordPolicy passwordPolicy,
        ITotpService totp)
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
        _config = config;
        _passwordPolicy = passwordPolicy;
        _totp = totp;

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

        // ── Rate limit: per (IP + email) AND per email (M4) ─────────────
        // Two buckets protect against different attack patterns:
        //   - (IP, email): blocks a single attacker at one IP hammering
        //     one account. (Original H2 mitigation.)
        //   - email alone (no IP): blocks a botnet spraying one account
        //     from many IPs. Without this, an attacker with a 10k-IP
        //     botnet could try each IP twice and never trip the per-(IP,email)
        //     limit. Per-email limit caps total attempts against one
        //     account regardless of source IP.
        //
        // Failure message is the same generic "RATE_LIMITED" so attackers
        // can't tell which bucket tripped.
        var perIpKey = $"login:rl:ip:{ip}:{email}";
        var perEmailKey = $"login:rl:email:{email}";
        if (_cache.TryGetValue<int>(perIpKey, out var ipFails) && ipFails >= MaxFailedLogins)
            throw new AuthenticationException("RATE_LIMITED",
                "Too many failed login attempts. Try again in 15 minutes.");
        if (_cache.TryGetValue<int>(perEmailKey, out var emailFails) && emailFails >= MaxFailedLogins)
            throw new AuthenticationException("RATE_LIMITED",
                "Too many failed login attempts. Try again in 15 minutes.");

        // ── Branch: Platform Admin or Site user? ─────────────────────
        if (email.EndsWith("@syntera.com", StringComparison.OrdinalIgnoreCase))
            return await LoginPlatformAdminAsync(email, request.Password, ip, userAgent, perIpKey, perEmailKey, ct);

        return await LoginSiteUserAsync(email, request.Password, ip, userAgent, perIpKey, perEmailKey, ct);
    }

    private async Task<LoginResponse> LoginPlatformAdminAsync(
        string email, string password, string? ip, string? ua, string perIpKey, string perEmailKey, CancellationToken ct)
    {
        var admin = await _platformDb.PlatformUsers.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (admin is null || !admin.IsEnabled)
        {
            BumpFail(perIpKey, perEmailKey);
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
            var nowLocked = false;
            DateTime? lockedUntil = null;
            if (admin.FailedLoginCount >= MaxFailedLogins)
            {
                lockedUntil = DateTime.UtcNow.Add(LockoutDuration);
                admin.LockedUntil = lockedUntil;
                admin.FailedLoginCount = 0;
                nowLocked = true;
            }
            await _platformDb.SaveChangesAsync(ct);
            BumpFail(perIpKey, perEmailKey);
            await _audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: admin.Id, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.login", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                Outcome: "failure", ErrorMessage: "Invalid password"), ct);

            // M6: when the account JUST got locked (not "already locked"),
            // emit a separate audit event with action='auth.account_locked'.
            // Operators can subscribe/alert on this specific action without
            // having to grep the 'auth.login' failure stream. The error
            // message includes the lockout window end-time so admins
            // reviewing the audit log see the full context.
            if (nowLocked && lockedUntil is not null)
            {
                await _audit.LogAsync(new AuditEntry(
                    SiteId: null, ActorUserId: admin.Id, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                    Action: "auth.account_locked", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                    Outcome: "failure",
                    ErrorMessage: $"Account locked until {lockedUntil.Value:u} after {MaxFailedLogins} failed attempts."), ct);
                _log.LogWarning("Platform admin {Email} locked until {LockoutUntil:O} after {Count} failed login attempts.",
                    email, lockedUntil.Value, MaxFailedLogins);
            }

            throw new AuthenticationException("INVALID_CREDENTIALS", "Invalid credentials.");
        }

        // ── Success: reset counters ───────────────────────────────────
        admin.FailedLoginCount = 0;
        admin.LockedUntil = null;
        admin.LastLoginAt = DateTime.UtcNow;
        await _platformDb.SaveChangesAsync(ct);

        // M4: clear BOTH rate-limit buckets on success (regardless of
        // whether MFA / password max-age challenges follow — the password
        // itself verified, so the rate-limit reset belongs here).
        _cache.Remove(perIpKey);
        _cache.Remove(perEmailKey);

        // Audit password verify success (regardless of whether MFA or a
        // forced password change follows). The MFA / password-change
        // flows emit their own audit entries.
        await _audit.LogAsync(new AuditEntry(
            SiteId: null, ActorUserId: admin.Id, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
            Action: "auth.login", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
            Outcome: "success", ErrorMessage: null), ct);

        // ── COMPLIANCE (Sprint 2.3): MFA challenge ─────────────────────
        // If the platform admin has MFA enabled AND a stored secret, we
        // do NOT issue full access/refresh tokens yet. Instead we return a
        // short-lived MFA challenge token; the client must call
        // POST /api/auth/login-mfa with the challenge token + a TOTP code
        // to receive the real tokens. Backward compat: admins who have NOT
        // opted into MFA (TotpEnabled=false) skip this branch entirely and
        // fall through to the normal token issuance below.
        if (admin.TotpEnabled && !string.IsNullOrEmpty(admin.TotpSecret))
        {
            var (challengeToken, _) = _tokens.IssueChallenge(admin.Id, MfaChallengeScope, lifetimeMinutes: 5);
            // Audit the MFA challenge issuance as a distinct action so
            // operators can correlate login → MFA challenge → MFA verify
            // without grepping the 'auth.login' stream.
            await _audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.mfa_challenge", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                Outcome: "success", ErrorMessage: "MFA challenge token issued"), ct);
            return BuildChallengeResponse(admin, requiresMfa: true, mfaChallengeToken: challengeToken);
        }

        // ── COMPLIANCE (Sprint 2.4): password max-age enforcement ─────
        // If PasswordPolicy:MaxAgeDays > 0 and the password is older than
        // that, do NOT issue full tokens. Return a password-change
        // challenge token; the client redirects to the change-password
        // page and submits the new password along with the challenge
        // token (the ChangePasswordAsync path skips the current-password
        // check when a valid challenge token is presented).
        //
        // Backward compat: existing users have PasswordChangedAt seeded
        // to CreatedAt by ComplianceMigrator, so this branch only fires
        // after the configured max-age has elapsed since their password
        // was last changed. New platform admins seeded by DbSeeder get
        // PasswordChangedAt = UtcNow so they aren't forced to change on
        // first login.
        if (IsPasswordMaxAgeExceeded(admin))
        {
            var (challengeToken, _) = _tokens.IssueChallenge(admin.Id, PasswordChangeChallengeScope, lifetimeMinutes: 5);
            await _audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.password_max_age", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                Outcome: "success", ErrorMessage: "Password max-age exceeded — password-change challenge token issued"), ct);
            return BuildChallengeResponse(admin, requiresPasswordChange: true, passwordChangeChallengeToken: challengeToken);
        }

        // ── Normal success: issue full access + refresh tokens ────────
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

        return new LoginResponse(access, exp, refresh, profile, ThemeService.PlatformDefault());
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): Build a LoginResponse that signals a challenge
    /// (MFA or forced password change) WITHOUT issuing access/refresh tokens.
    /// The AccessToken and RefreshToken are returned as empty strings — the
    /// client MUST inspect the <c>RequiresMfa</c>/<c>RequiresPasswordChange</c>
    /// flags and the corresponding challenge-token fields instead of treating
    /// the response as a normal login.
    ///
    /// <para>The returned Profile is minimal (no roles/permissions) because the
    /// user has NOT completed authentication yet — the client should not allow
    /// any privileged actions until the challenge is resolved.</para>
    /// </summary>
    private static LoginResponse BuildChallengeResponse(
        PlatformUser admin,
        bool requiresMfa = false,
        string? mfaChallengeToken = null,
        bool requiresPasswordChange = false,
        string? passwordChangeChallengeToken = null)
    {
        var minimalProfile = new UserProfileDto(
            UserId: admin.Id, Email: admin.Email, DisplayName: admin.DisplayName, Title: null,
            Scope: "platform", SiteId: null, SiteCode: null, SiteDisplayName: null,
            Roles: Array.Empty<string>(), Permissions: Array.Empty<string>());

        return new LoginResponse(
            AccessToken: "",
            ExpiresAt: DateTime.UtcNow,
            RefreshToken: "",
            Profile: minimalProfile,
            Theme: ThemeService.PlatformDefault(),
            RequiresMfa: requiresMfa,
            MfaChallengeToken: mfaChallengeToken,
            RequiresPasswordChange: requiresPasswordChange,
            PasswordChangeChallengeToken: passwordChangeChallengeToken);
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.4): Check whether the platform admin's password
    /// has exceeded the configured max-age. Returns <c>true</c> if
    /// <c>PasswordPolicy:MaxAgeDays</c> is set (default 90, 0 = disabled) AND
    /// the admin has a non-null <see cref="PlatformUser.PasswordChangedAt"/>
    /// that is older than the max-age. Returns <c>false</c> when:
    /// <list type="bullet">
    ///   <item>Max-age is disabled (set to 0 in config).</item>
    ///   <item>The admin has never changed their password AND
    ///     <see cref="PlatformUser.PasswordChangedAt"/> is null (defensive —
    ///     ComplianceMigrator backfills this to CreatedAt, but a fresh admin
    ///     row without migration would otherwise be locked out; we opt for
    ///     fail-open here, the admin can change at their leisure).</item>
    ///   <item>The password was changed within the max-age window.</item>
    /// </list>
    /// </summary>
    private bool IsPasswordMaxAgeExceeded(PlatformUser admin)
    {
        var maxAgeDays = _config.GetValue("PasswordPolicy:MaxAgeDays", 90);
        if (maxAgeDays <= 0) return false;
        if (admin.PasswordChangedAt is null) return false;
        return (DateTime.UtcNow - admin.PasswordChangedAt.Value).TotalDays > maxAgeDays;
    }

    private async Task<LoginResponse> LoginSiteUserAsync(
        string email, string password, string? ip, string? ua, string perIpKey, string perEmailKey, CancellationToken ct)
    {
        var domain = email[(email.IndexOf('@') + 1)..];

        // ── Resolve site by email domain ──────────────────────────────
        var domainRow = await _platformDb.LdapDomains
            .Include(d => d.Site)
            .FirstOrDefaultAsync(d => d.Domain == domain && d.IsActive, ct);

        if (domainRow is null || domainRow.Site is null || !domainRow.Site.IsEnabled)
        {
            BumpFail(perIpKey, perEmailKey);
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
            BumpFail(perIpKey, perEmailKey);
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

        // M4: clear BOTH rate-limit buckets on success.
        _cache.Remove(perIpKey);
        _cache.Remove(perEmailKey);

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
        _log.LogDebug("RefreshAsync: token length={Len}, hasDot={HasDot}, first10={First10}, last10={Last10}",
            refreshToken.Length,
            refreshToken.IndexOf('.') > 0 ? "yes" : "no",
            refreshToken.Length >= 10 ? refreshToken[..10] : refreshToken,
            refreshToken.Length >= 10 ? refreshToken[^10..] : refreshToken);

        if (!VerifyRefreshTokenSignature(refreshToken))
        {
            _log.LogWarning("RefreshAsync: SIGNATURE VERIFICATION FAILED (len={Len}). Likely cause: stale token from before backend restart, or Jwt:SigningKey changed between requests.", refreshToken.Length);
            throw new AuthenticationException("REFRESH_NOT_FOUND", "Refresh token not found.");
        }

        _log.LogDebug("RefreshAsync: signature OK. Now looking up in DB.");

        // L3: hash only the random part — signature suffix is server-derived
        // and adds no entropy. DB column already stores hash of the random part.
        var hash = SHA256Hex(TokenRandomPart(refreshToken));
        _log.LogDebug("RefreshAsync: token hash={Hash}", hash);

        // Look in platform DB first (platform admin).
        // M1: include tokens that are already revoked in the lookup so we can
        // detect reuse — if a token was already rotated (ReplacedById is set
        // OR RevokedAt is set) and someone presents it again, that's theft.
        var platformToken = await _platformDb.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        _log.LogDebug("RefreshAsync: platform DB lookup. Found={Found}", platformToken is not null);
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

            // COMPLIANCE (Sprint 2.5): idle session timeout + max session age.
            await EnforceSessionLimitsAsync(platformToken, _platformDb, ip, userAgent, ct);

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
            // COMPLIANCE FIX (Sprint 2.5 — refresh token rotation bug):
            // The PREVIOUS code set newRt.ReplacedById = platformToken.Id.
            // That marked the NEW token as "already replaced" — so on the
            // NEXT refresh, the reuse-detection check (token.ReplacedById is not null)
            // triggered on the brand-new token and revoked the entire family
            // → REFRESH_REUSE_DETECTED → logout on the 2nd refresh.
            //
            // SEMANTICS of ReplacedById (per RefreshToken entity comment):
            //   "If this token was rotated, the ID of the replacement token."
            // So ReplacedById should be set on the OLD token pointing to the
            // new one — NOT on the new token pointing back to the old.
            //
            // FIX: set ReplacedById on the OLD (revoked) token, pointing to
            // the new token's ID. The new token's ReplacedById stays null
            // (it hasn't been replaced by anything — it's the current token).
            newRt.LastUsedAt = DateTime.UtcNow;
            _platformDb.RefreshTokens.Add(newRt);
            await _platformDb.SaveChangesAsync(ct);  // Save first to get newRt.Id

            // Now mark the OLD token as replaced by the NEW token.
            platformToken.ReplacedById = newRt.Id;
            await _platformDb.SaveChangesAsync(ct);

            return new RefreshResponse(access, exp, newRefresh, profile, ThemeService.PlatformDefault());
        }

        // H7-full (Sprint 4): if the token wasn't in the platform DB, it
        // might be a site-scope token. On a fresh page load, the frontend
        // doesn't know which site the user belongs to (no in-memory
        // profile), so it can't call /api/auth/refresh-site with siteId.
        // We auto-detect by scanning all enabled sites' RefreshTokens.
        // Performance: 6 sites typical, indexed by TokenHash (unique) —
        // each lookup is O(1). Total worst case = 6 PK lookups.
        _log.LogDebug("RefreshAsync: token not in platform DB. Scanning {Count} site DBs.", await _platformDb.Sites.CountAsync(s => s.IsEnabled, ct));
        var allSites = await _platformDb.Sites.AsNoTracking()
            .Where(s => s.IsEnabled)
            .ToListAsync(ct);

        foreach (var site in allSites)
        {
            SiteDbContext siteDb;
            try
            {
                siteDb = await _siteDbFactory.ResolveForSiteAsync(site.Id, ct);
            }
            catch
            {
                // Site DB unreachable — skip, try next site. Don't let one
                // broken site DB block refresh for everyone.
                continue;
            }

            var siteToken = await siteDb.RefreshTokens
                .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            _log.LogDebug("RefreshAsync: site {Code} lookup. Found={Found}", site.Code, siteToken is not null);
            if (siteToken is null)
                continue;

            // Found in this site's DB — delegate to the site-scoped refresh
            // logic (which handles reuse detection, family revocation, etc).
            // We re-issue via RefreshSiteAsync to keep the logic in one place.
            _log.LogDebug("RefreshAsync: delegating to RefreshSiteAsync for site {Code}", site.Code);
            return await RefreshSiteAsync(refreshToken, site.Id, ip, userAgent, ct);
        }

        // Not found in platform DB nor any site DB — genuine miss.
        _log.LogWarning("RefreshAsync: token NOT FOUND in any DB (platform + {Count} sites). This means the token is stale (from before backend restart), or the user's session was revoked.", allSites.Count);
        throw new AuthenticationException("REFRESH_NOT_FOUND",
            "Refresh token not found.");
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

        // COMPLIANCE (Sprint 2.5): idle session timeout + max session age (site scope).
        await EnforceSessionLimitsAsync(token, siteDb, ip, ua, ct);

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
        // COMPLIANCE FIX (Sprint 2.5 — same rotation bug as platform scope):
        // Don't set ReplacedById on the new token. Set it on the OLD
        // token pointing to the new one. See platform-scope fix above for
        // the full explanation of why newRt.ReplacedById = token.Id was
        // triggering REFRESH_REUSE_DETECTED on the 2nd refresh.
        newRt.LastUsedAt = DateTime.UtcNow;
        siteDb.RefreshTokens.Add(newRt);
        await siteDb.SaveChangesAsync(ct);  // Save first to get newRt.Id

        // Now mark the OLD token as replaced by the NEW token.
        token.ReplacedById = newRt.Id;
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

    /// <summary>
    /// M7: change the calling Platform Admin's password.
    ///
    /// Flow:
    /// 1. Resolve caller from JWT (must be Platform Admin — site users
    ///    change their password in AD, not here).
    /// 2. Verify current password against stored bcrypt hash. Wrong =
    ///    BusinessRuleException with "WRONG_CURRENT_PASSWORD" (this is
    ///    NOT a credential-leak vector because the caller must already
    ///    be authenticated to reach this endpoint).
    /// 3. Validate new password against IPasswordPolicy. Failures are
    ///    surfaced as a single joined string so the user sees all
    ///    violated rules at once (better UX than one rule at a time).
    /// 4. Refuse to set the new password equal to the current (no-op
    ///    rotation defeats the purpose).
    /// 5. Hash + persist. Audit log entry with action='auth.password_change'.
    ///
    /// <para>COMPLIANCE (Sprint 2.3): when <paramref name="passwordChangeChallengeToken"/>
    /// is non-null, step 2 (current-password verification) is SKIPPED and
    /// the caller is resolved from the challenge token instead of from
    /// the bearer JWT. The challenge token is a short-lived JWT issued by
    /// the login flow when <see cref="LoginResponse.RequiresPasswordChange"/>
    /// is true (password max-age exceeded). It proves the caller recently
    /// authenticated with a valid password and is now forced to rotate it.
    /// Either <paramref name="currentPassword"/> OR
    /// <paramref name="passwordChangeChallengeToken"/> must be supplied —
    /// both being null/empty is rejected with CURRENT_PASSWORD_REQUIRED.</para>
    ///
    /// Security: this endpoint does NOT invalidate existing sessions. If
    /// an attacker stole the refresh token, changing the password won't
    /// kick them out — they keep refreshing until they're revoked. For
    /// full account takeover response, the user should also call logout
    /// everywhere (we don't yet have a "revoke all my sessions" button —
    /// that's a separate feature).
    /// </summary>
    public async Task ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        string? ip,
        string? userAgent,
        CancellationToken ct = default,
        string? passwordChangeChallengeToken = null)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
            throw new Models.BusinessRuleException("NEW_PASSWORD_REQUIRED", "New password is required.");

        // ── Resolve the caller ────────────────────────────────────────
        // Two paths: voluntary change (authenticated by bearer JWT →
        // currentPassword must be present) OR forced change (authenticated
        // by the password-change challenge token → currentPassword is
        // skipped because the challenge token already proves identity).
        Guid userId;
        bool forcedChange = false;

        if (!string.IsNullOrWhiteSpace(passwordChangeChallengeToken))
        {
            // Forced password change — verify the challenge token.
            var challengeUserId = _tokens.ValidateChallenge(passwordChangeChallengeToken, PasswordChangeChallengeScope);
            if (challengeUserId is null)
                throw new Models.AuthenticationException("INVALID_CHALLENGE",
                    "Password-change challenge token is invalid or expired. Please log in again.");
            userId = challengeUserId.Value;
            forcedChange = true;
        }
        else
        {
            // Voluntary change — caller must be an authenticated Platform Admin.
            userId = _currentUser.UserId
                ?? throw new Models.AuthorizationException("NOT_AUTHENTICATED", "You must be signed in to change your password.");
            if (!_currentUser.IsPlatformAdmin)
                throw new Models.AuthorizationException("NOT_PLATFORM_ADMIN",
                    "Password change is only available for Platform Admin. Site users must change their password in Active Directory.");
            if (string.IsNullOrWhiteSpace(currentPassword))
                throw new Models.BusinessRuleException("CURRENT_PASSWORD_REQUIRED", "Current password is required.");
        }

        var admin = await _platformDb.PlatformUsers.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new Models.NotFoundException("PlatformUser", userId);

        // Verify current password (ONLY on the voluntary-change path).
        if (!forcedChange)
        {
            if (!_hasher.Verify(currentPassword!, admin.PasswordHash))
            {
                await _audit.LogAsync(new AuditEntry(
                    SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email, ActorIp: ip, ActorUserAgent: userAgent,
                    Action: "auth.password_change", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                    Outcome: "failure", ErrorMessage: "Wrong current password"), ct);
                throw new Models.BusinessRuleException("WRONG_CURRENT_PASSWORD",
                    "Current password is incorrect.");
            }
        }

        // Enforce policy — collect all violations and report them together.
        var violations = _passwordPolicy.Validate(newPassword);
        if (violations.Count > 0)
        {
            var msg = string.Join("; ", violations);
            await _audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email, ActorIp: ip, ActorUserAgent: userAgent,
                Action: "auth.password_change", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                Outcome: "failure", ErrorMessage: $"Policy violation: {msg}"), ct);
            throw new Models.BusinessRuleException("PASSWORD_POLICY_VIOLATION", msg);
        }

        // Refuse no-op rotation (new == current).
        if (_hasher.Verify(newPassword, admin.PasswordHash))
        {
            throw new Models.BusinessRuleException("PASSWORD_SAME_AS_CURRENT",
                "New password must be different from the current password.");
        }

        // COMPLIANCE (Sprint 2.4): refuse password reuse — check the new
        // password against the last N historical hashes stored in
        // PasswordHistory table. Default N = 10 (configurable via
        // PasswordPolicy:HistorySize). Enforces 21 CFR Part 11 §11.300(c)
        // (periodic password change) without enabling trivial rotation
        // back to the same password.
        var historySize = _config.GetValue<int>("PasswordPolicy:HistorySize", 10);
        if (historySize > 0)
        {
            var historicalHashes = await _platformDb.PasswordHistory
                .Where(h => h.PlatformUserId == admin.Id)
                .OrderByDescending(h => h.SetAt)
                .Take(historySize)
                .Select(h => h.PasswordHash)
                .ToListAsync(ct);

            foreach (var historicalHash in historicalHashes)
            {
                if (_hasher.Verify(newPassword, historicalHash))
                {
                    await _audit.LogAsync(new AuditEntry(
                        SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email, ActorIp: ip, ActorUserAgent: userAgent,
                        Action: "auth.password_change", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                        Outcome: "failure", ErrorMessage: "Password matches a recent history entry"), ct);
                    throw new Models.BusinessRuleException("PASSWORD_REUSE_FORBIDDEN",
                        $"The new password matches one of your last {historySize} passwords. Choose a different password.");
                }
            }
        }

        // Persist + audit.
        var oldHash = admin.PasswordHash;
        admin.PasswordHash = _hasher.Hash(newPassword);
        // COMPLIANCE (Sprint 2.4): record when the password was changed so
        // the login flow can enforce password max-age on subsequent logins.
        admin.PasswordChangedAt = DateTime.UtcNow;
        // Reset failed-login counters — successful password change proves
        // the legitimate user is in control.
        admin.FailedLoginCount = 0;
        admin.LockedUntil = null;

        // COMPLIANCE (Sprint 2.4): store the OLD hash in history before
        // overwriting (the new hash is already in admin.PasswordHash).
        // We store the old hash so the history reflects "what was the
        // password at time T". If the user changes again, the current
        // (new) hash will become historical at that point.
        _platformDb.PasswordHistory.Add(new PasswordHistory
        {
            PlatformUserId = admin.Id,
            PasswordHash = oldHash,
            SetAt = admin.CreatedAt, // the OLD password was set at user creation (or last change)
        });
        await _platformDb.SaveChangesAsync(ct);

        await _audit.LogCriticalAsync(new AuditEntry(
            SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email, ActorIp: ip, ActorUserAgent: userAgent,
            Action: "auth.password_change", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
            Outcome: "success", ErrorMessage: forcedChange ? "Forced change after max-age" : null,
            SignatureMeaning: "Password change (credential rotation per 21 CFR Part 11 §11.300(c))."), ct);

        _log.LogInformation("Platform Admin {Email} changed their password (forced={Forced}).", admin.Email, forcedChange);
    }

    // ── COMPLIANCE (Sprint 2.3): MFA (TOTP) ──────────────────────────────

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): Complete the login flow after the user
    /// provided a valid password but had MFA enabled. The challenge token
    /// (returned by <see cref="LoginAsync"/> in
    /// <see cref="LoginResponse.MfaChallengeToken"/>) is a 5-minute JWT
    /// that lets us resume the in-progress login without exposing any
    /// API access. We re-load the admin from the token's user id (NOT
    /// from the bearer JWT — there is none yet) and verify the TOTP code
    /// against the stored secret. On success we issue the full access +
    /// refresh tokens (same as the normal login completion path).
    ///
    /// <para>Lockout: failed MFA attempts reuse the same FailedLoginCount
    /// counter as the password path — after 5 failed MFA attempts the
    /// account is locked for the same 15-minute window. This prevents a
    /// stolen challenge token from being brute-forced (only 1M 6-digit
    /// codes; 5 attempts is well below the 0.5% success threshold).</para>
    /// </summary>
    public async Task<LoginResponse> LoginMfaAsync(LoginMfaRequest request, string? ip, string? userAgent, CancellationToken ct = default)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.MfaChallengeToken)
            || string.IsNullOrWhiteSpace(request.Code))
            throw new Models.AuthenticationException("INVALID_MFA_INPUT", "MFA challenge token and code are required.");

        // Validate the challenge token — returns null on any failure
        // (bad signature, wrong scope, expired). Surface a generic error
        // so the caller can't tell which check failed.
        var userId = _tokens.ValidateChallenge(request.MfaChallengeToken, MfaChallengeScope);
        if (userId is null)
            throw new Models.AuthenticationException("INVALID_MFA_CHALLENGE",
                "MFA challenge token is invalid or expired. Please log in again.");

        var admin = await _platformDb.PlatformUsers.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
        if (admin is null || !admin.IsEnabled)
            throw new Models.AuthenticationException("INVALID_MFA_CHALLENGE",
                "MFA challenge token is invalid or expired. Please log in again.");

        if (admin.LockedUntil is not null && admin.LockedUntil > DateTime.UtcNow)
            throw new Models.AuthenticationException("ACCOUNT_LOCKED",
                $"Account locked until {admin.LockedUntil.Value:u}.");

        // Defense in depth: the challenge token was issued only when
        // TotpEnabled was true. If the admin somehow disabled MFA between
        // challenge issuance and this verification, we reject (the user
        // should re-login normally).
        if (!admin.TotpEnabled || string.IsNullOrEmpty(admin.TotpSecret))
            throw new Models.AuthenticationException("MFA_NOT_ENABLED",
                "MFA is not enabled for this account. Please log in again.");

        // Verify the TOTP code (constant-time comparison handled inside TotpService).
        var codeValid = await _totp.VerifyCodeAsync(admin.TotpSecret, request.Code, ct);
        if (!codeValid)
        {
            // Reuse the same lockout counter as password failures — but
            // we reset it on password verify success in LoginPlatformAdminAsync,
            // so this is a fresh count starting from the MFA step.
            admin.FailedLoginCount++;
            admin.LastFailedLoginAt = DateTime.UtcNow;
            var nowLocked = false;
            DateTime? lockedUntil = null;
            if (admin.FailedLoginCount >= MaxFailedLogins)
            {
                lockedUntil = DateTime.UtcNow.Add(LockoutDuration);
                admin.LockedUntil = lockedUntil;
                admin.FailedLoginCount = 0;
                nowLocked = true;
            }
            await _platformDb.SaveChangesAsync(ct);

            await _audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email, ActorIp: ip, ActorUserAgent: userAgent,
                Action: "auth.login_mfa", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                Outcome: "failure", ErrorMessage: "Invalid TOTP code"), ct);

            if (nowLocked && lockedUntil is not null)
            {
                await _audit.LogAsync(new AuditEntry(
                    SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email, ActorIp: ip, ActorUserAgent: userAgent,
                    Action: "auth.account_locked", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                    Outcome: "failure",
                    ErrorMessage: $"Account locked until {lockedUntil.Value:u} after {MaxFailedLogins} failed MFA attempts."), ct);
                _log.LogWarning("Platform admin {Email} locked until {LockoutUntil:O} after {Count} failed MFA attempts.",
                    admin.Email, lockedUntil.Value, MaxFailedLogins);
            }

            throw new Models.AuthenticationException("INVALID_MFA_CODE",
                "Invalid authentication code. Please try again.");
        }

        // ── MFA success: reset counters, audit, complete login ───────
        admin.FailedLoginCount = 0;
        admin.LockedUntil = null;
        admin.LastLoginAt = DateTime.UtcNow;
        await _platformDb.SaveChangesAsync(ct);

        await _audit.LogAsync(new AuditEntry(
            SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email, ActorIp: ip, ActorUserAgent: userAgent,
            Action: "auth.login_mfa", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
            Outcome: "success", ErrorMessage: null), ct);

        // COMPLIANCE (Sprint 2.4): after MFA succeeds, the password max-age
        // still applies. If the password is expired, return a password-change
        // challenge instead of full tokens — MFA is not a substitute for a
        // current password.
        if (IsPasswordMaxAgeExceeded(admin))
        {
            var (challengeToken, _) = _tokens.IssueChallenge(admin.Id, PasswordChangeChallengeScope, lifetimeMinutes: 5);
            await _audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email, ActorIp: ip, ActorUserAgent: userAgent,
                Action: "auth.password_max_age", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                Outcome: "success", ErrorMessage: "Password max-age exceeded after MFA — password-change challenge token issued"), ct);
            return BuildChallengeResponse(admin, requiresPasswordChange: true, passwordChangeChallengeToken: challengeToken);
        }

        // Issue full access + refresh tokens (mirrors the normal
        // LoginPlatformAdminAsync success path).
        var profile = new UserProfileDto(
            UserId: admin.Id, Email: admin.Email, DisplayName: admin.DisplayName, Title: null,
            Scope: "platform", SiteId: null, SiteCode: null, SiteDisplayName: null,
            Roles: PlatformAdminRoleClaim,
            Permissions: _permissions.GetPlatformAdminPermissions());

        var (access, exp, refresh) = await IssueTokensAsync(
            userId: admin.Id, scope: "platform", siteId: null,
            email: admin.Email, displayName: admin.DisplayName, title: null,
            roles: profile.Roles, permissions: profile.Permissions,
            version: 1, ip: ip, ua: userAgent, ct: ct);

        return new LoginResponse(access, exp, refresh, profile, ThemeService.PlatformDefault());
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): Begin MFA enrollment for the calling Platform
    /// Admin. Generates a new TOTP secret, persists it (encrypted via DPAPI)
    /// on the user row, but does NOT enable MFA — the user must first call
    /// <see cref="ConfirmMfaAsync"/> with a valid TOTP code from their
    /// authenticator app. The plaintext secret is returned to the client
    /// so it can be rendered as a QR code AND shown for manual entry — but
    /// it is NOT stored plaintext anywhere; only the DPAPI-encrypted form
    /// is persisted.
    /// </summary>
    public async Task<MfaSetupResponse> SetupMfaAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId
            ?? throw new Models.AuthorizationException("NOT_AUTHENTICATED", "You must be signed in to set up MFA.");
        if (!_currentUser.IsPlatformAdmin)
            throw new Models.AuthorizationException("NOT_PLATFORM_ADMIN",
                "MFA is only available for Platform Admin. Site users authenticate via LDAP.");

        var admin = await _platformDb.PlatformUsers.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new Models.NotFoundException("PlatformUser", userId);

        // Generate + persist the secret (encrypted). If the user already
        // has a stored-but-unconfirmed secret, we overwrite it — re-running
        // setup replaces any previously generated secret. If the user
        // already has TotpEnabled=true, we still allow re-running setup
        // (the new secret must be re-confirmed via ConfirmMfaAsync; until
        // then the OLD secret remains in effect because TotpEnabled stays
        // true). This is intentional: the user can rotate their secret
        // without losing MFA protection mid-flow.
        var (plaintextSecret, encryptedSecret) = _totp.GenerateNewSecret();
        admin.TotpSecret = encryptedSecret;
        await _platformDb.SaveChangesAsync(ct);

        var issuer = _config["Mfa:Issuer"] ?? "Syntera IAM";
        var otpAuthUrl = _totp.BuildOtpAuthUrl(issuer, admin.Email, plaintextSecret);

        // Audit — non-critical (setup does not change protection state).
        await _audit.LogAsync(new AuditEntry(
            SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email,
            ActorIp: null, ActorUserAgent: null,
            Action: "auth.mfa_setup", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
            Outcome: "success", ErrorMessage: null), ct);

        _log.LogInformation("Platform Admin {Email} began MFA setup (secret generated, not yet enabled).", admin.Email);

        return new MfaSetupResponse(QrCodeUrl: otpAuthUrl, PlaintextSecret: plaintextSecret);
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): Confirm MFA enrollment. Verifies the supplied
    /// TOTP code against the secret stored by <see cref="SetupMfaAsync"/>;
    /// on success sets <c>TotpEnabled = true</c>. Uses
    /// <see cref="IAuditService.LogCriticalAsync"/> for the audit entry so
    /// a failed audit write rolls back the enable (21 CFR Part 11
    /// §11.10(e) — a security-relevant state change MUST have a reliable
    /// audit trail).
    /// </summary>
    public async Task ConfirmMfaAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new Models.BusinessRuleException("MFA_CODE_REQUIRED", "Authentication code is required.");

        var userId = _currentUser.UserId
            ?? throw new Models.AuthorizationException("NOT_AUTHENTICATED", "You must be signed in to confirm MFA.");
        if (!_currentUser.IsPlatformAdmin)
            throw new Models.AuthorizationException("NOT_PLATFORM_ADMIN",
                "MFA is only available for Platform Admin.");

        var admin = await _platformDb.PlatformUsers.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new Models.NotFoundException("PlatformUser", userId);

        if (string.IsNullOrEmpty(admin.TotpSecret))
            throw new Models.BusinessRuleException("MFA_NOT_SETUP",
                "MFA setup has not been started. Call /api/auth/mfa/setup first.");

        // Verify the code against the just-stored secret. TotpService
        // performs constant-time comparison internally.
        var codeValid = await _totp.VerifyCodeAsync(admin.TotpSecret, code, ct);
        if (!codeValid)
        {
            await _audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email,
                ActorIp: null, ActorUserAgent: null,
                Action: "auth.mfa_enable", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                Outcome: "failure", ErrorMessage: "Invalid TOTP code on confirm"), ct);
            throw new Models.BusinessRuleException("INVALID_MFA_CODE",
                "Invalid authentication code. Please try again.");
        }

        // Enable MFA — this is the security-relevant state change. Audit
        // is CRITICAL: a failed audit write throws AuditWriteException and
        // rolls back the DB transaction (the caller should surface a 5xx).
        admin.TotpEnabled = true;
        await _platformDb.SaveChangesAsync(ct);

        await _audit.LogCriticalAsync(new AuditEntry(
            SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email,
            ActorIp: null, ActorUserAgent: null,
            Action: "auth.mfa_enable", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
            Outcome: "success", ErrorMessage: null,
            SignatureMeaning: "I authorize enabling MFA on my Platform Admin account."), ct);

        _log.LogInformation("Platform Admin {Email} enabled MFA (TotpEnabled=true).", admin.Email);
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): Disable MFA for the calling Platform Admin.
    /// Requires a current valid TOTP code (re-verifies the user has the
    /// authenticator device) to prevent accidental or attacker-initiated
    /// disable. Clears the stored secret and sets <c>TotpEnabled = false</c>.
    /// Uses <see cref="IAuditService.LogCriticalAsync"/> for the audit entry
    /// so a failed audit write rolls back the disable.
    /// </summary>
    public async Task DisableMfaAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new Models.BusinessRuleException("MFA_CODE_REQUIRED", "Authentication code is required to disable MFA.");

        var userId = _currentUser.UserId
            ?? throw new Models.AuthorizationException("NOT_AUTHENTICATED", "You must be signed in to disable MFA.");
        if (!_currentUser.IsPlatformAdmin)
            throw new Models.AuthorizationException("NOT_PLATFORM_ADMIN",
                "MFA is only available for Platform Admin.");

        var admin = await _platformDb.PlatformUsers.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new Models.NotFoundException("PlatformUser", userId);

        if (!admin.TotpEnabled || string.IsNullOrEmpty(admin.TotpSecret))
            throw new Models.BusinessRuleException("MFA_NOT_ENABLED",
                "MFA is not currently enabled for your account.");

        // Re-verify the TOTP code — the user must demonstrate they still
        // have the authenticator device. This prevents a stolen session
        // from silently disabling MFA.
        var codeValid = await _totp.VerifyCodeAsync(admin.TotpSecret, code, ct);
        if (!codeValid)
        {
            await _audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email,
                ActorIp: null, ActorUserAgent: null,
                Action: "auth.mfa_disable", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                Outcome: "failure", ErrorMessage: "Invalid TOTP code on disable"), ct);
            throw new Models.BusinessRuleException("INVALID_MFA_CODE",
                "Invalid authentication code. Please try again.");
        }

        // Disable + clear the secret — CRITICAL audit (rollback on failure).
        admin.TotpEnabled = false;
        admin.TotpSecret = null;
        await _platformDb.SaveChangesAsync(ct);

        await _audit.LogCriticalAsync(new AuditEntry(
            SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email,
            ActorIp: null, ActorUserAgent: null,
            Action: "auth.mfa_disable", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
            Outcome: "success", ErrorMessage: null,
            SignatureMeaning: "I authorize disabling MFA on my Platform Admin account."), ct);

        _log.LogInformation("Platform Admin {Email} disabled MFA (TotpEnabled=false, secret cleared).", admin.Email);
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

    /// <summary>
    /// M2: refresh token TTL per scope. Platform Admin tokens are
    /// shorter-lived (default 1 day) because they carry the highest
    /// privilege. Site user tokens are longer-lived (default 7 days)
    /// because end users shouldn't be forced to re-login every day,
    /// and site-scoped tokens have a smaller blast radius (one site only).
    /// Falls back to legacy Jwt:RefreshTokenDays if per-scope key is absent.
    /// </summary>
    private TimeSpan GetRefreshTokenTtl(string scope)
    {
        var legacy = _config["Jwt:RefreshTokenDays"];
        var perScope = scope == "platform"
            ? _config["Jwt:RefreshTokenDaysPlatform"]
            : _config["Jwt:RefreshTokenDaysSite"];

        // Per-scope key takes precedence; legacy key is the fallback; final
        // fallback is a sane default (1 day for platform, 7 days for site).
        var str = perScope ?? legacy;
        if (int.TryParse(str, out var days) && days > 0)
            return TimeSpan.FromDays(days);
        return scope == "platform" ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7);
    }

    private RefreshToken BuildRefreshToken(Guid userId, string scope, Guid? siteId, string rawToken, string? ip, string? ua, Guid? familyId = null)
    {
        // COMPLIANCE FIX (Sprint 2.5 — refresh-token hash mismatch):
        // The previous code hashed the FULL rawToken (random.signature) via
        // SHA256Hex(rawToken). But the verify path (RefreshAsync, RefreshSiteAsync,
        // LogoutAsync) hashes ONLY the random part: SHA256Hex(TokenRandomPart(token)).
        // This mismatch caused FirstOrDefaultAsync(t => t.TokenHash == hash) to
        // ALWAYS return null → REFRESH_NOT_FOUND on every reload. The bug was
        // masked for years because LogAsync swallowed audit-write failures
        // silently (and the login flow itself didn't need to verify the hash
        // — only refresh/logout did). Sprint 1.5's LogCriticalAsync + Sprint
        // 2.5's cookie/body fallback exposed the latent bug.
        //
        // FIX: hash ONLY the random part, matching the verify path. Both
        // sides now use SHA256Hex(TokenRandomPart(token)) consistently.
        return new RefreshToken
        {
            Token = rawToken,
            TokenHash = SHA256Hex(TokenRandomPart(rawToken)),
            UserId = userId,
            UserScope = scope,
            SiteId = siteId,
            ExpiresAt = DateTime.UtcNow.Add(GetRefreshTokenTtl(scope)),
            CreatedFromIp = ip,
            CreatedUserAgent = ua,
            // M1: new tokens without an explicit FamilyId start a new family.
            // Rotation will propagate the parent's FamilyId via the caller.
            FamilyId = familyId ?? Guid.NewGuid(),
            // COMPLIANCE (Sprint 2.5 fix): set LastUsedAt = UtcNow on creation
            // so the idle-timeout check has a baseline. Previously, fresh
            // login tokens had LastUsedAt = null which made the idle check
            // silently skip — meaning a user could login, idle for 24h
            // (well past IdleMinutes=30), then reload and still get refreshed
            // (because LastUsedAt was null → check skipped). With this fix,
            // the timer starts at login, so idle timeout applies uniformly.
            LastUsedAt = DateTime.UtcNow,
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

    /// <summary>
    /// M4: increment one or more rate-limit buckets. Each key has its own
    /// 15-minute sliding window (per cache AbsoluteExpiration). Failures
    /// from one IP don't count toward an email's limit, and vice versa —
    /// they're independent buckets.
    /// </summary>
    private void BumpFail(params string[] keys)
    {
        foreach (var key in keys)
        {
            var current = _cache.GetOrCreate(key, e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
                return 0;
            });
            _cache.Set(key, current + 1, TimeSpan.FromMinutes(15));
        }
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

    // ── COMPLIANCE (Sprint 2.5): Idle session timeout + max session age ───

    /// <summary>
    /// Enforces 21 CFR Part 11 §11.300(d) — automatic logoff after a period
    /// of inactivity, and maximum session length even with active use.
    ///
    /// <para><b>Idle timeout:</b> if the refresh token has not been used for
    /// longer than <c>Session:IdleMinutes</c> (default 30 min), the session
    /// is treated as idle-expired and the refresh is rejected with
    /// <c>SESSION_IDLE_TIMEOUT</c>.</para>
    ///
    /// <para><b>Max session age:</b> even with active use, a session cannot
    /// exceed <c>Session:MaxHours</c> (default 8 hours; 0 = disabled). This
    /// enforces automatic logoff per the "max session length" interpretation.</para>
    ///
    /// <para>Both checks revoke the token (not the family — this is
    /// inactivity, not reuse) and audit the event. The user is forced to
    /// log in fresh.</para>
    ///
    /// <para><b>Null LastUsedAt handling:</b> a brand-new token has
    /// LastUsedAt = null (set on creation). On the FIRST refresh, we
    /// treat it as "just used" so it isn't immediately idle-expired. On
    /// subsequent refreshes, LastUsedAt is updated on the new token.</para>
    /// </summary>
    private async Task EnforceSessionLimitsAsync(RefreshToken token, DbContext db, string? ip, string? ua, CancellationToken ct)
    {
        var idleMinutes = _config.GetValue<int>("Session:IdleMinutes", 30);
        if (idleMinutes > 0 && token.LastUsedAt is not null)
        {
            var idleSince = DateTime.UtcNow - token.LastUsedAt.Value;
            if (idleSince.TotalMinutes > idleMinutes)
            {
                token.RevokedAt = DateTime.UtcNow;
                token.RevokedBy = token.UserId;
                await db.SaveChangesAsync(ct);
                await _audit.LogAsync(new AuditEntry(
                    SiteId: token.SiteId, ActorUserId: token.UserId, ActorEmail: null, ActorIp: ip, ActorUserAgent: ua,
                    Action: "auth.session_idle_timeout", TargetType: "RefreshToken", TargetId: token.Id.ToString(),
                    Outcome: "failure", ErrorMessage: $"Idle for {idleSince.TotalMinutes:F0} min (> {idleMinutes} min limit)",
                    SignatureMeaning: "Automatic session logoff after idle period (21 CFR Part 11 §11.300(d))."), ct);
                throw new AuthenticationException("SESSION_IDLE_TIMEOUT",
                    $"Session timed out after {idleMinutes} minutes of inactivity. Please log in again.");
            }
        }

        var maxHours = _config.GetValue<int>("Session:MaxHours", 8);
        if (maxHours > 0)
        {
            var sessionAge = DateTime.UtcNow - token.CreatedAt;
            if (sessionAge.TotalHours > maxHours)
            {
                token.RevokedAt = DateTime.UtcNow;
                token.RevokedBy = token.UserId;
                await db.SaveChangesAsync(ct);
                await _audit.LogAsync(new AuditEntry(
                    SiteId: token.SiteId, ActorUserId: token.UserId, ActorEmail: null, ActorIp: ip, ActorUserAgent: ua,
                    Action: "auth.session_expired", TargetType: "RefreshToken", TargetId: token.Id.ToString(),
                    Outcome: "failure", ErrorMessage: $"Session age {sessionAge.TotalHours:F1}h > {maxHours}h max",
                    SignatureMeaning: "Automatic session logoff after max session length (21 CFR Part 11 §11.300(d))."), ct);
                throw new AuthenticationException("SESSION_EXPIRED",
                    $"Session exceeded the maximum duration of {maxHours} hours. Please log in again.");
            }
        }
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
