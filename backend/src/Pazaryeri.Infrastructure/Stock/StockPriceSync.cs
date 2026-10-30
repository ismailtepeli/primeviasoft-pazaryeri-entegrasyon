using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Stock;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Products;
using Quartz;

namespace Pazaryeri.Infrastructure.Stock;

/// <param name="WindowStart">Fiyat değiştiyse güncellenmiş dakikalık pencere başlangıcı; değişmediyse mevcut değer.</param>
public record PlannedItem(Guid ProductId, string Barcode, int Quantity, decimal Price, bool PriceChanged, DateTime? WindowStart, int WindowCount);

/// <param name="Deferred">Barkod başına dakikalık fiyat sınırı dolduğu için sonraki çalıştırmaya bırakılanlar.</param>
public record DeltaPlan(IReadOnlyList<PlannedItem> Items, int Deferred);

/// <summary>
/// Kanal için hangi barkodların stok/fiyatının değiştiğini bulur: kanalda var olan (Yayında/Onayda) ürünlerin istenen
/// stok (kanala açılan miktar) ve fiyatı (kanal fiyatı ya da katalog fiyatı), son gönderilenle karşılaştırılır.
/// </summary>
public class StockPriceDeltaPlanner(AppDbContext db, TimeProvider time)
{
    public static readonly ListingStatus[] SyncableStatuses = [ListingStatus.Live, ListingStatus.Pending];

    public async Task<DeltaPlan> PlanAsync(string channel, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var rule = await db.StockRules.AsNoTracking().FirstOrDefaultAsync(r => r.Channel == channel, ct);
        var products = await db.Products.AsNoTracking()
            .Where(p => p.Listings.Any(l => l.Channel == channel && SyncableStatuses.Contains(l.Status)))
            .Select(p => new { p.Id, p.Barcode, p.Stock, p.SafetyStock, p.Price }).ToListAsync(ct);
        var prices = await db.ChannelPrices.AsNoTracking().Where(c => c.Channel == channel).ToDictionaryAsync(c => c.ProductId, c => c.Price, ct);
        var states = await db.ChannelStockPriceStates.AsNoTracking().Where(s => s.Channel == channel).ToDictionaryAsync(s => s.ProductId, ct);

        var items = new List<PlannedItem>();
        var deferred = 0;
        foreach (var p in products)
        {
            var quantity = rule is null ? p.Stock
                : StockCalculator.ChannelQuantity(p.Stock, StockCalculator.EffectiveSafety(p.SafetyStock, rule.SafetyStock), rule.AllocationPercent);
            var price = prices.TryGetValue(p.Id, out var cp) ? cp : p.Price;
            states.TryGetValue(p.Id, out var state);
            if (!StockPriceDelta.IsChanged(state, quantity, price)) continue;

            var priceChanged = StockPriceDelta.PriceChanged(state, price);
            if (priceChanged)
            {
                var (allowed, start, count) = StockPriceDelta.PriceGuard(state, now);
                if (!allowed) { deferred++; continue; }
                items.Add(new PlannedItem(p.Id, p.Barcode, quantity, price, true, start, count));
            }
            else
            {
                items.Add(new PlannedItem(p.Id, p.Barcode, quantity, price, false, state?.PriceWindowStart, state?.PriceUpdatesInWindow ?? 0));
            }
        }
        return new DeltaPlan(items, deferred);
    }
}

/// <summary>
/// StockPriceSync işi: yalnızca değişen barkodları 1.000'lik paketlerle kanala gönderir, aynı gövdeyi 15 dakika içinde
/// yeniden göndermez, barkod başına dakikada 30 fiyat güncellemesi sınırına uyar. Item sonucunu
/// <see cref="StockPriceBatchPoller"/> sonradan işler.
/// </summary>
public class StockPriceSyncHandler(
    AppDbContext db,
    IChannelCredentialService credentials,
    IMarketplaceClientFactory clients,
    StockPriceDeltaPlanner planner,
    TimeProvider time) : ISyncJobHandler
{
    public SyncJobType Type => SyncJobType.StockPriceSync;

    public async Task HandleAsync(SyncJob job, CancellationToken ct)
    {
        var plan = await planner.PlanAsync(job.Channel, ct);
        if (plan.Items.Count == 0) return;

        var creds = await credentials.GetForClientAsync(job.Channel, ct)
            ?? throw new InvalidOperationException($"{job.Channel} için kayıtlı API bilgisi yok.");
        var client = clients.Create(creds);

        var now = time.GetUtcNow().UtcDateTime;
        var recent = (await db.SentBodyHashes.Where(h => h.Channel == job.Channel && h.SentAt > now - StockPriceDelta.DuplicateBodyWindow).Select(h => h.Hash).ToListAsync(ct)).ToHashSet();
        var states = await db.ChannelStockPriceStates.Where(s => s.Channel == job.Channel && plan.Items.Select(i => i.ProductId).Contains(s.ProductId)).ToDictionaryAsync(s => s.ProductId, ct);

        foreach (var chunk in plan.Items.Chunk(StockPriceDelta.MaxItemsPerRequest))
        {
            var body = chunk.Select(i => new StockPriceItem(i.Barcode, i.Quantity, i.Price, i.Price)).ToList();
            var hash = StockPriceDelta.BodyHash(body);
            if (recent.Contains(hash)) continue; // aynı gövde 15 dk içinde gitmiş; kanal reddederdi, sonraki çalıştırmada yeniden denenir

            var result = await client.UpdateStockAndPriceAsync(body, ct);

            foreach (var item in chunk)
            {
                if (!states.TryGetValue(item.ProductId, out var state))
                    db.ChannelStockPriceStates.Add(state = new ChannelStockPriceState { ProductId = item.ProductId, Channel = job.Channel });
                state.Quantity = item.Quantity;
                state.Price = item.Price;
                state.SentAt = now;
                if (item.PriceChanged) { state.PriceWindowStart = item.WindowStart; state.PriceUpdatesInWindow = item.WindowCount; }
                states[item.ProductId] = state;
            }
            db.StockPriceBatches.Add(new StockPriceBatch
            {
                Channel = job.Channel, BatchId = result.BatchId, SentAt = now, ItemCount = chunk.Length,
                BarcodesJson = JsonSerializer.Serialize(chunk.Select(i => i.Barcode)),
            });
            db.SentBodyHashes.Add(new SentBodyHash { Channel = job.Channel, Hash = hash, SentAt = now });
            recent.Add(hash);
            job.BatchId = result.BatchId;
            await db.SaveChangesAsync(ct); // paket başına kaydet: sonraki pakette hata olursa gönderilenler yeniden gönderilmez
        }

        var stale = await db.SentBodyHashes.Where(h => h.SentAt < now - TimeSpan.FromHours(1)).ToListAsync(ct);
        if (stale.Count > 0) { db.SentBodyHashes.RemoveRange(stale); await db.SaveChangesAsync(ct); }
    }
}

/// <summary>
/// Periyodik iş: gönderilmiş stok-fiyat toplu işlemlerinin item bazlı sonucunu okur. Reddedilen barkodların durumu
/// silinir (sonraki senkronda yeniden gönderilir) ve nedenler batch kaydına yazılır. 4 saatte sonuç gelmezse de
/// barkodlar yeniden gönderilir. Tüm kiracılara baktığı için kiracı filtresi bilerek atlanır.
/// </summary>
[DisallowConcurrentExecution]
public class StockPriceBatchPoller(IServiceScopeFactory scopes, TimeProvider time, ILogger<StockPriceBatchPoller> logger) : IJob
{
    public const int BatchSize = 50;
    public const string TimeoutMessage = "Kanal 4 saat içinde sonuç bildirmedi; barkodlar yeniden gönderilecek.";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken ct)
    {
        List<(Guid Id, Guid TenantId)> pending;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            pending = (await db.StockPriceBatches.IgnoreQueryFilters().Where(b => b.Pending).OrderBy(b => b.SentAt).Take(BatchSize)
                .Select(b => new { b.Id, b.TenantId }).ToListAsync(ct)).Select(b => (b.Id, b.TenantId)).ToList();
        }
        foreach (var (id, tenantId) in pending)
        {
            try { await PollOneAsync(id, tenantId, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Stok-fiyat batch sonucu işlenemedi ({BatchId}); sonraki turda yeniden denenecek.", id);
            }
        }
    }

    private async Task PollOneAsync(Guid id, Guid tenantId, CancellationToken ct)
    {
        using var _ = AmbientTenant.Use(tenantId);
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var batch = await db.StockPriceBatches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null || !batch.Pending) return;
        var now = time.GetUtcNow().UtcDateTime;

        BatchStatus? status = null;
        try
        {
            var creds = await sp.GetRequiredService<IChannelCredentialService>().GetForClientAsync(batch.Channel, ct);
            if (creds is not null) status = await sp.GetRequiredService<IMarketplaceClientFactory>().Create(creds).GetBatchResultAsync(batch.BatchId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation(ex, "Stok-fiyat batch {BatchId} sorgulanamadı.", batch.BatchId);
        }

        var barcodes = JsonSerializer.Deserialize<List<string>>(batch.BarcodesJson) ?? [];
        if (status is not null && ProductBatchApplier.IsDone(status))
        {
            var failed = status.Items.Where(i => !i.Status.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)).ToList();
            await ForgetAsync(db, batch.Channel, failed.Select(i => i.Barcode).ToList(), ct);
            batch.FailedCount = failed.Count;
            batch.ErrorSummary = failed.Count == 0 ? null : string.Join("; ", failed.Take(5).Select(f => $"{f.Barcode}: {string.Join(", ", f.FailureReasons.DefaultIfEmpty("reddedildi"))}"));
        }
        else if (now - batch.SentAt > StockPriceDelta.BatchTimeout)
        {
            await ForgetAsync(db, batch.Channel, barcodes, ct);
            batch.FailedCount = barcodes.Count;
            batch.ErrorSummary = TimeoutMessage;
        }
        else return; // henüz bitmedi

        batch.Pending = false;
        batch.CompletedAt = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Barkodların son gönderilen durumunu siler; sonraki senkron bunları değişmiş sayıp yeniden gönderir.</summary>
    private static async Task ForgetAsync(AppDbContext db, string channel, List<string> barcodes, CancellationToken ct)
    {
        if (barcodes.Count == 0) return;
        var ids = await db.Products.Where(p => barcodes.Contains(p.Barcode)).Select(p => p.Id).ToListAsync(ct);
        db.ChannelStockPriceStates.RemoveRange(await db.ChannelStockPriceStates.Where(s => s.Channel == channel && ids.Contains(s.ProductId)).ToListAsync(ct));
    }
}

/// <summary>
/// Periyodik iş (5 dk): kanalı bağlı her kiracı için bekleyen stok-fiyat değişikliği varsa StockPriceSync işi kuyruğa alır.
/// Değişiklik yoksa iş açılmaz, böylece iş kayıtları gereksiz birikmez.
/// </summary>
[DisallowConcurrentExecution]
public class StockPriceSyncScheduler(IServiceScopeFactory scopes, ILogger<StockPriceSyncScheduler> logger) : IJob
{
    public static readonly string[] Channels = ["Trendyol"];
    private static readonly SyncJobStatus[] InFlight = [SyncJobStatus.Pending, SyncJobStatus.Running, SyncJobStatus.Retrying];

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken ct)
    {
        List<(Guid TenantId, string Channel)> targets;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            targets = (await db.ChannelConnections.IgnoreQueryFilters().Where(c => c.Status == ConnectionStatus.Connected && Channels.Contains(c.Channel))
                .Select(c => new { c.TenantId, c.Channel }).ToListAsync(ct)).Select(c => (c.TenantId, c.Channel)).ToList();
        }

        foreach (var (tenantId, channel) in targets)
        {
            try
            {
                using var _ = AmbientTenant.Use(tenantId);
                using var scope = scopes.CreateScope();
                var sp = scope.ServiceProvider;
                var db = sp.GetRequiredService<AppDbContext>();
                if (!await sp.GetRequiredService<IModuleService>().IsEnabledAsync(ModuleIds.StockPrice, tenantId, ct)) continue;
                if (await db.SyncJobs.AnyAsync(j => j.Type == SyncJobType.StockPriceSync && j.Channel == channel && InFlight.Contains(j.Status), ct)) continue;
                if ((await sp.GetRequiredService<StockPriceDeltaPlanner>().PlanAsync(channel, ct)).Items.Count == 0) continue;
                await sp.GetRequiredService<IJobQueue>().EnqueueAsync(SyncJobType.StockPriceSync, channel, ct: ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Stok-fiyat senkronu kuyruğa alınamadı (kiracı {Tenant}, {Channel}).", tenantId, channel);
            }
        }
    }
}
