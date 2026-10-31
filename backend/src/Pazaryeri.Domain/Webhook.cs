using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>Kanalın bu kiracıya çağıracağı webhook'un gizli anahtarı (yalnızca SHA-256 özeti saklanır). Kanal başına tek kayıt.</summary>
public class WebhookEndpoint : TenantEntity
{
    public string Channel { get; set; } = string.Empty;
    public string SecretHash { get; set; } = string.Empty;
    /// <summary>Kanalın döndürdüğü webhook kimliği (varsa).</summary>
    public string? ExternalId { get; set; }
    public DateTime? LastEventAt { get; set; }
}

/// <summary>Alınmış webhook bildirimi; (kanal, paket, durum) başına tek kayıt olduğundan kanalın tekrar denemeleri çift işlem yaratmaz.</summary>
public class WebhookEvent : TenantEntity
{
    public string Channel { get; set; } = string.Empty;
    public string EventKey { get; set; } = string.Empty;
    public string? PackageId { get; set; }
    public string? Status { get; set; }
    public DateTime ReceivedAt { get; set; }
}
