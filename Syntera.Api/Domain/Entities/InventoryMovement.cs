using Syntera.Domain.Enums;

namespace Syntera.Domain.Entities;

/// <summary>
/// Append-only ledger entry for any stock change on a <see cref="Product"/>.
/// The current on-hand quantity of a product is derived as
/// <c>Σ(Quantity) WHERE Type=Inbound − Σ(Quantity) WHERE Type=Outbound</c>
/// and never stored directly — single source of truth = the ledger.
/// </summary>
public sealed class InventoryMovement : BaseEntity
{
    public Guid ProductId { get; set; }

    public InventoryMovementType Type { get; set; }

    public int Quantity { get; set; }

    /// <summary>Balance AFTER this movement. Stored for fast lookups
    /// without requiring a full SUM; recomputed by the service on each insert.</summary>
    public int BalanceAfter { get; set; }

    public string? Reference { get; set; }

    public string? Note { get; set; }

    /// <summary>Optional FK to the user who triggered the movement.</summary>
    public Guid? PerformedByUserId { get; set; }

    // Navigation
    public Product Product { get; set; } = null!;
}
