using Microsoft.EntityFrameworkCore;
using Syntera.Application.DTOs.Roles;
using Syntera.Application.DTOs.Users;
using Syntera.Application.Interfaces.Services;
using Syntera.Domain.Entities;
using Syntera.Domain.Exceptions;
using Syntera.Infrastructure.Data;

namespace Syntera.Application.Services;

/// <summary>
/// Site Business Admin operations: manage users, assign roles, grant direct
/// permissions. All operations are scoped to the current site (extracted
/// from JWT). A site business admin can NEVER touch users in another site.
/// </summary>
public interface IUserManagementService
{
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default);
    Task<UserDto> GetAsync(Guid userId, CancellationToken ct = default);
    Task<UserDto> CreateAsync(UserUpsertDto dto, Guid createdBy, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(Guid userId, UserUpsertDto dto, CancellationToken ct = default);
    Task DisableAsync(Guid userId, Guid disabledBy, CancellationToken ct = default);

    Task<UserDto> AssignRoleAsync(AssignRoleDto dto, Guid assignedBy, CancellationToken ct = default);
    Task RevokeRoleAsync(RevokeRoleDto dto, CancellationToken ct = default);

    Task<UserDto> GrantDirectPermissionAsync(GrantDirectPermissionDto dto, Guid approvedBy, CancellationToken ct = default);
    Task RevokeDirectPermissionAsync(RevokeDirectPermissionDto dto, CancellationToken ct = default);

    /// <summary>
    /// Platform Admin only — bootstrap the first Site Business Admin for a site.
    /// Creates the user (if not exists) and assigns the site-business-admin role.
    /// Used to break the chicken-and-egg problem: a site needs at least one
    /// business admin before any user management can happen via /api/site/users.
    /// </summary>
    Task<UserDto> AssignBusinessAdminAsync(Guid siteId, string email, string displayName, Guid assignedBy, CancellationToken ct = default);

    /// <summary>
    /// Platform Admin → list all business admins for a site.
    /// </summary>
    Task<IReadOnlyList<UserDto>> ListBusinessAdminsAsync(Guid siteId, CancellationToken ct = default);

    /// <summary>
    /// Platform Admin → revoke business admin role from a user.
    /// </summary>
    Task RevokeBusinessAdminAsync(Guid siteId, Guid userId, Guid revokedBy, CancellationToken ct = default);

    /// <summary>List all available roles in this site (auto-clones from templates if missing).</summary>
    Task<IReadOnlyList<SiteRoleDto>> ListRolesAsync(CancellationToken ct = default);
}

public sealed class UserManagementService : IUserManagementService
{
    private readonly PlatformDbContext _platformDb;
    private readonly ISiteDbContextFactory _siteDbFactory;
    private readonly ICurrentUserService _current;
    private readonly IAuditService _audit;
    private readonly ILogger<UserManagementService> _log;

    private const int MaxDirectPermissionDays = 90;

    public UserManagementService(
        PlatformDbContext platformDb,
        ISiteDbContextFactory siteDbFactory,
        ICurrentUserService current,
        IAuditService audit,
        ILogger<UserManagementService> log)
    {
        _platformDb = platformDb;
        _siteDbFactory = siteDbFactory;
        _current = current;
        _audit = audit;
        _log = log;
    }

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default)
    {
        var db = await _siteDbFactory.ResolveAsync(ct);
        var users = await db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.DirectPermissions).ThenInclude(up => up.Permission)
            .OrderBy(u => u.Email)
            .ToListAsync(ct);
        return users.Select(Map).ToList();
    }

    public async Task<UserDto> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var db = await _siteDbFactory.ResolveAsync(ct);
        var user = await db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.DirectPermissions).ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User", userId);
        return Map(user);
    }

    public async Task<UserDto> CreateAsync(UserUpsertDto dto, Guid createdBy, CancellationToken ct = default)
    {
        var db = await _siteDbFactory.ResolveAsync(ct);
        var email = dto.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new BusinessRuleException("EMAIL_TAKEN", "User with this email already exists.");

        var user = new User
        {
            Email = email,
            DisplayName = dto.DisplayName,
            IsEnabled = dto.IsEnabled,
            SiteId = _current.SiteId ?? Guid.Empty,
            PermissionsVersion = 1,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync(new AuditEntry(
            SiteId: _current.SiteId, ActorUserId: createdBy, ActorEmail: _current.Email,
            ActorIp: null, ActorUserAgent: null,
            Action: "user.create", TargetType: "User", TargetId: user.Id.ToString(),
            Outcome: "success", AfterJson: System.Text.Json.JsonSerializer.Serialize(new { user.Email, user.DisplayName })), ct);

        return Map(user);
    }

    public async Task<UserDto> UpdateAsync(Guid userId, UserUpsertDto dto, CancellationToken ct = default)
    {
        // Prevent self-edit — a user cannot modify their own account.
        if (userId == _current.UserId)
            throw new BusinessRuleException("SELF_EDIT_FORBIDDEN",
                "You cannot edit your own user account. Ask another admin to make changes.");

        var db = await _siteDbFactory.ResolveAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User", userId);

        user.DisplayName = dto.DisplayName;
        user.IsEnabled = dto.IsEnabled;
        await db.SaveChangesAsync(ct);
        return Map(user);
    }

    public async Task DisableAsync(Guid userId, Guid disabledBy, CancellationToken ct = default)
    {
        // Prevent self-disable — a user cannot disable their own account.
        if (userId == _current.UserId)
            throw new BusinessRuleException("SELF_DISABLE_FORBIDDEN",
                "You cannot disable your own account. Ask another admin to do this.");

        var db = await _siteDbFactory.ResolveAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User", userId);
        user.IsEnabled = false;
        user.PermissionsVersion++;
        await db.SaveChangesAsync(ct);
        await _audit.LogAsync(new AuditEntry(
            SiteId: _current.SiteId, ActorUserId: disabledBy, ActorEmail: _current.Email,
            ActorIp: null, ActorUserAgent: null,
            Action: "user.disable", TargetType: "User", TargetId: userId.ToString(),
            Outcome: "success"), ct);
    }

    public async Task<UserDto> AssignRoleAsync(AssignRoleDto dto, Guid assignedBy, CancellationToken ct = default)
    {
        // Prevent self-assignment — a user cannot assign roles to themselves.
        if (dto.UserId == _current.UserId)
            throw new BusinessRuleException("SELF_ASSIGN_FORBIDDEN",
                "You cannot assign roles to yourself. Ask another admin to do this.");

        var db = await _siteDbFactory.ResolveAsync(ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId, ct)
            ?? throw new NotFoundException("User", dto.UserId);
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == dto.RoleId, ct)
            ?? throw new NotFoundException("Role", dto.RoleId);

        // Only Platform Admin can assign the site-business-admin role.
        // Site Business Admins can only assign the 6 site roles
        // (viewer, eng-planner, supervisor, technician, eng-manager, qo-manager).
        if (role.Key == "site-business-admin" && !_current.IsPlatformAdmin)
            throw new AuthorizationException("INSUFFICIENT_PRIVILEGE",
                "Only Platform Admin (admin@syntera.com) can assign the site-business-admin role.");

        if (await db.UserRoles.AnyAsync(ur => ur.UserId == dto.UserId && ur.RoleId == dto.RoleId, ct))
            throw new BusinessRuleException("ROLE_ALREADY_ASSIGNED", "User already has this role.");

        if (dto.ExpiresAt is not null && dto.ExpiresAt <= DateTime.UtcNow)
            throw new BusinessRuleException("EXPIRY_IN_PAST", "Expiry must be in the future.");

        db.UserRoles.Add(new UserRole
        {
            UserId = dto.UserId,
            RoleId = dto.RoleId,
            AssignedBy = assignedBy,
            ExpiresAt = dto.ExpiresAt,
        });

        user.PermissionsVersion++;
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync(new AuditEntry(
            SiteId: _current.SiteId, ActorUserId: assignedBy, ActorEmail: _current.Email,
            ActorIp: null, ActorUserAgent: null,
            Action: "user_role.assign", TargetType: "User", TargetId: user.Id.ToString(),
            Outcome: "success",
            AfterJson: System.Text.Json.JsonSerializer.Serialize(new { role.Key, dto.ExpiresAt })), ct);

        return await GetAsync(dto.UserId, ct);
    }

    public async Task RevokeRoleAsync(RevokeRoleDto dto, CancellationToken ct = default)
    {
        // Prevent self-revoke — a user cannot revoke roles from themselves.
        if (dto.UserId == _current.UserId)
            throw new BusinessRuleException("SELF_REVOKE_FORBIDDEN",
                "You cannot revoke roles from yourself. Ask another admin to do this.");

        var db = await _siteDbFactory.ResolveAsync(ct);

        // Check if the role being revoked is site-business-admin — only platform admin can revoke it.
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == dto.RoleId, ct);
        if (role is not null && role.Key == "site-business-admin" && !_current.IsPlatformAdmin)
            throw new AuthorizationException("INSUFFICIENT_PRIVILEGE",
                "Only Platform Admin (admin@syntera.com) can revoke the site-business-admin role.");

        var ur = await db.UserRoles.FirstOrDefaultAsync(x => x.UserId == dto.UserId && x.RoleId == dto.RoleId, ct)
            ?? throw new NotFoundException("UserRole", $"{dto.UserId}/{dto.RoleId}");
        db.UserRoles.Remove(ur);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId, ct);
        if (user is not null) user.PermissionsVersion++;

        await db.SaveChangesAsync(ct);
    }

    public async Task<UserDto> GrantDirectPermissionAsync(GrantDirectPermissionDto dto, Guid approvedBy, CancellationToken ct = default)
    {
        var db = await _siteDbFactory.ResolveAsync(ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId, ct)
            ?? throw new NotFoundException("User", dto.UserId);
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Id == dto.PermissionId, ct)
            ?? throw new NotFoundException("Permission", dto.PermissionId);

        // Enforce max 90-day expiry.
        var maxExpiry = DateTime.UtcNow.AddDays(MaxDirectPermissionDays);
        if (dto.ExpiresAt > maxExpiry)
            throw new BusinessRuleException("EXPIRY_TOO_FAR",
                $"Direct permission expiry cannot exceed {MaxDirectPermissionDays} days from now.");

        if (dto.ExpiresAt <= DateTime.UtcNow)
            throw new BusinessRuleException("EXPIRY_IN_PAST", "Expiry must be in the future.");

        if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Length < 10)
            throw new BusinessRuleException("REASON_REQUIRED",
                "A meaningful reason (min 10 chars) is required for direct permission grants.");

        db.UserPermissions.Add(new UserPermission
        {
            UserId = dto.UserId,
            PermissionId = dto.PermissionId,
            Reason = dto.Reason,
            ApprovedBy = approvedBy,
            ExpiresAt = dto.ExpiresAt,
            IsDeny = dto.IsDeny,
        });

        user.PermissionsVersion++;
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync(new AuditEntry(
            SiteId: _current.SiteId, ActorUserId: approvedBy, ActorEmail: _current.Email,
            ActorIp: null, ActorUserAgent: null,
            Action: "permission.grant", TargetType: "User", TargetId: user.Id.ToString(),
            Outcome: "success",
            AfterJson: System.Text.Json.JsonSerializer.Serialize(new { perm.Key, dto.Reason, dto.ExpiresAt, dto.IsDeny })), ct);

        return await GetAsync(dto.UserId, ct);
    }

    public async Task RevokeDirectPermissionAsync(RevokeDirectPermissionDto dto, CancellationToken ct = default)
    {
        var db = await _siteDbFactory.ResolveAsync(ct);
        var up = await db.UserPermissions.FirstOrDefaultAsync(x => x.Id == dto.UserPermissionId, ct)
            ?? throw new NotFoundException("UserPermission", dto.UserPermissionId);
        up.IsRevoked = true;
        up.RevokedAt = DateTime.UtcNow;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == up.UserId, ct);
        if (user is not null) user.PermissionsVersion++;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Platform Admin → bootstrap the first Site Business Admin for a site.
    /// 1. Resolve the target site DB by siteId (NOT from JWT claim).
    /// 2. Find or create the user row by email.
    /// 3. Find the site-business-admin role (must be published first by Platform Admin).
    /// 4. Assign the role to the user (idempotent — skips if already assigned).
    /// </summary>
    public async Task<UserDto> AssignBusinessAdminAsync(
        Guid siteId, string email, string displayName, Guid assignedBy, CancellationToken ct = default)
    {
        email = email.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
            throw new BusinessRuleException("INVALID_EMAIL", "A valid email is required.");

        // Resolve the target site DB explicitly (Platform Admin has no site_id in JWT).
        var db = await _siteDbFactory.ResolveForSiteAsync(siteId, ct);

        // 1. Find or create the user.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            user = new User
            {
                Email = email,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? email : displayName,
                IsEnabled = true,
                SiteId = siteId,
                PermissionsVersion = 1,
            };
            db.Users.Add(user);
            await _audit.LogAsync(new AuditEntry(
                SiteId: siteId, ActorUserId: assignedBy, ActorEmail: _current.Email,
                ActorIp: null, ActorUserAgent: null,
                Action: "user.create", TargetType: "User", TargetId: email,
                Outcome: "success",
                AfterJson: System.Text.Json.JsonSerializer.Serialize(new { email, displayName })), ct);
        }

        // 2. Find the site-business-admin role (cloned from template during publish).
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Key == "site-business-admin", ct);

        if (role is null)
        {
            // Role not found in site DB — auto-clone it now.
            role = await AutoCloneRoleFromTemplateAsync(db, siteId, "site-business-admin", ct);
        }

        // Also auto-clone the 6 site roles if they don't exist yet.
        var siteRoles = new[] { "viewer", "eng-planner", "supervisor", "technician", "eng-manager", "qo-manager" };
        foreach (var roleKey in siteRoles)
        {
            var existing = await db.Roles.FirstOrDefaultAsync(r => r.Key == roleKey, ct);
            if (existing is null)
            {
                await AutoCloneRoleFromTemplateAsync(db, siteId, roleKey, ct);
            }
        }

        // 3. Assign role if not already assigned (idempotent).
        var existingAssignment = await db.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == user.Id && ur.RoleId == role.Id, ct);

        if (existingAssignment is null)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id,
                AssignedBy = assignedBy,
                ExpiresAt = null, // permanent
            });
            user.PermissionsVersion++;

            await _audit.LogAsync(new AuditEntry(
                SiteId: siteId, ActorUserId: assignedBy, ActorEmail: _current.Email,
                ActorIp: null, ActorUserAgent: null,
                Action: "business_admin.assign", TargetType: "User", TargetId: user.Id.ToString(),
                Outcome: "success",
                AfterJson: System.Text.Json.JsonSerializer.Serialize(new { email, role.Key })), ct);
        }

        await db.SaveChangesAsync(ct);

        // Return fresh user DTO with roles + permissions.
        var fresh = await db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.DirectPermissions).ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Id == user.Id, ct);
        return Map(fresh!);
    }

    private static UserDto Map(User u) => new(
        Id: u.Id, Email: u.Email, DisplayName: u.DisplayName,
        IsEnabled: u.IsEnabled, LastLoginAt: u.LastLoginAt,
        PermissionsVersion: u.PermissionsVersion,
        Roles: u.UserRoles.Select(ur => new RoleAssignmentDto(
            ur.RoleId, ur.Role?.Key ?? "", ur.Role?.DisplayName ?? "",
            ur.AssignedBy, ur.CreatedAt, ur.ExpiresAt)).ToList(),
        DirectPermissions: u.DirectPermissions.Select(up => new DirectPermissionDto(
            up.Id, up.Permission?.Key ?? "", up.Permission?.DisplayName ?? "",
            up.Reason, up.ApprovedBy, "", up.CreatedAt, up.ExpiresAt,
            up.IsDeny, up.IsRevoked)).ToList(),
        CreatedAt: u.CreatedAt, UpdatedAt: u.UpdatedAt);

    /// <summary>
    /// Auto-clones a role from the platform's RoleTemplates into a site DB.
    /// Called when a role is needed but missing from the site DB (e.g.,
    /// because the template was published before the cloning bug was fixed,
    /// or because the site DB was created after the last publish).
    /// </summary>
    /// <summary>
    /// Platform Admin → list all business admins for a site.
    /// Returns users who have the site-business-admin role assigned.
    /// </summary>
    public async Task<IReadOnlyList<UserDto>> ListBusinessAdminsAsync(Guid siteId, CancellationToken ct = default)
    {
        var db = await _siteDbFactory.ResolveForSiteAsync(siteId, ct);

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Key == "site-business-admin", ct);
        if (role is null)
            return Array.Empty<UserDto>();

        var admins = await db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.DirectPermissions).ThenInclude(up => up.Permission)
            .Where(u => u.UserRoles.Any(ur => ur.RoleId == role.Id))
            .OrderBy(u => u.Email)
            .ToListAsync(ct);

        return admins.Select(Map).ToList();
    }

    /// <summary>
    /// Platform Admin → revoke business admin role from a user.
    /// Does NOT delete the user — they may still have other roles or
    /// be a regular viewer. Only removes the site-business-admin role.
    /// </summary>
    public async Task RevokeBusinessAdminAsync(Guid siteId, Guid userId, Guid revokedBy, CancellationToken ct = default)
    {
        var db = await _siteDbFactory.ResolveForSiteAsync(siteId, ct);

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Key == "site-business-admin", ct)
            ?? throw new BusinessRuleException("ROLE_NOT_CLONED",
                "The site-business-admin role does not exist in this site's database.");

        var assignment = await db.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == role.Id, ct)
            ?? throw new NotFoundException("UserRole", $"{userId}/{role.Id}");

        db.UserRoles.Remove(assignment);

        // Bump permissions version so cached perms are invalidated.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null) user.PermissionsVersion++;

        await db.SaveChangesAsync(ct);

        await _audit.LogAsync(new AuditEntry(
            SiteId: siteId, ActorUserId: revokedBy, ActorEmail: _current.Email,
            ActorIp: null, ActorUserAgent: null,
            Action: "business_admin.revoke", TargetType: "User", TargetId: userId.ToString(),
            Outcome: "success"), ct);
    }

    /// <summary>
    /// List all available roles in this site. Auto-clones all 7 role templates
    /// from the platform DB if they don't exist in the site DB yet.
    /// This ensures the role dropdown is always populated when a site admin
    /// opens the user management page.
    /// </summary>
    public async Task<IReadOnlyList<SiteRoleDto>> ListRolesAsync(CancellationToken ct = default)
    {
        var siteId = _current.SiteId
            ?? throw new AuthorizationException("NO_SITE", "ListRoles requires site context.");

        var db = await _siteDbFactory.ResolveForSiteAsync(siteId, ct);

        // Auto-clone all role templates if they don't exist yet.
        var allTemplateKeys = await _platformDb.RoleTemplates
            .Where(t => t.IsPublished)
            .Select(t => t.Key)
            .ToListAsync(ct);

        foreach (var key in allTemplateKeys)
        {
            var exists = await db.Roles.AnyAsync(r => r.Key == key, ct);
            if (!exists)
            {
                await AutoCloneRoleFromTemplateAsync(db, siteId, key, ct);
            }
        }

        // Return all roles in the site DB.
        var roles = await db.Roles.AsNoTracking()
            .OrderBy(r => r.DisplayName)
            .Select(r => new SiteRoleDto(
                Id: r.Id,
                Key: r.Key,
                DisplayName: r.DisplayName,
                Description: r.Description,
                IsSiteAdminRole: r.IsSiteAdminRole))
            .ToListAsync(ct);

        return roles;
    }

    private async Task<Role> AutoCloneRoleFromTemplateAsync(
        SiteDbContext db, Guid siteId, string roleKey, CancellationToken ct)
    {
        // Load the template (with permissions) from the platform DB.
        var template = await _platformDb.RoleTemplates
            .Include(t => t.Permissions)
            .FirstOrDefaultAsync(t => t.Key == roleKey, ct)
            ?? throw new BusinessRuleException("ROLE_TEMPLATE_MISSING",
                $"The '{roleKey}' role template does not exist in the platform DB. " +
                "Contact your developer — the seeder should have created it.");

        // Create the role in the site DB.
        var role = new Role
        {
            Key = template.Key,
            DisplayName = template.DisplayName,
            Description = template.Description,
            IsSiteAdminRole = template.IsSiteAdminRole,
            OriginTemplateId = template.Id,
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct); // Save to get role.Id

        // Ensure all permission keys exist in the site's Permissions table.
        var desiredKeys = template.Permissions.Select(p => p.PermissionKey).Distinct().ToList();
        var existingPerms = await db.Permissions
            .Where(p => desiredKeys.Contains(p.Key))
            .ToListAsync(ct);
        var existingKeys = existingPerms.Select(p => p.Key).ToHashSet();
        var catalog = PermissionCatalog.Static;

        foreach (var key in desiredKeys)
        {
            if (!existingKeys.Contains(key))
            {
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
                db.Permissions.Add(newPerm);
                existingPerms.Add(newPerm);
            }
        }
        await db.SaveChangesAsync(ct);

        // Insert role-permission rows.
        foreach (var p in existingPerms)
        {
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = p.Id });
        }
        await db.SaveChangesAsync(ct);

        return role;
    }
}
