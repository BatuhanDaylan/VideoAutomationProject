namespace VideoAutomation.Domain.Entities;

/// <summary>
/// Base entity with common properties for all database entities.
/// </summary>
public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
