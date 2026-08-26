using Microsoft.Extensions.Logging;
using Syntera.Application.Common;
using Syntera.Application.DTOs.Inventory;
using Syntera.Application.Interfaces;
using Syntera.Domain.Entities;
using Syntera.Domain.Exceptions;

namespace Syntera.Application.Services;

public interface IInventoryService
{
    Task<IReadOnlyList<InventoryMovementDto>> HistoryAsync(Guid productId, CancellationToken ct = default);
    Task<PagedResult<InventoryMovementDto>> PageAsync(PageQuery query, CancellationToken ct = default);
    Task<InventoryMovementDto> RecordAsync(InventoryAdjustmentRequest req, Guid? userId, CancellationToken ct = default);
}

public sealed class InventoryService : IInventoryService
{
    private readonly IInventoryRepository _repo;
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<InventoryService> _log;

    public InventoryService(
        IInventoryRepository repo,
        IProductRepository products,
        IUnitOfWork uow,
        ILogger<InventoryService> log)
    {
        _repo = repo;
        _products = products;
        _uow = uow;
        _log = log;
    }

    public async Task<IReadOnlyList<InventoryMovementDto>> HistoryAsync(Guid productId, CancellationToken ct = default)
    {
        var list = await _repo.HistoryAsync(productId, ct);
        return list.Select(Map).ToList();
    }

    public async Task<PagedResult<InventoryMovementDto>> PageAsync(PageQuery query, CancellationToken ct = default)
    {
        var page = await _repo.PageAsync(query, ct);
        return new PagedResult<InventoryMovementDto>
        {
            Items = page.Items.Select(Map).ToList(),
            Total = page.Total,
            Page = page.Page,
            PageSize = page.PageSize,
        };
    }

    public async Task<InventoryMovementDto> RecordAsync(InventoryAdjustmentRequest req, Guid? userId, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(req.ProductId, ct)
            ?? throw new NotFoundException(nameof(Product), req.ProductId);

        if (product.ExpiryDate.HasValue && product.ExpiryDate.Value < DateTime.UtcNow)
            throw new BusinessRuleException("EXPIRED", "Cannot move stock of an expired product.");

        var current = await _products.GetStockAsync(req.ProductId, ct);
        var newBalance = current + req.Quantity;

        if (newBalance < 0)
            throw new BusinessRuleException("NEGATIVE_STOCK",
                $"Insufficient stock. Current: {current}, attempted delta: {req.Quantity}.");

        var movement = new InventoryMovement
        {
            ProductId = req.ProductId,
            Type = req.Type,
            Quantity = req.Quantity,
            BalanceAfter = newBalance,
            Reference = req.Reference,
            Note = req.Note,
            PerformedByUserId = userId,
        };

        await _repo.AddAsync(movement, ct);
        await _uow.SaveChangesAsync(ct);
        _log.LogInformation("Inventory movement recorded: {Type} {Qty} on {Sku}",
            movement.Type, movement.Quantity, product.Sku);
        return Map(movement);
    }

    private static InventoryMovementDto Map(InventoryMovement m) => new(
        m.Id, m.ProductId,
        m.Product?.Name ?? string.Empty,
        m.Product?.Sku ?? string.Empty,
        m.Type, m.Quantity, m.BalanceAfter,
        m.Reference, m.Note, m.PerformedByUserId, m.CreatedAt);
}
