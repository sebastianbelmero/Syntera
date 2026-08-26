using Microsoft.EntityFrameworkCore;
using Syntera.Application.Common;
using Syntera.Application.Interfaces;
using Syntera.Domain.Entities;
using Syntera.Infrastructure.Data;

namespace Syntera.Infrastructure.Repositories;

public sealed class ProductRepository : RepositoryBase<Product>, IProductRepository
{
    public ProductRepository(AppDbContext db) : base(db) { }

    public async Task<bool> SkuExistsAsync(string sku, Guid? excludeId = null, CancellationToken ct = default)
    {
        var q = Db.Products.Where(p => p.Sku == sku);
        if (excludeId.HasValue) q = q.Where(p => p.Id != excludeId.Value);
        return await q.AnyAsync(ct);
    }

    public async Task<PagedResult<Product>> SearchAsync(
        string? search, Guid? categoryId, Guid? supplierId,
        bool? activeOnly, PageQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        var q = Db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Supplier)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(p =>
                p.Name.Contains(search) ||
                p.Sku.Contains(search) ||
                (p.GenericName != null && p.GenericName.Contains(search)) ||
                (p.BrandName != null && p.BrandName.Contains(search)));
        if (categoryId.HasValue) q = q.Where(p => p.CategoryId == categoryId.Value);
        if (supplierId.HasValue) q = q.Where(p => p.SupplierId == supplierId.Value);
        if (activeOnly == true) q = q.Where(p => p.IsActive);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);
        return new PagedResult<Product> { Items = items, Total = total, Page = page, PageSize = size };
    }

    public override Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Db.Products
            .Include(p => p.Category)
            .Include(p => p.Supplier)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <summary>
    /// On-hand quantity = sum of inventory movements for a product.
    /// This is the canonical source of truth — never cached and
    /// never stored as a column on <see cref="Product"/>.
    /// </summary>
    public async Task<int> GetStockAsync(Guid productId, CancellationToken ct = default)
        => await Db.InventoryMovements
            .Where(m => m.ProductId == productId)
            .SumAsync(m => (int?)m.Quantity, ct) ?? 0;
}

public sealed class InventoryRepository : RepositoryBase<InventoryMovement>, IInventoryRepository
{
    public InventoryRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<InventoryMovement>> HistoryAsync(Guid productId, CancellationToken ct = default)
    {
        var items = await Db.InventoryMovements
            .AsNoTracking()
            .Include(m => m.Product)
            .Where(m => m.ProductId == productId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(ct);
        return items;
    }

    public override async Task<PagedResult<InventoryMovement>> PageAsync(PageQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        IQueryable<InventoryMovement> q = Db.InventoryMovements.AsNoTracking().Include(m => m.Product);
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(m =>
                (m.Product != null && m.Product.Name.Contains(query.Search)) ||
                (m.Reference != null && m.Reference.Contains(query.Search)));
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);
        return new PagedResult<InventoryMovement> { Items = items, Total = total, Page = page, PageSize = size };
    }
}

public sealed class SaleRepository : RepositoryBase<Sale>, ISaleRepository
{
    public SaleRepository(AppDbContext db) : base(db) { }

    public async Task<string> NextInvoiceNumberAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        var today = DateTime.UtcNow.Date;
        var prefix = $"INV-{year}-";
        var seq = await Db.Sales.CountAsync(s => s.CreatedAt >= today && s.InvoiceNumber.StartsWith(prefix), ct);
        return $"{prefix}{(seq + 1):D6}";
    }

    public Task<Sale?> GetWithItemsAsync(Guid id, CancellationToken ct = default)
        => Db.Sales
            .Include(s => s.Customer)
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Sale>> ListWithItemsAsync(DateTime? since = null, CancellationToken ct = default)
    {
        var q = Db.Sales
            .Include(s => s.Customer)
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .AsNoTracking();
        if (since.HasValue) q = q.Where(s => s.SaleDate.HasValue && s.SaleDate.Value >= since.Value);
        var items = await q.OrderByDescending(s => s.SaleDate).ToListAsync(ct);
        return items;
    }

    public override async Task<PagedResult<Sale>> PageAsync(PageQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        var q = Db.Sales
            .Include(s => s.Customer)
            .Include(s => s.Items)
            .AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(s =>
                s.InvoiceNumber.Contains(query.Search) ||
                (s.Customer != null && s.Customer.Name.Contains(query.Search)));
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(s => s.SaleDate)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);
        return new PagedResult<Sale> { Items = items, Total = total, Page = page, PageSize = size };
    }
}
