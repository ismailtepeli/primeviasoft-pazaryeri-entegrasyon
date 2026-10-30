using Pazaryeri.Domain;

namespace Pazaryeri.Application.Marketplace;

/// <summary>Kanalın ham sipariş/paket durumunu ortak duruma çevirir.</summary>
public static class OrderStatusMapper
{
    public static OrderStatus Map(string channel, string channelStatus) => channelStatus.Trim().ToLowerInvariant() switch
    {
        "created" or "awaiting" => OrderStatus.New,
        "picking" or "invoiced" => OrderStatus.Preparing,
        "shipped" or "atcollectionpoint" => OrderStatus.Shipped,
        "delivered" => OrderStatus.Delivered,
        "cancelled" or "unsupplied" => OrderStatus.Cancelled,
        "returned" or "undelivered" or "unpacked" => OrderStatus.Returned,
        _ => OrderStatus.New,
    };

    /// <summary>Stok düşümü/iade için ayrım: satış sayılan durumlar.</summary>
    public static bool IsActive(OrderStatus s) => s is OrderStatus.New or OrderStatus.Preparing or OrderStatus.Shipped or OrderStatus.Delivered;
    public static bool IsReversed(OrderStatus s) => s is OrderStatus.Cancelled or OrderStatus.Returned;
}
