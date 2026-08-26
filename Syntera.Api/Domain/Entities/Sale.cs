using Syntera.Domain.Enums;

namespace Syntera.Domain.Entities;

/// <summary>
/// Sale order header. Owns its items (one-to-many) and tracks a
/// workflow via <see cref="Status"/>. Money is stored as decimal with
/// scale 18,2 — sufficient for IDR and any other fiat currency we
/// may onboard without a migration.
/// </summary>
public sealed class Sale : BaseEntity
{
    /// <summary>Human-friendly invoice number, e.g. "INV-2026-000123".</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    public SaleStatus Status { get; set; } = SaleStatus.Draft;

    public DateTime? SaleDate { get; set; }

    public Guid CustomerId { get; set; }

    public Guid? CashierUserId { get; set; }

    public decimal SubTotal { get; set; }

    public decimal TaxRate { get; set; } = 0m;

    public decimal TaxAmount { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal GrandTotal { get; set; }

    public string? Note { get; set; }

    // Navigation
    public Customer Customer { get; set; } = null!;
    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
}
