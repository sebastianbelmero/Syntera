using Microsoft.AspNetCore.Mvc;
using Syntera.Backend.Controllers;
using Syntera.Backend.Services;
using Syntera.Backend.Authorization;

namespace Syntera.Backend.Controllers;

/// <summary>
/// Audit log query endpoint. Returns platform-wide logs for Platform Admin,
/// site-scoped logs for Site Business Admins (filtered to their site).
/// </summary>
[ApiController]
[Route("api/audit")]
public sealed class AuditLogsController : ApiControllerBase
{
    private readonly IAuditService _audit;

    public AuditLogsController(IAuditService audit)
    {
        _audit = audit;
    }

    [HttpGet("logs")]
    [HasPermission("audit.read")]
    public async Task<IActionResult> Query(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? action,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? outcome,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var q = new AuditQuery(from, to, action, actorUserId, outcome, skip, take);
        return Ok(await _audit.QueryAsync(q, ct));
    }
}
