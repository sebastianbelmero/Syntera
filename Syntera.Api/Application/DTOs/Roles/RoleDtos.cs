namespace Syntera.Application.DTOs.Roles;

public record RoleDto(
    Guid Id,
    string Key,
    string DisplayName,
    string? Description,
    bool IsSiteAdminRole,
    bool IsPublished,
    int Version,
    IReadOnlyList<string> PermissionKeys,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record RoleTemplateUpsertDto(
    string Key,
    string DisplayName,
    string? Description,
    bool IsSiteAdminRole,
    List<string> PermissionKeys);

public record PublishRoleTemplateDto(Guid RoleTemplateId);

public record PermissionDto(
    Guid Id,
    string Key,
    string DisplayName,
    string Group,
    bool IsPlatformOnly);

public record PermissionCatalogDto(
    IReadOnlyList<PermissionGroupDto> Groups);

public record PermissionGroupDto(
    string Group,
    IReadOnlyList<PermissionDto> Permissions);
