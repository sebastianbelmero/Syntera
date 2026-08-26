using Microsoft.EntityFrameworkCore;
using Syntera.Application.Common;
using Syntera.Application.Interfaces;
using Syntera.Domain.Entities;
using Syntera.Infrastructure.Data;

namespace Syntera.Infrastructure.Repositories;

public sealed class CategoryRepository : RepositoryBase<Category>, ICategoryRepository
{
    public CategoryRepository(AppDbContext db) : base(db) { }

    public async Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default)
    {
        var q = Db.Categories.Where(c => c.Slug == slug);
        if (excludeId.HasValue) q = q.Where(c => c.Id != excludeId.Value);
        return await q.AnyAsync(ct);
    }

    public override async Task<PagedResult<Category>> PageAsync(PageQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        var q = Db.Categories.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(c => c.Name.Contains(query.Search));
        var total = await q.CountAsync(ct);
        var items = await q
            .OrderBy(c => c.Name)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);
        return new PagedResult<Category> { Items = items, Total = total, Page = page, PageSize = size };
    }
}

public sealed class SupplierRepository : RepositoryBase<Supplier>, ISupplierRepository
{
    public SupplierRepository(AppDbContext db) : base(db) { }

    public override async Task<PagedResult<Supplier>> PageAsync(PageQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        var q = Db.Suppliers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(s =>
                s.Name.Contains(query.Search) ||
                (s.ContactPerson != null && s.ContactPerson.Contains(query.Search)) ||
                (s.City != null && s.City.Contains(query.Search)));
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<Supplier> { Items = items, Total = total, Page = page, PageSize = size };
    }
}

public sealed class CustomerRepository : RepositoryBase<Customer>, ICustomerRepository
{
    public CustomerRepository(AppDbContext db) : base(db) { }

    public override async Task<PagedResult<Customer>> PageAsync(PageQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        var q = Db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(c =>
                c.Name.Contains(query.Search) ||
                (c.ContactPerson != null && c.ContactPerson.Contains(query.Search)) ||
                (c.Phone != null && c.Phone.Contains(query.Search)));
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<Customer> { Items = items, Total = total, Page = page, PageSize = size };
    }
}
