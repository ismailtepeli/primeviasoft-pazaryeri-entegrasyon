using Pazaryeri.Domain;

namespace Pazaryeri.Application.Orders;

/// <summary>Yöneticinin B2B siparişi üzerindeki işlemleri.</summary>
public enum B2BAction { Approve, Reject, Ship, Deliver, Cancel }

/// <summary>
/// B2B sipariş akışı (mevcut durum kümesi üzerinde): Yeni = onay bekliyor, Hazırlanıyor = onaylandı ve hazırlanıyor,
/// Sevk edildi, Teslim edildi; iptal ve ret İptal durumuna götürür ve stoğu geri ekler. Sevk edilmiş sipariş iptal edilemez (iade ayrı akıştır).
/// </summary>
public static class B2BOrderFlow
{
    public static OrderStatus? Next(OrderStatus current, B2BAction action) => (current, action) switch
    {
        (OrderStatus.New, B2BAction.Approve) => OrderStatus.Preparing,
        (OrderStatus.New, B2BAction.Reject) => OrderStatus.Cancelled,
        (OrderStatus.Preparing, B2BAction.Ship) => OrderStatus.Shipped,
        (OrderStatus.Shipped, B2BAction.Deliver) => OrderStatus.Delivered,
        (OrderStatus.New or OrderStatus.Preparing, B2BAction.Cancel) => OrderStatus.Cancelled,
        _ => null,
    };

    public static IReadOnlyList<B2BAction> Allowed(OrderStatus status) =>
        Enum.GetValues<B2BAction>().Where(a => Next(status, a) is not null).ToList();

    /// <summary>Durumu geri ekleyen (stoğu iade eden) işlemler.</summary>
    public static bool RestoresStock(B2BAction action) => action is B2BAction.Reject or B2BAction.Cancel;

    public const int MaxCarrierLength = 100;
    public const int MaxTrackingLength = 100;
    public const int MaxReasonLength = 500;

    public static string? ValidateShipment(string? carrier, string? tracking) =>
        string.IsNullOrWhiteSpace(carrier) ? "Kargo firması zorunlu."
        : carrier.Trim().Length > MaxCarrierLength ? $"Kargo firması en fazla {MaxCarrierLength} karakter olabilir."
        : string.IsNullOrWhiteSpace(tracking) ? "Takip numarası zorunlu."
        : tracking.Trim().Length > MaxTrackingLength ? $"Takip numarası en fazla {MaxTrackingLength} karakter olabilir." : null;
}
