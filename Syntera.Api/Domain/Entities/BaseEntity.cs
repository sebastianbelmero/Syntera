namespace Syntera.Domain.Entities;

/// <summary>
/// Base class for all domain entities. Provides a strongly-typed
/// <see cref="Id"/> (Guid) and audit metadata (<see cref="CreatedAt"/>,
/// <see cref="UpdatedAt"/>). Both timestamps are written exclusively by
/// the EF Core SaveChanges pipeline (see <c>AppDbContext.OnSave</c>),
/// never by business code, to keep audit data trustworthy.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Soft-deletable entities carry <see cref="IsDeleted"/>; the SaveChanges
/// pipeline flips this flag and applies a global query filter so deleted
/// rows are invisible to normal queries but remain in the database for
/// audit/forensic purposes (no destructive deletes in Syntera).
/// </summary>
public abstract class SoftDeletableEntity : BaseEntity
{
    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}
