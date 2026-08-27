using Microsoft.AspNetCore.Mvc;
using Syntera.Api.Controllers;
using Syntera.Application.Common;
using Syntera.Application.DTOs.Sites;
using Syntera.Application.Interfaces.Services;
using Syntera.Application.Services;
using Syntera.Infrastructure.Authorization;

namespace Syntera.Api.Controllers.Platform;

[ApiController]
[Route("api/platform/sites")]
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
    [PlatformAdminOnly]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _sites.ListAsync(ct));

    [HttpGet("{id:guid}")]
    [PlatformAdminOnly]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _sites.GetAsync(id, ct));

    [HttpPost]
    [PlatformAdminOnly]
    public async Task<IActionResult> Create([FromBody] SiteUpsertDto dto, CancellationToken ct)
        => Ok(await _sites.CreateAsync(dto, _current.UserId ?? Guid.Empty, ct));

    [HttpPut("{id:guid}")]
    [PlatformAdminOnly]
    public async Task<IActionResult> Update(Guid id, [FromBody] SiteUpsertDto dto, CancellationToken ct)
        => Ok(await _sites.UpdateAsync(id, dto, ct));

    [HttpPost("{id:guid}/disable")]
    [PlatformAdminOnly]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        await _sites.DisableAsync(id, ct);
        return Ok(new { success = true });
    }

    [HttpGet("{siteId:guid}/ldap-config")]
    [PlatformAdminOnly]
    public async Task<IActionResult> GetLdapConfig(Guid siteId, CancellationToken ct)
        => Ok(await _sites.GetLdapConfigAsync(siteId, ct));

    [HttpPut("{siteId:guid}/ldap-config")]
    [PlatformAdminOnly]
    public async Task<IActionResult> UpsertLdapConfig(Guid siteId, [FromBody] LdapConfigUpsertDto dto, CancellationToken ct)
        => Ok(await _sites.UpsertLdapConfigAsync(siteId, dto, ct));

    [HttpPost("ldap-test")]
    [PlatformAdminOnly]
    public async Task<IActionResult> TestLdap([FromBody] LdapTestRequest req, CancellationToken ct)
        => Ok(await _sites.TestLdapAsync(req, ct));

    [HttpGet("{siteId:guid}/theme")]
    [PlatformAdminOnly]
    public async Task<IActionResult> GetTheme(Guid siteId, CancellationToken ct)
        => Ok(await _sites.GetThemeAsync(siteId, ct));

    [HttpPut("{siteId:guid}/theme")]
    [PlatformAdminOnly]
    public async Task<IActionResult> UpsertTheme(Guid siteId, [FromBody] ThemeUpsertDto dto, CancellationToken ct)
        => Ok(await _sites.UpsertThemeAsync(siteId, dto, ct));
}
