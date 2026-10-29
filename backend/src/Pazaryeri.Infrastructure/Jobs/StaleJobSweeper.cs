using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Quartz;

namespace Pazaryeri.Infrastructure.Jobs;

/// <summary>
/// Periyodik iş: işçi çökerse "Running" durumunda takılı kalan kayıtları Failed yapar.
/// Tüm kiracılara baktığı için kiracı filtresi bilerek atlanır.
/// </summary>
[DisallowConcurrentExecution]
public class StaleJobSweeper(AppDbContext db, ILogger<StaleJobSweeper> logger) : IJob
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - StaleAfter;
        var stale = await db.SyncJobs.IgnoreQueryFilters()
            .Where(j => j.Status == SyncJobStatus.Running && j.StartedAt < cutoff)
            .ToListAsync(cancellationToken);

        foreach (var job in stale)
        {
            job.Status = SyncJobStatus.Failed;
            job.Error = "İş zaman aşımına uğradı (işçi yanıt vermedi).";
            job.FinishedAt = DateTime.UtcNow;
        }
        if (stale.Count == 0) return;

        // Toplu güncellemede TenantId zaten dolu olduğu için SaveChanges kiracı istemez.
        await db.SaveChangesAsync(cancellationToken);
        logger.LogWarning("{Count} takılı senkron işi Failed yapıldı.", stale.Count);
    }
}
