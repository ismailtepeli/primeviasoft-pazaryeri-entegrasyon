namespace Pazaryeri.Application.Abstractions;

/// <summary>İstekteki oturumun kiracısı. Oturum yoksa null döner.</summary>
public interface ITenantProvider
{
    Guid? TenantId { get; }
}
