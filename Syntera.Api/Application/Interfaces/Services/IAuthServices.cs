namespace Syntera.Application.Interfaces.Services;

/// <summary>
/// Reads the authenticated user from the current HttpContext claims.
/// Scoped per-request so each request gets its own instance populated by
/// JWT middleware. Carries site-scope info critical for multi-tenant routing.
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    string? DisplayName { get; }

    /// <summary>Site ID claim (null for Platform Admin).</summary>
    Guid? SiteId { get; }

    /// <summary>Site code claim (null for Platform Admin). Used in audit log denormalization.</summary>
    string? SiteCode { get; }

    /// <summary>"platform" or "site". Determines which DB to use.</summary>
    string Scope { get; }

    /// <summary>Permission version carried in JWT. If it doesn't match the user's current value, re-resolve.</summary>
    long? PermissionsVersion { get; }

    /// <summary>Roles assigned to the current user.</summary>
    IReadOnlyCollection<string> Roles { get; }

    bool IsInRole(string role);

    /// <summary>
    /// True if the current user holds the given permission. The permission
    /// set is resolved from JWT claims (populated at token issuance from
    /// the effective permission set). Re-resolution triggers when the JWT's
    /// permission version is stale.
    /// </summary>
    bool HasPermission(string permissionKey);

    bool IsPlatformAdmin { get; }
    bool IsSiteBusinessAdmin { get; }
}

/// <summary>BCrypt password hasher abstraction (for platform admin credentials only).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
