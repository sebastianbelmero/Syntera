using Microsoft.EntityFrameworkCore;
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
        var db = await _siteDbFactory.ResolveAsync(ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId, ct)
            ?? throw new NotFoundException("User", dto.UserId);
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == dto.RoleId, ct)
            ?? throw new NotFoundException("Role", dto.RoleId);

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
        var db = await _siteDbFactory.ResolveAsync(ct);
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
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Key == "site-business-admin", ct)
            ?? throw new BusinessRuleException("ROLE_NOT_PUBLISHED",
                "The 'site-business-admin' role template has not been published yet. " +
                "Go to Role Templates page and publish it first.");

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
}
