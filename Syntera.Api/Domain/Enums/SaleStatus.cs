namespace Syntera.Domain.Enums;

/// <summary>
/// Lifecycle of a sale order. We intentionally avoid a "Cancelled" →
/// "Completed" transition by validating state transitions in the
/// application service, not at the enum level, to keep the type safe
/// while still allowing controlled rollbacks (e.g. refund flows).
/// </summary>
public enum SaleStatus
{
    Draft = 1,
    Pending = 2,
    Paid = 3,
    Shipped = 4,
    Completed = 5,
    Cancelled = 6,
}
