using Pazaryeri.Domain;

namespace Pazaryeri.Application.Pricing;

/// <param name="Base">Maliyet + kargo.</param>
/// <param name="WithProfit">Kâr eklenmiş tutar.</param>
/// <param name="BeforeRounding">Komisyon payı eklenmiş, yuvarlanmamış tutar.</param>
/// <param name="Price">Yuvarlanmış satış fiyatı.</param>
public record PriceBreakdown(decimal Base, decimal WithProfit, decimal BeforeRounding, decimal Price, decimal CommissionAmount, decimal NetProfit);

public static class PriceCalculator
{
    public const decimal MaxCommission = 99.99m;
    public const decimal MaxProfit = 1000m;
    public const decimal MaxShipping = 100_000m;
    public const decimal MaxCost = 100_000_000m;

    /// <summary>Kural değerleri geçersizse hata metni; geçerliyse null.</summary>
    public static string? Validate(decimal commissionPercent, decimal shippingCost, decimal profitPercent)
    {
        if (commissionPercent < 0 || commissionPercent > MaxCommission) return $"Komisyon 0 ile {MaxCommission:0.##} arasında olmalı.";
        if (profitPercent < 0 || profitPercent > MaxProfit) return $"Kâr 0 ile {MaxProfit:0} arasında olmalı.";
        if (shippingCost < 0 || shippingCost > MaxShipping) return "Kargo maliyeti negatif ya da çok büyük olamaz.";
        return null;
    }

    /// <summary>(maliyet + kargo) × (1 + kâr) ÷ (1 − komisyon) ve yuvarlama. Maliyet sıfırdan büyük olmalıdır.</summary>
    public static PriceBreakdown Calculate(decimal cost, decimal commissionPercent, decimal shippingCost, decimal profitPercent, PriceRounding rounding)
    {
        if (cost <= 0 || cost > MaxCost) throw new ArgumentOutOfRangeException(nameof(cost), "Maliyet sıfırdan büyük olmalı.");
        if (Validate(commissionPercent, shippingCost, profitPercent) is { } error) throw new ArgumentException(error);

        var baseAmount = cost + shippingCost;
        var withProfit = baseAmount * (1 + profitPercent / 100m);
        var before = withProfit / (1 - commissionPercent / 100m);
        var price = Round(before, rounding);
        var commission = decimal.Round(price * commissionPercent / 100m, 2, MidpointRounding.AwayFromZero);
        return new PriceBreakdown(Money(baseAmount), Money(withProfit), Money(before), price, commission, Money(price - commission - baseAmount));
    }

    public static PriceBreakdown Calculate(decimal cost, PricingRule rule) =>
        Calculate(cost, rule.CommissionPercent, rule.ShippingCost, rule.ProfitPercent, rule.Rounding);

    private static decimal Money(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);

    public static decimal Round(decimal value, PriceRounding rounding)
    {
        var cents = Money(value);
        return rounding switch
        {
            PriceRounding.None => cents,
            PriceRounding.NearestWhole => decimal.Round(value, 0, MidpointRounding.AwayFromZero),
            // "Yukarı" yuvarlamalar gerçek değer üzerinden yapılır: fiyat hesaplanan tutarın altına inmez (marj korunur).
            PriceRounding.UpWhole => decimal.Ceiling(value),
            PriceRounding.Ending90 => UpToEnding(value, 0.90m),
            PriceRounding.Ending99 => UpToEnding(value, 0.99m),
            _ => cents,
        };
    }

    /// <summary>Verilen tutardan küçük olmayan, kuruş kısmı <paramref name="ending"/> olan en küçük değer.</summary>
    private static decimal UpToEnding(decimal value, decimal ending)
    {
        var whole = decimal.Floor(value);
        var candidate = whole + ending;
        return value <= candidate ? candidate : candidate + 1;
    }
}
