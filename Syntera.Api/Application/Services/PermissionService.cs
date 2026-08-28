using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Syntera.Application.Interfaces.Services;
using Syntera.Domain.Entities;
using Syntera.Infrastructure.Data;

namespace Syntera.Application.Services;

/// <summary>
/// Resolves the effective permission set for a user, combining:
/// - All permissions from assigned roles (with expiry check)
/// - All direct grants (with expiry check)
/// - Minus any explicit denies
///
/// Caches the result in IMemoryCache with TTL of 5 minutes, invalidated
/// by User.PermissionsVersion bump on role/permission change.
/// </summary>
public interface IPermissionService
{
    Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> ResolveForUserAsync(
        SiteDbContext siteDb, Guid userId, CancellationToken ct = default);

    /// <summary>Static list of permissions granted to Platform Admin (admin@syntera.com).</summary>
    IReadOnlyList<string> GetPlatformAdminPermissions();

    /// <summary>Permission catalog (groups + permissions) for UI display.</summary>
    Task<PermissionCatalog> GetCatalogAsync(CancellationToken ct = default);
}

public sealed class PermissionService : IPermissionService
{
    private static readonly string[] PlatformAdminPermissions =
    {
        "site.create", "site.read", "site.update", "site.disable",
        "ldap.read", "ldap.write", "ldap.test_connection",
        "theme.read", "theme.write",
        "role_template.read", "role_template.write", "role_template.publish",
        "business_admin.assign", "business_admin.revoke",
        "platform.audit.read", "platform.config.read", "platform.config.write",
        "platform_user.read", "platform_user.create", "platform_user.update", "platform_user.disable",
    };

    private readonly IMemoryCache _cache;

    public PermissionService(IMemoryCache cache) => _cache = cache;

    public async Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> ResolveForUserAsync(
        SiteDbContext siteDb, Guid userId, CancellationToken ct = default)
    {
        var cacheKey = $"perm:{userId}:{siteDb.ContextId}";
        if (_cache.TryGetValue<(List<string>, List<string>)>(cacheKey, out var cached))
            return (cached.Item1, cached.Item2);

        var now = DateTime.UtcNow;

        // Load user with roles + direct permissions + permissions.
        var user = await siteDb.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.Permissions)
                        .ThenInclude(rp => rp.Permission)
            .Include(u => u.DirectPermissions)
                .ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
            return (Array.Empty<string>(), Array.Empty<string>());

        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var grants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var denies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ur in user.UserRoles)
        {
            if (ur.Role is null || ur.Role.IsDeleted) continue;
            if (ur.ExpiresAt is not null && ur.ExpiresAt <= now) continue;
            roles.Add(ur.Role.Key);
            foreach (var rp in ur.Role.Permissions)
            {
                if (rp.Permission is null) continue;
                grants.Add(rp.Permission.Key);
            }
        }

        foreach (var up in user.DirectPermissions)
        {
            if (up.Permission is null) continue;
            if (up.IsRevoked) continue;
            if (up.ExpiresAt <= now) continue;
            if (up.IsDeny)
                denies.Add(up.Permission.Key);
            else
                grants.Add(up.Permission.Key);
        }

        // Deny wins.
        foreach (var d in denies)
            grants.Remove(d);

        var rolesList = roles.OrderBy(r => r).ToList();
        var permsList = grants.OrderBy(p => p).ToList();

        _cache.Set(cacheKey, (rolesList, permsList), TimeSpan.FromMinutes(5));
        return (rolesList, permsList);
    }

    public IReadOnlyList<string> GetPlatformAdminPermissions() => PlatformAdminPermissions;

    public async Task<PermissionCatalog> GetCatalogAsync(CancellationToken ct = default)
    {
        // Permissions are static constants in this version. If they ever become
        // data-driven, load from Platform DB. Keeping static makes catalog queries
        // O(1) and avoids N+1 risk.
        await Task.CompletedTask;
        return PermissionCatalog.Static;
    }
}

/// <summary>Static permission catalog. Mirrored in Platform DB seed for UI display.</summary>
public sealed record PermissionCatalog(IReadOnlyList<PermissionGroup> Groups)
{
    public static PermissionCatalog Static { get; } = new(BuildStatic());

    private static List<PermissionGroup> BuildStatic() => new()
    {
        new("Site Management", new[]
        {
            new PermDef("site.create", "Create new site"),
            new PermDef("site.read", "View sites"),
            new PermDef("site.update", "Update site metadata"),
            new PermDef("site.disable", "Disable / enable site"),
        }),
        new("LDAP Configuration", new[]
        {
            new PermDef("ldap.read", "View LDAP config"),
            new PermDef("ldap.write", "Edit LDAP config"),
            new PermDef("ldap.test_connection", "Test LDAP connection"),
        }),
        new("Theme Management", new[]
        {
            new PermDef("theme.read", "View themes"),
            new PermDef("theme.write", "Edit theme palettes"),
        }),
        new("Role Templates", new[]
        {
            new PermDef("role_template.read", "View role templates"),
            new PermDef("role_template.write", "Create / edit role templates"),
            new PermDef("role_template.publish", "Publish role template to sites"),
        }),
        new("Delegation", new[]
        {
            new PermDef("business_admin.assign", "Assign Site Business Admin role"),
            new PermDef("business_admin.revoke", "Revoke Site Business Admin role"),
        }),
        new("Platform Audit", new[]
        {
            new PermDef("platform.audit.read", "Read platform audit logs"),
            new PermDef("platform.config.read", "Read platform settings"),
            new PermDef("platform.config.write", "Modify platform settings"),
        }),
        new("Platform Users", new[]
        {
            new PermDef("platform_user.read", "View platform admins"),
            new PermDef("platform_user.create", "Create platform admin"),
            new PermDef("platform_user.update", "Update platform admin"),
            new PermDef("platform_user.disable", "Disable platform admin"),
        }),
        new("User Management (Site)", new[]
        {
            new PermDef("user.read", "View users in own site"),
            new PermDef("user.write", "Create / update users in own site"),
            new PermDef("user.disable", "Disable users in own site"),
            new PermDef("user.sync", "Trigger LDAP sync"),
        }),
        new("Role Assignment (Site)", new[]
        {
            new PermDef("role.read", "View roles in own site"),
            new PermDef("user_role.assign", "Assign role to user"),
            new PermDef("user_role.revoke", "Revoke role from user"),
        }),
        new("Permission Grants (Site)", new[]
        {
            new PermDef("permission.read", "View direct permission grants"),
            new PermDef("permission.grant", "Grant direct permission (≤90d)"),
            new PermDef("permission.revoke", "Revoke direct permission"),
        }),
        new("Site Audit", new[]
        {
            new PermDef("audit.read", "Read own site audit logs"),
            new PermDef("report.read", "Read site reports"),
        }),
    };
}

public sealed record PermissionGroup(string Group, IReadOnlyList<PermDef> Permissions);
public sealed record PermDef(string Key, string Description);
