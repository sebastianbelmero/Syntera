namespace Syntera.Domain.Enums;

/// <summary>
/// Direction of an inventory movement. Keeping it as a single enum
/// (rather than a boolean) means future states (e.g. <c>Adjustment</c>,
/// <c>Return</c>, <c>Damage</c>) can be added without breaking the
/// schema or existing consumers.
/// </summary>
public enum InventoryMovementType
{
    Inbound = 1,
    Outbound = 2,
    Adjustment = 3,
    Return = 4,
    Damage = 5,
}
