using Pazaryeri.Application.Marketplace;
using Pazaryeri.Application.RateLimiting;

namespace Pazaryeri.Infrastructure.Marketplace.Trendyol;

/// <summary>Trendyol'un 14 Eylül 2026'dan beri uyguladığı servis grubu limitleri (istek/dk).</summary>
public enum TrendyolServiceGroup { ProductRead, ProductWrite, InventoryPriceWrite, Other }

public static class TrendyolRateLimits
{
    /// <summary>Aynı uç noktaya 10 saniyede en fazla 50 istek; 51.'de 429.</summary>
    public static readonly RateLimit PerEndpoint = new(50, TimeSpan.FromSeconds(10));

    /// <summary>Aynı barkod için dakikada en fazla 30 fiyat güncellemesi (stok-fiyat worker'ı kullanır).</summary>
    public static readonly RateLimit PricePerBarcode = RateLimit.PerMinute(30);

    // Satır: listeleme seviyesi. Sütun: ProductRead, ProductWrite, InventoryPriceWrite.
    private static readonly int[][] PerMinute =
    [
        [1000, 200, 350],   // 50.000 ürün
        [1250, 300, 500],   // 75.000 ürün
        [1500, 400, 1000],  // 150.000 ürün
        [1750, 500, 1500],  // 500.000 ürün
        [2000, 600, 2000],  // limitsiz
    ];

    /// <summary>Servis grubunun dakikalık limiti; grup dışı servisler (sipariş vb.) için null.</summary>
    public static RateLimit? ForGroup(ListingTier tier, TrendyolServiceGroup group) => group switch
    {
        TrendyolServiceGroup.ProductRead => RateLimit.PerMinute(PerMinute[(int)tier][0]),
        TrendyolServiceGroup.ProductWrite => RateLimit.PerMinute(PerMinute[(int)tier][1]),
        TrendyolServiceGroup.InventoryPriceWrite => RateLimit.PerMinute(PerMinute[(int)tier][2]),
        _ => null,
    };

    /// <summary>Bir istek için kovalar: varsa grup kovası + her zaman uç nokta kovası.</summary>
    public static IReadOnlyList<RateLimitRule> RulesFor(ListingTier tier, TrendyolServiceGroup group, HttpMethod method, string path)
    {
        var rules = new List<RateLimitRule>();
        if (ForGroup(tier, group) is { } g) rules.Add(new RateLimitRule($"group:{group}", g));
        var endpoint = path.Split('?')[0];
        rules.Add(new RateLimitRule($"endpoint:{method.Method} {endpoint}", PerEndpoint));
        return rules;
    }
}
