using Microsoft.EntityFrameworkCore;
using Syntera.Application.Common;
using Syntera.Application.Interfaces;
using Syntera.Domain.Entities;

namespace Syntera.Infrastructure.Data;

/// <summary>
/// EF Core implementation of <see cref="IRepository{TEntity,TKey}"/>.
/// Derives from <see cref="RepositoryBase{T}"/> for the boring shared
/// plumbing (page, list, save) and adds aggregate-specific filters
/// in subclasses. This keeps DRY at the implementation layer while
/// still letting individual repositories inject extra query logic.
/// </summary>
public abstract class RepositoryBase<TEntity> : IRepository<TEntity, Guid>
    where TEntity : class
{
    protected AppDbContext Db { get; }
    protected DbSet<TEntity> Set { get; }

    protected RepositoryBase(AppDbContext db)
    {
        Db = db;
        Set = db.Set<TEntity>();
    }

    public virtual Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Set.FirstOrDefaultAsync(e => EF.Property<Guid>(e, "Id") == id, ct);

    public virtual async Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken ct = default)
        => await Set.AsNoTracking().ToListAsync(ct);

    public virtual async Task<PagedResult<TEntity>> PageAsync(PageQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        var q = Set.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search) && typeof(TEntity) == typeof(Product))
        {
            // specialised search implemented in ProductRepository; here we no-op
        }
        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<TEntity> { Items = items, Total = total, Page = page, PageSize = size };
    }

    public virtual Task AddAsync(TEntity entity, CancellationToken ct = default)
        => Set.AddAsync(entity, ct).AsTask();

    public virtual Task UpdateAsync(TEntity entity, CancellationToken ct = default)
    {
        Set.Update(entity);
        return Task.CompletedTask;
    }

    public virtual Task DeleteAsync(TEntity entity, CancellationToken ct = default)
    {
        Set.Remove(entity);
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => Db.SaveChangesAsync(ct);
}
