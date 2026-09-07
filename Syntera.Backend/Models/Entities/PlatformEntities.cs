namespace Syntera.Backend.Models.Entities;

// CA1711: RoleTemplatePermission keeps the suffix intentionally — see User.cs.
#pragma warning disable CA1711

/// <summary>
/// Role template defined at the Platform level by <c>admin@syntera.com</c>.
/// When a new Site is created, all published role templates are cloned into
/// the site database as <see cref="Role"/> rows, and the template's
/// permission keys are mapped to the site's <see cref="Permission"/> rows.
/// Site Business Admins can then assign these roles to users — but they
/// cannot modify the role-permission mapping.
/// </summary>
public class RoleTemplate : BaseEntity
{
    /// <summary>Stable key, e.g., "viewer", "site-business-admin".</summary>
    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>If true, role grants site-business-admin privileges when cloned.</summary>
    public bool IsSiteAdminRole { get; set; }

    /// <summary>If true, template is published and can be cloned into new sites.</summary>
    public bool IsPublished { get; set; }

    /// <summary>Version number; bumped when permissions change. Sites track which version they cloned.</summary>
    public int Version { get; set; } = 1;

    // ─── Navigation ──────────────────────────────────────────────────────
    public ICollection<RoleTemplatePermission> Permissions { get; set; } = new List<RoleTemplatePermission>();
}

/// <summary>Many-to-many between RoleTemplate and a permission key (string, not FK — keys are global constants).</summary>
public class RoleTemplatePermission : BaseEntity
{
    public Guid RoleTemplateId { get; set; }
    public RoleTemplate RoleTemplate { get; set; } = null!;

    /// <summary>Permission key, e.g., "user.read". Resolved to site's Permission row at clone time.</summary>
    public string PermissionKey { get; set; } = string.Empty;
}

/// <summary>
/// Platform-level user record for <c>admin@syntera.com</c> only.
/// Stored in the master database (syntera_master), completely separate
/// from site users. Uses local bcrypt-hashed password (not LDAP) because
/// the Platform Admin must be able to log in even if all site LDAPs are down.
/// </summary>
public class PlatformUser : BaseEntity
{
    /// <summary>Email — must be @syntera.com domain. Currently only admin@syntera.com is allowed.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>BCrypt hash. Never stored or compared in plain text.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public DateTime? LastLoginAt { get; set; }

    public DateTime? LastFailedLoginAt { get; set; }

    public int FailedLoginCount { get; set; }

    public DateTime? LockedUntil { get; set; }

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): TOTP secret (Base32-encoded) for MFA.
    /// Null = MFA not enabled for this user. Stored encrypted via
    /// ASP.NET Core Data Protection (IDataProtector) — see TotpService.
    /// </summary>
    public string? TotpSecret { get; set; }

    /// <summary>
    /// COMPLIANCE (Sprint 2.3): True if MFA (TOTP) has been verified and is
    /// required for login. When true, login flow requires both password AND
    /// a valid TOTP code. Defaults to false (opt-in) for backward compat.
    /// </summary>
    public bool TotpEnabled { get; set; }

    /// <summary>
    /// COMPLIANCE (Sprint 2.4): Timestamp of last successful password
    /// change. Used to enforce password max-age (e.g., 90 days) per
    /// 21 CFR Part 11 §11.300(c). Null = never changed (or pre-feature).
    /// On first login (or migration), seeded to CreatedAt to avoid
    /// forcing immediate password change for existing users.
    /// </summary>
    public DateTime? PasswordChangedAt { get; set; }
}

/// <summary>
/// Server-side tracked refresh token. Each refresh-token string maps to
/// exactly one row in this table; rotation deletes the consumed row and
/// inserts a new one. Logout revokes the row. This is required because
/// JWTs are stateless — without a server-side revocation list, a stolen
/// refresh token could be used until natural expiry.
/// </summary>
public class RefreshToken : BaseEntity
{
    /// <summary>The opaque refresh token string (256-bit random, base64url-encoded).</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Hash of the token (SHA-256). Stored instead of the raw token to defend against DB read.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Owner. For site users, FK → User in the site DB. For platform admin, FK → PlatformUser in master DB.</summary>
    public Guid UserId { get; set; }

    /// <summary>"site" or "platform". Determines which DB to query when validating.</summary>
    public string UserScope { get; set; } = "site";

    /// <summary>If non-null, the SiteId this token belongs to. Null for platform admin tokens.</summary>
    public Guid? SiteId { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public Guid? RevokedBy { get; set; }

    /// <summary>If this token was rotated, the ID of the replacement token. Forms a chain.</summary>
    public Guid? ReplacedById { get; set; }

    /// <summary>
    /// SECURITY (M1): Family ID — all refresh tokens issued from the same
    /// initial login share the same FamilyId. When a refresh token is used
    /// AFTER it has been rotated (i.e., ReplacedById is set), this signals
    /// token theft: the legitimate client rotated to a new token, but an
    /// attacker who stole the old token is also using it. Defense: revoke
    /// the entire family (all tokens with this FamilyId) — both attacker
    /// and legitimate user are forced to re-authenticate.
    ///
    /// For backward compat, nullable. Null = pre-M1 token (treated as its
    /// own family of size 1 — no reuse detection).
    /// </summary>
    public Guid? FamilyId { get; set; }

    /// <summary>IP + User Agent of the client that requested this token, for forensic review.</summary>
    public string? CreatedFromIp { get; set; }

    public string? CreatedUserAgent { get; set; }

    /// <summary>
    /// COMPLIANCE (Sprint 2.5): Timestamp of the last time this refresh
    /// token was used (rotated to a new token, or validated via a refresh
    /// endpoint). Used for idle session timeout enforcement per
    /// 21 CFR Part 11 §11.300(d) — automatic logoff after a period of
    /// inactivity. On refresh, the service checks that
    /// <c>LastUsedAt &gt;= UtcNow - Session:IdleMinutes</c>; otherwise the
    /// session is treated as expired and the refresh is rejected with
    /// <c>SESSION_IDLE_TIMEOUT</c>. Null on first creation; treated as
    /// "just used" so a brand-new token isn't immediately idle-expired.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }
}

/// <summary>
/// COMPLIANCE (Sprint 2.4): Password history entry for Platform Admin.
/// Stores BCrypt hashes of the last N passwords (default 10). When the
/// Platform Admin attempts to change their password, the new password is
/// checked against this history — if it matches any previous hash, the
/// change is rejected with <c>PASSWORD_REUSE_FORBIDDEN</c>. This enforces
/// 21 CFR Part 11 §11.300(c) (periodic password change) without enabling
/// trivial rotation back to the same password.
/// </summary>
public class PasswordHistory : BaseEntity
{
    public Guid PlatformUserId { get; set; }

    /// <summary>BCrypt hash of the historical password. Never store plaintext.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>When this password was set (denormalized from CreatedAt for query convenience).</summary>
    public DateTime SetAt { get; set; }
}

/// <summary>
/// COMPLIANCE (Sprint 2.7): Two-person rule approval request for
/// publishing a role template. When two-person control is enabled
/// (<c>TwoPerson:Enabled=true</c> in config), <c>PublishAsync</c> creates
/// a <c>RoleTemplateApproval</c> row in <c>Pending</c> status instead of
/// immediately publishing. A second Platform Admin (different from the
/// requester) must call <c>POST /api/platform/role-templates/{id}/approve</c>
/// to actually trigger the publish. The requester cannot self-approve.
///
/// If two-person is disabled (default), <c>PublishAsync</c> proceeds
/// immediately as before — the approval workflow is opt-in for
/// regulated environments that require dual control for privilege
/// propagation.
/// </summary>
public class RoleTemplateApproval : BaseEntity
{
    public Guid RoleTemplateId { get; set; }

    /// <summary>The Platform Admin who requested the publish.</summary>
    public Guid RequestedBy { get; set; }

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Snapshot of the requested permission keys + display name + isSiteAdminRole at request time.</summary>
    public string RequestedSnapshotJson { get; set; } = string.Empty;

    /// <summary>Status: "pending", "approved", "rejected", "superseded".</summary>
    public string Status { get; set; } = "pending";

    /// <summary>The Platform Admin who approved/rejected (different from RequestedBy).</summary>
    public Guid? ActionBy { get; set; }

    public DateTime? ActionAt { get; set; }

    /// <summary>Optional reason for rejection.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Signature meaning captured at request time (for audit).</summary>
    public string? RequesterSignatureMeaning { get; set; }

    /// <summary>Signature meaning captured at approval time (for audit).</summary>
    public string? ApproverSignatureMeaning { get; set; }
}

#pragma warning restore CA1711
