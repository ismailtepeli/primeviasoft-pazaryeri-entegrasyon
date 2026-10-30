using Pazaryeri.Domain;

namespace Pazaryeri.Application.Pricing;

/// <param name="Quantity">Yalnızca Exact kipinde dolu.</param>
/// <param name="Label">Gösterilecek metin; Hidden kipinde null.</param>
public record StockView(int? Quantity, string? Label, bool InStock);

/// <summary>Bayi sitesinde stoğun nasıl görüneceği; satılabilir stok = fiziksel stok − ürünün güvenlik stoğu (en az 0).</summary>
public static class StockDisplay
{
    public const string InStock = "Stokta var", Low = "Az kaldı", OutOfStock = "Tükendi";

    public static int Available(int physical, int? safety) => Math.Max(0, physical - Math.Max(0, safety ?? 0));

    public static StockView Compute(int physical, int? safety, StockDisplayMode mode, int lowThreshold)
    {
        var available = Available(physical, safety);
        var inStock = available > 0;
        return mode switch
        {
            StockDisplayMode.Exact => new StockView(available, available.ToString(), inStock),
            StockDisplayMode.Hidden => new StockView(null, null, inStock),
            _ => new StockView(null, available <= 0 ? OutOfStock : available <= lowThreshold ? Low : InStock, inStock),
        };
    }
}
