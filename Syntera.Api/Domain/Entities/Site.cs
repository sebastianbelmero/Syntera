namespace Syntera.Domain.Entities;

/// <summary>
/// A registered tenant/site in the Syntera platform. Each Site owns its
/// own database, its own LDAP config, its own theme palette, and its own
/// user/role/permission store. Sites are completely isolated from each
/// other at the database level — there is no shared business data table
/// across sites.
/// </summary>
public class Site : BaseEntity
{
    /// <summary>Stable short code, e.g., "kalventis", "kalbe", "dankos". Used in JWT claim <c>site_id</c>.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name, e.g., "PT Kalventis Surya Pratama".</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>SQL Server connection string for this site's database. Stored encrypted via DPAPI.</summary>
    public string DatabaseConnectionString { get; set; } = string.Empty;

    /// <summary>Default theme palette key, e.g., "kalventis-navy". Resolved against SiteTheme table.</summary>
    public string DefaultThemeKey { get; set; } = string.Empty;

    /// <summary>When false, login from this site is rejected (maintenance / offboarding).</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Free-form notes for the Platform Admin (e.g., onboarding status, contact info).</summary>
    public string? Notes { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────
    public ICollection<SiteLdapDomain> LdapDomains { get; set; } = new List<SiteLdapDomain>();
    public SiteLdapConfig? LdapConfig { get; set; }
    public SiteTheme? Theme { get; set; }
}

/// <summary>
/// Maps an email domain (e.g., "kalventis.com") to a <see cref="Site"/>.
/// Multiple domains may point to the same Site (e.g., legacy aliases after M&amp;A).
/// The login flow uses this table to route authentication to the correct LDAP.
/// </summary>
public class SiteLdapDomain : BaseEntity
{
    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;

    /// <summary>Email domain (lowercase, no @), e.g., "kalventis.com". Unique across all sites.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>If false, this domain is parked (looked up for history but no new logins accepted).</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// LDAP server configuration for a <see cref="Site"/>. Simplified to match
/// the actual Kalventis-style AD setup: just host/port/baseDn/StartTLS.
///
/// Authentication uses <b>direct bind</b> — the user's own email + password
/// is used to bind to LDAP. No service account, no BindDn/BindPassword.
/// This matches the pattern in the reference JS implementation
/// (ldap-get-user.js) the customer provided.
/// </summary>
public class SiteLdapConfig : BaseEntity
{
    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;

    /// <summary>LDAP server host, e.g., "10.131.220.11" or "ldap.kalventis.dom".</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Port. Typically 389 (plain or StartTLS) or 636 (LDAPS). Plain 389 is allowed.</summary>
    public int Port { get; set; } = 389;

    /// <summary>If true, connection upgrades to TLS after initial connect on port 389 (StartTLS).</summary>
    public bool UseStartTls { get; set; }

    /// <summary>Base DN for the LDAP directory, e.g., "DC=KALVENTIS,DC=DOM".</summary>
    public string BaseDn { get; set; } = string.Empty;
}

/// <summary>
/// Theme palette for a <see cref="Site"/>. Stored as JSON so the Platform Admin
/// can adjust brand colors without redeploying. Loaded once into in-memory
/// cache at app start; cache invalidated via SiteTheme.UpdatedAt check.
/// </summary>
public class SiteTheme : BaseEntity
{
    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;

    /// <summary>Stable key, e.g., "kalventis-navy".</summary>
    public string ThemeKey { get; set; } = string.Empty;

    /// <summary>Light-mode palette JSON, e.g., {"primary":"#0B3D6F","accent":"#00A7B5"}.</summary>
    public string LightPaletteJson { get; set; } = "{}";

    /// <summary>Dark-mode palette JSON.</summary>
    public string DarkPaletteJson { get; set; } = "{}";

    /// <summary>Logo URL (optional; can be a CDN URL or data-URI).</summary>
    public string? LogoUrl { get; set; }
}
