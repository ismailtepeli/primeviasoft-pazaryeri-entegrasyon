using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pazaryeri.Application.Notifications;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Notifications;

/// <summary>Bildirim yayımı ve okuma. Kiracı filtresi bağlamdan gelir; bayi bildirimleri ayrıca bayi kimliğine bağlanır.</summary>
public class NotificationService(AppDbContext db, ILogger<NotificationService>? logger = null) : INotifier
{
    public const int PageSize = 30;
    private const int MaxText = 500;

    public Task NotifyAdminAsync(string kind, string title, string? body, string? link, string? dedupeKey = null, CancellationToken ct = default) =>
        PublishAsync(new Notification { Audience = NotificationAudience.Admin }, kind, title, body, link, dedupeKey, ct);

    public Task NotifyDealerAsync(Guid dealerId, string kind, string title, string? body, string? link, string? dedupeKey = null, CancellationToken ct = default) =>
        PublishAsync(new Notification { Audience = NotificationAudience.Dealer, DealerId = dealerId }, kind, title, body, link, dedupeKey, ct);

    private async Task PublishAsync(Notification n, string kind, string title, string? body, string? link, string? dedupeKey, CancellationToken ct)
    {
        n.Kind = kind;
        n.Title = Cut(title, 200)!;
        n.Body = Cut(body, MaxText);
        n.Link = link is { Length: > 0 } && link.StartsWith('/') && !link.StartsWith("//") ? Cut(link, 300) : null; // yalnızca uygulama içi adres
        n.DedupeKey = Cut(dedupeKey, 200);
        try
        {
            if (n.DedupeKey is not null && await db.Notifications.AnyAsync(x => x.DedupeKey == n.DedupeKey, ct)) return;
            db.Notifications.Add(n);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException) // eşzamanlı aynı anahtar ya da geçici hata: bildirim kaybı işi bozmaz
        {
            db.Entry(n).State = EntityState.Detached;
            logger?.LogWarning(ex, "Bildirim yazılamadı: {Kind}", kind);
        }
    }

    private static string? Cut(string? s, int max)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length <= max ? t : t[..(max - 1)] + "…";
    }

    private IQueryable<Notification> For(NotificationAudience audience, Guid? dealerId) =>
        db.Notifications.Where(n => n.Audience == audience && (audience == NotificationAudience.Admin || n.DealerId == dealerId));

    public async Task<NotificationPage> ListAsync(NotificationAudience audience, Guid? dealerId, CancellationToken ct)
    {
        var q = For(audience, dealerId).AsNoTracking();
        var unread = await q.CountAsync(n => n.ReadAt == null, ct);
        var items = await q.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(PageSize)
            .Select(n => new NotificationItem(n.Id, n.Kind, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt != null)).ToListAsync(ct);
        return new NotificationPage(items, unread);
    }

    public Task<int> UnreadCountAsync(NotificationAudience audience, Guid? dealerId, CancellationToken ct) =>
        For(audience, dealerId).AsNoTracking().CountAsync(n => n.ReadAt == null, ct);

    /// <summary>Bildirimi okundu yapar; başkasının ya da olmayan kayıt yok sayılır (false).</summary>
    public async Task<bool> MarkReadAsync(NotificationAudience audience, Guid? dealerId, Guid id, CancellationToken ct)
    {
        var scoped = For(audience, dealerId);
        if (!await scoped.AnyAsync(n => n.Id == id, ct)) return false;
        var now = DateTime.UtcNow;
        await scoped.Where(n => n.Id == id && n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        return true;
    }

    public async Task MarkAllReadAsync(NotificationAudience audience, Guid? dealerId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await For(audience, dealerId).Where(n => n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }
}
