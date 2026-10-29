using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

public enum SyncJobType
{
    CatalogSync,
    ProductSend,
    StockPriceSync,
    OrderSync,
    InvoiceSend,
}

public enum SyncJobStatus
{
    Pending,
    Running,
    Retrying,
    Succeeded,
    Failed,
}

/// <summary>Kanal ile yapılan her senkron işinin kaydı (kuyruğa atılan işin izi).</summary>
public class SyncJob : TenantEntity
{
    public SyncJobType Type { get; set; }
    /// <summary>Pazaryeri kanalı, örn. "Trendyol" veya "Amazon".</summary>
    public string Channel { get; set; } = string.Empty;
    public SyncJobStatus Status { get; set; } = SyncJobStatus.Pending;
    /// <summary>Kanalın döndürdüğü toplu işlem (batch) kimliği; sonuç takibi için.</summary>
    public string? BatchId { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    /// <summary>Hatadan etkilenen kayıt sayısı (örn. reddedilen ürün adedi); bilinmiyorsa 0.</summary>
    public int AffectedCount { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
