using Microsoft.Extensions.Logging;
using Syntera.Application.Common;
using Syntera.Application.DTOs.Sales;
using Syntera.Application.Interfaces;
using Syntera.Application.Logging;
using Syntera.Domain.Entities;
using Syntera.Domain.Enums;
using Syntera.Domain.Exceptions;

namespace Syntera.Application.Services;

public interface ISaleService
{
    Task<PagedResult<SaleDto>> PageAsync(PageQuery query, CancellationToken ct = default);
    Task<SaleDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<SaleDto> CreateAsync(SaleCreateDto dto, Guid? cashierId, CancellationToken ct = default);
    Task<SaleDto> UpdateStatusAsync(Guid id, SaleStatus status, CancellationToken ct = default);
}

public sealed class SaleService : ISaleService
{
    private readonly ISaleRepository _sales;
    private readonly IProductRepository _products;
    private readonly IInventoryRepository _movements;
    private readonly ICustomerRepository _customers;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<SaleService> _log;

    public SaleService(
        ISaleRepository sales,
        IProductRepository products,
        IInventoryRepository movements,
        ICustomerRepository customers,
        IUnitOfWork uow,
        ILogger<SaleService> log)
    {
        _sales = sales;
        _products = products;
        _movements = movements;
        _customers = customers;
        _uow = uow;
        _log = log;
    }

    public async Task<PagedResult<SaleDto>> PageAsync(PageQuery query, CancellationToken ct = default)
    {
        var page = await _sales.PageAsync(query, ct);
        return new PagedResult<SaleDto>
        {
            Items = page.Items.Select(Map).ToList(),
            Total = page.Total,
            Page = page.Page,
            PageSize = page.PageSize,
        };
    }

    public async Task<SaleDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var s = await _sales.GetWithItemsAsync(id, ct);
        return s is null ? null : Map(s);
    }

    public async Task<SaleDto> CreateAsync(SaleCreateDto dto, Guid? cashierId, CancellationToken ct = default)
    {
        var customer = await _customers.GetByIdAsync(dto.CustomerId, ct)
            ?? throw new NotFoundException(nameof(Customer), dto.CustomerId);

        if (dto.Items.Count == 0)
            throw new BusinessRuleException("EMPTY_SALE", "Sale must have at least one line.");

        // Pre-validate products, expiry, and stock BEFORE touching DB
        var resolvedItems = new List<(Product Product, int Qty, decimal UnitPrice, decimal Discount)>();
        foreach (var item in dto.Items)
        {
            var product = await _products.GetByIdAsync(item.ProductId, ct)
                ?? throw new NotFoundException(nameof(Product), item.ProductId);

            if (!product.IsActive)
                throw new BusinessRuleException("INACTIVE_PRODUCT",
                    $"Product '{product.Name}' is inactive and cannot be sold.");

            if (product.ExpiryDate.HasValue && product.ExpiryDate.Value < DateTime.UtcNow)
                throw new BusinessRuleException("EXPIRED_PRODUCT",
                    $"Product '{product.Name}' is expired.");

            var currentStock = await _products.GetStockAsync(product.Id, ct);
            if (currentStock < item.Quantity)
                throw new BusinessRuleException("INSUFFICIENT_STOCK",
                    $"Insufficient stock for '{product.Name}'. Have: {currentStock}, need: {item.Quantity}.");

            resolvedItems.Add((product, item.Quantity, item.UnitPrice, item.DiscountAmount));
        }

        var sale = new Sale
        {
            InvoiceNumber = await _sales.NextInvoiceNumberAsync(ct),
            Status = SaleStatus.Paid,
            SaleDate = dto.SaleDate ?? DateTime.UtcNow,
            CustomerId = customer.Id,
            CashierUserId = cashierId,
            Note = dto.Note,
            TaxRate = dto.TaxRate,
            DiscountAmount = dto.DiscountAmount,
        };

        decimal subTotal = 0;
        foreach (var (product, qty, unitPrice, discount) in resolvedItems)
        {
            var lineTotal = (unitPrice * qty) - discount;
            subTotal += lineTotal;
            sale.Items.Add(new SaleItem
            {
                ProductId = product.Id,
                Quantity = qty,
                UnitPrice = unitPrice,
                DiscountAmount = discount,
                LineTotal = lineTotal,
            });
        }

        sale.SubTotal = subTotal;
        sale.TaxAmount = Math.Round(subTotal * dto.TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
        sale.GrandTotal = subTotal + sale.TaxAmount - sale.DiscountAmount;

        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _sales.AddAsync(sale, ct);
            // Deduct stock atomically — outbound movements reference the sale invoice
            foreach (var (product, qty, _, _) in resolvedItems)
            {
                var balanceAfter = await _products.GetStockAsync(product.Id, ct) - qty;
                await _movements.AddAsync(new InventoryMovement
                {
                    ProductId = product.Id,
                    Quantity = -qty,
                    BalanceAfter = balanceAfter,
                    Type = InventoryMovementType.Outbound,
                    Reference = sale.InvoiceNumber,
                    PerformedByUserId = cashierId,
                    Note = $"Auto: {sale.InvoiceNumber}",
                }, ct);
            }
            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);
        }
        catch
        {
            await _uow.RollbackTransactionAsync(ct);
            throw;
        }

        SaleLogger.LogSaleCreated(_log, sale.InvoiceNumber, sale.GrandTotal);
        var fresh = await _sales.GetWithItemsAsync(sale.Id, ct) ?? sale;
        return Map(fresh);
    }

    public async Task<SaleDto> UpdateStatusAsync(Guid id, SaleStatus status, CancellationToken ct = default)
    {
        var sale = await _sales.GetWithItemsAsync(id, ct)
            ?? throw new NotFoundException(nameof(Sale), id);

        // Allowed transitions
        var allowed = sale.Status switch
        {
            SaleStatus.Draft => new[] { SaleStatus.Pending, SaleStatus.Cancelled },
            SaleStatus.Pending => new[] { SaleStatus.Paid, SaleStatus.Cancelled },
            SaleStatus.Paid => new[] { SaleStatus.Shipped, SaleStatus.Cancelled },
            SaleStatus.Shipped => new[] { SaleStatus.Completed },
            _ => Array.Empty<SaleStatus>(),
        };

        if (!allowed.Contains(status))
            throw new BusinessRuleException("ILLEGAL_TRANSITION",
                $"Cannot transition from {sale.Status} to {status}.");

        sale.Status = status;
        sale.UpdatedAt = DateTime.UtcNow;
        await _sales.UpdateAsync(sale, ct);
        await _uow.SaveChangesAsync(ct);
        return Map(sale);
    }

    // ── Helpers ──────────────────────────────────────────────────
    private static SaleDto Map(Sale s) => new(
        s.Id, s.InvoiceNumber, s.Status, s.SaleDate, s.CustomerId,
        s.Customer?.Name ?? string.Empty,
        s.CashierUserId, null,
        s.SubTotal, s.TaxRate, s.TaxAmount, s.DiscountAmount, s.GrandTotal, s.Note,
        s.Items.Select(i => new SaleItemDto(
            i.Id, i.ProductId,
            i.Product?.Name ?? string.Empty,
            i.Product?.Sku ?? string.Empty,
            i.Quantity, i.UnitPrice, i.DiscountAmount, i.LineTotal)).ToList(),
        s.CreatedAt);
}
