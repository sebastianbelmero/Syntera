using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Syntera.Backend.Authorization;
using Syntera.Backend.Controllers;
using Syntera.Backend.Models;
using Syntera.Backend.Models.Dtos.Sites;
using Syntera.Backend.Services;

namespace Syntera.Backend.Controllers;

/// <summary>
/// Platform Admin → Site Management.
///
/// Authorization:
///   - System Admin endpoints: Platform Admin only
///   - Business Admin endpoints: System Admin only (NOT Platform Admin)
///   - Site management (list, get, update): Platform Admin only
/// </summary>
[ApiController]
[Route("api/platform/sites")]
[Authorize] // Require auth, per-method attributes enforce specific roles
public sealed class SitesController : ApiControllerBase
{
    private readonly ISiteManagementService _sites;
    private readonly IUserManagementService _users;
    private readonly ICurrentUserService _current;

    public SitesController(ISiteManagementService sites, IUserManagementService users, ICurrentUserService current)
    {
        _sites = sites;
        _users = users;
        _current = current;
    }

    // ─── Site Management (Platform Admin only) ──────────────────────

    [HttpGet]
    [PlatformAdminOrSystemAdmin]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _sites.ListAsync(ct));

    [HttpGet("{id:guid}")]
    [PlatformAdminOrSystemAdmin]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _sites.GetAsync(id, ct));

    [HttpPut("{id:guid}")]
    [PlatformAdminOnly]
    public async Task<IActionResult> Update(Guid id, [FromBody] SiteUpdateDto dto, CancellationToken ct)
        => Ok(await _sites.UpdateAsync(id, dto, _current.UserId ?? Guid.Empty, ct));

    // ─── System Admin management (Platform Admin only) ──────────────

    [HttpPost("{siteId:guid}/system-admin")]
    [PlatformAdminOnly]
    public async Task<IActionResult> AssignSystemAdmin(Guid siteId, [FromBody] AssignAdminRequest req, CancellationToken ct)
        => Ok(await _users.AssignSystemAdminAsync(siteId, req.Email, req.DisplayName ?? "", _current.UserId ?? Guid.Empty, ct));

    [HttpGet("{siteId:guid}/system-admins")]
    [PlatformAdminOnly]
    public async Task<IActionResult> ListSystemAdmins(Guid siteId, CancellationToken ct)
        => Ok(await _users.ListSystemAdminsAsync(siteId, ct));

    [HttpDelete("{siteId:guid}/system-admin/{userId:guid}")]
    [PlatformAdminOnly]
    public async Task<IActionResult> RevokeSystemAdmin(Guid siteId, Guid userId, CancellationToken ct)
    {
        await _users.RevokeSystemAdminAsync(siteId, userId, _current.UserId ?? Guid.Empty, ct);
        return Ok(new { success = true });
    }

    // ─── Business Admin management (System Admin only) ─────────────
    // Platform Admin CANNOT assign/revoke Business Admin — must go
    // through System Admin. This enforces the delegation chain:
    //   Platform Admin → System Admin → Business Admin

    [HttpPost("{siteId:guid}/business-admin")]
    public async Task<IActionResult> AssignBusinessAdmin(Guid siteId, [FromBody] AssignAdminRequest req, CancellationToken ct)
    {
        // Only System Admin can assign Business Admin
        if (!_current.Roles.Contains("system-admin"))
            return Forbid();

        // System Admin can only manage their own site
        if (_current.SiteId != siteId)
            return Forbid();

        return Ok(await _users.AssignBusinessAdminAsync(siteId, req.Email, req.DisplayName ?? "", _current.UserId ?? Guid.Empty, ct));
    }

    [HttpGet("{siteId:guid}/business-admins")]
    public async Task<IActionResult> ListBusinessAdmins(Guid siteId, CancellationToken ct)
    {
        // Only System Admin can list Business Admins
        if (!_current.Roles.Contains("system-admin"))
            return Forbid();

        if (_current.SiteId != siteId)
            return Forbid();

        return Ok(await _users.ListBusinessAdminsAsync(siteId, ct));
    }

    [HttpDelete("{siteId:guid}/business-admin/{userId:guid}")]
    public async Task<IActionResult> RevokeBusinessAdmin(Guid siteId, Guid userId, CancellationToken ct)
    {
        if (!_current.Roles.Contains("system-admin"))
            return Forbid();

        if (_current.SiteId != siteId)
            return Forbid();

        await _users.RevokeBusinessAdminAsync(siteId, userId, _current.UserId ?? Guid.Empty, ct);
        return Ok(new { success = true });
    }

    // ─── LDAP Config (Platform Admin only) ─────────────────────────

    [HttpGet("{siteId:guid}/ldap-config")]
    [PlatformAdminOnly]
    public async Task<IActionResult> GetLdapConfig(Guid siteId, CancellationToken ct)
        => Ok(await _sites.GetLdapConfigAsync(siteId, ct));

    [HttpPut("{siteId:guid}/ldap-config")]
    [PlatformAdminOnly]
    public async Task<IActionResult> UpsertLdapConfig(Guid siteId, [FromBody] LdapConfigUpsertDto dto, CancellationToken ct)
        => Ok(await _sites.UpsertLdapConfigAsync(siteId, dto, _current.UserId ?? Guid.Empty, ct));

    [HttpPost("ldap-test")]
    [PlatformAdminOnly]
    public async Task<IActionResult> TestLdap([FromBody] LdapTestRequest req, CancellationToken ct)
        => Ok(await _sites.TestLdapAsync(req, ct));

    // ─── Theme (Platform Admin only) ────────────────────────────────

    [HttpGet("{siteId:guid}/theme")]
    [PlatformAdminOnly]
    public async Task<IActionResult> GetTheme(Guid siteId, CancellationToken ct)
        => Ok(await _sites.GetThemeAsync(siteId, ct));

    [HttpPut("{siteId:guid}/theme")]
    [PlatformAdminOnly]
    public async Task<IActionResult> UpsertTheme(Guid siteId, [FromBody] ThemeUpsertDto dto, CancellationToken ct)
        => Ok(await _sites.UpsertThemeAsync(siteId, dto, _current.UserId ?? Guid.Empty, ct));
}

/// <summary>Request body for assigning admin (System or Business).</summary>
public sealed record AssignAdminRequest(string Email, string? DisplayName);
