using Syntera.Application.Common;

namespace Syntera.Application.Interfaces;

/// <summary>
/// Generic repository contract for aggregate roots. Keeps the
/// application layer decoupled from EF Core and lets us swap in
/// a NoSQL or event-sourced backing store in the future without
/// rewriting services. Methods are async-first; synchronous flows
/// are intentionally not supported.
/// </summary>
/// <typeparam name="TEntity">Aggregate root type.</typeparam>
/// <typeparam name="TKey">Primary key type.</typeparam>
public interface IRepository<TEntity, in TKey> where TEntity : class
{
    Task<TEntity?> GetByIdAsync(TKey id, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken ct = default);
    Task<PagedResult<TEntity>> PageAsync(PageQuery query, CancellationToken ct = default);
    Task AddAsync(TEntity entity, CancellationToken ct = default);
    Task UpdateAsync(TEntity entity, CancellationToken ct = default);
    Task DeleteAsync(TEntity entity, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// Unit of work boundary. Wraps a single transaction across one or
/// more repositories so that a sale, for instance, can atomically
/// write to Sales + SaleItems + InventoryMovements.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
}
