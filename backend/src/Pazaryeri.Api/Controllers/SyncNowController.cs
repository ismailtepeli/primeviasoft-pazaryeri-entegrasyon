using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

/// <param name="Enqueued">Kuyruğa alınan iş sayısı.</param>
/// <param name="Skipped">Zaten bekleyen/çalışan işi olduğu için yeniden eklenmeyen iş sayısı.</param>
/// <param name="Channels">İşlem yapılan bağlı kanal sayısı.</param>
public record SyncNowResult(int Enqueued, int Skipped, int Channels);

[ApiController]
[Route("api/sync")]
[CoreModule]
public class SyncNowController(AppDbContext db, IJobQueue queue, IEnumerable<ISyncJobHandler> handlers) : ControllerBase
{
    /// <summary>Kullanıcının "Şimdi senkronla" ile tetikleyebildiği, periyodik çalışan iş türleri.</summary>
    public static readonly SyncJobType[] Syncable = [SyncJobType.OrderSync, SyncJobType.StockPriceSync];

    private static readonly SyncJobStatus[] InFlight = [SyncJobStatus.Pending, SyncJobStatus.Running, SyncJobStatus.Retrying];

    /// <summary>
    /// Bağlı her kanal için senkron işlerini hemen kuyruğa alır. İşleyicisi (handler) kayıtlı olmayan türler
    /// atlanır; kanal için zaten bekleyen/çalışan aynı türde iş varsa tekrar eklenmez.
    /// </summary>
    [HttpPost("now")]
    public async Task<SyncNowResult> Now(CancellationToken ct)
    {
        var channels = await db.ChannelConnections
            .Where(c => c.Status == ConnectionStatus.Connected)
            .Select(c => c.Channel)
            .ToListAsync(ct);

        var types = Syncable.Where(t => handlers.Any(h => h.Type == t)).ToList();
        var running = (await db.SyncJobs
            .Where(j => InFlight.Contains(j.Status))
            .Select(j => new { j.Channel, j.Type })
            .ToListAsync(ct))
            .Select(j => (j.Channel, j.Type))
            .ToHashSet();

        int enqueued = 0, skipped = 0;
        foreach (var channel in channels)
        {
            foreach (var type in types)
            {
                if (running.Contains((channel, type))) { skipped++; continue; }
                await queue.EnqueueAsync(type, channel, ct: ct);
                enqueued++;
            }
        }
        return new SyncNowResult(enqueued, skipped, channels.Count);
    }
}
