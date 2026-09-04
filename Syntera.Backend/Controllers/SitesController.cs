using Microsoft.AspNetCore.Mvc;
using Syntera.Backend.Controllers;
using Syntera.Backend.Models;
using Syntera.Backend.Models.Dtos.Sites;
using Syntera.Backend.Models.Dtos.Users;
using Syntera.Backend.Services;
using Syntera.Backend.Services;
using Syntera.Backend.Authorization;

namespace Syntera.Backend.Controllers;

/// <summary>
/// Platform Admin → Site Management.
///
/// Sites are PRE-DEFINED in backend configuration (appsettings.json Sites[]).
/// Only DisplayName, LdapDomains, LDAP config, and Theme are editable from
/// the frontend. Code, DatabaseConnectionString, and IsEnabled are locked.
/// </summary>
[ApiController]
[Route("api/platform/sites")]
[PlatformAdminOnly]
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

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _sites.ListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _sites.GetAsync(id, ct));

    /// <summary>Update editable fields (DisplayName, LdapDomains). Code &amp; ConnectionString are locked.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SiteUpdateDto dto, CancellationToken ct)
        => Ok(await _sites.UpdateAsync(id, dto, _current.UserId ?? Guid.Empty, ct));

    /// <summary>
    /// Bootstrap the first Site Business Admin for a site. Creates the user
    /// (if not exists) and assigns the site-business-admin role. Used to
    /// break the chicken-and-egg problem.
    /// </summary>
    [HttpPost("{siteId:guid}/business-admin")]
    public async Task<IActionResult> AssignBusinessAdmin(Guid siteId, [FromBody] AssignBusinessAdminRequest req, CancellationToken ct)
        => Ok(await _users.AssignBusinessAdminAsync(siteId, req.Email, req.DisplayName ?? "", _current.UserId ?? Guid.Empty, ct));

    /// <summary>List all business admins for a site.</summary>
    [HttpGet("{siteId:guid}/business-admins")]
    public async Task<IActionResult> ListBusinessAdmins(Guid siteId, CancellationToken ct)
        => Ok(await _users.ListBusinessAdminsAsync(siteId, ct));

    /// <summary>Revoke business admin role from a user.</summary>
    [HttpDelete("{siteId:guid}/business-admin/{userId:guid}")]
    public async Task<IActionResult> RevokeBusinessAdmin(Guid siteId, Guid userId, CancellationToken ct)
    {
        await _users.RevokeBusinessAdminAsync(siteId, userId, _current.UserId ?? Guid.Empty, ct);
        return Ok(new { success = true });
    }

    // ─── LDAP Config ──────────────────────────────────────────────────

    [HttpGet("{siteId:guid}/ldap-config")]
    public async Task<IActionResult> GetLdapConfig(Guid siteId, CancellationToken ct)
        => Ok(await _sites.GetLdapConfigAsync(siteId, ct));

    [HttpPut("{siteId:guid}/ldap-config")]
    public async Task<IActionResult> UpsertLdapConfig(Guid siteId, [FromBody] LdapConfigUpsertDto dto, CancellationToken ct)
        => Ok(await _sites.UpsertLdapConfigAsync(siteId, dto, _current.UserId ?? Guid.Empty, ct));

    [HttpPost("ldap-test")]
    public async Task<IActionResult> TestLdap([FromBody] LdapTestRequest req, CancellationToken ct)
        => Ok(await _sites.TestLdapAsync(req, ct));

    // ─── Theme ────────────────────────────────────────────────────────

    [HttpGet("{siteId:guid}/theme")]
    public async Task<IActionResult> GetTheme(Guid siteId, CancellationToken ct)
        => Ok(await _sites.GetThemeAsync(siteId, ct));

    [HttpPut("{siteId:guid}/theme")]
    public async Task<IActionResult> UpsertTheme(Guid siteId, [FromBody] ThemeUpsertDto dto, CancellationToken ct)
        => Ok(await _sites.UpsertThemeAsync(siteId, dto, _current.UserId ?? Guid.Empty, ct));
}

/// <summary>Request body for assigning a business admin.</summary>
public sealed record AssignBusinessAdminRequest(string Email, string? DisplayName);

