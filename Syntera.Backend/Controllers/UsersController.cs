using Microsoft.AspNetCore.Mvc;
using Syntera.Backend.Controllers;
using Syntera.Backend.Models.Dtos.Users;
using Syntera.Backend.Services;
using Syntera.Backend.Authorization;

namespace Syntera.Backend.Controllers;

/// <summary>
/// Site-scoped user management. All operations are scoped to the JWT's
/// site_id claim — a site business admin can never touch users in
/// another site. Each action is individually gated by a permission claim
/// (e.g. <c>user.read</c>, <c>user.write</c>, <c>user_role.assign</c>) so
/// eng-manager / supervisor / qo-manager roles can use endpoints they're
/// authorized for without getting full admin access to the whole controller.
/// </summary>
[ApiController]
[Route("api/site/users")]
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
    [HasPermission("user.read")]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _svc.ListAsync(ct));

    [HttpGet("roles")]
    [HasPermission("role.read")]
    public async Task<IActionResult> ListRoles(CancellationToken ct)
        => Ok(await _svc.ListRolesAsync(ct));

    [HttpGet("{id:guid}")]
    [HasPermission("user.read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _svc.GetAsync(id, ct));

    [HttpPost]
    [HasPermission("user.write")]
    public async Task<IActionResult> Create([FromBody] UserUpsertDto dto, CancellationToken ct)
        => Ok(await _svc.CreateAsync(dto, _current.UserId ?? Guid.Empty, ct));

    [HttpPut("{id:guid}")]
    [HasPermission("user.write")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UserUpsertDto dto, CancellationToken ct)
        => Ok(await _svc.UpdateAsync(id, dto, ct));

    [HttpPost("{id:guid}/disable")]
    [HasPermission("user.disable")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        await _svc.DisableAsync(id, _current.UserId ?? Guid.Empty, ct);
        return Ok(new { success = true });
    }

    [HttpPost("assign-role")]
    [HasPermission("user_role.assign")]
    public async Task<IActionResult> AssignRole([FromBody] AssignRoleDto dto, CancellationToken ct)
        => Ok(await _svc.AssignRoleAsync(dto, _current.UserId ?? Guid.Empty, ct));

    [HttpPost("revoke-role")]
    [HasPermission("user_role.revoke")]
    public async Task<IActionResult> RevokeRole([FromBody] RevokeRoleDto dto, CancellationToken ct)
    {
        await _svc.RevokeRoleAsync(dto, ct);
        return Ok(new { success = true });
    }

    [HttpPost("grant-permission")]
    [HasPermission("permission.grant")]
    public async Task<IActionResult> GrantPermission([FromBody] GrantDirectPermissionDto dto, CancellationToken ct)
        => Ok(await _svc.GrantDirectPermissionAsync(dto, _current.UserId ?? Guid.Empty, ct));

    [HttpPost("revoke-permission")]
    [HasPermission("permission.revoke")]
    public async Task<IActionResult> RevokePermission([FromBody] RevokeDirectPermissionDto dto, CancellationToken ct)
    {
        await _svc.RevokeDirectPermissionAsync(dto, ct);
        return Ok(new { success = true });
    }
}
