namespace Syntera.Application.DTOs.Suppliers;

public sealed record SupplierDto(
    Guid Id,
    string Name,
    string? ContactPerson,
    string? Email,
    string? Phone,
    string? Address,
    string? City,
    string? PostalCode,
    string? LicenseNumber,
    bool IsActive,
    int ProductCount,
    DateTime CreatedAt);

public sealed record SupplierUpsertDto(
    string Name,
    string? ContactPerson,
    string? Email,
    string? Phone,
    string? Address,
    string? City,
    string? PostalCode,
    string? LicenseNumber,
    bool IsActive = true);
