using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>Kanallardan bağımsız ortak sipariş durumu; kanalın kendi durumu paketlerde ayrıca saklanır.</summary>
public enum OrderStatus
{
    New,
    Preparing,
    Shipped,
    Delivered,
    Cancelled,
    Returned,
}

/// <summary>Bir kanaldan gelen sipariş. (Kanal, sipariş no) kiracı içinde tekildir.</summary>
public class Order : TenantEntity
{
    public string Channel { get; set; } = string.Empty;
    /// <summary>Kanalın sipariş numarası.</summary>
    public string OrderNumber { get; set; } = string.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.New;
    public DateTime OrderDate { get; set; }
    public decimal TotalPrice { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    /// <summary>Kanalda son değişiklik zamanı (paketlerin en yenisi).</summary>
    public DateTime LastModifiedAt { get; set; }
    /// <summary>B2B siparişlerinde siparişi veren bayi; pazaryeri siparişlerinde boş.</summary>
    public Guid? DealerId { get; set; }
    public string? ShippingAddress { get; set; }
    public string? Note { get; set; }
    /// <summary>B2B: KDV hariç toplam (TotalPrice KDV dahildir). Pazaryeri siparişlerinde boş.</summary>
    public decimal? TotalNet { get; set; }
    public string? Currency { get; set; }
    /// <summary>B2B: sevk bilgisi (bayiye görünür).</summary>
    public string? ShipmentCarrier { get; set; }
    public string? TrackingNumber { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    /// <summary>B2B: ret/iptal gerekçesi (bayiye görünür).</summary>
    public string? StatusNote { get; set; }
    public List<OrderPackage> Packages { get; set; } = [];
}

/// <summary>
/// Siparişin kanal paketi. Paket bölünür ya da iptal olursa eski paket silinmez, kendi durumuyla kalır
/// (paket geçmişi korunur). (Kanal, paket no) kiracı içinde tekildir.
/// </summary>
public class OrderPackage : TenantEntity
{
    public Guid OrderId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    /// <summary>Kanalın bildirdiği ham durum ("Created", "Picking", ...).</summary>
    public string ChannelStatus { get; set; } = string.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.New;
    public DateTime LastModifiedAt { get; set; }
    public decimal TotalPrice { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
}

public class OrderLine : TenantEntity
{
    public Guid PackageId { get; set; }
    /// <summary>Kanalın satır kimliği; paket bölünse de aynı kalır.</summary>
    public string LineId { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    /// <summary>B2B: Price KDV dahil mi ve KDV oranı.</summary>
    public bool PriceIncludesVat { get; set; }
    public int VatRate { get; set; }
    /// <summary>B2B: ürün kaydı (sipariş geçmişinden tekrar sipariş için).</summary>
    public Guid? ProductId { get; set; }
    /// <summary>Satış stok havuzundan düşüldü (ya da denendi); aynı satır bir daha düşülmez.</summary>
    public bool StockDeducted { get; set; }
    /// <summary>İptal/iade stoğu geri eklendi (ya da denendi).</summary>
    public bool StockRestored { get; set; }
}

/// <summary>Kanal başına sipariş senkronunun imleci: bu zamandan sonra değişen paketler çekilir.</summary>
public class OrderSyncState : TenantEntity
{
    public string Channel { get; set; } = string.Empty;
    public DateTime Cursor { get; set; }
    public DateTime? LastSyncAt { get; set; }
}
