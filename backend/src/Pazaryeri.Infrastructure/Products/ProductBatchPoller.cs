using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Application.Products;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Quartz;

namespace Pazaryeri.Infrastructure.Products;

/// <summary>
/// Periyodik iş: kanala gönderilmiş ve sonucu bekleyen toplu işlemleri (batch) 4 saat dolmadan sorgular,
/// item bazlı sonucu ürünlerin kanal durumuna yazar. Tüm kiracılara baktığı için kiracı filtresi bilerek atlanır;
/// her iş kendi kiracısının bağlamında işlenir.
/// </summary>
[DisallowConcurrentExecution]
public class ProductBatchPoller(IServiceScopeFactory scopes, TimeProvider time, ILogger<ProductBatchPoller> logger) : IJob
{
    public const int BatchSize = 50;

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken ct)
    {
        List<(Guid Id, Guid TenantId)> pending;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            pending = (await db.SyncJobs.IgnoreQueryFilters()
                    .Where(j => j.BatchPending && j.BatchId != null && j.Type == SyncJobType.ProductSend)
                    .OrderBy(j => j.BatchSentAt).Take(BatchSize)
                    .Select(j => new { j.Id, j.TenantId }).ToListAsync(ct))
                .Select(j => (j.Id, j.TenantId)).ToList();
        }

        foreach (var (jobId, tenantId) in pending)
        {
            try { await PollOneAsync(jobId, tenantId, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Batch sonucu işlenemedi (iş {JobId}); sonraki turda yeniden denenecek.", jobId);
            }
        }
    }

    private async Task PollOneAsync(Guid jobId, Guid tenantId, CancellationToken ct)
    {
        using var _ = AmbientTenant.Use(tenantId);
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        var job = await db.SyncJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || !job.BatchPending || job.BatchId is null) return;

        var payload = JsonSerializer.Deserialize<ProductSendPayload>(job.Payload ?? "{}");
        var ids = payload?.ProductIds ?? [];
        var barcodes = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Barcode, ct);
        var listings = await db.ProductListings.Where(l => l.Channel == job.Channel && ids.Contains(l.ProductId)).ToListAsync(ct);

        BatchStatus? status = null;
        try
        {
            var creds = await sp.GetRequiredService<IChannelCredentialService>().GetForClientAsync(job.Channel, ct);
            if (creds is not null) status = await sp.GetRequiredService<IMarketplaceClientFactory>().Create(creds).GetBatchResultAsync(job.BatchId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Geçici hata (429/5xx) sonucu bilinmez yapar; süre dolmadıkça sonraki turda yeniden denenir.
            logger.LogInformation(ex, "Batch {BatchId} sorgulanamadı.", job.BatchId);
        }

        var outcome = ProductBatchApplier.Apply(job, barcodes, listings, status, time.GetUtcNow().UtcDateTime);
        if (outcome != BatchOutcome.StillPending) await db.SaveChangesAsync(ct);
    }
}
