using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>
/// Kanal başına stok kuralı. Kanala açılan miktar = ⌊max(0, fiziksel stok − güvenlik stoğu) × yüzde ÷ 100⌋.
/// Kiracı başına kanal başına tek kural vardır.
/// </summary>
public class StockRule : TenantEntity
{
    public string Channel { get; set; } = string.Empty;
    /// <summary>Ürüne özel güvenlik stoğu girilmemişse kullanılan varsayılan; satılmayıp saklanan adet.</summary>
    public int SafetyStock { get; set; }
    /// <summary>Satılabilir stoğun bu kanala açılan payı (0–100).</summary>
    public decimal AllocationPercent { get; set; } = 100;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Kiracının stok davranışı ayarları (kiracı başına tek kayıt).</summary>
public class StockSettings : TenantEntity
{
    /// <summary>
    /// Açıksa bir kanalda satış olunca fiziksel stok düşer ve diğer tüm kanalların açılan miktarı yeniden hesaplanıp güncellenir
    /// (ortak stok havuzu). Kapalıysa bir kanaldaki satış diğer kanalları etkilemez. Davranışı sipariş akışı (#28) uygular.
    /// </summary>
    public bool DeductAcrossChannels { get; set; } = true;
}
