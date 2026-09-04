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

    /// <summary>
    /// Cookie name for the refresh token. httpOnly — JavaScript cannot read it.
    /// Path-scoped to /api/auth so it's only sent on auth endpoints (reduces
    /// surface area; business API calls don't carry the refresh cookie).
    /// </summary>
    public const string RefreshCookieName = "syntera_refresh";

    public AuthController(IAuthService auth, ILogger<AuthController> log, IHostEnvironment env)
    {
        _auth = auth;
        _log = log;
        _env = env;
    }

    /// <summary>
    /// Authenticate by email + password. Email domain determines auth method:
    /// @syntera.com → Platform Admin (local), anything else → site LDAP.
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
            SetRefreshCookie(result.RefreshToken);
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
            SetRefreshCookie(result.RefreshToken);
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
            SetRefreshCookie(result.RefreshToken);
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

    // ── Cookie helpers (H7) ─────────────────────────────────────────────

    private void SetRefreshCookie(string token)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = !_env.IsDevelopment(),  // HTTPS-only in Production
            SameSite = SameSiteMode.Lax,      // blocks CSRF on cross-site POSTs
            Path = "/api/auth",               // scoped to auth routes only
            IsEssential = true,
            // Match the refresh token's TTL (1 day per BuildRefreshToken).
            MaxAge = TimeSpan.FromDays(1),
            // Don't set Domain — host-only cookie, not sent to subdomains.
        };
        Response.Cookies.Append(RefreshCookieName, token, options);
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
