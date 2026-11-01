namespace Pazaryeri.Application.Notifications;

public static class NotificationKinds
{
    public const string B2BOrderNew = "b2b-order-new";
    public const string B2BOrderCancelled = "b2b-order-cancelled";
    public const string B2BOrderLost = "b2b-order-lost";
    public const string MarketplaceOrderNew = "marketplace-order-new";
    public const string DealerApplication = "dealer-application";
    public const string OrderReceived = "order-received";
    public const string OrderApproved = "order-approved";
    public const string OrderRejected = "order-rejected";
    public const string OrderShipped = "order-shipped";
    public const string OrderDelivered = "order-delivered";
    public const string OrderCancelled = "order-cancelled";
}

public record NotificationItem(Guid Id, string Kind, string Title, string? Body, string? Link, DateTime CreatedAt, bool Read);

/// <param name="Unread">Okunmamış toplam (listedeki sayfadan bağımsız).</param>
public record NotificationPage(IReadOnlyList<NotificationItem> Items, int Unread);

/// <summary>
/// Bildirim yayımlama. En iyi çaba: bildirim yazılamazsa asıl iş (sipariş, durum geçişi) etkilenmez.
/// <paramref name="dedupeKey"/> doluysa aynı anahtarla ikinci bildirim oluşmaz.
/// </summary>
public interface INotifier
{
    Task NotifyAdminAsync(string kind, string title, string? body, string? link, string? dedupeKey = null, CancellationToken ct = default);

    Task NotifyDealerAsync(Guid dealerId, string kind, string title, string? body, string? link, string? dedupeKey = null, CancellationToken ct = default);
}
