using Microsoft.Extensions.Logging;
using Syntera.Application.Common;
using Syntera.Application.DTOs.Customers;
using Syntera.Application.Interfaces;
using Syntera.Application.Logging;
using Syntera.Domain.Entities;
using Syntera.Domain.Exceptions;

namespace Syntera.Application.Services;

public interface ICustomerService
{
    Task<PagedResult<CustomerDto>> PageAsync(PageQuery query, CancellationToken ct = default);
    Task<CustomerDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<CustomerDto> CreateAsync(CustomerUpsertDto dto, CancellationToken ct = default);
    Task<CustomerDto> UpdateAsync(Guid id, CustomerUpsertDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CustomerService> _log;

    public CustomerService(ICustomerRepository repo, IUnitOfWork uow, ILogger<CustomerService> log)
    {
        _repo = repo; _uow = uow; _log = log;
    }

    public async Task<PagedResult<CustomerDto>> PageAsync(PageQuery query, CancellationToken ct = default)
    {
        var page = await _repo.PageAsync(query, ct);
        return new PagedResult<CustomerDto>
        {
            Items = page.Items.Select(Map).ToList(),
            Total = page.Total,
            Page = page.Page,
            PageSize = page.PageSize,
        };
    }

    public async Task<CustomerDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _repo.GetByIdAsync(id, ct);
        return c is null ? null : Map(c);
    }

    public async Task<CustomerDto> CreateAsync(CustomerUpsertDto dto, CancellationToken ct = default)
    {
        var entity = new Customer
        {
            Name = dto.Name.Trim(),
            ContactPerson = dto.ContactPerson,
            Email = dto.Email,
            Phone = dto.Phone,
            Address = dto.Address,
            City = dto.City,
            PostalCode = dto.PostalCode,
            TaxId = dto.TaxId,
            IsActive = dto.IsActive,
        };
        await _repo.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        CustomerLogger.LogCustomerCreated(_log, entity.Id, entity.Name);
        return Map(entity);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, CustomerUpsertDto dto, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(nameof(Customer), id);
        entity.Name = dto.Name.Trim();
        entity.ContactPerson = dto.ContactPerson;
        entity.Email = dto.Email;
        entity.Phone = dto.Phone;
        entity.Address = dto.Address;
        entity.City = dto.City;
        entity.PostalCode = dto.PostalCode;
        entity.TaxId = dto.TaxId;
        entity.IsActive = dto.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(nameof(Customer), id);
        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
    }

    private static CustomerDto Map(Customer c) => new(
        c.Id, c.Name, c.ContactPerson, c.Email, c.Phone, c.Address,
        c.City, c.PostalCode, c.TaxId, c.IsActive, 0, c.CreatedAt);
}
