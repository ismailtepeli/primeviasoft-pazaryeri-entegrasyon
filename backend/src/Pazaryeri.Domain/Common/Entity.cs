namespace Pazaryeri.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Kiracıya ait kayıt. Sorgularda otomatik olarak TenantId ile filtrelenir.</summary>
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}

public abstract class TenantEntity : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
}
