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

[ApiController]
[Route("api/dashboard")]
public class DashboardController(AppDbContext db) : ControllerBase
{
    public const string NotConnected = "NotConnected";

    /// <summary>Hata durumundaki senkron işleri, en yeni önce. <paramref name="take"/> kadar kayıt döner; toplam sayı da gelir.</summary>
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
