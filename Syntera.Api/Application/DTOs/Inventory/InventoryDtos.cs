using Syntera.Domain.Enums;

namespace Syntera.Application.DTOs.Inventory;

public sealed record InventoryMovementDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSku,
    InventoryMovementType Type,
    int Quantity,
    int BalanceAfter,
    string? Reference,
    string? Note,
    Guid? PerformedByUserId,
    DateTime CreatedAt);

public sealed record InventoryAdjustmentRequest(
    Guid ProductId,
    InventoryMovementType Type,
    int Quantity,
    string? Reference,
    string? Note);
