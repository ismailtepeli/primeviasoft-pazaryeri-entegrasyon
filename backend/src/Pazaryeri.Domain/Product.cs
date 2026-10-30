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
    public string? Brand { get; set; }
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    /// <summary>KDV oranı (%); kanallar gönderimde ister.</summary>
    public int VatRate { get; set; } = 20;
    /// <summary>Fiziksel stok (depodaki toplam adet).</summary>
    public int Stock { get; set; }
    /// <summary>Ürüne özel güvenlik stoğu; boşsa kanalın varsayılanı kullanılır.</summary>
    public int? SafetyStock { get; set; }
    public decimal Price { get; set; }
    /// <summary>B2B bayi sitesinde görünür mü (varsayılan kapalı; bilerek açılır).</summary>
    public bool B2BVisible { get; set; }
    /// <summary>B2B: bayinin tek siparişte alabileceği en az adet.</summary>
    public int MinOrderQuantity { get; set; } = 1;
    /// <summary>B2B: koli/çoklu adet; sipariş adedi bunun katı olmalı.</summary>
    public int PackSize { get; set; } = 1;
    /// <summary>Eşzamanlı güncellemeleri (aynı anda gelen siparişler) saptamak için satır sürümü.</summary>
    public byte[] RowVersion { get; set; } = [];
    /// <summary>Birim maliyet (TL); kanal fiyat kuralları satış fiyatını bundan hesaplar. Bilinmiyorsa null.</summary>
    public decimal? Cost { get; set; }
    /// <summary>Görseller; <see cref="ProductImage.Position"/> sırasına göre, ilki kapak.</summary>
    public List<ProductImage> Images { get; set; } = [];
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

public enum ImageKind
{
    /// <summary>Sunucuya yüklenmiş dosya.</summary>
    Upload,
    /// <summary>Dışarıdaki bir adrese işaret eder; dosya saklanmaz.</summary>
    Url,
}

/// <summary>Ürün görseli. Sıra 0'dan başlar; 0 kapak görselidir.</summary>
public class ProductImage : TenantEntity
{
    public const int MaxPerProduct = 8;
    /// <summary>Katalogda ürün başına önerilen asgari görsel sayısı.</summary>
    public const int RecommendedMin = 3;

    public Guid ProductId { get; set; }
    public int Position { get; set; }
    public ImageKind Kind { get; set; }
    /// <summary>Upload ise saklanan dosyanın uzantısı (.jpg/.png/.webp); Url ise boş.</summary>
    public string? Extension { get; set; }
    /// <summary>Url ise görselin adresi; Upload ise boş.</summary>
    public string? ExternalUrl { get; set; }
}

/// <summary>
/// Ürünün bir kanalda yayınlanması için gereken kanala özgü veriler (marka/kategori kimlikleri, özellikler).
/// Eşleme ekranları (kategori, özellik, marka eşlemesi) bu kaydı doldurur; eksikse ürün o kanala gönderilemez.
/// </summary>
public class ProductChannelData : TenantEntity
{
    public Guid ProductId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public long? BrandId { get; set; }
    public long? CategoryId { get; set; }
    public long? CargoCompanyId { get; set; }
    public decimal? DimensionalWeight { get; set; }
    /// <summary>[{"attributeId":1,"valueId":2,"customValue":null}] biçiminde kanal özellikleri.</summary>
    public string? AttributesJson { get; set; }
}
