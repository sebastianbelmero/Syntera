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

    public AuthController(IAuthService auth, ILogger<AuthController> log)
    {
        _auth = auth;
        _log = log;
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
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.RefreshToken))
            return BadRequest(ApiResponse<object>.Fail("EMPTY_TOKEN", "Refresh token is required."));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        try
        {
            var result = await _auth.RefreshAsync(req.RefreshToken, ip, ua, ct);
            return Ok(result);
        }
        catch (Models.DomainException ex)
        {
            return Unauthorized(ApiResponse<object>.Fail(ex.Code, ex.Message));
        }
    }

    /// <summary>Exchange a refresh token for a new access token (site user scope).</summary>
    [HttpPost("refresh-site")]
    [AllowAnonymous]
    public async Task<IActionResult> RefreshSite([FromBody] RefreshSiteRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.RefreshToken))
            return BadRequest(ApiResponse<object>.Fail("EMPTY_TOKEN", "Refresh token is required."));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        try
        {
            var result = await _auth.RefreshSiteAsync(req.RefreshToken, req.SiteId, ip, ua, ct);
            return Ok(result);
        }
        catch (Models.DomainException ex)
        {
            return Unauthorized(ApiResponse<object>.Fail(ex.Code, ex.Message));
        }
    }

    /// <summary>Logout by revoking the refresh token.</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.RefreshToken))
            return BadRequest(ApiResponse<object>.Fail("EMPTY_TOKEN", "Refresh token is required."));

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var revokedBy = Guid.TryParse(userId, out var g) ? g : (Guid?)null;

        await _auth.LogoutAsync(req.RefreshToken, revokedBy, ct);
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
            Scope: User.FindFirst("scope")?.Value ?? "anonymous",
            SiteId: Guid.TryParse(User.FindFirst("site_id")?.Value, out var sid) ? sid : null,
            SiteCode: User.FindFirst("site_code")?.Value,
            SiteDisplayName: null,
            Roles: User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList(),
            Permissions: User.FindAll("perm").Select(c => c.Value).ToList());
        return Ok(profile);
    }
}

public record RefreshSiteRequest(string RefreshToken, Guid SiteId);
