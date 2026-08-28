using Microsoft.AspNetCore.Mvc;
using Syntera.Api.Controllers;
using Syntera.Application.DTOs.Roles;
using Syntera.Application.Interfaces.Services;
using Syntera.Application.Services;
using Syntera.Infrastructure.Authorization;

namespace Syntera.Api.Controllers.Platform;

[ApiController]
[Route("api/platform/role-templates")]
public sealed class RoleTemplatesController : ApiControllerBase
{
    private readonly IRoleTemplateService _svc;
    private readonly ICurrentUserService _current;

    public RoleTemplatesController(IRoleTemplateService svc, ICurrentUserService current)
    {
        _svc = svc;
        _current = current;
    }

    [HttpGet]
    [PlatformAdminOnly]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _svc.ListAsync(ct));

    [HttpGet("{id:guid}")]
    [PlatformAdminOnly]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _svc.GetAsync(id, ct));

    [HttpPost]
    [PlatformAdminOnly]
    public async Task<IActionResult> Create([FromBody] RoleTemplateUpsertDto dto, CancellationToken ct)
        => Ok(await _svc.CreateAsync(dto, _current.UserId ?? Guid.Empty, ct));

    [HttpPut("{id:guid}")]
    [PlatformAdminOnly]
    public async Task<IActionResult> Update(Guid id, [FromBody] RoleTemplateUpsertDto dto, CancellationToken ct)
        => Ok(await _svc.UpdateAsync(id, dto, ct));

    [HttpPost("{id:guid}/publish")]
    [PlatformAdminOnly]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        await _svc.PublishAsync(id, _current.UserId ?? Guid.Empty, ct);
        return Ok(new { success = true });
    }

    [HttpGet("permission-catalog")]
    [PlatformAdminOnly]
    public async Task<IActionResult> PermissionCatalog(CancellationToken ct)
        => Ok(await _svc.GetPermissionCatalogAsync(ct));
}
