using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>Hesaplanan fiyatın nasıl yuvarlanacağı.</summary>
public enum PriceRounding
{
    /// <summary>Kuruşa yuvarla (2 hane).</summary>
    None,
    /// <summary>En yakın tam lira.</summary>
    NearestWhole,
    /// <summary>Yukarı doğru tam lira.</summary>
    UpWhole,
    /// <summary>Yukarı doğru en yakın "…,90".</summary>
    Ending90,
    /// <summary>Yukarı doğru en yakın "…,99".</summary>
    Ending99,
}

/// <summary>
/// Kanal başına fiyat kuralı. Satış fiyatı = (maliyet + kargo) × (1 + kâr) ÷ (1 − komisyon), ardından yuvarlama.
/// Kiracı başına kanal başına tek kural vardır.
/// </summary>
public class PricingRule : TenantEntity
{
    public string Channel { get; set; } = string.Empty;
    /// <summary>Pazaryerinin satıştan kestiği komisyon (%); 0 ile 100 arasında (100 hariç).</summary>
    public decimal CommissionPercent { get; set; }
    /// <summary>Ürün başına kargo maliyeti (₺).</summary>
    public decimal ShippingCost { get; set; }
    /// <summary>Maliyet üzerine eklenen kâr (%).</summary>
    public decimal ProfitPercent { get; set; }
    public PriceRounding Rounding { get; set; } = PriceRounding.None;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Kural uygulanarak bir ürün için bir kanalda hesaplanmış satış fiyatı.</summary>
public class ChannelPrice : TenantEntity
{
    public Guid ProductId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
}
