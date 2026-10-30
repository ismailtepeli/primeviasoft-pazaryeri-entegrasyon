using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

/// <param name="Status">"Connected", "AuthError" veya "NotConnected".</param>
/// <param name="LastSyncAt">Kanaldaki son başarılı senkronun bitiş zamanı (UTC); hiç yoksa null.</param>
public record ChannelStatusDto(string Channel, string Status, DateTime? LastSyncAt);

/// <param name="Target">Hatanın ilgili olduğu ekran (ön yüz rotası).</param>
public record SyncErrorDto(Guid Id, string Channel, string Type, string Message, int AffectedCount, DateTime OccurredAt, string Target);

public record SyncErrorPage(IReadOnlyList<SyncErrorDto> Items, int Total);

/// <param name="TodayOrders">Bugün (Türkiye saatiyle) gelen siparişler.</param>
/// <param name="AwaitingShipment">Hazırlanıyor durumunda, henüz kargoya verilmemiş siparişler.</param>
/// <param name="SyncErrors">Hata durumundaki senkron işleri.</param>
/// <param name="ActiveProducts">En az bir kanalda yayında olan ürünler.</param>
public record DashboardSummary(int TodayOrders, int AwaitingShipment, int SyncErrors, int ActiveProducts, int B2BPendingApproval = 0);

[ApiController]
[Route("api/dashboard")]
public class DashboardController(AppDbContext db, TimeProvider time) : ControllerBase
{
    public const string NotConnected = "NotConnected";
    private static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    /// <summary>Genel bakış KPI kartları: bugünkü sipariş, bekleyen kargo, senkron hatası, aktif ürün.</summary>
    [CoreModule]
    [HttpGet("summary")]
    public async Task<DashboardSummary> Summary(CancellationToken ct)
    {
        var local = time.GetUtcNow().UtcDateTime + TurkeyOffset;
        var startUtc = local.Date - TurkeyOffset; // yerel gün başlangıcı, UTC olarak
        var today = await db.Orders.CountAsync(o => o.OrderDate >= startUtc, ct);
        var awaiting = await db.Orders.CountAsync(o => o.Status == OrderStatus.Preparing, ct);
        var errors = await db.SyncJobs.CountAsync(j => j.Status == SyncJobStatus.Failed, ct);
        var active = await db.Products.CountAsync(p => p.Listings.Any(l => l.Status == ListingStatus.Live), ct);
        var b2bPending = await db.Orders.CountAsync(o => o.Channel == "B2B" && o.Status == OrderStatus.New, ct);
        return new DashboardSummary(today, awaiting, errors, active, b2bPending);
    }

    /// <summary>Hata durumundaki senkron işleri, en yeni önce. <paramref name="take"/> kadar kayıt döner; toplam sayı da gelir.</summary>
    [CoreModule]
    [HttpGet("sync-errors")]
    public async Task<SyncErrorPage> SyncErrors([FromQuery] int skip = 0, [FromQuery] int take = 5, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        var failed = db.SyncJobs.Where(j => j.Status == SyncJobStatus.Failed);
        var total = await failed.CountAsync(ct);
        var rows = await failed
            .OrderByDescending(j => j.FinishedAt ?? j.CreatedAt)
            .Skip(Math.Max(skip, 0)).Take(take)
            .Select(j => new { j.Id, j.Channel, j.Type, j.Error, j.AffectedCount, At = j.FinishedAt ?? j.CreatedAt })
            .ToListAsync(ct);

        var items = rows.Select(r => new SyncErrorDto(r.Id, r.Channel, r.Type.ToString(),
            string.IsNullOrWhiteSpace(r.Error) ? "Bilinmeyen hata" : r.Error, r.AffectedCount, r.At, TargetFor(r.Type))).ToList();
        return new SyncErrorPage(items, total);
    }

    /// <summary>İş türüne göre hatanın çözüleceği ekran.</summary>
    public static string TargetFor(SyncJobType type) => type switch
    {
        SyncJobType.CatalogSync => "/esleme",
        SyncJobType.ProductSend or SyncJobType.StockPriceSync => "/urunler",
        SyncJobType.OrderSync => "/siparisler",
        SyncJobType.InvoiceSend => "/faturalar",
        _ => "/magazalar",
    };

    /// <summary>Desteklenen her kanal için bağlantı durumu ve son senkron zamanı.</summary>
    [RequiresModule(ModuleIds.Marketplace)]
    [HttpGet("channels")]
    public async Task<List<ChannelStatusDto>> Channels(CancellationToken ct)
    {
        var connections = await db.ChannelConnections.ToDictionaryAsync(c => c.Channel, c => c.Status, ct);
        var lastSync = await db.SyncJobs
            .Where(j => j.Status == SyncJobStatus.Succeeded && j.FinishedAt != null)
            .GroupBy(j => j.Channel)
            .Select(g => new { Channel = g.Key, At = g.Max(j => j.FinishedAt) })
            .ToDictionaryAsync(x => x.Channel, x => x.At, ct);

        return SupportedChannels.All
            .Select(ch => new ChannelStatusDto(
                ch,
                connections.TryGetValue(ch, out var status) ? status.ToString() : NotConnected,
                lastSync.GetValueOrDefault(ch)))
            .ToList();
    }
}
