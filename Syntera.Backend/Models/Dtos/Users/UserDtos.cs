namespace Syntera.Backend.Models.Dtos.Users;

public record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? Title,
    bool IsEnabled,
    DateTime? LastLoginAt,
    long PermissionsVersion,
    IReadOnlyList<RoleAssignmentDto> Roles,
    IReadOnlyList<DirectPermissionDto> DirectPermissions,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record RoleAssignmentDto(
    Guid RoleId,
    string RoleKey,
    string RoleDisplayName,
    Guid AssignedBy,
    DateTime AssignedAt,
    DateTime? ExpiresAt);

public record DirectPermissionDto(
    Guid Id,
    string PermissionKey,
    string PermissionDisplayName,
    string Reason,
    Guid ApprovedBy,
    string ApprovedByEmail,
    DateTime GrantedAt,
    DateTime ExpiresAt,
    bool IsDeny,
    bool IsRevoked);

public record UserUpsertDto(
    string Email,
    string DisplayName,
    string? Title,
    bool IsEnabled);

public record AssignRoleDto(
    Guid UserId,
    Guid RoleId,
    DateTime? ExpiresAt,
    string? Reason);

public record RevokeRoleDto(
    Guid UserId,
    Guid RoleId);

public record GrantDirectPermissionDto(
    Guid UserId,
    Guid PermissionId,
    string Reason,
    DateTime ExpiresAt,
    bool IsDeny = false);

public record RevokeDirectPermissionDto(
    Guid UserPermissionId);

public record UserSyncTriggerDto(Guid SiteId);

public record UserSyncResultDto(
    Guid SyncHistoryId,
    string Status,
    int UsersFound,
    int UsersCreated,
    int UsersUpdated,
    int UsersDisabled,
    string? Errors);

/// <summary>Site-level role (simplified — no template metadata).</summary>
public record SiteRoleDto(
    Guid Id,
    string Key,
    string DisplayName,
    string? Description,
    bool IsSiteAdminRole);
