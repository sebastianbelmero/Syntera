using Microsoft.EntityFrameworkCore;
using Syntera.Backend.Models.Entities;

namespace Syntera.Backend.Data;

/// <summary>
/// DbContext for the platform master database (syntera_master).
/// Contains ONLY platform-level tables: Sites registry, LDAP configs,
/// theme templates, role templates, PlatformUsers (admin@syntera.com),
/// and platform audit logs. NEVER contains site business data.
/// </summary>
public sealed class PlatformDbContext : DbContext
{
    public PlatformDbContext(DbContextOptions<PlatformDbContext> options) : base(options) { }

    public DbSet<Site> Sites => Set<Site>();
    public DbSet<SiteLdapDomain> LdapDomains => Set<SiteLdapDomain>();
    public DbSet<SiteLdapConfig> LdapConfigs => Set<SiteLdapConfig>();
    public DbSet<SiteTheme> Themes => Set<SiteTheme>();
    public DbSet<RoleTemplate> RoleTemplates => Set<RoleTemplate>();
    public DbSet<RoleTemplatePermission> RoleTemplatePermissions => Set<RoleTemplatePermission>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>Platform-level key/value settings (e.g., AuditRetentionYears, TokenLifetimeMinutes).</summary>
    public DbSet<PlatformSetting> Settings => Set<PlatformSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Site ─────────────────────────────────────────────────────────
        modelBuilder.Entity<Site>(e =>
        {
            e.ToTable("Sites");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            e.Property(x => x.DatabaseConnectionString).HasMaxLength(1024).IsRequired();
            e.Property(x => x.DefaultThemeKey).HasMaxLength(64).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(2000);
        });

        modelBuilder.Entity<SiteLdapDomain>(e =>
        {
            e.ToTable("SiteLdapDomains");
            e.HasKey(x => x.Id);
            e.Property(x => x.Domain).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.Domain).IsUnique();
            e.HasOne(x => x.Site)
                .WithMany(s => s.LdapDomains)
                .HasForeignKey(x => x.SiteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SiteLdapConfig>(e =>
        {
            e.ToTable("SiteLdapConfigs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Host).HasMaxLength(255).IsRequired();
            e.Property(x => x.BaseDn).HasMaxLength(255).IsRequired();
            e.Property(x => x.UpnDomain).HasMaxLength(255);
            e.HasOne(x => x.Site)
                .WithOne(s => s.LdapConfig)
                .HasForeignKey<SiteLdapConfig>(x => x.SiteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SiteTheme>(e =>
        {
            e.ToTable("SiteThemes");
            e.HasKey(x => x.Id);
            e.Property(x => x.ThemeKey).HasMaxLength(64).IsRequired();
            e.Property(x => x.LightPaletteJson).HasMaxLength(4096).IsRequired();
            e.Property(x => x.DarkPaletteJson).HasMaxLength(4096).IsRequired();
            e.Property(x => x.LogoUrl).HasMaxLength(512);
            e.HasOne(x => x.Site)
                .WithOne(s => s.Theme)
                .HasForeignKey<SiteTheme>(x => x.SiteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RoleTemplate>(e =>
        {
            e.ToTable("RoleTemplates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<RoleTemplatePermission>(e =>
        {
            e.ToTable("RoleTemplatePermissions");
            e.HasKey(x => x.Id);
            e.Property(x => x.PermissionKey).HasMaxLength(128).IsRequired();
            e.HasOne(x => x.RoleTemplate)
                .WithMany(r => r.Permissions)
                .HasForeignKey(x => x.RoleTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.RoleTemplateId, x.PermissionKey }).IsUnique();
        });

        modelBuilder.Entity<PlatformUser>(e =>
        {
            e.ToTable("PlatformUsers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(160).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(255).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("RefreshTokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.UserScope).HasMaxLength(16).IsRequired();
            e.HasIndex(x => new { x.UserId, x.UserScope });
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Timestamp).IsRequired();
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.SiteId);
            e.HasIndex(x => x.ActorUserId);
            e.HasIndex(x => x.Action);
            e.Property(x => x.ActorEmail).HasMaxLength(160);
            e.Property(x => x.ActorIp).HasMaxLength(64);
            e.Property(x => x.ActorUserAgent).HasMaxLength(512);
            e.Property(x => x.Action).HasMaxLength(128).IsRequired();
            e.Property(x => x.TargetType).HasMaxLength(64);
            e.Property(x => x.TargetId).HasMaxLength(64);
            e.Property(x => x.Outcome).HasMaxLength(16).IsRequired();
            e.Property(x => x.Hash).HasMaxLength(128).IsRequired();
            e.Property(x => x.PreviousHash).HasMaxLength(128).IsRequired();
            // NOTE: AuditLog rows are append-only. We do NOT register any
            // UPDATE/DELETE handler — the SaveChanges pipeline explicitly
            // throws if any AuditLog entry is in Modified/Deleted state.
        });

        modelBuilder.Entity<PlatformSetting>(e =>
        {
            e.ToTable("PlatformSettings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(128);
            e.Property(x => x.Value).HasMaxLength(2000);
        });
    }

    public override int SaveChanges()
    {
        StampAudit();
        RejectAuditLogMutation();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAudit();
        RejectAuditLogMutation();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampAudit()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }

    /// <summary>
    /// AuditLog is append-only. Any attempt to UPDATE or DELETE a row
    /// throws immediately — defends against accidental mutation in app
    /// code and signals hostile intent if exploited.
    /// </summary>
    private void RejectAuditLogMutation()
    {
        foreach (var entry in ChangeTracker.Entries<AuditLog>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "AuditLog is append-only; UPDATE and DELETE are forbidden. " +
                    "Use a new corrective entry if a correction is needed.");
            }
        }
    }
}

/// <summary>Platform-level key/value setting.</summary>
public class PlatformSetting
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string? Description { get; set; }
}
