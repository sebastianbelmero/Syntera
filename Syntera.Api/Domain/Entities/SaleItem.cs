namespace Syntera.Domain.Entities;

/// <summary>
/// Sale order line. Captures the unit price at the moment of sale —
/// never re-resolved from <see cref="Product"/> — so historical
/// invoices stay accurate even if the catalogue price changes.
/// </summary>
public sealed class SaleItem : BaseEntity
{
    public Guid SaleId { get; set; }

    public Guid ProductId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal LineTotal { get; set; }

    // Navigation
    public Sale Sale { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
