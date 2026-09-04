using Microsoft.EntityFrameworkCore;
using Syntera.Backend.Models.Entities;

namespace Syntera.Backend.Data;

/// <summary>
/// DbContext for a single Site's database. The connection string is
/// resolved at runtime from the Platform DB's <see cref="Site"/> table
/// based on the JWT claim <c>site_id</c>. Each request gets a fresh,
/// scoped <c>SiteDbContext</c> instance — connection pooling is handled
/// by SQL Server provider automatically.
/// </summary>
public sealed class SiteDbContext : DbContext
{
    public SiteDbContext(DbContextOptions<SiteDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserSyncHistory> UserSyncHistory => Set<UserSyncHistory>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(160).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            e.Property(x => x.Title).HasMaxLength(160);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("Roles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Permission>(e =>
        {
            e.ToTable("Permissions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            e.Property(x => x.Group).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<UserRole>(e =>
        {
            e.ToTable("UserRoles");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role)
                .WithMany(r => r.UserAssignments)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique();
        });

        modelBuilder.Entity<RolePermission>(e =>
        {
            e.ToTable("RolePermissions");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Role)
                .WithMany(r => r.Permissions)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Permission)
                .WithMany()
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique();
        });

        modelBuilder.Entity<UserPermission>(e =>
        {
            e.ToTable("UserPermissions");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.User)
                .WithMany(u => u.DirectPermissions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Permission)
                .WithMany()
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            e.HasIndex(x => new { x.UserId, x.PermissionId });
            e.HasIndex(x => x.ExpiresAt);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Timestamp).IsRequired();
            e.HasIndex(x => x.Timestamp);
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
        });

        modelBuilder.Entity<UserSyncHistory>(e =>
        {
            e.ToTable("UserSyncHistory");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(16).IsRequired();
            e.Property(x => x.Errors).HasMaxLength(8000);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("RefreshTokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.UserScope).HasMaxLength(16).IsRequired();
            // M1: index FamilyId for fast "revoke entire family" query on token reuse.
            e.HasIndex(x => x.FamilyId);
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

    private void RejectAuditLogMutation()
    {
        foreach (var entry in ChangeTracker.Entries<AuditLog>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "AuditLog is append-only; UPDATE and DELETE are forbidden.");
            }
        }
    }
}
