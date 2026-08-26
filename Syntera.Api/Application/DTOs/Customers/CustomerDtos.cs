namespace Syntera.Application.DTOs.Customers;

public sealed record CustomerDto(
    Guid Id,
    string Name,
    string? ContactPerson,
    string? Email,
    string? Phone,
    string? Address,
    string? City,
    string? PostalCode,
    string? TaxId,
    bool IsActive,
    int TotalOrders,
    DateTime CreatedAt);

public sealed record CustomerUpsertDto(
    string Name,
    string? ContactPerson,
    string? Email,
    string? Phone,
    string? Address,
    string? City,
    string? PostalCode,
    string? TaxId,
    bool IsActive = true);
