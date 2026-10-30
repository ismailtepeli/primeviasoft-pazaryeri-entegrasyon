using System.Security.Cryptography;
using System.Text;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Domain;

namespace Pazaryeri.Application.Stock;

/// <summary>Stok-fiyat senkronunun saf mantığı: değişiklik tespiti, barkod başına fiyat sınırı, gövde özeti.</summary>
public static class StockPriceDelta
{
    /// <summary>Trendyol stok-fiyat isteği başına en fazla bu kadar barkod alır.</summary>
    public const int MaxItemsPerRequest = 1000;
    /// <summary>Aynı barkod için dakikada en fazla bu kadar fiyat güncellemesi yapılır.</summary>
    public const int MaxPriceUpdatesPerBarcodePerMinute = 30;
    /// <summary>Aynı gövde bu süre içinde yeniden gönderilmez.</summary>
    public static readonly TimeSpan DuplicateBodyWindow = TimeSpan.FromMinutes(15);
    /// <summary>Toplu işlem sonucunu bekleme süresi; dolarsa sonuç bilinmiyor sayılır ve ürünler yeniden gönderilir.</summary>
    public static readonly TimeSpan BatchTimeout = TimeSpan.FromHours(4);
    public static readonly TimeSpan PriceWindow = TimeSpan.FromMinutes(1);

    /// <summary>Hiç gönderilmemişse ya da stok/fiyat son gönderilenden farklıysa değişmiştir.</summary>
    public static bool IsChanged(ChannelStockPriceState? state, int quantity, decimal price) =>
        state is null || state.Quantity != quantity || state.Price != price;

    public static bool PriceChanged(ChannelStockPriceState? state, decimal price) => state is null || state.Price != price;

    /// <summary>
    /// Fiyatı değişen barkod için dakikalık fiyat güncelleme sınırını uygular. Sınır dolmuşsa izin verilmez (öğe sonraki
    /// çalıştırmaya kalır); verilirse güncellenmiş pencere bilgisi döner. Pencere süresi geçtiyse sayaç sıfırlanır.
    /// </summary>
    public static (bool Allowed, DateTime WindowStart, int Count) PriceGuard(ChannelStockPriceState? state, DateTime now)
    {
        var start = state?.PriceWindowStart;
        var count = state?.PriceUpdatesInWindow ?? 0;
        if (start is null || now - start.Value >= PriceWindow) return (true, now, 1);
        return count >= MaxPriceUpdatesPerBarcodePerMinute ? (false, start.Value, count) : (true, start.Value, count + 1);
    }

    /// <summary>Gövdenin barkoda göre sıralanmış, kültürden bağımsız özeti (aynı içerik → aynı özet).</summary>
    public static string BodyHash(IEnumerable<StockPriceItem> items)
    {
        var canonical = string.Join('\n', items.OrderBy(i => i.Barcode, StringComparer.Ordinal)
            .Select(i => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{i.Barcode}|{i.Quantity}|{i.SalePrice:0.00}|{i.ListPrice:0.00}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
