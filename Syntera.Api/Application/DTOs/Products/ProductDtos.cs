using Syntera.Domain.Enums;

namespace Syntera.Application.DTOs.Products;

public sealed record ProductDto(
    Guid Id,
    string Name,
    string Sku,
    string? Barcode,
    string? RegistrationNumber,
    string? GenericName,
    string? BrandName,
    string? Manufacturer,
    DrugClass DrugClass,
    string? Potency,
    string? PackSize,
    decimal CostPrice,
    decimal SellingPrice,
    decimal? DiscountPrice,
    int ReorderLevel,
    DateTime? ExpiryDate,
    string? BatchNumber,
    bool IsActive,
    int Stock,
    bool IsExpired,
    bool IsLowStock,
    Guid CategoryId,
    string CategoryName,
    Guid SupplierId,
    string SupplierName,
    DateTime CreatedAt);

public sealed record ProductUpsertDto(
    string Name,
    string Sku,
    string? Barcode,
    string? RegistrationNumber,
    string? GenericName,
    string? BrandName,
    string? Manufacturer,
    DrugClass DrugClass,
    string? Potency,
    string? PackSize,
    decimal CostPrice,
    decimal SellingPrice,
    decimal? DiscountPrice,
    int ReorderLevel,
    DateTime? ExpiryDate,
    string? BatchNumber,
    bool IsActive,
    Guid CategoryId,
    Guid SupplierId);

public sealed record ProductStockAdjustDto(
    int Quantity,
    string? Note);
