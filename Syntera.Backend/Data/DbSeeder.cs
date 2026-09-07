using Microsoft.EntityFrameworkCore;
using Syntera.Backend.Models.Entities;
using Syntera.Backend.Data;
using Syntera.Backend.Services;

// Seeder runs once at startup — LoggerMessage delegate optimization
// (CA1848, CA1873) is not worth the complexity for these infrequent calls.
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Performance",
    "CA1848:Use the LoggerMessage delegates",
    Justification = "Seeder runs once at startup; performance is not critical here.")]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Performance",
    "CA1873:LoggerMessage argument evaluation",
    Justification = "Seeder runs once at startup; performance is not critical here.")]

namespace Syntera.Backend.Data;

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
    /// <param name="protector">
    /// Optional connection-string protector (COMPLIANCE Sprint 2.6). When
    /// provided AND <c>ConnectionProtection:Enabled=true</c> in config, the
    /// seeder encrypts new writes and auto-migrates existing plaintext
    /// rows in the Sites table to encrypted form (idempotent — never
    /// re-encrypts an already-encrypted value). When null (e.g. tests, or
    /// the protector service is not registered), the seeder falls back to
    /// storing plaintext connection strings (original pre-Sprint-2.6
    /// behavior — preserves backward compat).
    /// </param>
    public static async Task SeedPlatformAsync(
        PlatformDbContext db,
        IConfiguration config,
        ILogger? logger = null,
        IConnectionStringProtector? protector = null)
    {
        // ── Default platform settings ──────────────────────────────────
        await EnsureSetting(db, "AuditRetentionYears", "10", "Audit log retention period in years (compliance).");
        await EnsureSetting(db, "TokenAccessTokenMinutes", "15", "JWT access token lifetime in minutes.");
        await EnsureSetting(db, "TokenRefreshTokenDays", "1", "Refresh token lifetime in days.");
        await EnsureSetting(db, "DirectPermissionMaxDays", "90", "Max days for direct permission grants.");
        await EnsureSetting(db, "MaxFailedLogins", "5", "Failed login attempts before lockout.");

        // ── Default role templates ─────────────────────────────────────
        // Role hierarchy:
        //   Platform Admin → System Admin (per site) → Business Admin → End Users
        
        // Tier 2: System Administrator (1+ per site, assigned by Platform Admin)
        // Can only assign Business Admin. Otherwise has Viewer-level access.
        await EnsureRoleTemplate(db, "system-admin", "System Administrator",
            "System Administrator — assigns Business Admin for this site. Viewer-level access otherwise.",
            isSiteAdminRole: false,
            permissions: SystemAdminPermissions);

        // Tier 3: Site Business Administrator (assigned by System Admin)
        await EnsureRoleTemplate(db, "site-business-admin", "Site Business Administrator",
            "Manages users, roles, and permissions within own site.",
            isSiteAdminRole: true,
            permissions: SiteBusinessAdminPermissions);

        // Tier 4: End User roles
        await EnsureRoleTemplate(db, "viewer", "Viewer", "Read-only access to dashboards and own profile.",
            isSiteAdminRole: false,
            permissions: ViewerPermissions);

        await EnsureRoleTemplate(db, "eng-planner", "Eng Planner", "Engineering planner — dashboard + audit access.",
            isSiteAdminRole: false,
            permissions: EngPlannerPermissions);

        await EnsureRoleTemplate(db, "supervisor", "Supervisor", "Supervisor — dashboard + audit + reports.",
            isSiteAdminRole: false,
            permissions: SupervisorPermissions);

        await EnsureRoleTemplate(db, "technician", "Technician", "Technician — dashboard access.",
            isSiteAdminRole: false,
            permissions: TechnicianPermissions);

        await EnsureRoleTemplate(db, "eng-manager", "Eng Manager", "Engineering Manager — manages users in own site.",
            isSiteAdminRole: true,
            permissions: EngManagerPermissions);

        await EnsureRoleTemplate(db, "qo-manager", "QO Manager", "Quality Operations Manager — audit + reports.",
            isSiteAdminRole: false,
            permissions: QoManagerPermissions);

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
    ///
    /// <para>COMPLIANCE (Sprint 2.6): when <paramref name="protector"/> is
    /// supplied AND <c>ConnectionProtection:Enabled=true</c> in config, the
    /// seeder encrypts the connection string before writing it. Existing
    /// plaintext rows are auto-migrated on the next seeder run — the
    /// migration is idempotent: rows whose value already starts with
    /// <c>"ENC:"</c> are left untouched. The protector itself is also
    /// idempotent (Protect short-circuits on <c>ENC:</c> input), but we
    /// additionally decrypt-and-compare plaintexts to skip no-op
    /// ciphertext rotations that would bump UpdatedAt on every seeder
    /// run (DPAPI uses a random IV — identical plaintexts produce
    /// different ciphertexts each call).</para>
    /// </summary>
    private static async Task EnsureSitesAsync(
        PlatformDbContext db,
        IConfiguration config,
        ILogger? logger,
        IConnectionStringProtector? protector)
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
                // COMPLIANCE (Sprint 2.6): encrypt the connection string
                // before storing it on a NEW site row. When the protector is
                // null or ConnectionProtection:Enabled=false, Protect returns
                // the plaintext as-is (no-op) — backward-compat with the
                // pre-Sprint-2.6 behavior.
                var storedConnStr = protector is not null && !string.IsNullOrWhiteSpace(connStr)
                    ? protector.Protect(connStr!)
                    : connStr ?? "";

                site = new Site
                {
                    Code = sc.Code,
                    DisplayName = sc.DisplayName,
                    DatabaseConnectionString = storedConnStr,
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
                // COMPLIANCE (Sprint 2.6): sync the stored connection string
                // from config (allows password rotation) AND auto-migrate
                // any legacy plaintext row to encrypted form. The stored
                // value may already be plaintext (legacy DB) or encrypted
                // (already migrated). We need to:
                //   1. If config provides a fresh value → it's the source of
                //      truth. Decrypt the stored value and compare
                //      plaintexts; only write back if different (avoids a
                //      no-op UpdatedAt bump — DPAPI uses a random IV).
                //   2. Else if the stored value is plaintext AND encryption
                //      is enabled → migrate it in place (one-time).
                //   3. Else → leave the stored value alone.
                var fromConfig = connStr ?? "";
                var stored = site.DatabaseConnectionString ?? "";
                var nextStored = stored; // default: keep what's there

                if (protector is not null)
                {
                    if (!string.IsNullOrWhiteSpace(fromConfig))
                    {
                        // Config provides a value — compare plaintexts to
                        // skip no-op writes (password unchanged).
                        var storedPlaintext = SafeUnprotect(protector, stored, logger, sc.Code);
                        if (!string.Equals(storedPlaintext, fromConfig, StringComparison.Ordinal))
                        {
                            nextStored = protector.Protect(fromConfig);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(stored) && !protector.IsEncrypted(stored))
                    {
                        // No config value, but stored is plaintext — migrate
                        // it to encrypted form. Idempotent: subsequent seeder
                        // runs see IsEncrypted==true and skip this branch.
                        nextStored = protector.Protect(stored);
                    }
                    // else: no config + already encrypted → nothing to do.
                }
                else
                {
                    // Protector not registered / ConnectionProtection:Enabled=false.
                    // Preserve the original pre-Sprint-2.6 behavior: store
                    // the config value verbatim (no encryption).
                    nextStored = !string.IsNullOrWhiteSpace(fromConfig) ? fromConfig : stored;
                }

                if (!string.Equals(nextStored, stored, StringComparison.Ordinal))
                {
                    site.DatabaseConnectionString = nextStored;
                    if (logger is not null && protector is not null)
                    {
                        logger.LogInformation(
                            "Site {Code}: connection string updated (encrypted={Encrypted}).",
                            sc.Code, protector.IsEncrypted(nextStored));
                    }
                }

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

    /// <summary>
    /// Wraps <see cref="IConnectionStringProtector.Unprotect"/> with a
    /// try/catch so a corrupted or undecryptable stored value does not
    /// crash the seeder. On failure, logs the error and returns the raw
    /// stored value — the caller (EnsureSitesAsync) treats this as a
    /// plaintext mismatch and overwrites with the config value (the
    /// documented recovery path: operator re-enters the password in
    /// appsettings.json and restarts, then the seeder re-encrypts it).
    /// </summary>
    private static string SafeUnprotect(
        IConnectionStringProtector protector,
        string value,
        ILogger? logger,
        string siteCode)
    {
        if (string.IsNullOrEmpty(value)) return value;
        try
        {
            return protector.Unprotect(value);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "Failed to decrypt the stored connection string for site {Code} during the seeder run. " +
                "Will fall back to the config value (the documented recovery path). " +
                "Verify the DPAPI key ring at DataProtection:KeyPath.",
                siteCode);
            // Return the raw value — the caller compares it to the config
            // plaintext, finds them different (the raw ciphertext is not
            // the plaintext), and overwrites with the freshly-encrypted
            // config value.
            return value;
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

    // ── Static readonly permission arrays ──────────────────────────
    
    /// <summary>
    /// System Admin: Viewer-level access + ability to assign Business Admin.
    /// This is the ONLY role (besides Platform Admin) that can assign
    /// the site-business-admin role.
    /// </summary>
    private static readonly string[] SystemAdminPermissions =
    {
        "dashboard.read", "audit.read", "profile.read",
        "business_admin.assign", "business_admin.revoke",
    };

    private static readonly string[] ViewerPermissions =
    {
        "dashboard.read", "audit.read", "profile.read",
    };

    private static readonly string[] EngPlannerPermissions =
    {
        "dashboard.read", "audit.read", "report.read", "profile.read",
    };

    private static readonly string[] SupervisorPermissions =
    {
        "dashboard.read", "audit.read", "report.read", "profile.read",
    };

    private static readonly string[] TechnicianPermissions =
    {
        "dashboard.read", "profile.read",
    };

    private static readonly string[] EngManagerPermissions =
    {
        "dashboard.read", "audit.read", "report.read", "profile.read",
        "user.read", "user.write", "user.disable",
        "role.read", "user_role.assign", "user_role.revoke",
        "permission.read", "permission.grant", "permission.revoke",
    };

    private static readonly string[] QoManagerPermissions =
    {
        "dashboard.read", "audit.read", "report.read", "profile.read",
    };

    private static readonly string[] SiteBusinessAdminPermissions =
    {
        "user.read", "user.write", "user.disable",
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
