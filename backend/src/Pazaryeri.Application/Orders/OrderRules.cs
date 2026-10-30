using Pazaryeri.Domain;

namespace Pazaryeri.Application.Orders;

/// <summary>Sipariş listesi kuralları: sekmeler ve gecikme.</summary>
public static class OrderRules
{
    /// <summary>Yeni sipariş bu süre içinde toplamaya alınmalı.</summary>
    public static readonly TimeSpan NewDeadline = TimeSpan.FromHours(24);
    /// <summary>Hazırlanan sipariş bu süre içinde kargoya verilmeli.</summary>
    public static readonly TimeSpan PreparingDeadline = TimeSpan.FromHours(48);

    public static bool IsLate(OrderStatus status, DateTime orderDateUtc, DateTime nowUtc) => status switch
    {
        OrderStatus.New => nowUtc - orderDateUtc > NewDeadline,
        OrderStatus.Preparing => nowUtc - orderDateUtc > PreparingDeadline,
        _ => false,
    };

    /// <summary>Sekme adı ("new", "preparing", "shipped", "delivered", "cancelled") → durumlar; bilinmiyorsa null.</summary>
    public static OrderStatus[]? StatusesForTab(string? tab) => tab?.ToLowerInvariant() switch
    {
        "new" => [OrderStatus.New],
        "preparing" => [OrderStatus.Preparing],
        "shipped" => [OrderStatus.Shipped],
        "delivered" => [OrderStatus.Delivered],
        "cancelled" => [OrderStatus.Cancelled, OrderStatus.Returned],
        _ => null,
    };
}
