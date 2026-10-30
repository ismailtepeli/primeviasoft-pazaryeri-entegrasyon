namespace Pazaryeri.Application.Stock;

public static class StockCalculator
{
    public const int MaxStock = 10_000_000;

    /// <summary>Ürüne özel güvenlik stoğu varsa o, yoksa kanalın varsayılanı geçerlidir.</summary>
    public static int EffectiveSafety(int? productSafety, int ruleSafety) => productSafety ?? ruleSafety;

    /// <summary>Kanal için satılabilir miktar: stoktan güvenlik stoğu düşülür, kalanın yüzdesi aşağı yuvarlanır; asla negatif olmaz.</summary>
    public static int ChannelQuantity(int physicalStock, int safetyStock, decimal allocationPercent)
    {
        var sellable = Math.Max(0, physicalStock - Math.Max(0, safetyStock));
        var percent = Math.Clamp(allocationPercent, 0m, 100m);
        return (int)decimal.Floor(sellable * percent / 100m);
    }

    /// <summary>Kural değerleri geçersizse hata metni; geçerliyse null.</summary>
    public static string? Validate(int safetyStock, decimal allocationPercent)
    {
        if (safetyStock < 0 || safetyStock > MaxStock) return "Güvenlik stoğu 0 ile 10.000.000 arasında olmalı.";
        if (allocationPercent < 0 || allocationPercent > 100) return "Kanala açılan pay 0 ile 100 arasında olmalı.";
        return null;
    }
}
