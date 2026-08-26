namespace Syntera.Domain.Entities;

/// <summary>
/// Supplier / manufacturer / distributor. A supplier can supply
/// multiple products and a product can have multiple suppliers in the
/// long run; the model currently uses a 1:N relation (single supplier
/// per product) for simplicity but the schema reserves the relationship
/// on the product side so flipping to M:N later is a non-breaking
/// change (just add a join table and rerun migrations).
/// </summary>
public sealed class Supplier : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? ContactPerson { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? PostalCode { get; set; }

    /// <summary>BPOM distributor licence number (Narkotika/Psikotropika).</summary>
    public string? LicenseNumber { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }

    // Navigation
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
