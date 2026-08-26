using Microsoft.Extensions.Logging;
using Syntera.Application.Common;
using Syntera.Application.DTOs.Products;
using Syntera.Application.Interfaces;
using Syntera.Application.Logging;
using Syntera.Domain.Entities;
using Syntera.Domain.Enums;
using Syntera.Domain.Exceptions;

namespace Syntera.Application.Services;

public interface IProductService
{
    Task<PagedResult<ProductDto>> SearchAsync(
        string? search, Guid? categoryId, Guid? supplierId,
        bool? activeOnly, PageQuery query, CancellationToken ct = default);

    Task<ProductDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ProductDto> CreateAsync(ProductUpsertDto dto, CancellationToken ct = default);
    Task<ProductDto> UpdateAsync(Guid id, ProductUpsertDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ProductDto> AdjustStockAsync(Guid id, ProductStockAdjustDto dto, Guid? userId, CancellationToken ct = default);
}

public sealed class ProductService : IProductService
{
    private readonly IProductRepository _repo;
    private readonly IInventoryRepository _movements;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ProductService> _log;

    public ProductService(
        IProductRepository repo,
        IInventoryRepository movements,
        IUnitOfWork uow,
        ILogger<ProductService> log)
    {
        _repo = repo;
        _movements = movements;
        _uow = uow;
        _log = log;
    }

    public async Task<PagedResult<ProductDto>> SearchAsync(
        string? search, Guid? categoryId, Guid? supplierId,
        bool? activeOnly, PageQuery query, CancellationToken ct = default)
    {
        var page = await _repo.SearchAsync(search, categoryId, supplierId, activeOnly, query, ct);
        var items = new List<ProductDto>(page.Items.Count);
        foreach (var p in page.Items)
        {
            var stock = await _repo.GetStockAsync(p.Id, ct);
            items.Add(Map(p, stock));
        }
        return new PagedResult<ProductDto>
        {
            Items = items,
            Total = page.Total,
            Page = page.Page,
            PageSize = page.PageSize,
        };
    }

    public async Task<ProductDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var p = await _repo.GetByIdAsync(id, ct);
        if (p is null) return null;
        var stock = await _repo.GetStockAsync(p.Id, ct);
        return Map(p, stock);
    }

    public async Task<ProductDto> CreateAsync(ProductUpsertDto dto, CancellationToken ct = default)
    {
        if (await _repo.SkuExistsAsync(dto.Sku, null, ct))
            throw new BusinessRuleException("SKU_CONFLICT", $"SKU '{dto.Sku}' already exists.");

        var entity = new Product
        {
            Name = dto.Name.Trim(),
            Sku = dto.Sku.Trim().ToUpperInvariant(),
            Barcode = dto.Barcode,
            RegistrationNumber = dto.RegistrationNumber,
            GenericName = dto.GenericName,
            BrandName = dto.BrandName,
            Manufacturer = dto.Manufacturer,
            DrugClass = dto.DrugClass,
            Potency = dto.Potency,
            PackSize = dto.PackSize,
            CostPrice = dto.CostPrice,
            SellingPrice = dto.SellingPrice,
            DiscountPrice = dto.DiscountPrice,
            ReorderLevel = dto.ReorderLevel,
            ExpiryDate = dto.ExpiryDate,
            BatchNumber = dto.BatchNumber,
            IsActive = dto.IsActive,
            CategoryId = dto.CategoryId,
            SupplierId = dto.SupplierId,
        };

        await _repo.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        ProductLogger.LogProductCreated(_log, entity.Id, entity.Sku);
        return Map(entity, 0);
    }

    public async Task<ProductDto> UpdateAsync(Guid id, ProductUpsertDto dto, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(nameof(Product), id);

        if (entity.Sku != dto.Sku && await _repo.SkuExistsAsync(dto.Sku, entity.Id, ct))
            throw new BusinessRuleException("SKU_CONFLICT", $"SKU '{dto.Sku}' already exists.");

        entity.Name = dto.Name.Trim();
        entity.Sku = dto.Sku.Trim().ToUpperInvariant();
        entity.Barcode = dto.Barcode;
        entity.RegistrationNumber = dto.RegistrationNumber;
        entity.GenericName = dto.GenericName;
        entity.BrandName = dto.BrandName;
        entity.Manufacturer = dto.Manufacturer;
        entity.DrugClass = dto.DrugClass;
        entity.Potency = dto.Potency;
        entity.PackSize = dto.PackSize;
        entity.CostPrice = dto.CostPrice;
        entity.SellingPrice = dto.SellingPrice;
        entity.DiscountPrice = dto.DiscountPrice;
        entity.ReorderLevel = dto.ReorderLevel;
        entity.ExpiryDate = dto.ExpiryDate;
        entity.BatchNumber = dto.BatchNumber;
        entity.IsActive = dto.IsActive;
        entity.CategoryId = dto.CategoryId;
        entity.SupplierId = dto.SupplierId;
        entity.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        var stock = await _repo.GetStockAsync(entity.Id, ct);
        return Map(entity, stock);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(nameof(Product), id);
        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ProductDto> AdjustStockAsync(Guid id, ProductStockAdjustDto dto, Guid? userId, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(nameof(Product), id);

        if (entity.ExpiryDate.HasValue && entity.ExpiryDate.Value < DateTime.UtcNow)
            throw new BusinessRuleException("EXPIRED", "Cannot move stock of an expired product.");

        var current = await _repo.GetStockAsync(id, ct);
        var newBalance = current + dto.Quantity; // Quantity can be + (in) or - (out)

        if (newBalance < 0)
            throw new BusinessRuleException("NEGATIVE_STOCK",
                $"Insufficient stock. Current: {current}, attempted delta: {dto.Quantity}.");

        var movement = new InventoryMovement
        {
            ProductId = id,
            Quantity = dto.Quantity,
            BalanceAfter = newBalance,
            Type = dto.Quantity > 0 ? InventoryMovementType.Inbound : InventoryMovementType.Outbound,
            Note = dto.Note,
            PerformedByUserId = userId,
            Reference = "MANUAL",
        };

        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _movements.AddAsync(movement, ct);
            entity.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(entity, ct);
            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);
        }
        catch
        {
            await _uow.RollbackTransactionAsync(ct);
            throw;
        }

        ProductLogger.LogStockAdjusted(_log, entity.Sku, dto.Quantity, newBalance);

        return Map(entity, newBalance);
    }

    // ── Helpers ──────────────────────────────────────────────────
    private static ProductDto Map(Product p, int stock) => new(
        p.Id, p.Name, p.Sku, p.Barcode, p.RegistrationNumber, p.GenericName,
        p.BrandName, p.Manufacturer, p.DrugClass, p.Potency, p.PackSize,
        p.CostPrice, p.SellingPrice, p.DiscountPrice, p.ReorderLevel,
        p.ExpiryDate, p.BatchNumber, p.IsActive, stock,
        IsExpired: p.ExpiryDate.HasValue && p.ExpiryDate.Value < DateTime.UtcNow,
        IsLowStock: stock <= p.ReorderLevel,
        p.CategoryId,
        p.Category?.Name ?? string.Empty,
        p.SupplierId,
        p.Supplier?.Name ?? string.Empty,
        p.CreatedAt);
}
