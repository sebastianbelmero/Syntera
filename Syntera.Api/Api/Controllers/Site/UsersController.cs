using Microsoft.AspNetCore.Mvc;
using Syntera.Api.Controllers;
using Syntera.Application.DTOs.Users;
using Syntera.Application.Interfaces.Services;
using Syntera.Application.Services;
using Syntera.Infrastructure.Authorization;

namespace Syntera.Api.Controllers.Site;

/// <summary>
/// Site-scoped user management. All operations are scoped to the JWT's
/// site_id claim — a site business admin can never touch users in
/// another site. Requires the site-business-admin flag in the JWT.
/// </summary>
[ApiController]
[Route("api/site/users")]
[SiteBusinessAdmin]
public sealed class UsersController : ApiControllerBase
{
    private readonly IUserManagementService _svc;
    private readonly ICurrentUserService _current;

    public UsersController(IUserManagementService svc, ICurrentUserService current)
    {
        _svc = svc;
        _current = current;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _svc.ListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _svc.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UserUpsertDto dto, CancellationToken ct)
        => Ok(await _svc.CreateAsync(dto, _current.UserId ?? Guid.Empty, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UserUpsertDto dto, CancellationToken ct)
        => Ok(await _svc.UpdateAsync(id, dto, ct));

    [HttpPost("{id:guid}/disable")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        await _svc.DisableAsync(id, _current.UserId ?? Guid.Empty, ct);
        return Ok(new { success = true });
    }

    [HttpPost("assign-role")]
    public async Task<IActionResult> AssignRole([FromBody] AssignRoleDto dto, CancellationToken ct)
        => Ok(await _svc.AssignRoleAsync(dto, _current.UserId ?? Guid.Empty, ct));

    [HttpPost("revoke-role")]
    public async Task<IActionResult> RevokeRole([FromBody] RevokeRoleDto dto, CancellationToken ct)
    {
        await _svc.RevokeRoleAsync(dto, ct);
        return Ok(new { success = true });
    }

    [HttpPost("grant-permission")]
    public async Task<IActionResult> GrantPermission([FromBody] GrantDirectPermissionDto dto, CancellationToken ct)
        => Ok(await _svc.GrantDirectPermissionAsync(dto, _current.UserId ?? Guid.Empty, ct));

    [HttpPost("revoke-permission")]
    public async Task<IActionResult> RevokePermission([FromBody] RevokeDirectPermissionDto dto, CancellationToken ct)
    {
        await _svc.RevokeDirectPermissionAsync(dto, ct);
        return Ok(new { success = true });
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken ct)
        => Ok(await _svc.TriggerSyncAsync(_current.UserId ?? Guid.Empty, ct));
}
