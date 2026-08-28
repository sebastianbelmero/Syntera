namespace Syntera.Domain.Entities;

/// <summary>
/// Immutable, append-only audit log entry. Once written, NO row in this
/// table may be modified or deleted by any application code path — the
/// SaveChanges pipeline rejects UPDATE/DELETE on this entity type, and
/// direct SQL access is restricted at the DB role level for the app user.
///
/// Each row carries a <see cref="PreviousHash"/> pointing to the previous
/// entry's hash for the same scope, forming a tamper-evident chain.
/// Compliance: CFR Part 11, ISO 27001, SOX — required for pharmaceutical.
///
/// Retention: 10 years by default, configurable via Platform setting
/// <c>Audit:RetentionYears</c>. Archived records move to cold storage
/// (Azure Blob Cool tier) past retention via a monthly job.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>UTC timestamp. Required, immutable.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Site scope. For platform-level actions, this is null.</summary>
    public Guid? SiteId { get; set; }

    /// <summary>Actor user ID (null = anonymous, e.g., failed login).</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Actor email at time of action (denormalized for forensic queries even after user deletion).</summary>
    public string? ActorEmail { get; set; }

    /// <summary>Actor IP address (denormalized).</summary>
    public string? ActorIp { get; set; }

    /// <summary>Actor user agent (denormalized).</summary>
    public string? ActorUserAgent { get; set; }

    /// <summary>Action category, e.g., "auth.login", "user.create", "permission.grant".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Target entity type (e.g., "User", "Role").</summary>
    public string? TargetType { get; set; }

    /// <summary>Target entity ID (e.g., the user being modified).</summary>
    public string? TargetId { get; set; }

    /// <summary>JSON snapshot of before-state (for UPDATE/DELETE actions).</summary>
    public string? BeforeJson { get; set; }

    /// <summary>JSON snapshot of after-state (for CREATE/UPDATE actions).</summary>
    public string? AfterJson { get; set; }

    /// <summary>Outcome: "success" or "failure".</summary>
    public string Outcome { get; set; } = "success";

    /// <summary>Optional error message (for failures).</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>SHA-256 hash of (PreviousHash + canonical JSON of this row). Forms the chain.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>Hash of the previous AuditLog row in the same scope. Empty for the first row.</summary>
    public string PreviousHash { get; set; } = string.Empty;
}

/// <summary>
/// Tracks each LDAP user sync run for a Site. Used by Site Business Admins
/// to verify their LDAP directory was successfully imported before users
/// attempt login (pre-provisioning requirement).
/// </summary>
public class UserSyncHistory : BaseEntity
{
    public Guid SiteId { get; set; }

    /// <summary>Who triggered the sync (FK → User, must be site business admin).</summary>
    public Guid TriggeredBy { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedAt { get; set; }

    /// <summary>"success", "partial", "failed".</summary>
    public string Status { get; set; } = "running";

    /// <summary>Number of users found in LDAP.</summary>
    public int UsersFound { get; set; }

    /// <summary>Number of users created in Syntera.</summary>
    public int UsersCreated { get; set; }

    /// <summary>Number of users updated (e.g., display name change).</summary>
    public int UsersUpdated { get; set; }

    /// <summary>Number of users disabled in Syntera (no longer in LDAP).</summary>
    public int UsersDisabled { get; set; }

    /// <summary>Errors encountered, one per line.</summary>
    public string? Errors { get; set; }
}
