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
    // Hoisted per CA1861: SeedPlatformAsync runs on every boot — avoid re-allocating
    // these arrays on each call.
    private static readonly string[] ViewerPermissions =
        { "dashboard.read", "audit.read", "profile.read" };

    private static readonly string[] SiteBusinessAdminPermissions =
    {
        "user.read", "user.write", "user.disable", "user.sync",
        "role.read", "user_role.assign", "user_role.revoke",
        "permission.read", "permission.grant", "permission.revoke",
        "audit.read", "report.read",
    };

    /// <param name="db">Platform DB context.</param>
    /// <param name="adminEmail">Email for the platform admin (default: admin@syntera.com).</param>
    /// <param name="adminPassword">Plain-text password to hash with bcrypt. MUST come from
    /// user-secrets / env-var, never hardcoded.</param>
    public static async Task SeedPlatformAsync(PlatformDbContext db, string? adminEmail = null, string? adminPassword = null)
    {
        adminEmail = string.IsNullOrWhiteSpace(adminEmail) ? "admin@syntera.com" : adminEmail.ToLowerInvariant();

        // ── Default platform settings ──────────────────────────────────
        await EnsureSetting(db, "AuditRetentionYears", "10", "Audit log retention period in years (compliance).");
        await EnsureSetting(db, "TokenAccessTokenMinutes", "15", "JWT access token lifetime in minutes.");
        await EnsureSetting(db, "TokenRefreshTokenDays", "1", "Refresh token lifetime in days.");
        await EnsureSetting(db, "DirectPermissionMaxDays", "90", "Max days for direct permission grants.");
        await EnsureSetting(db, "MaxFailedLogins", "5", "Failed login attempts before lockout.");

        // ── Default role templates ─────────────────────────────────────
        await EnsureRoleTemplate(db, "viewer", "Viewer", "Read-only access to dashboards and own profile.",
            isSiteAdminRole: false,
            permissions: ViewerPermissions);

        await EnsureRoleTemplate(db, "site-business-admin", "Site Business Admin",
            "Manages users, roles, and permissions within own site.",
            isSiteAdminRole: true,
            permissions: SiteBusinessAdminPermissions);

        // ── Default Platform Admin user ────────────────────────────────
        await EnsurePlatformAdminAsync(db, adminEmail, adminPassword);

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

    /// <summary>
    /// Creates the Platform Admin user if it doesn't exist. The password
    /// is hashed with bcrypt (work factor 12) before storage. If the user
    /// already exists, no change is made — to reset the password, use the
    /// dedicated CLI command (future work) or update directly in DB.
    /// </summary>
    private static async Task EnsurePlatformAdminAsync(PlatformDbContext db, string email, string? password)
    {
        if (await db.PlatformUsers.AnyAsync(u => u.Email == email)) return;

        if (string.IsNullOrWhiteSpace(password))
        {
            // In dev, fall back to a known weak password that the user MUST change.
            // In production, this branch is never hit because Program.cs fails-fast
            // when Seed:PlatformAdminPassword is missing.
            password = "ChangeMe!Strong#1";
        }

        var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

        db.PlatformUsers.Add(new PlatformUser
        {
            Email = email,
            PasswordHash = hash,
            DisplayName = "Platform Admin",
            IsEnabled = true,
        });
    }
}
