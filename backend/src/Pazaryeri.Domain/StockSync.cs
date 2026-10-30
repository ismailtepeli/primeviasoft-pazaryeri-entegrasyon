using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>
/// Bir ürünün bir kanala EN SON gönderilen (ve kanalın kabul ettiği) stok ve fiyatı. Senkron yalnızca bundan farklı
/// olanları gönderir (delta). Kayıt yoksa ürün kanala henüz hiç stok-fiyat gönderilmemiştir.
/// </summary>
public class ChannelStockPriceState : TenantEntity
{
    public Guid ProductId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    /// <summary>Barkod başına fiyat güncelleme sınırı (dakikada 30) için pencere başlangıcı.</summary>
    public DateTime? PriceWindowStart { get; set; }
    public int PriceUpdatesInWindow { get; set; }
}

/// <summary>Kanala gönderilmiş bir stok-fiyat toplu işlemi (en çok 1.000 barkod); item sonucu sonradan işlenir.</summary>
public class StockPriceBatch : TenantEntity
{
    public string Channel { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public int ItemCount { get; set; }
    /// <summary>Sonuç bekleniyor mu.</summary>
    public bool Pending { get; set; } = true;
    public int FailedCount { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>Gönderilen barkodlar (JSON dizi); sonuç eşlemesi için.</summary>
    public string BarcodesJson { get; set; } = "[]";
    /// <summary>İlk birkaç hata ("barkod: neden"); yoksa boş.</summary>
    public string? ErrorSummary { get; set; }
}

/// <summary>Son gönderilen toplu işlem gövdelerinin özeti: aynı gövde 15 dakika içinde tekrar gönderilmez.</summary>
public class SentBodyHash : TenantEntity
{
    public string Channel { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
