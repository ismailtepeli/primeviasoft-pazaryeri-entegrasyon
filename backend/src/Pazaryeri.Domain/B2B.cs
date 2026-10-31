using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

public enum StockDisplayMode
{
    /// <summary>Kesin adet gösterilir.</summary>
    Exact,
    /// <summary>"Stokta var", "Az kaldı", "Tükendi" gösterilir.</summary>
    Level,
    /// <summary>Stok bilgisi gösterilmez.</summary>
    Hidden,
}

/// <summary>Kiracının B2B bayi sitesi ayarları (kiracı başına tek kayıt).</summary>
public class B2BSettings : TenantEntity
{
    public StockDisplayMode StockDisplay { get; set; } = StockDisplayMode.Level;
    /// <summary>Satılabilir stok bu değere eşit ya da altındaysa "Az kaldı" denir.</summary>
    public int LowStockThreshold { get; set; } = 5;
    /// <summary>Fiyat listesi dışı (genel) fiyatın bayiye KDV dahil mi hariç mi gösterileceği. Varsayılan dahil; liste fiyatları kendi ayarını kullanır.</summary>
    public bool ShowVatIncluded { get; set; } = true;
}

/// <summary>Bir bayi grubuna bir ürünü ya da kategoriyi (alt kategorileriyle) gizler. ProductId ya da CategoryId'den tam biri doludur.</summary>
public class GroupVisibilityRule : TenantEntity
{
    public Guid GroupId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? CategoryId { get; set; }
}
