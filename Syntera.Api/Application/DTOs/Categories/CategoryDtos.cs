namespace Syntera.Application.DTOs.Categories;

public sealed record CategoryDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid? ParentId,
    string? ParentName,
    int ProductCount,
    DateTime CreatedAt);

public sealed record CategoryUpsertDto(
    string Name,
    string? Description,
    Guid? ParentId);

public sealed record CategoryTreeNodeDto(
    Guid Id,
    string Name,
    string Slug,
    IReadOnlyList<CategoryTreeNodeDto> Children) : IReadOnlyList<CategoryTreeNodeDto>
{
    private readonly IReadOnlyList<CategoryTreeNodeDto> _inner = Children;
    public CategoryTreeNodeDto this[int index] => _inner[index];
    public int Count => _inner.Count;
    public IEnumerator<CategoryTreeNodeDto> GetEnumerator() => _inner.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _inner.GetEnumerator();
}
