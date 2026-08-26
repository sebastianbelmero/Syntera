using System.Threading;
using Microsoft.Extensions.Logging;
using Syntera.Application.Common;
using Syntera.Application.DTOs.Categories;
using Syntera.Application.Interfaces;
using Syntera.Application.Logging;
using Syntera.Domain.Entities;
using Syntera.Domain.Exceptions;

namespace Syntera.Application.Services;

public interface ICategoryService
{
    Task<PagedResult<CategoryDto>> PageAsync(PageQuery query, CancellationToken ct = default);
    Task<CategoryDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryTreeNodeDto>> GetTreeAsync(CancellationToken ct = default);
    Task<CategoryDto> CreateAsync(CategoryUpsertDto dto, CancellationToken ct = default);
    Task<CategoryDto> UpdateAsync(Guid id, CategoryUpsertDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CategoryService> _log;

    public CategoryService(
        ICategoryRepository repo,
        IUnitOfWork uow,
        ILogger<CategoryService> log)
    {
        _repo = repo;
        _uow = uow;
        _log = log;
    }

    public async Task<PagedResult<CategoryDto>> PageAsync(PageQuery query, CancellationToken ct = default)
    {
        var page = await _repo.PageAsync(query, ct);
        var items = page.Items.Select(Map).ToList();
        return new PagedResult<CategoryDto>
        {
            Items = items,
            Total = page.Total,
            Page = page.Page,
            PageSize = page.PageSize,
        };
    }

    public async Task<CategoryDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<CategoryTreeNodeDto>> GetTreeAsync(CancellationToken ct = default)
    {
        var all = await _repo.ListAsync(ct);
        var lookup = all.ToDictionary(c => c.Id);

        var roots = new List<CategoryTreeNodeDto>();
        foreach (var c in all.Where(c => c.ParentId is null))
            roots.Add(Build(c, lookup));

        return roots;
    }

    public async Task<CategoryDto> CreateAsync(CategoryUpsertDto dto, CancellationToken ct = default)
    {
        var slug = Slugify(dto.Name);
        if (await _repo.SlugExistsAsync(slug, null, ct))
            throw new BusinessRuleException("SLUG_CONFLICT", $"Slug '{slug}' already exists.");

        var entity = new Category
        {
            Name = dto.Name.Trim(),
            Slug = slug,
            Description = dto.Description,
            ParentId = dto.ParentId,
        };

        await _repo.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        CategoryLogger.LogCategoryCreated(_log, entity.Id, entity.Slug);
        return Map(entity);
    }

    public async Task<CategoryDto> UpdateAsync(Guid id, CategoryUpsertDto dto, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(nameof(Category), id);

        var newSlug = Slugify(dto.Name);
        if (entity.Slug != newSlug && await _repo.SlugExistsAsync(newSlug, entity.Id, ct))
            throw new BusinessRuleException("SLUG_CONFLICT", $"Slug '{newSlug}' already exists.");

        // Cycle detection (self / direct parent)
        if (dto.ParentId.HasValue && dto.ParentId == entity.Id)
            throw new BusinessRuleException("CYCLE_PARENT", "A category cannot be its own parent.");

        entity.Name = dto.Name.Trim();
        entity.Slug = newSlug;
        entity.Description = dto.Description;
        entity.ParentId = dto.ParentId;
        entity.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(nameof(Category), id);

        // Soft delete only — referential integrity kept by EF filter.
        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
    }

    // ── Helpers ──────────────────────────────────────────────────
    private static CategoryDto Map(Category c) => new(
        c.Id, c.Name, c.Slug, c.Description, c.ParentId, null, 0, c.CreatedAt);

    private static CategoryTreeNodeDto Build(Category c, Dictionary<Guid, Category> lookup)
    {
        var children = lookup.Values.Where(x => x.ParentId == c.Id)
            .Select(x => Build(x, lookup)).ToList();
        return new CategoryTreeNodeDto(c.Id, c.Name, c.Slug, children);
    }

    private static string Slugify(string text)
    {
        var slug = text.Trim().ToLowerInvariant()
            .Replace(' ', '-').Replace('/', '-');
        // collapse repeated dashes
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}
