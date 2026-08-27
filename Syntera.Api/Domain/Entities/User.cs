namespace Syntera.Domain.Entities;

/// <summary>
/// End user in a Site's database. Users are always authenticated via their
/// site LDAP — Syntera itself never stores user passwords. A User row is
/// created by the Site Business Admin (pre-provisioning) BEFORE the user
/// attempts their first login. LDAP only confirms identity; Syntera controls
/// authorization.
/// </summary>
public class User : SoftDeletableEntity
{
    /// <summary>Full email address (lowercase). Must match a domain registered in <see cref="SiteLdapDomain"/>.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Display name (synced from LDAP <c>displayName</c> on login if available).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Site this user belongs to (always the site that owns this database).</summary>
    public Guid SiteId { get; set; }

    /// <summary>If false, user cannot log in even if LDAP bind succeeds. Business Admin can disable.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Last successful login time (UTC). Updated by login flow.</summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Last failed login attempt (UTC). Used for rate-limiting at user level.</summary>
    public DateTime? LastFailedLoginAt { get; set; }

    /// <summary>Number of consecutive failed logins since last success.</summary>
    public int FailedLoginCount { get; set; } = 0;

    /// <summary>If set, account is locked until this time (UTC). Auto-unlocks after.</summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>
    /// Monotonically increasing counter. Bumped whenever the user's roles or direct
    /// permissions change. The JWT carries this version; if it doesn't match the
    /// current value on a request, the permission engine re-resolves the effective
    /// permission set and the client must refresh its token.
    /// </summary>
    public long PermissionsVersion { get; set; } = 1;

    // ─── Navigation ──────────────────────────────────────────────────────
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<UserPermission> DirectPermissions { get; set; } = new List<UserPermission>();
}

/// <summary>
/// Many-to-many between User and Role within a Site database.
/// Assigned by the Site Business Admin using roles cloned from the Platform's
/// published role templates.
/// </summary>
public class UserRole : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    /// <summary>Who assigned this role (FK → User, must be a business admin of the same site).</summary>
    public Guid AssignedBy { get; set; }

    /// <summary>If set, role auto-revokes at this time. Null = permanent (until manually revoked).</summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// A role defined within a Site database. Created by cloning a published
/// <see cref="RoleTemplate"/> from the Platform DB. Site Business Admins
/// can ONLY assign existing roles — they cannot create new ones or modify
/// role-permission mappings.
/// </summary>
public class Role : SoftDeletableEntity
{
    /// <summary>Stable key, e.g., "viewer", "site-business-admin".</summary>
    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>If true, this role grants site-business-admin privileges within its site.</summary>
    public bool IsSiteAdminRole { get; set; } = false;

    /// <summary>Origin template ID (Platform DB). For audit traceability.</summary>
    public Guid? OriginTemplateId { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────
    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
    public ICollection<UserRole> UserAssignments { get; set; } = new List<UserRole>();
}

/// <summary>
/// Atomic permission key, e.g., "user.read", "role.assign", "audit.read".
/// Defined at the Platform level and seeded into each Site DB at provisioning.
/// </summary>
public class Permission : BaseEntity
{
    /// <summary>Unique dotted key, e.g., "user.write".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Human-readable label, e.g., "Create / update users".</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Resource group for UI grouping, e.g., "User Management".</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>If true, this permission is reserved for Platform Admin and cannot be granted at site level.</summary>
    public bool IsPlatformOnly { get; set; } = false;
}

/// <summary>Many-to-many between Role and Permission within a Site database.</summary>
public class RolePermission : BaseEntity
{
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}

/// <summary>
/// Direct (ad-hoc) permission grant to a specific user, bypassing the role
/// layer. Used for temporary elevated access. Per compliance requirements,
/// every direct grant MUST have an expiry (max 90 days), a reason, and an
/// approver. Expired grants are auto-revoked by a background job.
/// </summary>
public class UserPermission : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;

    /// <summary>Required: business justification, e.g., "Quarter close - temp access to reports".</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>FK → User (the Site Business Admin who approved this grant).</summary>
    public Guid ApprovedBy { get; set; }

    /// <summary>Required: must be ≤ 90 days from <see cref="CreatedAt"/>. Enforced at save time.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>If true, this is an explicit DENY (overrides any grant). Used for紧急 revocation.</summary>
    public bool IsDeny { get; set; } = false;

    /// <summary>If true, grant was auto-revoked by expiry sweeper (no longer effective).</summary>
    public bool IsRevoked { get; set; } = false;

    public DateTime? RevokedAt { get; set; }

    public Guid? RevokedBy { get; set; }
}
