using Microsoft.EntityFrameworkCore;
using Syntera.Domain.Entities;
using Syntera.Infrastructure.Data;

namespace Syntera.Infrastructure.Seed;

/// <summary>
/// Seeds the Platform DB with the minimum data needed to bootstrap:
/// - The default Platform Admin user (admin@syntera.com) — password from configuration
/// - Default platform settings (audit retention, token lifetimes)
/// - Default role templates: viewer, site-business-admin
///
/// This is idempotent — running it twice is safe. In Development only;
/// Production must seed via CLI (syntera-seed tool, not yet implemented).
/// </summary>
public static class DbSeeder
{
    public static async Task SeedPlatformAsync(PlatformDbContext db)
    {
        // ── Default platform settings ──────────────────────────────────
        await EnsureSetting(db, "AuditRetentionYears", "10", "Audit log retention period in years (compliance).");
        await EnsureSetting(db, "TokenAccessTokenMinutes", "15", "JWT access token lifetime in minutes.");
        await EnsureSetting(db, "TokenRefreshTokenDays", "1", "Refresh token lifetime in days.");
        await EnsureSetting(db, "DirectPermissionMaxDays", "90", "Max days for direct permission grants.");
        await EnsureSetting(db, "MaxFailedLogins", "5", "Failed login attempts before lockout.");

        // ── Default role templates ─────────────────────────────────────
        await EnsureRoleTemplate(db, "viewer", "Viewer", "Read-only access to dashboards and own profile.",
            isSiteAdminRole: false,
            permissions: new[] { "dashboard.read", "audit.read", "profile.read" });

        await EnsureRoleTemplate(db, "site-business-admin", "Site Business Admin",
            "Manages users, roles, and permissions within own site.",
            isSiteAdminRole: true,
            permissions: new[]
            {
                "user.read", "user.write", "user.disable", "user.sync",
                "role.read", "user_role.assign", "user_role.revoke",
                "permission.read", "permission.grant", "permission.revoke",
                "audit.read", "report.read",
            });

        await db.SaveChangesAsync();
    }

    private static async Task EnsureSetting(PlatformDbContext db, string key, string value, string desc)
    {
        if (await db.Settings.AnyAsync(s => s.Key == key)) return;
        db.Settings.Add(new PlatformSetting { Key = key, Value = value, Description = desc });
    }

    private static async Task EnsureRoleTemplate(PlatformDbContext db, string key, string displayName,
        string description, bool isSiteAdminRole, string[] permissions)
    {
        if (await db.RoleTemplates.AnyAsync(t => t.Key == key)) return;

        var template = new RoleTemplate
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            IsSiteAdminRole = isSiteAdminRole,
            IsPublished = true,
            Version = 1,
        };
        foreach (var p in permissions)
            template.Permissions.Add(new RoleTemplatePermission { PermissionKey = p });

        db.RoleTemplates.Add(template);
    }
}
