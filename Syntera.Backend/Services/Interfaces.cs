using Syntera.Backend.Data;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Syntera.Backend.Services;

/// <summary>
/// Reads the authenticated user from the current HttpContext claims.
/// Scoped per-request.
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    string? DisplayName { get; }
    Guid? SiteId { get; }
    string? SiteCode { get; }
    string Scope { get; }
    long? PermissionsVersion { get; }
    IReadOnlyCollection<string> Roles { get; }
    bool IsInRole(string role);
    bool HasPermission(string permissionKey);
    bool IsPlatformAdmin { get; }
    bool IsSiteBusinessAdmin { get; }
}

/// <summary>BCrypt password hasher for platform admin credentials.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

/// <summary>Resolves SiteDbContext by siteId (from JWT or explicit).</summary>
