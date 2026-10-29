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
}

/// <summary>Kiracının ana katalogundaki ürün.</summary>
public class Product : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
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
    public string? ErrorMessage { get; set; }
}
