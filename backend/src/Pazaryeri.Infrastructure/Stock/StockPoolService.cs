using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Stock;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Stock;

/// <summary>
/// Ortak stok havuzu. Her stok değişikliği <see cref="StockMovement"/> defterine yazılır; (kanal, tür, referans, ürün)
/// tekil olduğu için aynı olay yeniden işlense de stok bir kez değişir. Eşzamanlı siparişlerde satır sürümü
/// (RowVersion) çakışmasında satır yeniden okunup tekrar denenir; böylece kayıp güncelleme olmaz.
/// </summary>
public class StockPoolService(AppDbContext db, IJobQueue queue) : IStockPoolService
{
    private const int MaxAttempts = 20;
    private static readonly SyncJobStatus[] InFlight = [SyncJobStatus.Pending, SyncJobStatus.Running, SyncJobStatus.Retrying];

    public async Task<IReadOnlyList<StockLineResult>> DeductForSaleAsync(string channel, string reference, IReadOnlyList<StockLine> lines, CancellationToken ct = default)
    {
        Validate(channel, reference);
        if (!await PoolEnabledAsync(ct)) return lines.Select(l => new StockLineResult(l.Barcode, StockOutcome.PoolDisabled)).ToList();

        var results = new List<StockLineResult>();
        foreach (var line in Merge(lines))
            results.Add(await WithRetryAsync(line.Barcode, () => DeductOneAsync(channel, reference, line, ct), ct));

        if (results.Any(r => r.Outcome == StockOutcome.Applied)) await TriggerSyncAsync(channel, ct);
        return results;
    }

    public async Task<IReadOnlyList<StockLineResult>> RestoreAsync(string channel, string saleReference, string eventReference, IReadOnlyList<StockLine> lines, CancellationToken ct = default)
    {
        Validate(channel, saleReference);
        if (string.IsNullOrWhiteSpace(eventReference) || eventReference.Length > 200) throw new ArgumentException("Olay referansı geçersiz.", nameof(eventReference));
        if (!await PoolEnabledAsync(ct)) return lines.Select(l => new StockLineResult(l.Barcode, StockOutcome.PoolDisabled)).ToList();

        var results = new List<StockLineResult>();
        foreach (var line in Merge(lines))
            results.Add(await WithRetryAsync(line.Barcode, () => RestoreOneAsync(channel, saleReference, eventReference, line, ct), ct));

        if (results.Any(r => r.Outcome == StockOutcome.Applied)) await TriggerSyncAsync(channel, ct);
        return results;
    }

    private static void Validate(string channel, string reference)
    {
        if (string.IsNullOrWhiteSpace(channel) || channel.Length > 40) throw new ArgumentException("Kanal geçersiz.", nameof(channel));
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 200) throw new ArgumentException("Referans geçersiz.", nameof(reference));
    }

    /// <summary>Aynı barkod birden çok satırda gelirse adetler toplanır (tekil anahtar tek hareket ister).</summary>
    private static List<StockLine> Merge(IReadOnlyList<StockLine> lines) =>
        lines.GroupBy(l => l.Barcode.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new StockLine(g.Key, g.Sum(l => l.Quantity))).ToList();

    private async Task<bool> PoolEnabledAsync(CancellationToken ct) =>
        (await db.StockSettings.AsNoTracking().FirstOrDefaultAsync(ct))?.DeductAcrossChannels ?? true;

    private async Task<StockLineResult> DeductOneAsync(string channel, string reference, StockLine line, CancellationToken ct)
    {
        if (line.Quantity <= 0) return new(line.Barcode, StockOutcome.InvalidQuantity);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Barcode == line.Barcode, ct);
        if (product is null) return new(line.Barcode, StockOutcome.UnknownBarcode);
        if (await db.StockMovements.AnyAsync(m => m.Channel == channel && m.Type == StockMovementType.Sale && m.Reference == reference && m.ProductId == product.Id, ct))
            return new(line.Barcode, StockOutcome.Duplicate, StockAfter: product.Stock);

        var applied = Math.Min(line.Quantity, Math.Max(0, product.Stock)); // stok yetmezse negatife düşmez; fark "fazla satış" olarak kaydedilir
        product.Stock -= applied;
        db.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id, Channel = channel, Type = StockMovementType.Sale, Quantity = -applied, Requested = line.Quantity,
            StockAfter = product.Stock, Reference = reference,
        });
        await db.SaveChangesAsync(ct);
        return new(line.Barcode, StockOutcome.Applied, applied, product.Stock, line.Quantity - applied);
    }

    private async Task<StockLineResult> RestoreOneAsync(string channel, string saleReference, string eventReference, StockLine line, CancellationToken ct)
    {
        if (line.Quantity <= 0) return new(line.Barcode, StockOutcome.InvalidQuantity);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Barcode == line.Barcode, ct);
        if (product is null) return new(line.Barcode, StockOutcome.UnknownBarcode);
        if (await db.StockMovements.AnyAsync(m => m.Channel == channel && m.Type == StockMovementType.Restore && m.Reference == eventReference && m.ProductId == product.Id, ct))
            return new(line.Barcode, StockOutcome.Duplicate, StockAfter: product.Stock);

        var sold = await db.StockMovements.Where(m => m.Channel == channel && m.Type == StockMovementType.Sale && m.Reference == saleReference && m.ProductId == product.Id)
            .Select(m => -m.Quantity).FirstOrDefaultAsync(ct);
        var restored = await db.StockMovements.Where(m => m.Channel == channel && m.Type == StockMovementType.Restore && m.SaleReference == saleReference && m.ProductId == product.Id)
            .SumAsync(m => (int?)m.Quantity, ct) ?? 0;
        var restorable = sold - restored; // yalnızca gerçekten düşülen ve henüz geri eklenmemiş kadar
        var applied = Math.Min(line.Quantity, restorable);
        if (applied <= 0) return new(line.Barcode, StockOutcome.NothingToRestore, StockAfter: product.Stock);

        product.Stock += applied;
        db.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id, Channel = channel, Type = StockMovementType.Restore, Quantity = applied, Requested = line.Quantity,
            StockAfter = product.Stock, Reference = eventReference, SaleReference = saleReference,
        });
        await db.SaveChangesAsync(ct);
        return new(line.Barcode, StockOutcome.Applied, applied, product.Stock);
    }

    /// <summary>Sürüm çakışmasında satırı yeniden okuyup tekrar dener; eşzamanlı aynı olayın tekil anahtar ihlali "tekrar" sayılır.</summary>
    private async Task<StockLineResult> WithRetryAsync(string barcode, Func<Task<StockLineResult>> action, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                db.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(2, 10 * attempt), ct); // çakışan yazarlar aynı anda yeniden denemesin
            }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && IsUniqueViolation(ex))
            {
                db.ChangeTracker.Clear();
                return new(barcode, StockOutcome.Duplicate);
            }
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("IX_StockMovements", StringComparison.OrdinalIgnoreCase) == true
        || ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Stok değişti: kanalın kendisi hariç, bağlı diğer kanalların stok-fiyat senkronunu hemen kuyruğa alır.</summary>
    private async Task TriggerSyncAsync(string sourceChannel, CancellationToken ct)
    {
        var targets = await db.ChannelConnections.AsNoTracking()
            .Where(c => c.Status == ConnectionStatus.Connected && c.Channel != sourceChannel && StockPriceSyncScheduler.Channels.Contains(c.Channel))
            .Select(c => c.Channel).ToListAsync(ct);
        foreach (var channel in targets)
        {
            if (await db.SyncJobs.AnyAsync(j => j.Type == SyncJobType.StockPriceSync && j.Channel == channel && InFlight.Contains(j.Status), ct)) continue;
            await queue.EnqueueAsync(SyncJobType.StockPriceSync, channel, ct: ct);
        }
    }
}
