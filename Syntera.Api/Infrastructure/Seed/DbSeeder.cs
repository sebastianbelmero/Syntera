using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Syntera.Domain.Entities;
using Syntera.Domain.Enums;
using Syntera.Infrastructure.Data;

namespace Syntera.Infrastructure.Seed;

/// <summary>
/// Idempotent database seeder. Runs at startup (when the
/// SEED_SAMPLE_DATA env var is true) and creates the roles, the
/// initial admin user, and a small demo dataset of categories /
/// suppliers / products / customers so the front-end has
/// something to render on first launch. Safe to re-run.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(
        AppDbContext db,
        UserManager<IdentityUser> users,
        RoleManager<IdentityRole> roles,
        bool seedSampleData,
        string adminEmail,
        string adminPassword,
        CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        await EnsureRole(roles, "Admin", ct);
        await EnsureRole(roles, "Cashier", ct);
        await EnsureRole(roles, "Pharmacist", ct);

        var admin = await users.FindByEmailAsync(adminEmail);
        if (admin is null)
        {
            admin = new IdentityUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                LockoutEnabled = false,
            };
            var create = await users.CreateAsync(admin, adminPassword);
            if (!create.Succeeded)
                throw new InvalidOperationException(
                    "Seeding admin user failed: " +
                    string.Join("; ", create.Errors.Select(e => e.Description)));
            await users.AddToRoleAsync(admin, "Admin");
        }

        if (!seedSampleData) return;

        await SeedCatalogAsync(db, ct);
    }

    private static async Task EnsureRole(
        RoleManager<IdentityRole> roles, string name, CancellationToken ct)
    {
        if (!await roles.RoleExistsAsync(name))
            await roles.CreateAsync(new IdentityRole(name));
    }

    private static async Task SeedCatalogAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(ct)) return;

        var catAnti = new Category { Name = "Antibiotik", Slug = "antibiotik" };
        var catAnal = new Category { Name = "Analgesik", Slug = "analgesik" };
        var catVit  = new Category { Name = "Suplemen Vitamin", Slug = "suplemen-vitamin" };
        var catAntiBiotSub = new Category
        {
            Name = "Penisilin",
            Slug = "penisilin",
            Parent = catAnti,
        };
        db.Categories.AddRange(catAnti, catAnal, catVit, catAntiBiotSub);

        var supplierKalbe = new Supplier
        {
            Name = "PT Kalbe Farma Tbk",
            ContactPerson = "Andi Wijaya",
            Email = "b2b@kalbe.co.id",
            Phone = "+62 21 5592 1000",
            Address = "Jl. Jend. Gatot Subroto Kav. 23",
            City = "Jakarta",
            PostalCode = "12930",
            LicenseNumber = "POM-IND-0001",
            IsActive = true,
        };
        var supplierHex = new Supplier
        {
            Name = "PT Hexpharm Jaya Laboratories",
            ContactPerson = "Siti Aminah",
            Email = "cs@hexpharm.co.id",
            Phone = "+62 22 662 1234",
            Address = "Jl. Pelajar Pejuang 45 No. 69",
            City = "Bandung",
            PostalCode = "40273",
            LicenseNumber = "POM-IND-0002",
            IsActive = true,
        };
        var supplierDankos = new Supplier
        {
            Name = "PT Dankos Farma",
            ContactPerson = "Budi Santoso",
            Email = "info@dankos.co.id",
            Phone = "+62 21 550 2233",
            Address = "Jl. Industri Raya Blok A-1",
            City = "Jakarta",
            PostalCode = "12950",
            LicenseNumber = "POM-IND-0003",
            IsActive = true,
        };
        db.Suppliers.AddRange(supplierKalbe, supplierHex, supplierDankos);

        await db.SaveChangesAsync(ct);

        var products = new[]
        {
            new Product
            {
                Name = "Amoxicillin 500 mg",
                Sku = "AML500",
                Barcode = "0896800000012",
                RegistrationNumber = "DBL9876543210",
                GenericName = "Amoxicillin Trihydrate",
                BrandName = "Kalbe Amoxsan",
                Manufacturer = "PT Kalbe Farma",
                DrugClass = DrugClass.PrescriptionOnly,
                Potency = "500 mg",
                PackSize = "Strip @ 10 kaplet",
                CostPrice = 1200m,
                SellingPrice = 2200m,
                ReorderLevel = 20,
                ExpiryDate = DateTime.UtcNow.AddDays(420),
                BatchNumber = "AMX2026-001",
                IsActive = true,
                CategoryId = catAnti.Id,
                SupplierId = supplierKalbe.Id,
            },
            new Product
            {
                Name = "Paracetamol 500 mg",
                Sku = "PCT500",
                Barcode = "0896800000029",
                GenericName = "Paracetamol",
                BrandName = "Hexamol",
                Manufacturer = "PT Hexpharm Jaya",
                DrugClass = DrugClass.RestrictedOTC,
                Potency = "500 mg",
                PackSize = "Strip @ 10 tablet",
                CostPrice = 350m,
                SellingPrice = 750m,
                ReorderLevel = 50,
                ExpiryDate = DateTime.UtcNow.AddDays(600),
                BatchNumber = "PCT2026-004",
                IsActive = true,
                CategoryId = catAnal.Id,
                SupplierId = supplierHex.Id,
            },
            new Product
            {
                Name = "Vitamin C 1000 mg Effervescent",
                Sku = "VITC1K",
                Barcode = "0896800000036",
                GenericName = "Ascorbic Acid",
                BrandName = "Dankovit-C",
                Manufacturer = "PT Dankos Farma",
                DrugClass = DrugClass.OverTheCounter,
                Potency = "1000 mg",
                PackSize = "Tube @ 10 tablet eff",
                CostPrice = 9000m,
                SellingPrice = 14500m,
                ReorderLevel = 15,
                ExpiryDate = DateTime.UtcNow.AddDays(720),
                BatchNumber = "VITC2026-010",
                IsActive = true,
                CategoryId = catVit.Id,
                SupplierId = supplierDankos.Id,
            },
            new Product
            {
                Name = "Ibuprofen 400 mg",
                Sku = "IBU400",
                Barcode = "0896800000043",
                GenericName = "Ibuprofen",
                BrandName = "Bufecta",
                Manufacturer = "PT Kalbe Farma",
                DrugClass = DrugClass.RestrictedOTC,
                Potency = "400 mg",
                PackSize = "Strip @ 10 tablet",
                CostPrice = 800m,
                SellingPrice = 1500m,
                ReorderLevel = 30,
                ExpiryDate = DateTime.UtcNow.AddDays(540),
                BatchNumber = "IBU2026-002",
                IsActive = true,
                CategoryId = catAnal.Id,
                SupplierId = supplierKalbe.Id,
            },
            new Product
            {
                Name = "Cetirizine 10 mg",
                Sku = "CTR10",
                Barcode = "0896800000050",
                GenericName = "Cetirizine HCl",
                BrandName = "Hexizine",
                Manufacturer = "PT Hexpharm Jaya",
                DrugClass = DrugClass.RestrictedOTC,
                Potency = "10 mg",
                PackSize = "Strip @ 10 tablet",
                CostPrice = 450m,
                SellingPrice = 950m,
                ReorderLevel = 25,
                ExpiryDate = DateTime.UtcNow.AddDays(480),
                BatchNumber = "CTR2026-007",
                IsActive = true,
                CategoryId = catAnal.Id,
                SupplierId = supplierHex.Id,
            },
        };

        db.Products.AddRange(products);
        await db.SaveChangesAsync(ct);

        // Initial inbound stock
        var movements = new[]
        {
            new InventoryMovement { ProductId = products[0].Id, Quantity = 100, Type = InventoryMovementType.Inbound, BalanceAfter = 100, Reference = "PO-001", Note = "Initial stock" },
            new InventoryMovement { ProductId = products[1].Id, Quantity = 200, Type = InventoryMovementType.Inbound, BalanceAfter = 200, Reference = "PO-001", Note = "Initial stock" },
            new InventoryMovement { ProductId = products[2].Id, Quantity = 80,  Type = InventoryMovementType.Inbound, BalanceAfter = 80,  Reference = "PO-001", Note = "Initial stock" },
            new InventoryMovement { ProductId = products[3].Id, Quantity = 150, Type = InventoryMovementType.Inbound, BalanceAfter = 150, Reference = "PO-001", Note = "Initial stock" },
            new InventoryMovement { ProductId = products[4].Id, Quantity = 50,  Type = InventoryMovementType.Inbound, BalanceAfter = 50,  Reference = "PO-001", Note = "Initial stock" },
        };
        db.InventoryMovements.AddRange(movements);

        var customer = new Customer
        {
            Name = "Apotek Sehat Sentosa",
            ContactPerson = "Dewi Lestari",
            Email = "apothek.sehat@example.com",
            Phone = "+62 21 555 1234",
            Address = "Jl. Merdeka No. 45",
            City = "Jakarta",
            PostalCode = "10110",
            TaxId = "01.234.567.8-901.000",
            IsActive = true,
        };
        db.Customers.Add(customer);

        await db.SaveChangesAsync(ct);
    }
}
