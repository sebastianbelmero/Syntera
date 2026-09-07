using Microsoft.AspNetCore.Mvc;
using Syntera.Backend.Controllers;
using Syntera.Backend.Models.Dtos.Roles;
using Syntera.Backend.Services;
using Syntera.Backend.Authorization;

namespace Syntera.Backend.Controllers;

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

    // COMPLIANCE (Sprint 2.7): publish now branches on TwoPerson:Enabled.
    // When the flag is OFF (default), the response shape is identical to
    // pre-Sprint-2.7 ({ success: true }) for backward compatibility. When
    // the flag is ON, the response carries { success, status, approvalId }
    // so the frontend can redirect to the approval review page.
    [HttpPost("{id:guid}/publish")]
    [PlatformAdminOnly]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        var result = await _svc.PublishAsync(id, _current.UserId ?? Guid.Empty, ct);
        if (result.Status == "pending_approval")
            return Ok(new { success = true, status = "pending", approvalId = result.ApprovalId });
        return Ok(new { success = true });
    }

    // ── COMPLIANCE (Sprint 2.7): Two-person approval endpoints ──────────
    // All four endpoints are Platform-Admin-only. The approve path is
    // additionally protected by a self-approval check in the service
    // (requester cannot approve their own request — 21 CFR Part 11
    // §11.10(g) authority checks).

    /// <summary>List all pending publish approvals (review queue).</summary>
    [HttpGet("approvals")]
    [PlatformAdminOnly]
    public async Task<IActionResult> ListApprovals(CancellationToken ct)
        => Ok(await _svc.ListPendingApprovalsAsync(ct));

    /// <summary>Get a single approval (any status) for the review page.</summary>
    [HttpGet("approvals/{approvalId:guid}")]
    [PlatformAdminOnly]
    public async Task<IActionResult> GetApproval(Guid approvalId, CancellationToken ct)
        => Ok(await _svc.GetApprovalAsync(approvalId, ct));

    /// <summary>
    /// Approve a pending publish request. The caller must NOT be the
    /// requester (two-person rule — service enforces). Body carries the
    /// signature meaning per 21 CFR Part 11 §11.50.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [PlatformAdminOnly]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApprovePublishRequest? req, CancellationToken ct)
    {
        // Defense in depth: FluentValidation already runs in the model
        // binding pipeline, but if the body is null (e.g. an empty POST)
        // we still need a friendly 400 instead of an NRE in the service.
        if (req is null || string.IsNullOrWhiteSpace(req.SignatureMeaning))
            return Fail("INVALID_INPUT", "SignatureMeaning is required for approval (21 CFR Part 11 §11.50).");

        await _svc.ApprovePublishAsync(id, _current.UserId ?? Guid.Empty, req.SignatureMeaning, ct);
        return Ok(new { success = true, status = "approved" });
    }

    /// <summary>
    /// Reject (or cancel) a pending publish request. Self-rejection IS
    /// allowed — the requester can withdraw their own request. Body
    /// carries the reason for the audit trail.
    /// </summary>
    [HttpPost("{id:guid}/reject")]
    [PlatformAdminOnly]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectPublishRequest? req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Reason))
            return Fail("INVALID_INPUT", "Reason is required for rejection.");

        await _svc.RejectPublishAsync(id, _current.UserId ?? Guid.Empty, req.Reason, ct);
        return Ok(new { success = true, status = "rejected" });
    }

    [HttpGet("permission-catalog")]
    [PlatformAdminOnly]
    public async Task<IActionResult> PermissionCatalog(CancellationToken ct)
        => Ok(await _svc.GetPermissionCatalogAsync(ct));
}
