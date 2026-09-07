using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Syntera.Backend.Services;
using Syntera.Backend.Models;

namespace Syntera.Backend.Authorization;

/// <summary>
/// Authorization attribute that checks for a specific permission key in
/// the current user's JWT claims. Use on controller actions:
/// <code>[HasPermission("user.write")]</code>
///
/// Fails-closed: anonymous requests are denied. Platform Admin tokens
/// bypass site-level permission checks (they carry platform-admin claim).
/// </summary>
public sealed class HasPermissionAttribute : AuthorizeAttribute, IAuthorizationFilter
{
    private readonly string _permissionKey;

    public HasPermissionAttribute(string permissionKey)
    {
        _permissionKey = permissionKey;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedResult();
            return;
        }

        // Platform admin bypasses site-level permission checks.
        var isPlatformAdmin = user.Claims.Any(c =>
            c.Type == CurrentUserService.ClaimIsPlatformAdmin && c.Value == "true");
        if (isPlatformAdmin) return;

        // Check perm claim.
        var hasPerm = user.Claims.Any(c =>
            c.Type == CurrentUserService.ClaimPermissions && c.Value == _permissionKey);
        if (!hasPerm)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
        }
    }
}

/// <summary>
/// Restricts an endpoint to Platform Admin only (admin@syntera.com).
/// </summary>
public sealed class PlatformAdminOnlyAttribute : AuthorizeAttribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedResult();
            return;
        }
        var isPlatform = user.Claims.Any(c =>
            c.Type == CurrentUserService.ClaimIsPlatformAdmin && c.Value == "true");
        if (!isPlatform)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
        }
    }
}

/// <summary>
/// Restricts an endpoint to Site Business Admin (within their own site) only.
/// Platform Admin also passes (they can do anything site admins can do).
/// System Admin also passes (per their role: assign/manage Business Admins for the site).
///
/// <b>SECURITY:</b> Previously this attribute also granted access to eng-manager,
/// supervisor, and qo-manager roles — that was a privilege escalation vector
/// (those roles are <i>not</i> site admins). Use <see cref="HasPermissionAttribute"/>
/// on individual actions to gate specific operations (e.g., eng-manager needs
/// <c>user.read</c> but not <c>permission.grant</c>).
/// </summary>
public sealed class SiteBusinessAdminAttribute : AuthorizeAttribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedResult();
            return;
        }
        // Platform Admin bypasses all checks
        var isPlatform = user.Claims.Any(c =>
            c.Type == CurrentUserService.ClaimIsPlatformAdmin && c.Value == "true");
        if (isPlatform) return;
        // Site Business Admin
        var isSiteAdmin = user.Claims.Any(c =>
            c.Type == CurrentUserService.ClaimIsSiteBusinessAdmin && c.Value == "true");
        if (isSiteAdmin) return;
        // System Admin (Tier 2 — between Platform Admin and Business Admin)
        var isSystemAdmin = user.Claims.Any(c =>
            c.Type == System.Security.Claims.ClaimTypes.Role && c.Value == "system-admin");
        if (isSystemAdmin) return;

        context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
    }
}

/// <summary>
/// Platform Admin OR System Admin — for endpoints that both roles need.
/// Used for: GET /api/platform/sites (list sites), audit logs, etc.
/// </summary>
public sealed class PlatformAdminOrSystemAdminAttribute : AuthorizeAttribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedResult();
            return;
        }
        var isPlatform = user.Claims.Any(c =>
            c.Type == CurrentUserService.ClaimIsPlatformAdmin && c.Value == "true");
        if (isPlatform) return;
        var isSystemAdmin = user.Claims.Any(c =>
            c.Type == System.Security.Claims.ClaimTypes.Role && c.Value == "system-admin");
        if (isSystemAdmin) return;

        context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
    }
}
