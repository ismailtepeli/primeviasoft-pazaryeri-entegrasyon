using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>
/// Dropshipping tedarikçisinin XML ürün beslemesi. Adres periyodik okunur; ürünler barkoda göre eklenir, stok ve fiyat güncel tutulur.
/// Ad, açıklama, görsel, marka ve kategori yalnızca ürün ilk eklenirken yazılır (sonradan yapılan elle düzenleme ezilmez).
/// </summary>
public class ProductFeed : TenantEntity
{
    public const int MaxNameLength = 100;
    public const int MaxUrlLength = 2000;
    public const int MinIntervalMinutes = 15;
    public const int MaxIntervalMinutes = 24 * 60;

    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    /// <summary>Ürün öğesinin XML yolu, örn. "Urunler/Urun" ya da "rss/channel/item"; çözümlemeyle bulunur.</summary>
    public string ItemPath { get; set; } = string.Empty;
    /// <summary>Alan eşlemesi: rol → XML yolları (JSON). Roller <c>FeedRoles</c> içindedir.</summary>
    public string MappingJson { get; set; } = "{}";
    public string? Format { get; set; }
    /// <summary>Satış fiyatı = besleme fiyatı × (1 + kâr yüzdesi / 100); besleme fiyatı ürünün maliyetine yazılır.</summary>
    public decimal MarkupPercent { get; set; }
    /// <summary>Besleme fiyatı KDV dahil mi; değilse satış fiyatına KDV eklenir.</summary>
    public bool PriceIncludesVat { get; set; } = true;
    /// <summary>Beslemede KDV oranı yoksa kullanılan oran (%).</summary>
    public int DefaultVatRate { get; set; } = 20;
    /// <summary>Beslemede yalnızca "stokta var/yok" bilgisi varsa stokta var için yazılacak adet.</summary>
    public int AvailableStock { get; set; } = 10;
    public bool SyncPrice { get; set; } = true;
    public bool SyncStock { get; set; } = true;
    /// <summary>Beslemeden kalkan ürünlerin stoğu sıfırlanır (ürün silinmez).</summary>
    public bool ZeroMissing { get; set; } = true;
    public bool PublishB2C { get; set; }
    public bool PublishB2B { get; set; }
    public int IntervalMinutes { get; set; } = 60;

    public DateTime? LastRunAt { get; set; }
    /// <summary>Başlangıç zamanı; doluysa iş sürüyor (takılı kalmasına karşı zaman aşımıyla yok sayılır).</summary>
    public DateTime? RunningSince { get; set; }
    /// <summary>Success, Partial (bazı kalemler hatalı) ya da Failed.</summary>
    public string? LastStatus { get; set; }
    public int LastCreated { get; set; }
    public int LastUpdated { get; set; }
    public int LastSkipped { get; set; }
    public int LastFailed { get; set; }
    public string? LastError { get; set; }
    /// <summary>Son çalışmadaki ilk kalem hataları (JSON).</summary>
    public string? LastErrorsJson { get; set; }
}
