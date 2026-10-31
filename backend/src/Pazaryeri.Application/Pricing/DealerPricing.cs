namespace Pazaryeri.Application.Pricing;

public enum PriceSource { General, GroupList, DealerList }

/// <summary>Bir fiyat listesinin çözümleme için gereken kısmı.</summary>
public record PriceListRule(decimal? DiscountPercent, bool PricesIncludeVat, bool ShowVatIncluded, string Currency);

public record ResolvedPrice(decimal Net, decimal Gross, decimal Display, bool DisplayIncludesVat, string Currency, PriceSource Source);

/// <summary>
/// Bayiye uygulanacak fiyatın tek çözümleme noktası. Öncelik: bayiye özel liste > bayi grubu listesi > genel satış fiyatı.
/// Her listede önce kalem fiyatı, yoksa liste iskontosu uygulanır; ikisi de yoksa bir alt düzeye inilir.
/// Genel satış fiyatı (Product.Price) KDV dahil kabul edilir (pazaryeri satış fiyatı); gösterimi kiracı ayarına (generalShowVatIncluded) bağlıdır.
/// </summary>
public static class DealerPricing
{
    public const string BaseCurrency = "TRY";

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static ResolvedPrice Resolve(decimal generalPrice, int vatRate,
        PriceListRule? dealerList, decimal? dealerItemPrice, PriceListRule? groupList, decimal? groupItemPrice, bool generalShowVatIncluded = true)
    {
        if (TryList(generalPrice, vatRate, dealerList, dealerItemPrice, PriceSource.DealerList) is { } d) return d;
        if (TryList(generalPrice, vatRate, groupList, groupItemPrice, PriceSource.GroupList) is { } g) return g;
        return Build(generalPrice, includesVat: true, vatRate, show: generalShowVatIncluded, BaseCurrency, PriceSource.General);
    }

    private static ResolvedPrice? TryList(decimal generalPrice, int vatRate, PriceListRule? list, decimal? itemPrice, PriceSource source)
    {
        if (list is null) return null;
        if (itemPrice is { } p) return Build(p, list.PricesIncludeVat, vatRate, list.ShowVatIncluded, list.Currency, source);
        if (list.DiscountPercent is { } pct and > 0)
        {
            var discounted = generalPrice * (1 - pct / 100m);
            // İskonto genel (KDV dahil) fiyattan hesaplanır; liste KDV hariç fiyat tutsa da sonuç dahil tabanlıdır.
            return Build(discounted, true, vatRate, list.ShowVatIncluded, list.Currency, source);
        }
        return null;
    }

    private static ResolvedPrice Build(decimal price, bool includesVat, int vatRate, bool show, string currency, PriceSource source)
    {
        var factor = 1 + vatRate / 100m;
        var gross = includesVat ? price : price * factor;
        var net = includesVat ? price / factor : price;
        gross = Round(gross); net = Round(net);
        return new ResolvedPrice(net, gross, show ? gross : net, show, currency, source);
    }
}

public record QuantityCheck(bool Valid, int Suggested, string? Error);

/// <summary>Minimum sipariş ve koli adedi doğrulaması.</summary>
public static class OrderQuantityRules
{
    /// <summary>Ürün ayarlarını tutarlı hale getirir: adetler en az 1; en az sipariş koli adedinin katına yuvarlanır.</summary>
    public static (int Min, int Pack) Normalize(int minOrder, int packSize)
    {
        var pack = Math.Max(packSize, 1);
        var min = Math.Max(minOrder, 1);
        return ((min + pack - 1) / pack * pack, pack);
    }

    public static QuantityCheck Check(int quantity, int minOrder, int packSize)
    {
        var (min, pack) = Normalize(minOrder, packSize);
        if (quantity < 1) return new QuantityCheck(false, min, "Adet en az 1 olmalı.");
        if (quantity < min) return new QuantityCheck(false, min, $"En az {min} adet sipariş verilebilir.");
        if (quantity % pack != 0)
        {
            var up = (quantity + pack - 1) / pack * pack;
            return new QuantityCheck(false, up, $"Bu ürün {pack} adetlik kolilerle satılır; {up} adet girin.");
        }
        return new QuantityCheck(true, quantity, null);
    }

    /// <summary>Kalem düzeyindeki geçersiz kılma değerleri: verilen (boş olmayan) alan en az 1, en çok 100000 olmalı.</summary>
    public static string? ValidateOverrides(int? minOrder, int? packSize) =>
        minOrder is < 1 or > 100000 || packSize is < 1 or > 100000 ? "Minimum ve koli adedi 1 ile 100000 arasında olmalı." : null;

    public static string? ValidateSettings(int minOrder, int packSize) =>
        minOrder < 1 || packSize < 1 ? "Minimum sipariş ve koli adedi en az 1 olmalı."
        : minOrder > 100000 || packSize > 100000 ? "Adetler 100000'i aşamaz." : null;
}
