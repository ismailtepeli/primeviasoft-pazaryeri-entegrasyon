using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>Kiracının ürün kategorisi; ParentId ile ağaç oluşturur. Kanal kategorilerine eşleme #21+ kapsamındadır.</summary>
public class Category : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
}
