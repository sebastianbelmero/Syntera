using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Syntera.Domain.Entities;

namespace Syntera.Infrastructure.Data;

/// <summary>
/// Single DbContext for both ASP.NET Core Identity tables and the
/// pharmaceutical domain. Combining them means a single migration
/// history and a single transactional boundary — important for
/// audit-style writes that span both (e.g. user-triggered sale).
/// </summary>
public sealed class AppDbContext : IdentityDbContext<IdentityUser, IdentityRole, string>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Identity tables get a sensible prefix to avoid name clashes.
        foreach (var entity in b.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (tableName != null && tableName.StartsWith("AspNet"))
            {
                entity.SetTableName(tableName.Replace("AspNet", "Id_"));
            }
        }

        // ── Category ──────────────────────────────────────────────────
        b.Entity<Category>(e =>
        {
            e.ToTable("Categories");
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(160).IsRequired();
            e.Property(c => c.Slug).HasMaxLength(160).IsRequired();
            e.HasIndex(c => c.Slug).IsUnique();
            e.Property(c => c.Description).HasMaxLength(500);
            e.Property(c => c.IsDeleted);
            e.HasQueryFilter(c => !c.IsDeleted);
            e.HasOne(c => c.Parent)
                .WithMany(p => p.Children)
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Supplier ──────────────────────────────────────────────────
        b.Entity<Supplier>(e =>
        {
            e.ToTable("Suppliers");
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).HasMaxLength(160).IsRequired();
            e.Property(s => s.ContactPerson).HasMaxLength(160);
            e.Property(s => s.Email).HasMaxLength(160);
            e.Property(s => s.Phone).HasMaxLength(40);
            e.Property(s => s.City).HasMaxLength(120);
            e.Property(s => s.PostalCode).HasMaxLength(20);
            e.Property(s => s.LicenseNumber).HasMaxLength(80);
            e.HasQueryFilter(s => !s.IsDeleted);
        });

        // ── Product ───────────────────────────────────────────────────
        b.Entity<Product>(e =>
        {
            e.ToTable("Products");
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Sku).HasMaxLength(64).IsRequired();
            e.HasIndex(p => p.Sku).IsUnique();
            e.Property(p => p.Barcode).HasMaxLength(64);
            e.Property(p => p.RegistrationNumber).HasMaxLength(80);
            e.Property(p => p.GenericName).HasMaxLength(200);
            e.Property(p => p.BrandName).HasMaxLength(200);
            e.Property(p => p.Manufacturer).HasMaxLength(200);
            e.Property(p => p.Potency).HasMaxLength(80);
            e.Property(p => p.PackSize).HasMaxLength(80);
            e.Property(p => p.BatchNumber).HasMaxLength(64);
            e.Property(p => p.CostPrice).HasPrecision(18, 2);
            e.Property(p => p.SellingPrice).HasPrecision(18, 2);
            e.Property(p => p.DiscountPrice).HasPrecision(18, 2);
            e.HasQueryFilter(p => !p.IsDeleted);
            e.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Supplier)
                .WithMany(s => s.Products)
                .HasForeignKey(p => p.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── InventoryMovement ─────────────────────────────────────────
        b.Entity<InventoryMovement>(e =>
        {
            e.ToTable("InventoryMovements");
            e.HasKey(m => m.Id);
            e.Property(m => m.Reference).HasMaxLength(80);
            e.Property(m => m.Note).HasMaxLength(500);
            e.HasOne(m => m.Product)
                .WithMany(p => p.Movements)
                .HasForeignKey(m => m.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => m.ProductId);
            e.HasIndex(m => new { m.Type, m.CreatedAt });
        });

        // ── Customer ─────────────────────────────────────────────────
        b.Entity<Customer>(e =>
        {
            e.ToTable("Customers");
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(160).IsRequired();
            e.Property(c => c.ContactPerson).HasMaxLength(160);
            e.Property(c => c.Email).HasMaxLength(160);
            e.Property(c => c.Phone).HasMaxLength(40);
            e.Property(c => c.City).HasMaxLength(120);
            e.Property(c => c.PostalCode).HasMaxLength(20);
            e.Property(c => c.TaxId).HasMaxLength(40);
            e.HasQueryFilter(c => !c.IsDeleted);
        });

        // ── Sale ──────────────────────────────────────────────────────
        b.Entity<Sale>(e =>
        {
            e.ToTable("Sales");
            e.HasKey(s => s.Id);
            e.Property(s => s.InvoiceNumber).HasMaxLength(40).IsRequired();
            e.HasIndex(s => s.InvoiceNumber).IsUnique();
            e.Property(s => s.SubTotal).HasPrecision(18, 2);
            e.Property(s => s.TaxAmount).HasPrecision(18, 2);
            e.Property(s => s.DiscountAmount).HasPrecision(18, 2);
            e.Property(s => s.GrandTotal).HasPrecision(18, 2);
            e.Property(s => s.Note).HasMaxLength(500);
            e.HasOne(s => s.Customer)
                .WithMany(c => c.Sales)
                .HasForeignKey(s => s.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(s => new { s.Status, s.SaleDate });
        });

        b.Entity<SaleItem>(e =>
        {
            e.ToTable("SaleItems");
            e.HasKey(i => i.Id);
            e.Property(i => i.UnitPrice).HasPrecision(18, 2);
            e.Property(i => i.DiscountAmount).HasPrecision(18, 2);
            e.Property(i => i.LineTotal).HasPrecision(18, 2);
            e.HasOne(i => i.Sale)
                .WithMany(s => s.Items)
                .HasForeignKey(i => i.SaleId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Product)
                .WithMany(p => p.SaleItems)
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>
    /// Centralised audit + soft-delete filter refresh. Each call to
    /// <see cref="SaveChangesAsync(CancellationToken)"/> stamps
    /// <c>CreatedAt</c> on inserted entities and <c>UpdatedAt</c> on
    /// any mutated entity, so application code never has to remember.
    /// </summary>
    public override int SaveChanges()
    {
        StampAudit();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        StampAudit();
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
}
