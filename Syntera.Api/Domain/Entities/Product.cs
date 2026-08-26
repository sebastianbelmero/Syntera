using Syntera.Domain.Enums;

namespace Syntera.Domain.Entities;

/// <summary>
/// Pharmaceutical product. Captures regulatory identifiers
/// (<see cref="RegistrationNumber"/>, BPOM), stock-keeping unit, and
/// potency / pack size — enough to render a price list, run a POS
/// flow, and drive expiry-aware stock valuation.
/// </summary>
public sealed class Product : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string? Barcode { get; set; }

    /// <summary>BPOM registration number (e.g. "DBL987654321").</summary>
    public string? RegistrationNumber { get; set; }

    public string? GenericName { get; set; }

    public string? BrandName { get; set; }

    public string? Manufacturer { get; set; }

    public DrugClass DrugClass { get; set; } = DrugClass.OverTheCounter;

    /// <summary>Potency / strength, e.g. "500 mg", "5 mL".</summary>
    public string? Potency { get; set; }

    /// <summary>Pack size, e.g. "Strip @ 10 kaplet".</summary>
    public string? PackSize { get; set; }

    public decimal CostPrice { get; set; }

    public decimal SellingPrice { get; set; }

    public decimal? DiscountPrice { get; set; }

    /// <summary>Reorder threshold (units). Triggers restock alerts.</summary>
    public int ReorderLevel { get; set; } = 10;

    /// <summary>Hard expiry flag — products past <see cref="ExpiryDate"/>
    /// cannot be sold. Controlled by the application service.</summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>Batch / lot number for pharmacovigilance traceability.</summary>
    public string? BatchNumber { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }

    // Foreign keys
    public Guid CategoryId { get; set; }
    public Guid SupplierId { get; set; }

    // Navigation
    public Category Category { get; set; } = null!;
    public Supplier Supplier { get; set; } = null!;
    public ICollection<InventoryMovement> Movements { get; set; } = new List<InventoryMovement>();
    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
}
