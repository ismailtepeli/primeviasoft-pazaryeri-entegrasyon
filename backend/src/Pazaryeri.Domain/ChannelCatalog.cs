using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>
/// Kanalın herkese açık kataloğu (kategori ağacı, markalar, özellikler). Kiracıya özgü değildir: tüm kiracılar aynı
/// önbelleği paylaşır ve herhangi bir bağlı satıcının bilgisiyle yenilenir. Kaldırılan kayıtlar silinmez, IsActive=false olur.
/// </summary>
public abstract class CatalogEntry : Entity
{
    public string Channel { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    /// <summary>Kaydın içeriği (ad, üst, zorunluluk…) en son ne zaman değişti ya da kaldırıldı.</summary>
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

public class ChannelCategory : CatalogEntry
{
    public long ExternalId { get; set; }
    public long? ParentExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Alt kategorisi yok; ürün yalnızca bu kategorilere aktarılabilir.</summary>
    public bool IsLeaf { get; set; }
}

public class ChannelBrand : CatalogEntry
{
    public long ExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Bir kategorinin özelliği; aynı özellik farklı kategorilerde farklı zorunluluk taşıyabilir.</summary>
public class ChannelAttribute : CatalogEntry
{
    public long CategoryExternalId { get; set; }
    public long ExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Required { get; set; }
    /// <summary>Değer listesi dışında serbest metin de girilebilir.</summary>
    public bool AllowCustom { get; set; }
    public bool Varianter { get; set; }
    public bool Slicer { get; set; }
}

public class ChannelAttributeValue : CatalogEntry
{
    public long CategoryExternalId { get; set; }
    public long AttributeExternalId { get; set; }
    public long ExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public enum CatalogKind
{
    Categories,
    Brands,
    Attributes,
}

/// <summary>Bir katalog senkronunun özeti; ne kadar kayıt eklendi, değişti, kaldırıldı.</summary>
public class CatalogSyncRun : Entity
{
    public string Channel { get; set; } = string.Empty;
    public CatalogKind Kind { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public int Added { get; set; }
    public int Changed { get; set; }
    public int Removed { get; set; }
    public string? Error { get; set; }
}
