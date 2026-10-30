using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>
/// B2B fiyat listesi. Bir bayi grubuna ya da tek bir bayiye bağlanır (ikisi birden olmaz); hiçbirine bağlı değilse atıl kalır.
/// Kanal fiyat kurallarından (#25) ayrı bir kavramdır: yalnızca bayi sitesinde kullanılır.
/// </summary>
public class PriceList : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Para birimi etiketi (TRY, USD, EUR); kur dönüşümü yapılmaz.</summary>
    public string Currency { get; set; } = "TRY";
    public Guid? GroupId { get; set; }
    public Guid? DealerId { get; set; }
    /// <summary>Listede fiyatı olmayan ürünlere uygulanacak, ürünün satış fiyatından yüzdesel iskonto (0–100); boşsa uygulanmaz.</summary>
    public decimal? DiscountPercent { get; set; }
    /// <summary>Kalemlere girilen fiyatlar KDV dahil mi.</summary>
    public bool PricesIncludeVat { get; set; }
    /// <summary>Bayi sitesinde fiyatlar KDV dahil mi gösterilsin.</summary>
    public bool ShowVatIncluded { get; set; }
    public bool IsActive { get; set; } = true;
    public List<PriceListItem> Items { get; set; } = [];
}

/// <summary>Fiyat listesindeki ürün fiyatı; liste başına ürün başına tek kayıt.</summary>
public class PriceListItem : TenantEntity
{
    public Guid PriceListId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Price { get; set; }
}
