using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Quartz;

namespace Pazaryeri.Infrastructure.Catalog;

/// <summary>
/// Haftalık iş: katalog senkronunu desteklenen her kanal için, o kanala bağlı bir kiracı adına kuyruğa alır.
/// Katalog herkese açık olduğu için tek kiracının bilgisi yeterlidir; bağlı kiracı yoksa kanal atlanır.
/// </summary>
[DisallowConcurrentExecution]
public class CatalogWeeklySync(IServiceScopeFactory scopes, ILogger<CatalogWeeklySync> logger) : IJob
{
    public static readonly string[] Channels = ["Trendyol"];

    private static readonly SyncJobStatus[] InFlight = [SyncJobStatus.Pending, SyncJobStatus.Running, SyncJobStatus.Retrying];

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken ct)
    {
        foreach (var channel in Channels)
        {
            Guid? tenantId;
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                tenantId = await db.ChannelConnections.IgnoreQueryFilters()
                    .Where(c => c.Channel == channel && c.Status == ConnectionStatus.Connected)
                    .OrderBy(c => c.CreatedAt).Select(c => (Guid?)c.TenantId).FirstOrDefaultAsync(ct);
            }
            if (tenantId is null) { logger.LogInformation("{Channel} için bağlı kiracı yok; katalog senkronu atlandı.", channel); continue; }

            using var _ = AmbientTenant.Use(tenantId.Value);
            using var tenantScope = scopes.CreateScope();
            var tenantDb = tenantScope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await tenantDb.SyncJobs.AnyAsync(j => j.Type == SyncJobType.CatalogSync && j.Channel == channel && InFlight.Contains(j.Status), ct)) continue;

            await tenantScope.ServiceProvider.GetRequiredService<IJobQueue>().EnqueueAsync(SyncJobType.CatalogSync, channel, ct: ct);
            logger.LogInformation("{Channel} katalog senkronu kuyruğa alındı.", channel);
        }
    }
}
