using Syntera.Domain.Enums;

namespace Syntera.Domain.Entities;

/// <summary>
/// Drug / product category (e.g. "Antibiotik", "Analgesik",
/// "Suplemen Vitamin"). A category owns a unique <see cref="Slug"/>
/// used by the front-end for SEO-friendly URLs and a nullable
/// <see cref="ParentId"/> that supports a self-referencing tree of
/// arbitrary depth (e.g. "Suplemen › Vitamin › B-Complex").
/// </summary>
public sealed class Category : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid? ParentId { get; set; }

    /// <summary>Soft-delete flag. Filtered automatically by EF query filters.</summary>
    public bool IsDeleted { get; set; }

    // Navigation
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
