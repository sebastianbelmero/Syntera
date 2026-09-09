using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Syntera.Backend.Data;
using Syntera.Backend.Models.Dtos.Auth;
using Syntera.Backend.Models.Entities;
using Syntera.Backend.Services;

namespace Syntera.Backend.Services;

/// <summary>
/// Platform-Admin authentication flows, extracted from the former AuthService
/// god-class: local bcrypt login (with MFA challenge / password max-age
/// challenge branching), TOTP MFA login completion, MFA enrollment
/// (setup/confirm/disable), and password change (voluntary + forced).
///
/// <para>Site users never touch this service — they authenticate via their
/// site's LDAP (see SiteUserAuthService). The Platform Admin uses a local
/// bcrypt credential so login works even when every site LDAP is down.</para>
/// </summary>
public interface IPlatformAdminAuthService
{
    /// <summary>Password login for @syntera.com accounts. Returns a full token
    /// pair, or a challenge response (MFA / password max-age) without tokens.</summary>
    Task<LoginResponse> LoginAsync(
        string email, string password, string? ip, string? ua, string perIpKey, string perEmailKey, CancellationToken ct);

    /// <summary>COMPLIANCE (Sprint 2.3): complete login after a valid password
    /// when MFA was enabled — validates the challenge token + TOTP code.</summary>
    Task<LoginResponse> LoginMfaAsync(LoginMfaRequest request, string? ip, string? userAgent, CancellationToken ct = default);

    /// <summary>M7: change the calling Platform Admin's password (voluntary or
    /// forced via password-change challenge token).</summary>
    Task ChangePasswordAsync(
        string currentPassword, string newPassword, string? ip, string? userAgent,
        CancellationToken ct = default, string? passwordChangeChallengeToken = null);

    /// <summary>COMPLIANCE (Sprint 2.3): begin MFA enrollment (generate +
    /// persist encrypted secret, NOT yet enabled).</summary>
    Task<MfaSetupResponse> SetupMfaAsync(CancellationToken ct = default);

    /// <summary>COMPLIANCE (Sprint 2.3): confirm MFA enrollment with a valid
    /// TOTP code — sets TotpEnabled=true (critical audit).</summary>
    Task ConfirmMfaAsync(string code, CancellationToken ct = default);

    /// <summary>COMPLIANCE (Sprint 2.3): disable MFA — requires a current
    /// valid TOTP code (critical audit).</summary>
    Task DisableMfaAsync(string code, CancellationToken ct = default);
}

public sealed class PlatformAdminAuthService : IPlatformAdminAuthService
{
    private readonly PlatformDbContext _platformDb;
    private readonly ITokenService _tokens;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditService _audit;
    private readonly IPermissionService _permissions;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PlatformAdminAuthService> _log;
    private readonly ICurrentUserService _currentUser;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly ITotpService _totp;
    private readonly IRefreshTokenService _refreshTokens;

    public PlatformAdminAuthService(
        PlatformDbContext platformDb,
        ITokenService tokens,
        IPasswordHasher hasher,
        IAuditService audit,
        IPermissionService permissions,
        IMemoryCache cache,
        ILogger<PlatformAdminAuthService> log,
        ICurrentUserService currentUser,
        Microsoft.Extensions.Configuration.IConfiguration config,
        IPasswordPolicy passwordPolicy,
        ITotpService totp,
        IRefreshTokenService refreshTokens)
    {
        _platformDb = platformDb;
        _tokens = tokens;
        _hasher = hasher;
        _audit = audit;
        _permissions = permissions;
        _cache = cache;
        _log = log;
        _currentUser = currentUser;
        _config = config;
        _passwordPolicy = passwordPolicy;
        _totp = totp;
        _refreshTokens = refreshTokens;
    }

    public async Task<LoginResponse> LoginAsync(
        string email, string password, string? ip, string? ua, string perIpKey, string perEmailKey, CancellationToken ct)
    {
        var admin = await _platformDb.PlatformUsers.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (admin is null || !admin.IsEnabled)
        {
            LoginThrottle.BumpFail(_cache, perIpKey, perEmailKey);
            await _audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: null, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.login", TargetType: "PlatformUser", TargetId: null,
                Outcome: "failure", ErrorMessage: "Unknown or disabled platform admin"), ct);
            throw new Models.AuthenticationException("INVALID_CREDENTIALS", "Invalid credentials.");
        }

        if (admin.LockedUntil is not null && admin.LockedUntil > DateTime.UtcNow)
        {
            throw new Models.AuthenticationException("ACCOUNT_LOCKED",
                $"Account locked until {admin.LockedUntil.Value:u}.");
        }

        if (!_hasher.Verify(password, admin.PasswordHash))
        {
            admin.FailedLoginCount++;
            admin.LastFailedLoginAt = DateTime.UtcNow;
            var nowLocked = false;
            DateTime? lockedUntil = null;
            if (admin.FailedLoginCount >= AuthConstants.MaxFailedLogins)
            {
                lockedUntil = DateTime.UtcNow.Add(AuthConstants.LockoutDuration);
                admin.LockedUntil = lockedUntil;
                admin.FailedLoginCount = 0;
                nowLocked = true;
            }
            await _platformDb.SaveChangesAsync(ct);
            LoginThrottle.BumpFail(_cache, perIpKey, perEmailKey);
            await _audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: admin.Id, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                Action: "auth.login", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                Outcome: "failure", ErrorMessage: "Invalid password"), ct);

            // M6: when the account JUST got locked (not "already locked"),
            // emit a separate audit event with action='auth.account_locked'.
            if (nowLocked && lockedUntil is not null)
            {
                await _audit.LogAsync(new AuditEntry(
                    SiteId: null, ActorUserId: admin.Id, ActorEmail: email, ActorIp: ip, ActorUserAgent: ua,
                    Action: "auth.account_locked", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                    Outcome: "failure",
                    ErrorMessage: $"Account locked until {lockedUntil.Value:u} after {AuthConstants.MaxFailedLogins} failed attempts."), ct);
                _log.LogWarning("Platform admin {Email} locked until {LockoutUntil:O} after {Count} failed login attempts.",
                    email, lockedUntil.Value, AuthConstants.MaxFailedLogins);
            }

            throw new Models.AuthenticationException("INVALID_CREDENTIALS", "Invalid credentials.");
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
        // to receive the real tokens.
        if (admin.TotpEnabled && !string.IsNullOrEmpty(admin.TotpSecret))
        {
            var (challengeToken, _) = _tokens.IssueChallenge(admin.Id, AuthConstants.MfaChallengeScope, lifetimeMinutes: 5);
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
        if (IsPasswordMaxAgeExceeded(admin))
        {
            var (challengeToken, _) = _tokens.IssueChallenge(admin.Id, AuthConstants.PasswordChangeChallengeScope, lifetimeMinutes: 5);
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
            Roles: AuthConstants.PlatformAdminRoleClaim,
            Permissions: _permissions.GetPlatformAdminPermissions());

        var (access, exp, refresh) = await _refreshTokens.IssueTokensAsync(
            userId: admin.Id, scope: "platform", siteId: null,
            email: admin.Email, displayName: admin.DisplayName, title: null,
            roles: profile.Roles, permissions: profile.Permissions,
            version: 1, ip: ip, ua: ua, ct: ct);

        // FIX (P0 — refresh-token persistence): persist the refresh token to the
        // platform DB BEFORE returning it. The previous code only issued the
        // token pair and returned it — the row was never written to RefreshTokens,
        // so every subsequent refresh lookup failed with REFRESH_NOT_FOUND and
        // forced the Platform Admin to re-login on every page reload. The
        // site-user login path persisted correctly — only the platform paths
        // (here + LoginMfaAsync) were missing the write.
        var loginToken = _refreshTokens.BuildToken(admin.Id, "platform", null, refresh, ip, ua);
        _platformDb.RefreshTokens.Add(loginToken);
        await _platformDb.SaveChangesAsync(ct);

        return new LoginResponse(access, exp, refresh, profile, ThemeService.PlatformDefault());
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): complete login after the user provided a
    /// valid password but had MFA enabled. The challenge token (5-minute JWT)
    /// lets us resume the in-progress login without exposing any API access.
    /// Lockout: failed MFA attempts reuse the same FailedLoginCount counter —
    /// after 5 failures the account locks for the same 15-minute window.
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
        var userId = _tokens.ValidateChallenge(request.MfaChallengeToken, AuthConstants.MfaChallengeScope);
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
        // challenge issuance and this verification, we reject.
        if (!admin.TotpEnabled || string.IsNullOrEmpty(admin.TotpSecret))
            throw new Models.AuthenticationException("MFA_NOT_ENABLED",
                "MFA is not enabled for this account. Please log in again.");

        // Verify the TOTP code (constant-time comparison handled inside TotpService).
        var codeValid = await _totp.VerifyCodeAsync(admin.TotpSecret, request.Code, ct);
        if (!codeValid)
        {
            // Reuse the same lockout counter as password failures.
            admin.FailedLoginCount++;
            admin.LastFailedLoginAt = DateTime.UtcNow;
            var nowLocked = false;
            DateTime? lockedUntil = null;
            if (admin.FailedLoginCount >= AuthConstants.MaxFailedLogins)
            {
                lockedUntil = DateTime.UtcNow.Add(AuthConstants.LockoutDuration);
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
                    ErrorMessage: $"Account locked until {lockedUntil.Value:u} after {AuthConstants.MaxFailedLogins} failed MFA attempts."), ct);
                _log.LogWarning("Platform admin {Email} locked until {LockoutUntil:O} after {Count} failed MFA attempts.",
                    admin.Email, lockedUntil.Value, AuthConstants.MaxFailedLogins);
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
        // challenge instead of full tokens.
        if (IsPasswordMaxAgeExceeded(admin))
        {
            var (challengeToken, _) = _tokens.IssueChallenge(admin.Id, AuthConstants.PasswordChangeChallengeScope, lifetimeMinutes: 5);
            await _audit.LogAsync(new AuditEntry(
                SiteId: null, ActorUserId: admin.Id, ActorEmail: admin.Email, ActorIp: ip, ActorUserAgent: userAgent,
                Action: "auth.password_max_age", TargetType: "PlatformUser", TargetId: admin.Id.ToString(),
                Outcome: "success", ErrorMessage: "Password max-age exceeded after MFA — password-change challenge token issued"), ct);
            return BuildChallengeResponse(admin, requiresPasswordChange: true, passwordChangeChallengeToken: challengeToken);
        }

        // Issue full access + refresh tokens (mirrors the normal
        // platform login success path).
        var profile = new UserProfileDto(
            UserId: admin.Id, Email: admin.Email, DisplayName: admin.DisplayName, Title: null,
            Scope: "platform", SiteId: null, SiteCode: null, SiteDisplayName: null,
            Roles: AuthConstants.PlatformAdminRoleClaim,
            Permissions: _permissions.GetPlatformAdminPermissions());

        var (access, exp, refresh) = await _refreshTokens.IssueTokensAsync(
            userId: admin.Id, scope: "platform", siteId: null,
            email: admin.Email, displayName: admin.DisplayName, title: null,
            roles: profile.Roles, permissions: profile.Permissions,
            version: 1, ip: ip, ua: userAgent, ct: ct);

        // FIX (P0 — refresh-token persistence): same fix as the password login
        // path — the MFA completion path must persist the refresh token row too.
        var loginToken = _refreshTokens.BuildToken(admin.Id, "platform", null, refresh, ip, userAgent);
        _platformDb.RefreshTokens.Add(loginToken);
        await _platformDb.SaveChangesAsync(ct);

        return new LoginResponse(access, exp, refresh, profile, ThemeService.PlatformDefault());
    }

    /// <summary>
    /// M7: change the calling Platform Admin's password. Flow: resolve caller
    /// (bearer JWT for voluntary change, or password-change challenge token for
    /// forced change) → verify current password (voluntary only) → enforce
    /// policy → refuse no-op rotation → refuse history reuse → persist +
    /// critical audit. Does NOT invalidate existing sessions.
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
            var challengeUserId = _tokens.ValidateChallenge(passwordChangeChallengeToken, AuthConstants.PasswordChangeChallengeScope);
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
        // password against the last N historical hashes (§11.300(c)).
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
        // overwriting.
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

    // ── COMPLIANCE (Sprint 2.3): MFA (TOTP) enrollment ──────────────────

    public async Task<MfaSetupResponse> SetupMfaAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId
            ?? throw new Models.AuthorizationException("NOT_AUTHENTICATED", "You must be signed in to set up MFA.");
        if (!_currentUser.IsPlatformAdmin)
            throw new Models.AuthorizationException("NOT_PLATFORM_ADMIN",
                "MFA is only available for Platform Admin. Site users authenticate via LDAP.");

        var admin = await _platformDb.PlatformUsers.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new Models.NotFoundException("PlatformUser", userId);

        // Generate + persist the secret (encrypted). Re-running setup replaces
        // any previously generated secret; the OLD secret remains in effect
        // until the new one is confirmed via ConfirmMfaAsync.
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

        // Verify the code against the just-stored secret.
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

        // Enable MFA — CRITICAL audit: a failed audit write throws
        // AuditWriteException and rolls back the enable.
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
        // have the authenticator device. Prevents a stolen session from
        // silently disabling MFA.
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

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): build a LoginResponse that signals a challenge
    /// (MFA or forced password change) WITHOUT issuing access/refresh tokens.
    /// The Profile is minimal (no roles/permissions) because the user has NOT
    /// completed authentication yet.
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
    /// COMPLIANCE (Sprint 2.4): check whether the platform admin's password
    /// has exceeded the configured max-age (0 = disabled; null PasswordChangedAt
    /// fails open — the admin can change at their leisure).
    /// </summary>
    private bool IsPasswordMaxAgeExceeded(PlatformUser admin)
    {
        var maxAgeDays = _config.GetValue("PasswordPolicy:MaxAgeDays", 90);
        if (maxAgeDays <= 0) return false;
        if (admin.PasswordChangedAt is null) return false;
        return (DateTime.UtcNow - admin.PasswordChangedAt.Value).TotalDays > maxAgeDays;
    }
}
