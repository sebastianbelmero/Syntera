namespace Syntera.Domain.Entities;

/// <summary>
/// Customer (apotek / klinik / rumah sakit / end-user). Decoupled
/// from IdentityUser — the buyer profile is independent of any
/// operator who logs in to use the application. A single Identity
/// user may act on behalf of many customers in a B2B flow.
/// </summary>
public sealed class Customer : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? ContactPerson { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? PostalCode { get; set; }

    /// <summary>NPWP / SIUP — for B2B invoicing.</summary>
    public string? TaxId { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }

    // Navigation
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
}
