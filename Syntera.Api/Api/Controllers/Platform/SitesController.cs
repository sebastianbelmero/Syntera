using Microsoft.AspNetCore.Mvc;
using Syntera.Api.Controllers;
using Syntera.Application.Common;
using Syntera.Application.DTOs.Sites;
using Syntera.Application.Interfaces.Services;
using Syntera.Application.Services;
using Syntera.Infrastructure.Authorization;

namespace Syntera.Api.Controllers.Platform;

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
    private readonly ICurrentUserService _current;

    public SitesController(ISiteManagementService sites, ICurrentUserService current)
    {
        _sites = sites;
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
