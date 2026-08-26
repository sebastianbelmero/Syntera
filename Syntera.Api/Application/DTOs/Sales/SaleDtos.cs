using Syntera.Domain.Enums;

namespace Syntera.Application.DTOs.Sales;

public sealed record SaleDto(
    Guid Id,
    string InvoiceNumber,
    SaleStatus Status,
    DateTime? SaleDate,
    Guid CustomerId,
    string CustomerName,
    Guid? CashierUserId,
    string? CashierName,
    decimal SubTotal,
    decimal TaxRate,
    decimal TaxAmount,
    decimal DiscountAmount,
    decimal GrandTotal,
    string? Note,
    IReadOnlyList<SaleItemDto> Items,
    DateTime CreatedAt);

public sealed record SaleItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSku,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal LineTotal);

public sealed record SaleItemInput(
    Guid ProductId,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount);

public sealed record SaleCreateDto(
    Guid CustomerId,
    DateTime? SaleDate,
    decimal TaxRate,
    decimal DiscountAmount,
    string? Note,
    IReadOnlyList<SaleItemInput> Items);

public sealed record SaleStatusUpdateDto(SaleStatus Status);
