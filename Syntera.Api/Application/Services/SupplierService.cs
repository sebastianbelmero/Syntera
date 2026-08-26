using Microsoft.Extensions.Logging;
using Syntera.Application.Common;
using Syntera.Application.DTOs.Suppliers;
using Syntera.Application.Interfaces;
using Syntera.Domain.Entities;
using Syntera.Domain.Exceptions;

namespace Syntera.Application.Services;

public interface ISupplierService
{
    Task<PagedResult<SupplierDto>> PageAsync(PageQuery query, CancellationToken ct = default);
    Task<SupplierDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<SupplierDto> CreateAsync(SupplierUpsertDto dto, CancellationToken ct = default);
    Task<SupplierDto> UpdateAsync(Guid id, SupplierUpsertDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class SupplierService : ISupplierService
{
    private readonly ISupplierRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<SupplierService> _log;

    public SupplierService(ISupplierRepository repo, IUnitOfWork uow, ILogger<SupplierService> log)
    {
        _repo = repo; _uow = uow; _log = log;
    }

    public async Task<PagedResult<SupplierDto>> PageAsync(PageQuery query, CancellationToken ct = default)
    {
        var page = await _repo.PageAsync(query, ct);
        return new PagedResult<SupplierDto>
        {
            Items = page.Items.Select(Map).ToList(),
            Total = page.Total,
            Page = page.Page,
            PageSize = page.PageSize,
        };
    }

    public async Task<SupplierDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var s = await _repo.GetByIdAsync(id, ct);
        return s is null ? null : Map(s);
    }

    public async Task<SupplierDto> CreateAsync(SupplierUpsertDto dto, CancellationToken ct = default)
    {
        var entity = new Supplier
        {
            Name = dto.Name.Trim(),
            ContactPerson = dto.ContactPerson,
            Email = dto.Email,
            Phone = dto.Phone,
            Address = dto.Address,
            City = dto.City,
            PostalCode = dto.PostalCode,
            LicenseNumber = dto.LicenseNumber,
            IsActive = dto.IsActive,
        };
        await _repo.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        _log.LogInformation("Supplier created: {Id} ({Name})", entity.Id, entity.Name);
        return Map(entity);
    }

    public async Task<SupplierDto> UpdateAsync(Guid id, SupplierUpsertDto dto, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(nameof(Supplier), id);
        entity.Name = dto.Name.Trim();
        entity.ContactPerson = dto.ContactPerson;
        entity.Email = dto.Email;
        entity.Phone = dto.Phone;
        entity.Address = dto.Address;
        entity.City = dto.City;
        entity.PostalCode = dto.PostalCode;
        entity.LicenseNumber = dto.LicenseNumber;
        entity.IsActive = dto.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(nameof(Supplier), id);
        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
    }

    private static SupplierDto Map(Supplier s) => new(
        s.Id, s.Name, s.ContactPerson, s.Email, s.Phone, s.Address,
        s.City, s.PostalCode, s.LicenseNumber, s.IsActive, 0, s.CreatedAt);
}
