using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Application.Stock;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Stock;
using Quartz;

namespace Pazaryeri.Infrastructure.Orders;

/// <summary>
/// Kanaldan gelen paketleri ortak sipariş modeline işler (idempotent). Paket bölünür ya da iptal olursa eski paket
/// kendi durumuyla kalır. Yeni satırlar stok havuzundan bir kez düşülür; iptal/iade edilen paketlerin düşülmüş satırları
/// (başka aktif pakette yoksa) bir kez geri eklenir.
/// </summary>
public class OrderIngestService(AppDbContext db, IStockPoolService stock)
{
    public async Task<int> IngestAsync(string channel, IReadOnlyList<MarketplaceOrder> incoming, CancellationToken ct)
    {
        var touched = 0;
        // Aynı siparişin paketleri birlikte işlenir: bölünmede eski paket iptali ile yeni paketin aktifliği aynı turda görülür.
        foreach (var group in incoming.Where(o => !string.IsNullOrWhiteSpace(o.OrderNumber) && !string.IsNullOrWhiteSpace(o.PackageId)).GroupBy(o => o.OrderNumber))
        {
            db.ChangeTracker.Clear();
            var first = group.First();
            var order = await db.Orders.Include(x => x.Packages).ThenInclude(p => p.Lines)
                .FirstOrDefaultAsync(x => x.Channel == channel && x.OrderNumber == group.Key, ct);
            if (order is null)
            {
                order = new Order { Channel = channel, OrderNumber = group.Key, OrderDate = first.OrderDate, CustomerName = first.CustomerName };
                db.Orders.Add(order);
            }

            var changed = false;
            foreach (var o in group)
            {
                var modified = o.LastModified ?? o.OrderDate;
                var package = order.Packages.FirstOrDefault(p => p.PackageId == o.PackageId);
                if (package is not null && package.LastModifiedAt > modified) continue; // eski bildirim, daha yeni durumu ezmesin
                if (package is null)
                {
                    package = new OrderPackage { OrderId = order.Id, Channel = channel, PackageId = o.PackageId };
                    order.Packages.Add(package);
                    db.OrderPackages.Add(package);
                }
                package.ChannelStatus = o.Status;
                package.Status = OrderStatusMapper.Map(channel, o.Status);
                package.LastModifiedAt = modified;
                package.TotalPrice = o.TotalPrice;
                foreach (var l in o.Lines)
                {
                    var line = package.Lines.FirstOrDefault(x => x.LineId == l.LineId);
                    if (line is null)
                    {
                        line = new OrderLine { PackageId = package.Id, LineId = l.LineId };
                        package.Lines.Add(line);
                        db.OrderLines.Add(line);
                    }
                    line.Barcode = l.Barcode; line.Name = l.Name; line.Quantity = l.Quantity; line.Price = l.Price;
                }
                changed = true;
            }
            if (!changed) { db.ChangeTracker.Clear(); continue; }

            Recalculate(order);
            await db.SaveChangesAsync(ct);
            await ApplyStockAsync(channel, order.Id, ct);
            touched++;
        }
        return touched;
    }

    /// <summary>Sipariş durumu: aktif paketlerin en gerisi; hepsi iptal/iade ise iptal (ya da iade).</summary>
    public static void Recalculate(Order order)
    {
        var active = order.Packages.Where(p => OrderStatusMapper.IsActive(p.Status)).ToList();
        if (active.Count > 0)
        {
            order.Status = active.Min(p => p.Status);
            order.TotalPrice = active.Sum(p => p.TotalPrice);
        }
        else
        {
            order.Status = order.Packages.Any(p => p.Status == OrderStatus.Returned) ? OrderStatus.Returned : OrderStatus.Cancelled;
            order.TotalPrice = order.Packages.Sum(p => p.TotalPrice);
        }
        order.LastModifiedAt = order.Packages.Count == 0 ? order.OrderDate : order.Packages.Max(p => p.LastModifiedAt);
    }

    private async Task ApplyStockAsync(string channel, Guid orderId, CancellationToken ct)
    {
        db.ChangeTracker.Clear(); // stok servisi kendi kayıtlarını yazar; bayrakları ExecuteUpdate ile işleriz
        var order = await db.Orders.AsNoTracking().Include(x => x.Packages).ThenInclude(p => p.Lines).FirstAsync(x => x.Id == orderId, ct);
        var activeLineIds = order.Packages.Where(p => OrderStatusMapper.IsActive(p.Status)).SelectMany(p => p.Lines).Select(l => l.LineId).ToHashSet();

        foreach (var line in order.Packages.Where(p => OrderStatusMapper.IsActive(p.Status)).SelectMany(p => p.Lines).Where(l => !l.StockDeducted && l.Quantity > 0))
        {
            await stock.DeductForSaleAsync(channel, SaleReference(order.OrderNumber, line.LineId), [new StockLine(line.Barcode, line.Quantity)], ct);
            await db.OrderLines.Where(l => l.Id == line.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.StockDeducted, true), ct);
        }
        foreach (var line in order.Packages.Where(p => OrderStatusMapper.IsReversed(p.Status)).SelectMany(p => p.Lines).Where(l => l.StockDeducted && !l.StockRestored && l.Quantity > 0))
        {
            var reference = SaleReference(order.OrderNumber, line.LineId);
            if (activeLineIds.Contains(line.LineId)) continue; // paket bölünmesinde satır başka aktif pakette sürüyor; geri eklenmez
            await stock.RestoreAsync(channel, reference, "iptal:" + reference, [new StockLine(line.Barcode, line.Quantity)], ct);
            await db.OrderLines.Where(l => l.Id == line.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.StockRestored, true), ct);
        }
    }

    public static string SaleReference(string orderNumber, string lineId) => $"{orderNumber}:{lineId}";
}

/// <summary>
/// OrderSync işi: imleçten (son değişen paket zamanı, 2 dk örtüşmeyle) bu yana değişen paketleri sayfa sayfa çeker ve işler.
/// İmleç yalnızca tüm sayfalar işlenince ilerler; hata olursa iş yeniden denenir (işleme idempotent).
/// </summary>
public class OrderSyncHandler(
    AppDbContext db,
    IChannelCredentialService credentials,
    IMarketplaceClientFactory clients,
    OrderIngestService ingest,
    TimeProvider time) : ISyncJobHandler
{
    public static readonly TimeSpan Overlap = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan InitialBackfill = TimeSpan.FromDays(7);
    public const int PageSize = 200;
    public const int MaxPages = 100;

    public SyncJobType Type => SyncJobType.OrderSync;

    public async Task HandleAsync(SyncJob job, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var creds = await credentials.GetForClientAsync(job.Channel, ct)
            ?? throw new InvalidOperationException($"{job.Channel} için kayıtlı API bilgisi yok.");
        var client = clients.Create(creds);

        var state = await db.OrderSyncStates.AsNoTracking().FirstOrDefaultAsync(s => s.Channel == job.Channel, ct);
        var cursor = state?.Cursor ?? now - InitialBackfill;
        var from = cursor - Overlap;
        var newest = cursor;
        var touched = 0;

        for (var page = 0; page < MaxPages; page++)
        {
            var result = await client.GetOrdersAsync(new OrderQuery(Page: page, Size: PageSize, ModifiedFrom: from), ct);
            foreach (var o in result.Orders) newest = Max(newest, o.LastModified ?? o.OrderDate);
            touched += await ingest.IngestAsync(job.Channel, result.Orders, ct);
            if (page + 1 >= result.TotalPages) break;
        }
        job.AffectedCount = touched;

        db.ChangeTracker.Clear();
        var tracked = await db.OrderSyncStates.FirstOrDefaultAsync(s => s.Channel == job.Channel, ct);
        if (tracked is null) db.OrderSyncStates.Add(tracked = new OrderSyncState { Channel = job.Channel });
        tracked.Cursor = newest;
        tracked.LastSyncAt = now;
        await db.SaveChangesAsync(ct);
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}

/// <summary>Periyodik iş (3 dk): kanalı bağlı her kiracı için OrderSync işi kuyruğa alır (zaten bekleyen varsa almaz).</summary>
[DisallowConcurrentExecution]
public class OrderSyncScheduler(IServiceScopeFactory scopes, ILogger<OrderSyncScheduler> logger) : IJob
{
    private static readonly SyncJobStatus[] InFlight = [SyncJobStatus.Pending, SyncJobStatus.Running, SyncJobStatus.Retrying];

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken ct)
    {
        List<(Guid TenantId, string Channel)> targets;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            targets = (await db.ChannelConnections.IgnoreQueryFilters().Where(c => c.Status == ConnectionStatus.Connected && StockPriceSyncScheduler.Channels.Contains(c.Channel))
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
                if (await db.SyncJobs.AnyAsync(j => j.Type == SyncJobType.OrderSync && j.Channel == channel && InFlight.Contains(j.Status), ct)) continue;
                await sp.GetRequiredService<IJobQueue>().EnqueueAsync(SyncJobType.OrderSync, channel, ct: ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Sipariş senkronu kuyruğa alınamadı (kiracı {Tenant}, {Channel}).", tenantId, channel);
            }
        }
    }
}
