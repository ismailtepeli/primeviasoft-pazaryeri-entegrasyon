using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

public enum NotificationAudience
{
    /// <summary>Yönetici paneli; kiracının tüm yöneticileri aynı bildirimleri görür (okundu bilgisi ortaktır).</summary>
    Admin,
    /// <summary>Tek bir bayi (<see cref="Notification.DealerId"/>); firmanın tüm kullanıcıları görür.</summary>
    Dealer,
}

/// <summary>
/// Uygulama içi bildirim: sipariş geldiğinde ve süreç değiştiğinde yöneticiye ya da bayiye gösterilir. <see cref="DedupeKey"/>
/// doluysa (kiracı, anahtar) tekildir; aynı olay yeniden işlense de tek bildirim oluşur.
/// </summary>
public class Notification : TenantEntity
{
    public NotificationAudience Audience { get; set; }
    public Guid? DealerId { get; set; }
    /// <summary>Bildirim türü (ör. b2b-order-new); ekranda simge/renk seçmek için.</summary>
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    /// <summary>Uygulama içi göreli adres (ör. /siparis/{id}).</summary>
    public string? Link { get; set; }
    public string? DedupeKey { get; set; }
    public DateTime? ReadAt { get; set; }
}
