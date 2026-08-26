using Syntera.Application.Common;
using Syntera.Domain.Entities;

namespace Syntera.Application.Interfaces;

/// <summary>Strongly-typed repositories per aggregate — keeps the
/// service layer expressive and IDE-friendly (no magic strings).</summary>

public interface IProductRepository : IRepository<Product, Guid>
{
    Task<PagedResult<Product>> SearchAsync(
        string? search,
        Guid? categoryId,
        Guid? supplierId,
        bool? activeOnly,
        PageQuery page,
        CancellationToken ct = default);

    Task<bool> SkuExistsAsync(string sku, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>Computes on-hand quantity for a product by replaying
    /// the inventory ledger. Single source of truth — never cached.</summary>
    Task<int> GetStockAsync(Guid productId, CancellationToken ct = default);
}

public interface ICategoryRepository : IRepository<Category, Guid>
{
    Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default);
}

public interface ISupplierRepository : IRepository<Supplier, Guid> { }

public interface ICustomerRepository : IRepository<Customer, Guid> { }

public interface IInventoryRepository : IRepository<InventoryMovement, Guid>
{
    Task<IReadOnlyList<InventoryMovement>> HistoryAsync(Guid productId, CancellationToken ct = default);
}

public interface ISaleRepository : IRepository<Sale, Guid>
{
    Task<string> NextInvoiceNumberAsync(CancellationToken ct = default);
    Task<Sale?> GetWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Sale>> ListWithItemsAsync(DateTime? since = null, CancellationToken ct = default);
}
