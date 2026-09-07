using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Syntera.Backend.Models.Dtos.Roles;
using Syntera.Backend.Services;
using Syntera.Backend.Models.Entities;
using Syntera.Backend.Models;
using Syntera.Backend.Data;
using System.Text.Json;

namespace Syntera.Backend.Services;

/// <summary>
/// Manages role templates (defined by Platform Admin). When a template is
/// published, it is cloned into every enabled site's database as a Role
/// with corresponding RolePermissions. Existing roles with the same key
/// are updated in-place (their permission set is replaced, version bumped).
/// </summary>
public interface IRoleTemplateService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default);
    Task<RoleDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<RoleDto> CreateAsync(RoleTemplateUpsertDto dto, Guid createdBy, CancellationToken ct = default);
    Task<RoleDto> UpdateAsync(Guid id, RoleTemplateUpsertDto dto, CancellationToken ct = default);

    /// <summary>
    /// Publish a role template. When two-person control is enabled
    /// (<c>TwoPerson:Enabled=true</c> AND <c>TwoPerson:ApplyToPublish=true</c>),
    /// this creates a <c>pending</c> <see cref="RoleTemplateApproval"/> row
    /// instead of publishing immediately — the result's <c>Status</c> is
    /// <c>"pending_approval"</c> and <c>ApprovalId</c> is set. Otherwise the
    /// publish completes immediately and <c>Status</c> is <c>"published"</c>.
    /// Either path is audit-logged via <see cref="IAuditService.LogCriticalAsync"/>.
    /// </summary>
    Task<PublishResultDto> PublishAsync(Guid id, Guid publishedBy, CancellationToken ct = default);

    /// <summary>
    /// COMPLIANCE (Sprint 2.7): Approve a pending publish request (two-person
    /// rule). The caller must be a Platform Admin OTHER than the requester
    /// (21 CFR Part 11 §11.10(g) authority checks). Performs the actual
    /// publish (cloning to all enabled sites) and marks the approval
    /// <c>approved</c>. Audit-logged as <c>role_template.publish_approved</c>.
    /// </summary>
    Task ApprovePublishAsync(Guid roleTemplateId, Guid approvedBy, string signatureMeaning, CancellationToken ct = default);

    /// <summary>
    /// COMPLIANCE (Sprint 2.7): Reject a pending publish request. Self-rejection
    /// is allowed (the requester can cancel their own request). Marks the
    /// approval <c>rejected</c> with a reason. Audit-logged as
    /// <c>role_template.publish_rejected</c>.
    /// </summary>
    Task RejectPublishAsync(Guid roleTemplateId, Guid rejectedBy, string reason, CancellationToken ct = default);

    /// <summary>
    /// COMPLIANCE (Sprint 2.7): List all pending approvals (denormalized with
    /// role template key/display name + requester email). Ordered by
    /// <c>RequestedAt</c> descending. Only callable by Platform Admin
    /// (controller-level <c>[PlatformAdminOnly]</c>).
    /// </summary>
    Task<IReadOnlyList<RoleTemplateApprovalDto>> ListPendingApprovalsAsync(CancellationToken ct = default);

    /// <summary>
    /// COMPLIANCE (Sprint 2.7): Get a single approval (any status) by ID.
    /// Used by the approval review page. Throws <see cref="NotFoundException"/>
    /// if the approval row does not exist.
    /// </summary>
    Task<RoleTemplateApprovalDto> GetApprovalAsync(Guid approvalId, CancellationToken ct = default);

    Task<PermissionCatalogDto> GetPermissionCatalogAsync(CancellationToken ct = default);
}

/// <summary>
/// Publish outcome stats returned by <see cref="ExecutePublishAsync"/> so
/// the caller can build an accurate audit entry (SitesClonedTo / SitesFailed
/// counts). Kept private to the service — not part of the public contract.
/// </summary>
internal sealed record PublishStats(int SitesTotal, int SitesClonedTo, int SitesFailed);

public sealed partial class RoleTemplateService : IRoleTemplateService
{
    private readonly PlatformDbContext _db;
    private readonly ISiteDbContextFactory _siteDbFactory;
    private readonly IAuditService _audit;
    private readonly ILogger<RoleTemplateService> _log;
    // COMPLIANCE (Sprint 2.7): reads TwoPerson:Enabled / TwoPerson:ApplyToPublish
    // at request time so a config reload via reloadOnChange:true flips behavior
    // without an app restart. Mirrors the AuthService IConfiguration pattern.
    private readonly IConfiguration _config;

    // COMPLIANCE (Sprint 2.7): default signature meanings — captured at
    // request and approval time so the audit trail records the meaning of
    // each electronic signature per 21 CFR Part 11 §11.50.
    internal const string RequesterSignatureMeaningDefault =
        "I request approval to publish this role template (two-person rule per 21 CFR Part 11 §11.10).";
    internal const string PublishApprovedSignatureMeaningDefault =
        "Two-person approval: publish request approved (21 CFR Part 11 §11.10(g) authority checks).";
    internal const string PublishRejectedSignatureMeaningDefault =
        "Two-person approval: publish request rejected (21 CFR Part 11 §11.10(g) authority checks).";

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Failed to clone role template {TemplateKey} to site {SiteCode}")]
    private partial void LogCloneFailure(Exception exception, string templateKey, string siteCode);

    public RoleTemplateService(
        PlatformDbContext db,
        ISiteDbContextFactory siteDbFactory,
        IAuditService audit,
        ILogger<RoleTemplateService> log,
        IConfiguration config)
    {
        _db = db;
        _siteDbFactory = siteDbFactory;
        _audit = audit;
        _log = log;
        _config = config;
    }

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default)
    {
        var templates = await _db.RoleTemplates.AsNoTracking()
            .Include(t => t.Permissions)
            .OrderBy(t => t.Key)
            .ToListAsync(ct);
        return templates.Select(Map).ToList();
    }

    public async Task<RoleDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var t = await _db.RoleTemplates.AsNoTracking()
            .Include(x => x.Permissions)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("RoleTemplate", id);
        return Map(t);
    }

    public async Task<RoleDto> CreateAsync(RoleTemplateUpsertDto dto, Guid createdBy, CancellationToken ct = default)
    {
        if (await _db.RoleTemplates.AnyAsync(t => t.Key == dto.Key, ct))
            throw new BusinessRuleException("KEY_TAKEN", $"Role template key '{dto.Key}' is already in use.");

        var template = new RoleTemplate
        {
            Key = dto.Key,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            IsSiteAdminRole = dto.IsSiteAdminRole,
            IsPublished = false,
            Version = 1,
        };

        foreach (var k in dto.PermissionKeys.Distinct())
            template.Permissions.Add(new RoleTemplatePermission { PermissionKey = k });

        _db.RoleTemplates.Add(template);
        await _db.SaveChangesAsync(ct);

        await _audit.LogCriticalAsync(new AuditEntry(
            SiteId: null, ActorUserId: createdBy, ActorEmail: null,
            ActorIp: null, ActorUserAgent: null,
            Action: "role_template.create", TargetType: "RoleTemplate", TargetId: template.Id.ToString(),
            Outcome: "success",
            AfterJson: JsonSerializer.Serialize(new { template.Key, template.DisplayName, template.IsSiteAdminRole, PermissionKeys = template.Permissions.Select(p => p.PermissionKey).ToList() }),
            SignatureMeaning: "Role template creation (privilege definition)."), ct);

        return Map(template);
    }

    public async Task<RoleDto> UpdateAsync(Guid id, RoleTemplateUpsertDto dto, CancellationToken ct = default)
    {
        // COMPLIANCE (Sprint 1.4): capture BEFORE state for audit. Previously,
        // role_template.update had NO audit entry — a Platform Admin could
        // change a published template's permissions (which propagate to ALL
        // sites on next publish) with NO audit trail. This violated 21 CFR
        // Part 11 §11.10(e) + §11.10(j) and was a privilege-escalation risk.
        var beforeTemplate = await _db.RoleTemplates
            .AsNoTracking()
            .Include(t => t.Permissions)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (beforeTemplate is null)
            throw new NotFoundException("RoleTemplate", id);

        var beforeJson = JsonSerializer.Serialize(new
        {
            beforeTemplate.Key,
            beforeTemplate.DisplayName,
            beforeTemplate.Description,
            beforeTemplate.IsSiteAdminRole,
            beforeTemplate.IsPublished,
            beforeTemplate.Version,
            PermissionKeys = beforeTemplate.Permissions.Select(p => p.PermissionKey).ToList(),
        });

        // ─── 100% raw SQL, zero EF tracking ───────────────────────────
        // Previous EF-based approaches threw DbUpdateConcurrencyException
        // due to change-tracker state conflicts. Raw SQL with a transaction
        // is bulletproof and atomic.
        using var tx = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            // 1. UPDATE the role template row.
            var updated = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE RoleTemplates
                SET [Key] = {dto.Key},
                    DisplayName = {dto.DisplayName},
                    Description = {dto.Description ?? (string?)null},
                    IsSiteAdminRole = {dto.IsSiteAdminRole},
                    UpdatedAt = {DateTime.UtcNow}
                WHERE Id = {id}", ct);

            if (updated == 0)
                throw new NotFoundException("RoleTemplate", id);

            // 2. DELETE all existing permission rows.
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM RoleTemplatePermissions
                WHERE RoleTemplateId = {id}", ct);

            // 3. INSERT new permission rows.
            var now = DateTime.UtcNow;
            foreach (var k in dto.PermissionKeys.Distinct())
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO RoleTemplatePermissions (Id, RoleTemplateId, PermissionKey, CreatedAt, UpdatedAt)
                    VALUES ({Guid.NewGuid()}, {id}, {k}, {now}, {now})", ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // Reload for the response DTO (read-only, no tracking).
        var result = await _db.RoleTemplates
            .AsNoTracking()
            .Include(x => x.Permissions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        var afterJson = JsonSerializer.Serialize(new
        {
            result!.Key,
            result.DisplayName,
            result.Description,
            result.IsSiteAdminRole,
            result.IsPublished,
            result.Version,
            PermissionKeys = result.Permissions.Select(p => p.PermissionKey).ToList(),
        });

        // Critical audit: failure to record = treated as failed operation.
        await _audit.LogCriticalAsync(new AuditEntry(
            SiteId: null, ActorUserId: null, ActorEmail: null,
            ActorIp: null, ActorUserAgent: null,
            Action: "role_template.update", TargetType: "RoleTemplate", TargetId: id.ToString(),
            Outcome: "success",
            BeforeJson: beforeJson,
            AfterJson: afterJson,
            SignatureMeaning: "Role template update — affects permissions propagated to all sites on next publish (21 CFR Part 11 §11.10(e))."), ct);

        return Map(result!);
    }

    // ── COMPLIANCE (Sprint 2.7): Publish — two-person rule ──────────────

    public async Task<PublishResultDto> PublishAsync(Guid id, Guid publishedBy, CancellationToken ct = default)
    {
        var twoPersonEnabled = _config.GetValue<bool>("TwoPerson:Enabled");
        var applyToPublish = _config.GetValue<bool>("TwoPerson:ApplyToPublish", true);

        if (twoPersonEnabled && applyToPublish)
        {
            // Two-person rule: create an approval request instead of
            // publishing immediately. The actual publish happens when a
            // second Platform Admin calls ApprovePublishAsync.
            var approvalId = await CreateApprovalRequestAsync(id, publishedBy, ct);
            return new PublishResultDto(Status: "pending_approval", ApprovalId: approvalId);
        }

        // Existing flow: publish immediately. Behavior is IDENTICAL to the
        // pre-Sprint-2.7 implementation when TwoPerson:Enabled=false (default).
        var template = await LoadTemplateForPublishAsync(id, ct);
        var stats = await ExecutePublishAsync(template, ct);

        await _audit.LogCriticalAsync(new AuditEntry(
            SiteId: null, ActorUserId: publishedBy, ActorEmail: null,
            ActorIp: null, ActorUserAgent: null,
            Action: "role_template.publish", TargetType: "RoleTemplate", TargetId: template.Id.ToString(),
            Outcome: "success",
            BeforeJson: JsonSerializer.Serialize(new { template.Key, template.Version, SitesClonedTo = stats.SitesClonedTo }),
            AfterJson: JsonSerializer.Serialize(new { template.Key, template.Version, SitesClonedTo = stats.SitesClonedTo, SitesFailed = stats.SitesFailed }),
            SignatureMeaning: "Role template publication — propagates permissions to all enabled sites (21 CFR Part 11 §11.10(j) — actions of any person shall be recorded)."), ct);

        return new PublishResultDto(Status: "published", ApprovalId: null);
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.7): Creates a <c>pending</c> approval row instead
    /// of publishing. Snapshot the template state at request time so the
    /// reviewer can detect if the requester edits the template between
    /// request and approval (the snapshot is what gets audited, not the
    /// live template state at approval time — this defends against a
    /// last-minute privilege escalation where the requester changes the
    /// permission set after a reviewer has verbally agreed to sign off).
    /// </summary>
    private async Task<Guid> CreateApprovalRequestAsync(Guid id, Guid requestedBy, CancellationToken ct)
    {
        var template = await LoadTemplateForPublishAsync(id, ct);

        // Defense in depth: if there is already a pending approval for this
        // template, refuse a new one. The reviewer should act on the existing
        // request (approve or reject) before a new one is created. This
        // prevents an attacker (or a confused operator) from drowning the
        // review queue with duplicate requests for the same template.
        var pendingExists = await _db.RoleTemplateApprovals
            .AsNoTracking()
            .AnyAsync(a => a.RoleTemplateId == id && a.Status == "pending", ct);
        if (pendingExists)
            throw new BusinessRuleException("APPROVAL_PENDING",
                "There is already a pending approval request for this template.");

        // Snapshot the template state — this is what the reviewer sees and
        // what gets audited. The actual publish at approval time uses the
        // CURRENT template state (so an in-flight edit IS picked up), but the
        // snapshot stays as the request-time record for forensic comparison.
        var snapshot = new
        {
            template.Key,
            template.DisplayName,
            template.Description,
            template.IsSiteAdminRole,
            template.Version,
            PermissionKeys = template.Permissions.Select(p => p.PermissionKey).ToList(),
        };
        var snapshotJson = JsonSerializer.Serialize(snapshot);

        var approval = new RoleTemplateApproval
        {
            RoleTemplateId = id,
            RequestedBy = requestedBy,
            RequestedAt = DateTime.UtcNow,
            RequestedSnapshotJson = snapshotJson,
            Status = "pending",
            RequesterSignatureMeaning = RequesterSignatureMeaningDefault,
        };
        _db.RoleTemplateApprovals.Add(approval);
        await _db.SaveChangesAsync(ct);

        // Critical audit: a failed audit write rolls back the business
        // operation (21 CFR Part 11 §11.10(e) — reliable audit trails).
        await _audit.LogCriticalAsync(new AuditEntry(
            SiteId: null, ActorUserId: requestedBy, ActorEmail: null,
            ActorIp: null, ActorUserAgent: null,
            Action: "role_template.publish_requested", TargetType: "RoleTemplate", TargetId: id.ToString(),
            Outcome: "success",
            AfterJson: snapshotJson,
            SignatureMeaning: approval.RequesterSignatureMeaning), ct);

        return approval.Id;
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.7): Approve a pending publish request. The
    /// approver must be a different Platform Admin from the requester
    /// (separation of duties — 21 CFR Part 11 §11.10(g)). Performs the
    /// actual publish (cloning to all enabled sites) using the CURRENT
    /// template state, then marks the approval <c>approved</c> and emits a
    /// critical audit entry carrying both signatures.
    /// </summary>
    public async Task ApprovePublishAsync(Guid id, Guid approvedBy, string signatureMeaning, CancellationToken ct = default)
    {
        var approval = await _db.RoleTemplateApprovals
            .FirstOrDefaultAsync(a => a.RoleTemplateId == id && a.Status == "pending", ct)
            ?? throw new NotFoundException("RoleTemplateApproval", $"pending for {id}");

        // SECURITY: the requester cannot approve their own request. This is
        // the core two-person-rule invariant — without it, a single
        // compromised Platform Admin could publish any template. The check
        // is on RequestedBy (the original requester), not on the template's
        // current state, so a re-request after a rejection is still safe.
        if (approval.RequestedBy == approvedBy)
            throw new BusinessRuleException("SELF_APPROVAL_FORBIDDEN",
                "You cannot approve your own publish request (two-person rule). Ask another Platform Admin to review.");

        // Load the current template state (NOT the snapshot — the snapshot
        // is the request-time record; the publish uses whatever the
        // template looks like now, so a last-minute edit by the requester
        // IS picked up and IS audited via publish_approved.AfterJson).
        var template = await LoadTemplateForPublishAsync(id, ct);
        var stats = await ExecutePublishAsync(template, ct);

        // Mark the approval approved. The requester's signature meaning is
        // already on the row (set at request time); we now stamp the
        // approver's signature meaning for the audit trail.
        approval.Status = "approved";
        approval.ActionBy = approvedBy;
        approval.ActionAt = DateTime.UtcNow;
        approval.ApproverSignatureMeaning = signatureMeaning;
        await _db.SaveChangesAsync(ct);

        await _audit.LogCriticalAsync(new AuditEntry(
            SiteId: null, ActorUserId: approvedBy, ActorEmail: null,
            ActorIp: null, ActorUserAgent: null,
            Action: "role_template.publish_approved", TargetType: "RoleTemplate", TargetId: id.ToString(),
            Outcome: "success",
            AfterJson: JsonSerializer.Serialize(new
            {
                template.Key,
                template.Version,
                SitesClonedTo = stats.SitesClonedTo,
                SitesFailed = stats.SitesFailed,
                RequestedBy = approval.RequestedBy,
                ApprovedBy = approvedBy,
                RequesterSignatureMeaning = approval.RequesterSignatureMeaning,
                ApproverSignatureMeaning = signatureMeaning,
            }),
            SignatureMeaning: PublishApprovedSignatureMeaningDefault), ct);
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.7): Reject (or cancel) a pending publish
    /// request. Self-rejection IS allowed — the requester can withdraw
    /// their own request. Marks the approval <c>rejected</c> with a reason.
    /// The actual publish does NOT happen.
    /// </summary>
    public async Task RejectPublishAsync(Guid id, Guid rejectedBy, string reason, CancellationToken ct = default)
    {
        var approval = await _db.RoleTemplateApprovals
            .FirstOrDefaultAsync(a => a.RoleTemplateId == id && a.Status == "pending", ct)
            ?? throw new NotFoundException("RoleTemplateApproval", $"pending for {id}");

        approval.Status = "rejected";
        approval.ActionBy = rejectedBy;
        approval.ActionAt = DateTime.UtcNow;
        approval.RejectionReason = reason;
        await _db.SaveChangesAsync(ct);

        await _audit.LogCriticalAsync(new AuditEntry(
            SiteId: null, ActorUserId: rejectedBy, ActorEmail: null,
            ActorIp: null, ActorUserAgent: null,
            Action: "role_template.publish_rejected", TargetType: "RoleTemplate", TargetId: id.ToString(),
            Outcome: "success",
            AfterJson: JsonSerializer.Serialize(new
            {
                RequestedBy = approval.RequestedBy,
                RejectedBy = rejectedBy,
                Reason = reason,
            }),
            SignatureMeaning: PublishRejectedSignatureMeaningDefault), ct);
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.7): List all pending approvals, joined with the
    /// role template (for key/display name) and the requester's platform
    /// user row (for email). Ordered by <see cref="RoleTemplateApproval.RequestedAt"/>
    /// descending so the most recent requests surface first. Returns a
    /// denormalized DTO so the frontend review queue can render without a
    /// second round-trip per row.
    /// </summary>
    public async Task<IReadOnlyList<RoleTemplateApprovalDto>> ListPendingApprovalsAsync(CancellationToken ct = default)
    {
        // Left join on PlatformUsers so a requester that has been deleted
        // (Defensive — PlatformUsers are not soft-deleted today) still
        // surfaces in the queue with an empty email rather than dropping.
        var rows = await (
            from a in _db.RoleTemplateApprovals.AsNoTracking()
            where a.Status == "pending"
            join t in _db.RoleTemplates on a.RoleTemplateId equals t.Id
            join u in _db.PlatformUsers on a.RequestedBy equals u.Id into requesters
            from r in requesters.DefaultIfEmpty()
            orderby a.RequestedAt descending
            select new
            {
                Approval = a,
                TemplateKey = t.Key,
                TemplateDisplayName = t.DisplayName,
                RequestedByEmail = r != null ? r.Email : string.Empty,
            }).ToListAsync(ct);

        return rows.Select(row => new RoleTemplateApprovalDto(
            Id: row.Approval.Id,
            RoleTemplateId: row.Approval.RoleTemplateId,
            RoleTemplateKey: row.TemplateKey,
            RoleTemplateDisplayName: row.TemplateDisplayName,
            RequestedBy: row.Approval.RequestedBy,
            RequestedByEmail: row.RequestedByEmail,
            RequestedAt: row.Approval.RequestedAt,
            RequestedSnapshotJson: row.Approval.RequestedSnapshotJson,
            Status: row.Approval.Status,
            ActionBy: row.Approval.ActionBy,
            ActionByEmail: null, // pending → no action yet
            ActionAt: row.Approval.ActionAt,
            RejectionReason: row.Approval.RejectionReason,
            RequesterSignatureMeaning: row.Approval.RequesterSignatureMeaning,
            ApproverSignatureMeaning: row.Approval.ApproverSignatureMeaning)).ToList();
    }

    /// <summary>
    /// COMPLIANCE (Sprint 2.7): Get a single approval by ID (any status).
    /// Joins the role template (for key/display name), the requester
    /// (for email), AND the approver (for email, when the row has been
    /// actioned). Used by the approval review page to render the snapshot
    /// and the request/approve/reject metadata.
    /// </summary>
    public async Task<RoleTemplateApprovalDto> GetApprovalAsync(Guid approvalId, CancellationToken ct = default)
    {
        var row = await (
            from a in _db.RoleTemplateApprovals.AsNoTracking()
            where a.Id == approvalId
            join t in _db.RoleTemplates on a.RoleTemplateId equals t.Id
            join uReq in _db.PlatformUsers on a.RequestedBy equals uReq.Id into requesters
            from r in requesters.DefaultIfEmpty()
            join uAct in _db.PlatformUsers on a.ActionBy equals uAct.Id into actors
            from act in actors.DefaultIfEmpty()
            select new
            {
                Approval = a,
                TemplateKey = t.Key,
                TemplateDisplayName = t.DisplayName,
                RequestedByEmail = r != null ? r.Email : string.Empty,
                ActionByEmail = act != null ? act.Email : null,
            }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("RoleTemplateApproval", approvalId);

        return new RoleTemplateApprovalDto(
            Id: row.Approval.Id,
            RoleTemplateId: row.Approval.RoleTemplateId,
            RoleTemplateKey: row.TemplateKey,
            RoleTemplateDisplayName: row.TemplateDisplayName,
            RequestedBy: row.Approval.RequestedBy,
            RequestedByEmail: row.RequestedByEmail,
            RequestedAt: row.Approval.RequestedAt,
            RequestedSnapshotJson: row.Approval.RequestedSnapshotJson,
            Status: row.Approval.Status,
            ActionBy: row.Approval.ActionBy,
            ActionByEmail: row.ActionByEmail,
            ActionAt: row.Approval.ActionAt,
            RejectionReason: row.Approval.RejectionReason,
            RequesterSignatureMeaning: row.Approval.RequesterSignatureMeaning,
            ApproverSignatureMeaning: row.Approval.ApproverSignatureMeaning);
    }

    // ── Publish helpers (shared by the direct + two-person paths) ──────

    /// <summary>
    /// Loads the template with its permissions, tracked, ready for the
    /// IsPublished/Version bump in <see cref="ExecutePublishAsync"/>.
    /// </summary>
    private async Task<RoleTemplate> LoadTemplateForPublishAsync(Guid id, CancellationToken ct)
        => await _db.RoleTemplates
            .Include(t => t.Permissions)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("RoleTemplate", id);

    /// <summary>
    /// Performs the actual publish: marks the template IsPublished=true,
    /// bumps the Version, and clones into every enabled site. Audit is
    /// intentionally NOT emitted here — the caller (PublishAsync or
    /// ApprovePublishAsync) emits the appropriate audit action
    /// (<c>role_template.publish</c> vs <c>role_template.publish_approved</c>)
    /// so the two paths can carry different signature meanings + payload
    /// shapes without this method needing to know which path called it.
    /// </summary>
    private async Task<PublishStats> ExecutePublishAsync(RoleTemplate template, CancellationToken ct)
    {
        template.IsPublished = true;
        template.Version++;
        await _db.SaveChangesAsync(ct);

        // Clone into every enabled site.
        var sites = await _db.Sites.Where(s => s.IsEnabled).ToListAsync(ct);
        var clonedSites = new List<string>();
        var failedSites = new List<(string Code, string Error)>();

        foreach (var site in sites)
        {
            try
            {
                await CloneTemplateToSiteAsync(template, site, ct);
                clonedSites.Add(site.Code);
            }
            catch (Exception ex)
            {
                LogCloneFailure(ex, template.Key, site.Code);
                failedSites.Add((site.Code, ex.Message));
            }
        }

        // If any site failed, throw with details so the user knows.
        if (failedSites.Count > 0)
        {
            var errors = string.Join("; ", failedSites.Select(f => $"{f.Code}: {f.Error}"));
            throw new BusinessRuleException("PUBLISH_PARTIAL_FAILURE",
                $"Published to {clonedSites.Count}/{sites.Count} sites. Failures: {errors}");
        }

        return new PublishStats(SitesTotal: sites.Count, SitesClonedTo: clonedSites.Count, SitesFailed: failedSites.Count);
    }

    private async Task CloneTemplateToSiteAsync(RoleTemplate template, Site site, CancellationToken ct)
    {
        // CRITICAL: resolve by explicit siteId, NOT from JWT claim.
        // PublishAsync is called by Platform Admin who has no site_id claim.
        var siteDb = await _siteDbFactory.ResolveForSiteAsync(site.Id, ct);

        var role = await siteDb.Roles.FirstOrDefaultAsync(r => r.Key == template.Key, ct);

        if (role is null)
        {
            role = new Role
            {
                Key = template.Key,
                DisplayName = template.DisplayName,
                Description = template.Description,
                IsSiteAdminRole = template.IsSiteAdminRole,
                OriginTemplateId = template.Id,
            };
            siteDb.Roles.Add(role);
            await siteDb.SaveChangesAsync(ct); // Save to get role.Id
        }
        else
        {
            role.DisplayName = template.DisplayName;
            role.Description = template.Description;
            role.IsSiteAdminRole = template.IsSiteAdminRole;
            role.OriginTemplateId = template.Id;
        }

        // Ensure all permission keys exist in the site's Permissions table.
        // Permissions are global constants — if a site DB doesn't have them
        // yet, create them on the fly.
        var desiredKeys = template.Permissions.Select(p => p.PermissionKey).Distinct().ToList();
        var existingPerms = await siteDb.Permissions
            .Where(p => desiredKeys.Contains(p.Key))
            .ToListAsync(ct);
        var existingKeys = existingPerms.Select(p => p.Key).ToHashSet();
        var catalog = PermissionCatalog.Static;

        foreach (var key in desiredKeys)
        {
            if (!existingKeys.Contains(key))
            {
                // Find display info from static catalog.
                var catPerm = catalog.Groups
                    .SelectMany(g => g.Permissions)
                    .FirstOrDefault(p => p.Key == key);
                var group = catPerm != null
                    ? catalog.Groups.First(g => g.Permissions.Contains(catPerm)).Group
                    : "Custom";

                var newPerm = new Permission
                {
                    Key = key,
                    DisplayName = catPerm?.Description ?? key,
                    Group = group,
                    IsPlatformOnly = false,
                };
                siteDb.Permissions.Add(newPerm);
                existingPerms.Add(newPerm);
            }
        }
        await siteDb.SaveChangesAsync(ct);

        // Remove existing role-permission rows (use raw SQL to avoid tracking issues).
        await siteDb.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM RolePermissions WHERE RoleId = {role.Id}", ct);

        // Insert new role-permission rows.
        foreach (var p in existingPerms)
        {
            siteDb.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = p.Id });
        }

        // Bump all users with this role so they get fresh perm resolution.
        var affectedUserIds = await siteDb.UserRoles
            .Where(ur => ur.RoleId == role.Id)
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(ct);
        foreach (var uid in affectedUserIds)
        {
            var u = await siteDb.Users.FirstOrDefaultAsync(x => x.Id == uid, ct);
            if (u is not null) u.PermissionsVersion++;
        }

        await siteDb.SaveChangesAsync(ct);
    }

    public async Task<PermissionCatalogDto> GetPermissionCatalogAsync(CancellationToken ct = default)
    {
        var catalog = PermissionCatalog.Static;
        var groups = catalog.Groups.Select(g => new PermissionGroupDto(
            g.Group,
            g.Permissions.Select(p => new PermissionDto(
                Id: Guid.Empty, // catalog is static, no DB id
                Key: p.Key,
                DisplayName: p.Description,
                Group: g.Group,
                IsPlatformOnly: false)).ToList())).ToList();
        await Task.CompletedTask;
        return new PermissionCatalogDto(groups);
    }

    private static RoleDto Map(RoleTemplate t) => new(
        Id: t.Id, Key: t.Key, DisplayName: t.DisplayName, Description: t.Description,
        IsSiteAdminRole: t.IsSiteAdminRole, IsPublished: t.IsPublished, Version: t.Version,
        PermissionKeys: t.Permissions.Select(p => p.PermissionKey).ToList(),
        CreatedAt: t.CreatedAt, UpdatedAt: t.UpdatedAt);
}
