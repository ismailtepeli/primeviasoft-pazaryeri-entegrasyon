using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Notifications;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Auth;
using Pazaryeri.Infrastructure.Notifications;

namespace Pazaryeri.Api.Controllers;

public record UnreadCountDto(int Unread);

/// <summary>Yönetici paneli bildirimleri: son bildirimler, okunmamış sayısı ve okundu işaretleme. Tüm yöneticiler aynı kutuyu görür.</summary>
[ApiController]
[Route("api/notifications")]
[CoreModule]
public class NotificationsController(NotificationService notifications) : ControllerBase
{
    [HttpGet]
    public Task<NotificationPage> List(CancellationToken ct) => notifications.ListAsync(NotificationAudience.Admin, null, ct);

    /// <summary>Rozet için hafif sorgu; panel bunu düzenli aralıkla çağırır.</summary>
    [HttpGet("unread-count")]
    public async Task<UnreadCountDto> Unread(CancellationToken ct) => new(await notifications.UnreadCountAsync(NotificationAudience.Admin, null, ct));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct) => await notifications.MarkReadAsync(NotificationAudience.Admin, null, id, ct) ? NoContent() : NotFound();

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(NotificationAudience.Admin, null, ct);
        return NoContent();
    }
}

/// <summary>Bayi sitesi bildirimleri (api/store/notifications): bayi yalnızca kendi firmasının bildirimlerini görür.</summary>
[ApiController]
[Route("api/store/notifications")]
[Authorize(Policy = AuthPolicies.Dealer)]
[RequiresModule(ModuleIds.B2BStore)]
public class StoreNotificationsController(NotificationService notifications) : ControllerBase
{
    private Guid DealerId => Guid.Parse(User.FindFirstValue(AppClaims.DealerId)!);

    [HttpGet]
    public Task<NotificationPage> List(CancellationToken ct) => notifications.ListAsync(NotificationAudience.Dealer, DealerId, ct);

    [HttpGet("unread-count")]
    public async Task<UnreadCountDto> Unread(CancellationToken ct) => new(await notifications.UnreadCountAsync(NotificationAudience.Dealer, DealerId, ct));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct) => await notifications.MarkReadAsync(NotificationAudience.Dealer, DealerId, id, ct) ? NoContent() : NotFound();

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(NotificationAudience.Dealer, DealerId, ct);
        return NoContent();
    }
}
