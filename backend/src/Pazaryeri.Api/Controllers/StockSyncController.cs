using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Stock;

namespace Pazaryeri.Api.Controllers;

/// <param name="Supported">Kanal için stok-fiyat senkronu uygulanmış mı.</param>
/// <param name="PendingChanges">Kanala henüz gönderilmemiş stok/fiyat değişikliği olan barkod sayısı.</param>
/// <param name="Deferred">Dakikalık fiyat sınırı dolduğu için sonraya bırakılan barkod sayısı.</param>
/// <param name="PendingBatches">Sonucu beklenen toplu işlem sayısı.</param>
/// <param name="LastError">Son tamamlanan toplu işlemin hata özeti; hata yoksa null.</param>
public record StockSyncStatusDto(string Channel, bool Supported, int PendingChanges, int Deferred, int PendingBatches,
    DateTime? LastSentAt, int LastSentItems, int LastFailed, string? LastError, bool SyncInFlight);

[ApiController]
[Route("api/stock/sync")]
public class StockSyncController(AppDbContext db, StockPriceDeltaPlanner planner) : ControllerBase
{
    private static readonly SyncJobStatus[] InFlight = [SyncJobStatus.Pending, SyncJobStatus.Running, SyncJobStatus.Retrying];

    [HttpGet("{channel}")]
    public async Task<ActionResult<StockSyncStatusDto>> Status(string channel, CancellationToken ct)
    {
        if (!SupportedChannels.All.Contains(channel)) return BadRequest(new { error = "Bilinmeyen kanal." });
        if (!StockPriceSyncScheduler.Channels.Contains(channel))
            return new StockSyncStatusDto(channel, false, 0, 0, 0, null, 0, 0, null, false);

        var plan = await planner.PlanAsync(channel, ct);
        var batches = db.StockPriceBatches.AsNoTracking().Where(b => b.Channel == channel);
        var last = await batches.OrderByDescending(b => b.SentAt).FirstOrDefaultAsync(ct);
        var lastCompleted = await batches.Where(b => !b.Pending).OrderByDescending(b => b.CompletedAt).FirstOrDefaultAsync(ct);
        return new StockSyncStatusDto(channel, true, plan.Items.Count, plan.Deferred,
            await batches.CountAsync(b => b.Pending, ct), last?.SentAt, last?.ItemCount ?? 0,
            lastCompleted?.FailedCount ?? 0, lastCompleted?.ErrorSummary,
            await db.SyncJobs.AnyAsync(j => j.Type == SyncJobType.StockPriceSync && j.Channel == channel && InFlight.Contains(j.Status), ct));
    }
}
