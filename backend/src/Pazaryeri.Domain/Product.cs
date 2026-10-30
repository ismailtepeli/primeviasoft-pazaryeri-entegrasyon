using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>Ürünün bir kanaldaki durumu.</summary>
public enum ListingStatus
{
    /// <summary>Kanala gönderildi, kanalın onayı bekleniyor.</summary>
    Pending,
    Live,
    /// <summary>Kanal ürünü reddetti ya da gönderim başarısız oldu.</summary>
    Error,
    /// <summary>Kanal için zorunlu bilgi (eşleme, özellik vb.) eksik; gönderilemez.</summary>
    Missing,
    /// <summary>Kanalda yayından kaldırılmış.</summary>
    Passive,
}

/// <summary>Kiracının ana katalogundaki ürün.</summary>
public class Product : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    /// <summary>Varyantları gruplayan ürün ana kodu (productMainId); tek başına ürünlerde boş.</summary>
    public string? MainId { get; set; }
    /// <summary>Varyant özeti, örn. "Beden: M, Renk: Mavi".</summary>
    public string? Variant { get; set; }
    public int Stock { get; set; }
    public decimal Price { get; set; }
    public List<ProductListing> Listings { get; set; } = [];
}

/// <summary>Ürünün bir kanaldaki durumu; ürün başına kanal başına tek kayıt.</summary>
public class ProductListing : TenantEntity
{
    public Guid ProductId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public ListingStatus Status { get; set; } = ListingStatus.Pending;
    /// <summary>Kanalın bildirdiği hata nedenleri (batch failureReasons); birden fazlaysa satır satır.</summary>
    public string? ErrorMessage { get; set; }
}
