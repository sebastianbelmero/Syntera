namespace Syntera.Backend.Models.Dtos.Roles;

public record RoleDto(
    Guid Id,
    string Key,
    string DisplayName,
    string? Description,
    bool IsSiteAdminRole,
    bool IsPublished,
    int Version,
    IReadOnlyList<string> PermissionKeys,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record RoleTemplateUpsertDto(
    string Key,
    string DisplayName,
    string? Description,
    bool IsSiteAdminRole,
    List<string> PermissionKeys);

public record PublishRoleTemplateDto(Guid RoleTemplateId);

public record PermissionDto(
    Guid Id,
    string Key,
    string DisplayName,
    string Group,
    bool IsPlatformOnly);

public record PermissionCatalogDto(
    IReadOnlyList<PermissionGroupDto> Groups);

public record PermissionGroupDto(
    string Group,
    IReadOnlyList<PermissionDto> Permissions);

// ── COMPLIANCE (Sprint 2.7): Two-person approval workflow ──────────────
// These DTOs support the two-person rule for role template publication.
// When TwoPerson:Enabled=true (opt-in, default false), PublishAsync creates
// a pending RoleTemplateApproval instead of publishing immediately. A
// second Platform Admin (different from the requester) must call approve
// to actually trigger the publish, or reject to cancel. Per 21 CFR Part
// 11 §11.10(g) authority checks, the requester cannot self-approve.

/// <summary>
/// Result of <c>PublishAsync</c>. When <c>Status</c> is <c>"pending_approval"</c>,
/// <c>ApprovalId</c> is the ID of the newly created approval row; the caller
/// (frontend) should redirect to the approval review page. When <c>Status</c>
/// is <c>"published"</c>, the publish completed immediately and
/// <c>ApprovalId</c> is null (two-person rule was disabled).
/// </summary>
public sealed record PublishResultDto(
    string Status,
    Guid? ApprovalId);

/// <summary>
/// Denormalized view of a <see cref="Syntera.Backend.Models.Entities.RoleTemplateApproval"/>
/// row, joined with the role template (for key + display name) and the
/// platform users (for the requester's and approver's emails). The snapshot
/// JSON is included verbatim so the reviewer can see exactly what was
/// requested at request time (defends against a requester editing the
/// template between request and approval).
/// </summary>
public sealed record RoleTemplateApprovalDto(
    Guid Id,
    Guid RoleTemplateId,
    string RoleTemplateKey,
    string RoleTemplateDisplayName,
    Guid RequestedBy,
    string RequestedByEmail,
    DateTime RequestedAt,
    string RequestedSnapshotJson,
    string Status,
    Guid? ActionBy,
    string? ActionByEmail,
    DateTime? ActionAt,
    string? RejectionReason,
    string? RequesterSignatureMeaning,
    string? ApproverSignatureMeaning);

/// <summary>
/// Body for <c>POST /api/platform/role-templates/{id}/approve</c>.
/// <see cref="SignatureMeaning"/> captures the meaning the approver associates
/// with their signature (21 CFR Part 11 §11.50 — signature manifestation).
/// Required so the audit trail records WHY the approver signed off.
/// </summary>
public sealed record ApprovePublishRequest(string SignatureMeaning);

/// <summary>
/// Body for <c>POST /api/platform/role-templates/{id}/reject</c>.
/// <see cref="Reason"/> captures the rejection rationale — required so the
/// requester (and the auditor) can see why the publish was blocked.
/// Self-rejection (requester cancelling their own request) is allowed and
/// uses the same endpoint.
/// </summary>
public sealed record RejectPublishRequest(string Reason);
