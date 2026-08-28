using Microsoft.EntityFrameworkCore;
using Syntera.Domain.Entities;
using Syntera.Infrastructure.Data;

// Seeder runs once at startup — LoggerMessage delegate optimization
// (CA1848, CA1873) is not worth the complexity for these infrequent calls.
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Performance",
    "CA1848:Use the LoggerMessage delegates",
    Justification = "Seeder runs once at startup; performance is not critical here.")]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Performance",
    "CA1873:LoggerMessage argument evaluation",
    Justification = "Seeder runs once at startup; performance is not critical here.")]

namespace Syntera.Infrastructure.Seed;

/// <summary>
/// Seeds the Platform DB with the minimum data needed to bootstrap:
/// - The default Platform Admin user (admin@syntera.com) — password from configuration
/// - Default platform settings (audit retention, token lifetimes)
/// - Default role templates: viewer, site-business-admin
/// - 6 fixed sites (Kalventis, Kalbe, Fima, GOF, Dankos, Hexpharm) with their
///   connection strings, themes, and LDAP domains — read from configuration.
///
/// This is idempotent — running it twice is safe.
/// </summary>
public static class DbSeeder
{
    /// <param name="db">Platform DB context.</param>
    /// <param name="config">App configuration (for site connection strings &amp; admin creds).</param>
    /// <param name="logger">Optional logger.</param>
    public static async Task SeedPlatformAsync(
        PlatformDbContext db,
        IConfiguration config,
        ILogger? logger = null)
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
            permissions: ViewerPermissions);

        await EnsureRoleTemplate(db, "site-business-admin", "Site Business Admin",
            "Manages users, roles, and permissions within own site.",
            isSiteAdminRole: true,
            permissions: SiteBusinessAdminPermissions);

        // ── 6 fixed sites ──────────────────────────────────────────────
        await EnsureSitesAsync(db, config, logger);

        // ── Default Platform Admin user ────────────────────────────────
        var adminEmail = config["Seed:PlatformAdminEmail"] ?? "admin@syntera.com";
        var adminPassword = config["Seed:PlatformAdminPassword"];
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
    /// Seeds the 6 fixed sites from configuration. Each site's connection
    /// string is read from <c>ConnectionStrings:Sites:{code}</c>. If a site
    /// already exists (by code), its connection string and theme are
    /// updated to match config — this lets operators change DB passwords
    /// via config without touching the DB.
    /// </summary>
    private static async Task EnsureSitesAsync(PlatformDbContext db, IConfiguration config, ILogger? logger)
    {
        var siteConfigs = config.GetSection("Sites").Get<SiteSeedConfig[]>() ?? Array.Empty<SiteSeedConfig>();

        foreach (var sc in siteConfigs)
        {
            var connStr = config[$"ConnectionStrings:Sites:{sc.Code}"];
            if (string.IsNullOrWhiteSpace(connStr))
            {
                logger?.LogWarning("No connection string found for site {Code}. Site will be disabled.", sc.Code);
            }

            var site = await db.Sites
                .Include(s => s.LdapDomains)
                .Include(s => s.Theme)
                .FirstOrDefaultAsync(s => s.Code == sc.Code);

            if (site is null)
            {
                site = new Site
                {
                    Code = sc.Code,
                    DisplayName = sc.DisplayName,
                    DatabaseConnectionString = connStr ?? "",
                    DefaultThemeKey = $"{sc.Code}-default",
                    IsEnabled = !string.IsNullOrWhiteSpace(connStr),
                    Notes = $"Pre-seeded site ({sc.Code}).",
                };

                // Add the primary email domain.
                site.LdapDomains.Add(new SiteLdapDomain
                {
                    Domain = sc.EmailDomain.ToLowerInvariant(),
                    IsActive = true,
                });

                db.Sites.Add(site);
                if (logger is not null)
                {
                    logger.LogInformation("Seeded site {Code} ({DisplayName}).", sc.Code, sc.DisplayName);
                }
            }
            else
            {
                // Update connection string from config (allows password rotation).
                site.DatabaseConnectionString = connStr ?? site.DatabaseConnectionString;
                site.DisplayName = sc.DisplayName;
                site.DefaultThemeKey = $"{sc.Code}-default";

                // Ensure the primary email domain exists.
                if (!site.LdapDomains.Any(d => string.Equals(d.Domain, sc.EmailDomain, StringComparison.OrdinalIgnoreCase)))
                {
                    site.LdapDomains.Add(new SiteLdapDomain
                    {
                        Domain = sc.EmailDomain.ToLowerInvariant(),
                        IsActive = true,
                    });
                }
            }

            // Upsert theme for this site.
            await EnsureSiteThemeAsync(db, site, sc);
        }
    }

    private static async Task EnsureSiteThemeAsync(PlatformDbContext db, Site site, SiteSeedConfig sc)
    {
        var theme = site.Theme;
        var isNew = theme is null;
        theme ??= new SiteTheme { SiteId = site.Id };

        theme.ThemeKey = $"{sc.Code}-default";
        theme.LightPaletteJson = System.Text.Json.JsonSerializer.Serialize(sc.LightPalette);
        theme.DarkPaletteJson = System.Text.Json.JsonSerializer.Serialize(sc.DarkPalette);

        if (isNew) db.Themes.Add(theme);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Creates the Platform Admin user if it doesn't exist. The password
    /// is hashed with bcrypt (work factor 12) before storage.
    /// </summary>
    private static async Task EnsurePlatformAdminAsync(PlatformDbContext db, string email, string? password)
    {
        email = string.IsNullOrWhiteSpace(email) ? "admin@syntera.com" : email.ToLowerInvariant();
        if (await db.PlatformUsers.AnyAsync(u => u.Email == email)) return;

        if (string.IsNullOrWhiteSpace(password))
            password = "ChangeMe!Strong#1";

        var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

        db.PlatformUsers.Add(new PlatformUser
        {
            Email = email,
            PasswordHash = hash,
            DisplayName = "Platform Admin",
            IsEnabled = true,
        });
    }

    // ── Static readonly permission arrays (CA1861: avoid allocating
    //    new[] on every call — pull up to static readonly fields). ──────
    private static readonly string[] ViewerPermissions =
    {
        "dashboard.read", "audit.read", "profile.read",
    };

    private static readonly string[] SiteBusinessAdminPermissions =
    {
        "user.read", "user.write", "user.disable", "user.sync",
        "role.read", "user_role.assign", "user_role.revoke",
        "permission.read", "permission.grant", "permission.revoke",
        "audit.read", "report.read",
    };
}

/// <summary>Seed configuration for a single site (from appsettings Sites section).</summary>
public sealed class SiteSeedConfig
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string EmailDomain { get; set; } = "";
    public ThemePaletteConfig LightPalette { get; set; } = new();
    public ThemePaletteConfig DarkPalette { get; set; } = new();
}

public sealed class ThemePaletteConfig
{
    public string Primary { get; set; } = "#0B3D6F";
    public string Accent { get; set; } = "#00A7B5";
    public string Background { get; set; } = "#F8FAFC";
    public string Surface { get; set; } = "#FFFFFF";
    public string Text { get; set; } = "#243447";
    public string Muted { get; set; } = "#64748B";
    public string Border { get; set; } = "#E2E8F0";
    public string Success { get; set; } = "#10B981";
    public string Warning { get; set; } = "#F59E0B";
    public string Danger { get; set; } = "#EF4444";
}
