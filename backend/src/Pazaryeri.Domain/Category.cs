using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>Kiracının ürün kategorisi; ParentId ile ağaç oluşturur. Kanal kategorilerine eşleme #21+ kapsamındadır.</summary>
public class Category : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    /// <summary>B2B bayi sitesinde bu kategori (ve alt kategorileri) gizli mi.</summary>
    public bool B2BHidden { get; set; }
}

/// <summary>
/// Kiracının kendi kategorisinin bir kanalın kategorisine eşlemesi. Kanal tarafında yalnızca en alt seviye
/// (yaprak) kategori seçilebilir. Kanal kategorisi kiracıdan bağımsız katalogdadır (ChannelCategory).
/// </summary>
public class CategoryMapping : TenantEntity
{
    public Guid CategoryId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public long ExternalCategoryId { get; set; }
}

/// <summary>
/// Kiracı kategorisindeki ürünlerin bir kanal özelliğini (ör. Renk) nasıl dolduracağı: ya tüm ürünler için sabit bir değer,
/// ya da ürünün kendi özelliğinden (Varyant alanındaki "Renk: Mavi" gibi bir anahtar) değer eşlemesiyle.
/// Kanal kategorisi, kategori eşlemesinden (<see cref="CategoryMapping"/>) gelir.
/// </summary>
public class AttributeMapping : TenantEntity
{
    public Guid CategoryId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public long ExternalAttributeId { get; set; }
    /// <summary>Ürünün kendi özellik adı ("Renk"); sabit değer kullanılıyorsa boş.</summary>
    public string? SourceAttribute { get; set; }
    public long? FixedValueId { get; set; }
    public string? FixedCustomValue { get; set; }
    public List<AttributeValueMapping> Values { get; set; } = [];
}

/// <summary>Ürünün kendi değerinin ("Mavi") kanal değerine (ya da serbest metne) eşlemesi.</summary>
public class AttributeValueMapping : TenantEntity
{
    public Guid AttributeMappingId { get; set; }
    public string SourceValue { get; set; } = string.Empty;
    public long? ExternalValueId { get; set; }
    public string? CustomValue { get; set; }
}
