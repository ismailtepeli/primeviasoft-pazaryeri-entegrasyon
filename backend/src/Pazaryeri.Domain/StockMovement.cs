using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

public static class StockMovementChannels
{
    /// <summary>Elle yapılan düzeltmelerin kanal adı.</summary>
    public const string Manual = "Manuel";
}

public enum StockMovementType
{
    /// <summary>Bir kanaldaki satış fiziksel stoğu düşürdü.</summary>
    Sale,
    /// <summary>İptal ya da iade stoğu geri ekledi.</summary>
    Restore,
    /// <summary>Elle düzeltme (ürün sayfasından stok değişikliği).</summary>
    Adjustment,
}

/// <summary>
/// Stok havuzunun defteri: fiziksel stoktaki her değişiklik burada izlenir. (Kanal, Tür, Referans, Ürün) dörtlüsü tekildir;
/// bu sayede aynı sipariş/iptal olayı yeniden işlense de stok bir kez değişir.
/// </summary>
public class StockMovement : TenantEntity
{
    public Guid ProductId { get; set; }
    /// <summary>Hareketin geldiği kanal ("Trendyol" gibi); elle düzeltmelerde "Manuel".</summary>
    public string Channel { get; set; } = string.Empty;
    public StockMovementType Type { get; set; }
    /// <summary>Stoğa yansıyan işaretli adet: satışta negatif, geri eklemede pozitif.</summary>
    public int Quantity { get; set; }
    /// <summary>İstenen adet; stok yetmediğinde (fazla satış) <see cref="Quantity"/> bundan küçük olur.</summary>
    public int Requested { get; set; }
    /// <summary>Hareketten sonraki fiziksel stok.</summary>
    public int StockAfter { get; set; }
    /// <summary>Olayın kimliği: satışta "sipariş/paket-satır", geri eklemede iptal/iade olayı, düzeltmede rastgele.</summary>
    public string Reference { get; set; } = string.Empty;
    /// <summary>Geri eklemede hangi satışa ait olduğu.</summary>
    public string? SaleReference { get; set; }
}
