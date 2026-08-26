namespace Syntera.Domain.Entities;

/// <summary>
/// Base class for all domain entities. Provides a strongly-typed
/// <see cref="Id"/> (Guid) and audit metadata (<see cref="CreatedAt"/>,
/// <see cref="UpdatedAt"/>) so every record carries an immutable creation
/// timestamp and a last-mutation timestamp — both written by the EF Core
/// SaveChanges pipeline (see <c>AppDbContext.SaveOverride</c>), never by
/// business code, to keep audit data trustworthy across aggregates.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
