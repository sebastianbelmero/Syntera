using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Syntera.Backend.Data;
using Syntera.Backend.Models.Entities;
using Syntera.Backend.Services;

namespace Syntera.Backend.Tests;

/// <summary>
/// Hybrid RBAC resolution: roles + direct grants − expiry checks − explicit
/// denies. Guards the permission engine used by every [HasPermission] gate.
/// </summary>
public sealed class PermissionServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly SiteDbContext _db;
    private readonly PermissionService _svc;

    public PermissionServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _db = new SiteDbContext(new DbContextOptionsBuilder<SiteDbContext>()
            .UseSqlite(_conn)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options);
        _db.Database.EnsureCreated();
        _svc = new PermissionService(new MemoryCache(new MemoryCacheOptions()));
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private async Task<(Guid UserId, Guid RoleId, Guid DirectPermId, Guid DeniedPermId)> SeedAsync()
    {
        var user = new User { Email = "u@test.local", DisplayName = "U", SiteId = Guid.NewGuid(), PermissionsVersion = 1 };
        var role = new Role { Key = "qa-officer", DisplayName = "QA Officer", IsSiteAdminRole = false };
        var roleReadPerm = new Permission { Key = "report.read", DisplayName = "Read reports", Group = "Site Audit" };
        var roleWritePerm = new Permission { Key = "user.write", DisplayName = "Manage users", Group = "User Management (Site)" };
        var directPerm = new Permission { Key = "audit.read", DisplayName = "Read audit", Group = "Site Audit" };

        _db.Users.Add(user);
        _db.Roles.Add(role);
        _db.Permissions.AddRange(roleReadPerm, roleWritePerm, directPerm);
        _db.SaveChanges();

        _db.RolePermissions.AddRange(
            new RolePermission { RoleId = role.Id, PermissionId = roleReadPerm.Id },
            new RolePermission { RoleId = role.Id, PermissionId = roleWritePerm.Id });
        _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, AssignedBy = user.Id });
        _db.UserPermissions.AddRange(
            new UserPermission
            {
                UserId = user.Id, PermissionId = directPerm.Id, Reason = "temp access",
                ApprovedBy = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(5),
            },
            // Explicit DENY of the same permission row the role grants — deny wins.
            new UserPermission
            {
                UserId = user.Id, PermissionId = roleWritePerm.Id, Reason = "emergency revoke",
                ApprovedBy = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(5), IsDeny = true,
            });
        await _db.SaveChangesAsync();

        return (user.Id, role.Id, directPerm.Id, roleWritePerm.Id);
    }

    [Fact]
    public async Task Resolve_RolesPlusDirectGrants_MinusDenies()
    {
        var (userId, _, _, _) = await SeedAsync();

        var (roles, perms) = await _svc.ResolveForUserAsync(_db, userId);

        Assert.Contains("qa-officer", roles);
        Assert.Contains("report.read", perms);
        Assert.Contains("audit.read", perms);
        // user.write granted by role but DENIED directly → deny wins.
        Assert.DoesNotContain("user.write", perms);
    }

    [Fact]
    public async Task Resolve_ExpiredRoleAssignment_Ignored()
    {
        var (userId, roleId, _, _) = await SeedAsync();
        var assignment = await _db.UserRoles.SingleAsync();
        assignment.ExpiresAt = DateTime.UtcNow.AddHours(-1);
        await _db.SaveChangesAsync();

        // Bypass the 5-minute cache with a fresh service instance.
        var fresh = new PermissionService(new MemoryCache(new MemoryCacheOptions()));
        var (roles, perms) = await fresh.ResolveForUserAsync(_db, userId);

        Assert.DoesNotContain("qa-officer", roles);
        Assert.DoesNotContain("report.read", perms);
        // Direct (unexpired) grant still applies.
        Assert.Contains("audit.read", perms);
    }

    [Fact]
    public async Task Resolve_ExpiredDirectGrant_Ignored()
    {
        var (userId, _, _, _) = await SeedAsync();
        var grant = await _db.UserPermissions.SingleAsync(up => up.IsDeny == false);
        grant.ExpiresAt = DateTime.UtcNow.AddHours(-1);
        await _db.SaveChangesAsync();

        var fresh = new PermissionService(new MemoryCache(new MemoryCacheOptions()));
        var (_, perms) = await fresh.ResolveForUserAsync(_db, userId);

        Assert.DoesNotContain("audit.read", perms);
        // Role-sourced permission is unaffected.
        Assert.Contains("report.read", perms);
        // With the deny live and the role grant live, user.write is still denied.
        Assert.DoesNotContain("user.write", perms);
    }

    [Fact]
    public async Task Resolve_UnknownUser_ReturnsEmpty()
    {
        await SeedAsync();
        var (roles, perms) = await _svc.ResolveForUserAsync(_db, Guid.NewGuid());
        Assert.Empty(roles);
        Assert.Empty(perms);
    }

    [Fact]
    public void PlatformAdminPermissions_ContainCorePlatformKeys()
    {
        var perms = _svc.GetPlatformAdminPermissions();
        Assert.Contains("site.create", perms);
        Assert.Contains("role_template.publish", perms);
        Assert.Contains("platform.audit.read", perms);
    }

    [Fact]
    public void Catalog_ListsAllPermissionGroups()
    {
        var catalog = _svc.GetCatalogAsync().GetAwaiter().GetResult();
        Assert.Contains(catalog.Groups, g => g.Group == "Site Management");
        Assert.Contains(catalog.Groups, g => g.Group == "Permission Grants (Site)");
        // Every platform-admin permission must be resolvable in the catalog.
        foreach (var p in _svc.GetPlatformAdminPermissions())
            Assert.Contains(catalog.Groups.SelectMany(g => g.Permissions), d => d.Key == p);
    }
}
