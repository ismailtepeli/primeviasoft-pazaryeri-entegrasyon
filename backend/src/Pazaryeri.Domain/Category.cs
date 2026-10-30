using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>Kiracının ürün kategorisi; ParentId ile ağaç oluşturur. Kanal kategorilerine eşleme #21+ kapsamındadır.</summary>
public class Category : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
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
