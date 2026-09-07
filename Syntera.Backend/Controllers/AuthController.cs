using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Syntera.Backend.Controllers;
using Syntera.Backend.Models;
using Syntera.Backend.Models.Dtos.Auth;
using Syntera.Backend.Services;

namespace Syntera.Backend.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ApiControllerBase
{
    private readonly IAuthService _auth;
    private readonly ILogger<AuthController> _log;
    private readonly IHostEnvironment _env;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

    /// <summary>
    /// Cookie name for the refresh token. httpOnly — JavaScript cannot read it.
    /// Path-scoped to /api/auth so it's only sent on auth endpoints (reduces
    /// surface area; business API calls don't carry the refresh cookie).
    /// </summary>
    public const string RefreshCookieName = "syntera_refresh";

    public AuthController(
        IAuthService auth,
        ILogger<AuthController> log,
        IHostEnvironment env,
        Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _auth = auth;
        _log = log;
        _env = env;
        _config = config;
    }

    /// <summary>
    /// Authenticate by email + password. Email domain determines auth method:
    /// @syntera.com → Platform Admin (local), anything else → site LDAP.
    ///
    /// <para>COMPLIANCE (Sprint 2.3): the response may now carry
    /// <see cref="LoginResponse.RequiresMfa"/> or
    /// <see cref="LoginResponse.RequiresPasswordChange"/> instead of a
    /// full token pair. When either is true, <c>AccessToken</c> and
    /// <c>RefreshToken</c> are empty and the client MUST follow the
    /// corresponding challenge token (call POST /api/auth/login-mfa or
    /// POST /api/auth/change-password, respectively).</para>
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(ApiResponse<object>.Fail("INVALID_INPUT", "Email and password are required."));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        try
        {
            var result = await _auth.LoginAsync(req, ip, ua, ct);
            // SECURITY (H7): set refresh token as httpOnly cookie so JS
            // cannot read it (XSS can't exfiltrate). SameSite=Lax prevents
            // cross-site CSRF on auth endpoints. Secure=true in Production
            // (HTTPS only). Path=/api/auth scopes the cookie to auth routes
            // only — business API calls don't carry it.
            //
            // COMPLIANCE (Sprint 2.3): only set the cookie when we actually
            // issued a refresh token. Challenge responses (MFA / forced
            // password change) return an empty refresh token and must NOT
            // overwrite any existing cookie with an empty value (which would
            // effectively log the user out of an existing valid session).
            if (!result.RequiresMfa && !result.RequiresPasswordChange
                && !string.IsNullOrEmpty(result.RefreshToken))
            {
                SetRefreshCookie(result.RefreshToken, result.Profile.Scope);
            }
            return Ok(result);
        }
        catch (Models.DomainException ex)
        {
            return ex is Models.AuthenticationException
                ? Unauthorized(ApiResponse<object>.Fail(ex.Code, ex.Message))
                : BadRequest(ApiResponse<object>.Fail(ex.Code, ex.Message));
        }
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): complete the login flow after the user
    /// provided a valid password but had MFA enabled. Accepts the MFA
    /// challenge token (issued by POST /api/auth/login with
    /// <see cref="LoginResponse.RequiresMfa"/> = true) and a 6-digit TOTP
    /// code from the user's authenticator app. On success, returns the
    /// full access + refresh tokens (same shape as a normal login
    /// response). On failure, returns 401 with INVALID_MFA_CODE (or
    /// INVALID_MFA_CHALLENGE if the challenge token itself is bad).
    /// </summary>
    [HttpPost("login-mfa")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LoginMfa([FromBody] LoginMfaRequest? req, CancellationToken ct)
    {
        if (req is null
            || string.IsNullOrWhiteSpace(req.MfaChallengeToken)
            || string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(ApiResponse<object>.Fail("INVALID_INPUT",
                "MFA challenge token and code are required."));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        try
        {
            var result = await _auth.LoginMfaAsync(req, ip, ua, ct);
            // MFA-success path that returns a password-change challenge (the
            // admin had both MFA enabled AND an expired password) returns no
            // refresh token; don't touch the cookie in that case.
            if (!result.RequiresPasswordChange && !string.IsNullOrEmpty(result.RefreshToken))
                SetRefreshCookie(result.RefreshToken, result.Profile.Scope);
            return Ok(result);
        }
        catch (Models.DomainException ex)
        {
            return ex is Models.AuthenticationException
                ? Unauthorized(ApiResponse<object>.Fail(ex.Code, ex.Message))
                : BadRequest(ApiResponse<object>.Fail(ex.Code, ex.Message));
        }
    }

    /// <summary>Exchange a refresh token for a new access token (platform admin scope).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest? req, CancellationToken ct)
    {
        // SECURITY (H7): prefer refresh token from httpOnly cookie; fall back
        // to JSON body for backward compat with old frontend builds.
        var refreshToken = ReadRefreshToken(req?.RefreshToken);
        if (string.IsNullOrWhiteSpace(refreshToken))
            return BadRequest(ApiResponse<object>.Fail("EMPTY_TOKEN", "Refresh token is required."));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        try
        {
            var result = await _auth.RefreshAsync(refreshToken, ip, ua, ct);
            // Rotate the cookie to the new token.
            SetRefreshCookie(result.RefreshToken, result.Profile.Scope);
            return Ok(result);
        }
        catch (Models.DomainException ex)
        {
            // On any refresh failure, clear the cookie so the browser state
            // matches the (now-revoked) server state.
            ClearRefreshCookie();
            return Unauthorized(ApiResponse<object>.Fail(ex.Code, ex.Message));
        }
    }

    /// <summary>Exchange a refresh token for a new access token (site user scope).</summary>
    [HttpPost("refresh-site")]
    [AllowAnonymous]
    public async Task<IActionResult> RefreshSite([FromBody] RefreshSiteRequest? req, CancellationToken ct)
    {
        var refreshToken = ReadRefreshToken(req?.RefreshToken);
        if (string.IsNullOrWhiteSpace(refreshToken) || req is null || req.SiteId == Guid.Empty)
            return BadRequest(ApiResponse<object>.Fail("EMPTY_TOKEN", "Refresh token and siteId are required."));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        try
        {
            var result = await _auth.RefreshSiteAsync(refreshToken, req.SiteId, ip, ua, ct);
            SetRefreshCookie(result.RefreshToken, result.Profile.Scope);
            return Ok(result);
        }
        catch (Models.DomainException ex)
        {
            ClearRefreshCookie();
            return Unauthorized(ApiResponse<object>.Fail(ex.Code, ex.Message));
        }
    }

    /// <summary>Logout by revoking the refresh token.</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? req, CancellationToken ct)
    {
        var refreshToken = ReadRefreshToken(req?.RefreshToken);
        if (string.IsNullOrWhiteSpace(refreshToken))
            return BadRequest(ApiResponse<object>.Fail("EMPTY_TOKEN", "Refresh token is required."));

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var revokedBy = Guid.TryParse(userId, out var g) ? g : (Guid?)null;

        await _auth.LogoutAsync(refreshToken, revokedBy, ct);
        // Always clear the cookie on logout, even if server-side revoke failed.
        ClearRefreshCookie();
        return Ok(ApiResponse<object>.Ok(null, "Logged out."));
    }

    /// <summary>Returns the current user's profile (from JWT claims).</summary>
    [HttpGet("profile")]
    [Authorize]
    public IActionResult Profile()
    {
        var profile = new UserProfileDto(
            UserId: Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : Guid.Empty,
            Email: User.FindFirst("email")?.Value ?? "",
            DisplayName: User.FindFirst("display_name")?.Value ?? "",
            Title: User.FindFirst("title")?.Value,
            Scope: User.FindFirst("scope")?.Value ?? "anonymous",
            SiteId: Guid.TryParse(User.FindFirst("site_id")?.Value, out var sid) ? sid : null,
            SiteCode: User.FindFirst("site_code")?.Value,
            SiteDisplayName: null,
            Roles: User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList(),
            Permissions: User.FindAll("perm").Select(c => c.Value).ToList());
        return Ok(profile);
    }

    /// <summary>
    /// M7: change the calling Platform Admin's password. Enforces password
    /// policy (length, complexity), verifies current password, refuses
    /// no-op rotation. Site users change their password in AD, not here.
    ///
    /// <para>COMPLIANCE (Sprint 2.3): the endpoint is anonymous so the
    /// FORCED password-change flow can use the password-change challenge
    /// token (issued by POST /api/auth/login with
    /// <see cref="LoginResponse.RequiresPasswordChange"/> = true) when the
    /// password has exceeded its max-age. The AuthService enforces
    /// authentication via either the bearer JWT (voluntary change while
    /// authenticated) OR the challenge token (forced change). At least one
    /// of <see cref="ChangePasswordRequest.CurrentPassword"/> or
    /// <see cref="ChangePasswordRequest.PasswordChangeChallengeToken"/>
    /// must be present — when only the challenge token is present, the
    /// current-password check is skipped (the challenge token already
    /// proves identity via JWT signature).</para>
    /// </summary>
    [HttpPost("change-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest? req, CancellationToken ct)
    {
        // Allow either currentPassword (voluntary) or challenge token (forced).
        // The AuthService validates whichever path is taken.
        if (req is null || string.IsNullOrWhiteSpace(req.NewPassword))
            return BadRequest(ApiResponse<object>.Fail("INVALID_INPUT",
                "New password is required."));
        if (string.IsNullOrWhiteSpace(req.CurrentPassword)
            && string.IsNullOrWhiteSpace(req.PasswordChangeChallengeToken))
            return BadRequest(ApiResponse<object>.Fail("INVALID_INPUT",
                "Either current password or a password-change challenge token is required."));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        try
        {
            await _auth.ChangePasswordAsync(
                currentPassword: req.CurrentPassword ?? "",
                newPassword: req.NewPassword,
                ip: ip,
                userAgent: ua,
                ct: ct,
                passwordChangeChallengeToken: req.PasswordChangeChallengeToken);
            return Ok(ApiResponse<object>.Ok(null, "Password changed."));
        }
        catch (Models.DomainException ex)
        {
            // Authorization + BusinessRule + Authentication exceptions
            // (NOT_PLATFORM_ADMIN, WRONG_CURRENT_PASSWORD,
            // PASSWORD_POLICY_VIOLATION, INVALID_CHALLENGE, etc.) —
            // surface as 400 (or 401 for AuthenticationException) with
            // the structured envelope.
            return ex is Models.AuthenticationException
                ? Unauthorized(ApiResponse<object>.Fail(ex.Code, ex.Message))
                : BadRequest(ApiResponse<object>.Fail(ex.Code, ex.Message));
        }
    }

    // ── COMPLIANCE (Sprint 2.3): MFA (TOTP) endpoints ────────────────────

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): Begin MFA (TOTP) enrollment for the
    /// authenticated Platform Admin. Returns an otpauth:// URL (for QR
    /// code generation) and the plaintext Base32 secret (for manual entry
    /// on devices without a camera). MFA is NOT enabled until the user
    /// confirms via POST /api/auth/mfa/confirm with a valid TOTP code.
    /// </summary>
    [HttpPost("mfa/setup")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<MfaSetupResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MfaSetup(CancellationToken ct)
    {
        try
        {
            var result = await _auth.SetupMfaAsync(ct);
            return Ok(result);
        }
        catch (Models.DomainException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Code, ex.Message));
        }
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): Confirm MFA enrollment. Verifies the
    /// supplied TOTP code against the secret generated by POST
    /// /api/auth/mfa/setup; on success enables MFA for the calling
    /// Platform Admin. From this point forward, login requires both
    /// password AND a valid TOTP code.
    /// </summary>
    [HttpPost("mfa/confirm")]
    [Authorize]
    public async Task<IActionResult> MfaConfirm([FromBody] ConfirmMfaRequest? req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(ApiResponse<object>.Fail("INVALID_INPUT",
                "Authentication code is required."));

        try
        {
            await _auth.ConfirmMfaAsync(req.Code, ct);
            return Ok(ApiResponse<object>.Ok(null, "MFA enabled."));
        }
        catch (Models.DomainException ex)
        {
            // Authorization + BusinessRule exceptions (NOT_PLATFORM_ADMIN,
            // MFA_NOT_SETUP, INVALID_MFA_CODE, etc.) — surface as 400.
            // AuditWriteException (from LogCriticalAsync) is a runtime
            // exception that bubbles up to the global exception middleware
            // and surfaces as a 5xx (compliance: a failed audit write
            // MUST fail the business operation).
            return BadRequest(ApiResponse<object>.Fail(ex.Code, ex.Message));
        }
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): Disable MFA for the authenticated Platform
    /// Admin. Requires a current valid TOTP code (re-verifies the user has
    /// the authenticator device) to prevent accidental or
    /// attacker-initiated disable. On success, clears the stored secret
    /// and sets TotpEnabled=false.
    /// </summary>
    [HttpPost("mfa/disable")]
    [Authorize]
    public async Task<IActionResult> MfaDisable([FromBody] DisableMfaRequest? req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(ApiResponse<object>.Fail("INVALID_INPUT",
                "Authentication code is required to disable MFA."));

        try
        {
            await _auth.DisableMfaAsync(req.Code, ct);
            return Ok(ApiResponse<object>.Ok(null, "MFA disabled."));
        }
        catch (Models.DomainException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Code, ex.Message));
        }
    }

    // ── Cookie helpers (H7) ─────────────────────────────────────────────

    private void SetRefreshCookie(string token, string scope)
    {
        // M2: cookie MaxAge must match the refresh token's TTL (which is
        // scope-specific — see AuthService.GetRefreshTokenTtl). If cookie
        // outlives the token, browser still sends stale cookies; if cookie
        // expires first, user must re-login despite valid token.
        var ttl = GetRefreshCookieTtl(scope);
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = !_env.IsDevelopment(),  // HTTPS-only in Production
            SameSite = SameSiteMode.Lax,      // blocks CSRF on cross-site POSTs
            Path = "/api/auth",               // scoped to auth routes only
            IsEssential = true,
            MaxAge = ttl,
            // Don't set Domain — host-only cookie, not sent to subdomains.
        };
        Response.Cookies.Append(RefreshCookieName, token, options);
    }

    /// <summary>
    /// M2: cookie TTL matches the refresh token's TTL per scope. Mirrors
    /// AuthService.GetRefreshTokenTtl but in the controller context (we
    /// don't share state with AuthService to keep the cookie logic visible
    /// in one place).
    /// </summary>
    private TimeSpan GetRefreshCookieTtl(string scope)
    {
        var legacy = _config["Jwt:RefreshTokenDays"];
        var perScope = scope == "platform"
            ? _config["Jwt:RefreshTokenDaysPlatform"]
            : _config["Jwt:RefreshTokenDaysSite"];
        var str = perScope ?? legacy;
        if (int.TryParse(str, out var days) && days > 0)
            return TimeSpan.FromDays(days);
        return scope == "platform" ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7);
    }

    private void ClearRefreshCookie()
    {
        // Must match the same Path/Domain/Secure attributes used when setting
        // the cookie, otherwise the browser won't actually delete it.
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            Path = "/api/auth",
            Secure = !_env.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
        });
    }

    /// <summary>
    /// Read refresh token from httpOnly cookie first (preferred, more secure);
    /// fall back to JSON body for backward compat with old frontend builds
    /// that haven't migrated to cookie-based refresh yet.
    /// </summary>
    private string? ReadRefreshToken(string? bodyToken)
    {
        if (Request.Cookies.TryGetValue(RefreshCookieName, out var cookieToken) && !string.IsNullOrWhiteSpace(cookieToken))
            return cookieToken;
        return bodyToken;
    }
}

public record RefreshSiteRequest(string RefreshToken, Guid SiteId);

// NOTE: ChangePasswordRequest has moved to Models/Dtos/Auth/AuthDtos.cs
// (Sprint 2.3) so it can carry the optional PasswordChangeChallengeToken
// field used by the forced-change flow. The DTO is in the
// Syntera.Backend.Models.Dtos.Auth namespace, which this controller
// already imports.
