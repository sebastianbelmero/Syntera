using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Syntera.Application.Interfaces.Services;

namespace Syntera.Infrastructure.Identity;

/// <summary>
/// Reads the authenticated user from the current <see cref="HttpContext"/>
/// claims. Scoped per-request. All claims are read once and cached on the
/// instance — repeated access does not re-query the HttpContext.
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    public const string ClaimSiteId = "site_id";
    public const string ClaimSiteCode = "site_code";
    public const string ClaimScope = "scope";
    public const string ClaimPermissionsVersion = "perm_ver";
    public const string ClaimPermissions = "perm";
    public const string ClaimIsPlatformAdmin = "is_platform_admin";
    public const string ClaimIsSiteBusinessAdmin = "is_site_admin";
    public const string ClaimEmail = "email";
    public const string ClaimDisplayName = "display_name";

    private readonly IHttpContextAccessor _http;
    private readonly Lazy<ClaimsPrincipal?> _user;

    public CurrentUserService(IHttpContextAccessor http)
    {
        _http = http;
        _user = new Lazy<ClaimsPrincipal?>(() => _http.HttpContext?.User);
    }

    public Guid? UserId
    {
        get
        {
            var id = _user.Value?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(id, out var g) ? g : null;
        }
    }

    public string? Email => _user.Value?.FindFirstValue(ClaimEmail);
    public string? DisplayName => _user.Value?.FindFirstValue(ClaimDisplayName);

    public Guid? SiteId
    {
        get
        {
            var v = _user.Value?.FindFirstValue(ClaimSiteId);
            return Guid.TryParse(v, out var g) ? g : null;
        }
    }

    public string? SiteCode => _user.Value?.FindFirstValue(ClaimSiteCode);
    public string Scope => _user.Value?.FindFirstValue(ClaimScope) ?? "anonymous";

    public long? PermissionsVersion
    {
        get
        {
            var v = _user.Value?.FindFirstValue(ClaimPermissionsVersion);
            return long.TryParse(v, out var n) ? n : null;
        }
    }

    public IReadOnlyCollection<string> Roles
        => _user.Value?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];

    public bool IsInRole(string role)
        => _user.Value?.IsInRole(role) ?? false;

    public bool HasPermission(string permissionKey)
    {
        var user = _user.Value;
        if (user is null) return false;
        return user.HasClaim(ClaimPermissions, permissionKey);
    }

    public bool IsPlatformAdmin
        => bool.TryParse(_user.Value?.FindFirstValue(ClaimIsPlatformAdmin), out var b) && b;

    public bool IsSiteBusinessAdmin
        => bool.TryParse(_user.Value?.FindFirstValue(ClaimIsSiteBusinessAdmin), out var b) && b;
}
