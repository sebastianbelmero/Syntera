using Microsoft.Extensions.Caching.Memory;
using Syntera.Backend.Models.Dtos.Auth;
using Syntera.Backend.Services;

namespace Syntera.Backend.Services;

/// <summary>
/// Core authentication contract. Resolves authentication strategy by email domain:
/// - <c>@syntera.com</c> → Platform Admin (local bcrypt credential)
/// - Any registered site domain → Site LDAP (LDAPS / StartTLS)
/// - Unknown domain → reject
///
/// After successful auth, issues JWT (15min) + refresh token (1d platform /
/// 7d site, rotating). Refresh tokens are tracked server-side for revocation.
/// The login flow is the SINGLE entry point for the entire platform.
///
/// <para><b>REFACTOR (2026-09):</b> this was a 1.700-line god-class. The
/// implementation now lives in four focused services and this type is a thin
/// facade that keeps the controller-facing contract stable:
/// <list type="bullet">
///   <item><see cref="IPlatformAdminAuthService"/> — platform login, MFA, password change</item>
///   <item><see cref="ISiteUserAuthService"/> — site LDAP login</item>
///   <item><see cref="IRefreshFlowService"/> — refresh rotation, logout, reuse detection</item>
///   <item><see cref="IRefreshTokenService"/> — token crypto, persistence rows, session limits</item>
/// </list></para>
/// </summary>
public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, string? ip, string? userAgent, CancellationToken ct = default);
    Task<RefreshResponse> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken ct = default);

    /// <summary>
    /// Site-scoped refresh — part of the contract (previously reached only via
    /// a downcasting extension method that threw for any non-concrete
    /// implementation).
    /// </summary>
    Task<RefreshResponse> RefreshSiteAsync(string refreshToken, Guid siteId, string? ip, string? userAgent, CancellationToken ct = default);

    Task LogoutAsync(string refreshToken, Guid? revokedBy, CancellationToken ct = default);

    /// <summary>
    /// M7: change the calling user's password (Platform Admin only — site
    /// users change their password in AD). Enforces password policy, verifies
    /// the current password, and refuses no-op rotation + history reuse.
    ///
    /// <para>COMPLIANCE (Sprint 2.3): when <paramref name="passwordChangeChallengeToken"/>
    /// is non-null, the current-password verification is SKIPPED and the caller
    /// is resolved from the challenge token (forced-change flow).</para>
    /// </summary>
    Task ChangePasswordAsync(string currentPassword, string newPassword, string? ip, string? userAgent, CancellationToken ct = default, string? passwordChangeChallengeToken = null);

    // ── COMPLIANCE (Sprint 2.3): MFA (TOTP) ────────────────────────────────

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): complete login after the user provided a valid
    /// password but MFA (TOTP) was enabled. Validates the MFA challenge token,
    /// verifies the TOTP code, and on success issues the full token pair.
    /// </summary>
    Task<LoginResponse> LoginMfaAsync(LoginMfaRequest request, string? ip, string? userAgent, CancellationToken ct = default);

    /// <summary>Begin MFA enrollment for the calling Platform Admin.</summary>
    Task<MfaSetupResponse> SetupMfaAsync(CancellationToken ct = default);

    /// <summary>Confirm MFA enrollment — enables MFA (critical audit).</summary>
    Task ConfirmMfaAsync(string code, CancellationToken ct = default);

    /// <summary>Disable MFA — requires a current valid TOTP code (critical audit).</summary>
    Task DisableMfaAsync(string code, CancellationToken ct = default);
}

/// <summary>
/// Facade over the extracted auth services. Owns only the login router:
/// email validation, the (IP,email)+(email) rate-limit gate, and the
/// platform-vs-site branch by email domain.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly IPlatformAdminAuthService _platformAuth;
    private readonly ISiteUserAuthService _siteAuth;
    private readonly IRefreshFlowService _refreshFlow;
    private readonly IMemoryCache _cache;

    public AuthService(
        IPlatformAdminAuthService platformAuth,
        ISiteUserAuthService siteAuth,
        IRefreshFlowService refreshFlow,
        IMemoryCache cache)
    {
        _platformAuth = platformAuth;
        _siteAuth = siteAuth;
        _refreshFlow = refreshFlow;
        _cache = cache;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
            throw new Models.AuthenticationException("INVALID_EMAIL", "Valid email is required.");

        // ── Rate limit: per (IP + email) AND per email (M4) ─────────────
        // Two buckets protect against different attack patterns:
        //   - (IP, email): blocks a single attacker at one IP hammering
        //     one account.
        //   - email alone (no IP): blocks a botnet spraying one account
        //     from many IPs.
        //
        // Failure message is the same generic "RATE_LIMITED" so attackers
        // can't tell which bucket tripped.
        if (LoginThrottle.IsBlocked(_cache, ip, email, AuthConstants.MaxFailedLogins))
            throw new Models.AuthenticationException("RATE_LIMITED",
                "Too many failed login attempts. Try again in 15 minutes.");

        var perIpKey = LoginThrottle.PerIpKey(ip, email);
        var perEmailKey = LoginThrottle.PerEmailKey(email);

        // ── Branch: Platform Admin or Site user? ─────────────────────
        if (email.EndsWith("@syntera.com", StringComparison.OrdinalIgnoreCase))
            return await _platformAuth.LoginAsync(email, request.Password, ip, userAgent, perIpKey, perEmailKey, ct);

        return await _siteAuth.LoginAsync(email, request.Password, ip, userAgent, perIpKey, perEmailKey, ct);
    }

    public Task<RefreshResponse> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken ct = default)
        => _refreshFlow.RefreshAsync(refreshToken, ip, userAgent, ct);

    public Task<RefreshResponse> RefreshSiteAsync(string refreshToken, Guid siteId, string? ip, string? userAgent, CancellationToken ct = default)
        => _refreshFlow.RefreshSiteAsync(refreshToken, siteId, ip, userAgent, ct);

    public Task LogoutAsync(string refreshToken, Guid? revokedBy, CancellationToken ct = default)
        => _refreshFlow.LogoutAsync(refreshToken, revokedBy, ct);

    public Task ChangePasswordAsync(
        string currentPassword, string newPassword, string? ip, string? userAgent,
        CancellationToken ct = default, string? passwordChangeChallengeToken = null)
        => _platformAuth.ChangePasswordAsync(currentPassword, newPassword, ip, userAgent, ct, passwordChangeChallengeToken);

    public Task<LoginResponse> LoginMfaAsync(LoginMfaRequest request, string? ip, string? userAgent, CancellationToken ct = default)
        => _platformAuth.LoginMfaAsync(request, ip, userAgent, ct);

    public Task<MfaSetupResponse> SetupMfaAsync(CancellationToken ct = default)
        => _platformAuth.SetupMfaAsync(ct);

    public Task ConfirmMfaAsync(string code, CancellationToken ct = default)
        => _platformAuth.ConfirmMfaAsync(code, ct);

    public Task DisableMfaAsync(string code, CancellationToken ct = default)
        => _platformAuth.DisableMfaAsync(code, ct);
}
