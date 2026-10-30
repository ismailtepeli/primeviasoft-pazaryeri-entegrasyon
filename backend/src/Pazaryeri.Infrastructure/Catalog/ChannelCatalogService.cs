using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Catalog;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Catalog;

/// <summary>
/// Kanal kataloğunu önbelleğe alır ve değişiklikleri izler: yeni kayıt Added, içeriği değişen Changed,
/// kanalda artık görünmeyen Removed (IsActive=false; silinmez, geri gelirse yeniden etkinleşir).
/// </summary>
public class ChannelCatalogService(AppDbContext db, TimeProvider time) : IChannelCatalogService
{
    public static readonly TimeSpan AttributeTtl = TimeSpan.FromDays(7);

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task<CatalogSyncSummary> SyncCategoriesAsync(string channel, IChannelCatalogClient client, CancellationToken ct = default)
    {
        var run = StartRun(channel, CatalogKind.Categories);
        try
        {
            var fetched = (await client.GetCategoriesAsync(ct)).GroupBy(c => c.Id).Select(g => g.First()).ToList();
            if (fetched.Count == 0) throw new InvalidOperationException($"{channel} kategori ağacı boş döndü; mevcut veri korundu.");

            var parents = fetched.Where(c => c.ParentId is not null).Select(c => c.ParentId!.Value).ToHashSet();
            var existing = await db.ChannelCategories.Where(c => c.Channel == channel).ToDictionaryAsync(c => c.ExternalId, ct);
            var now = Now;
            int added = 0, changed = 0;

            foreach (var c in fetched)
            {
                var leaf = !parents.Contains(c.Id);
                if (!existing.TryGetValue(c.Id, out var row))
                {
                    db.ChannelCategories.Add(new ChannelCategory
                    {
                        Channel = channel, ExternalId = c.Id, ParentExternalId = c.ParentId, Name = c.Name, IsLeaf = leaf,
                        FirstSeenAt = now, LastSeenAt = now, ChangedAt = now,
                    });
                    added++;
                    continue;
                }
                var differs = row.Name != c.Name || row.ParentExternalId != c.ParentId || row.IsLeaf != leaf;
                if (!row.IsActive) { row.IsActive = true; added++; row.ChangedAt = now; }
                else if (differs) { changed++; row.ChangedAt = now; }
                row.Name = c.Name; row.ParentExternalId = c.ParentId; row.IsLeaf = leaf; row.LastSeenAt = now;
            }

            var seen = fetched.Select(c => c.Id).ToHashSet();
            var removed = 0;
            foreach (var row in existing.Values.Where(r => r.IsActive && !seen.Contains(r.ExternalId)))
            {
                row.IsActive = false; row.ChangedAt = now; removed++;
            }
            return await FinishAsync(run, added, changed, removed, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { await FailAsync(run, ex, ct); throw; }
    }

    public async Task<CatalogSyncSummary> SyncBrandsAsync(string channel, IChannelCatalogClient client, CancellationToken ct = default)
    {
        var run = StartRun(channel, CatalogKind.Brands);
        try
        {
            var startedAt = Now;
            int added = 0, changed = 0, total = 0;

            await foreach (var page in client.GetBrandPagesAsync(ct))
            {
                var batch = page.GroupBy(b => b.Id).Select(g => g.First()).ToList();
                var ids = batch.Select(b => b.Id).ToList();
                var existing = await db.ChannelBrands.Where(b => b.Channel == channel && ids.Contains(b.ExternalId)).ToDictionaryAsync(b => b.ExternalId, ct);
                var now = Now;
                foreach (var b in batch)
                {
                    total++;
                    if (!existing.TryGetValue(b.Id, out var row))
                    {
                        db.ChannelBrands.Add(new ChannelBrand { Channel = channel, ExternalId = b.Id, Name = b.Name, FirstSeenAt = now, LastSeenAt = now, ChangedAt = now });
                        added++;
                        continue;
                    }
                    if (!row.IsActive) { row.IsActive = true; added++; row.ChangedAt = now; }
                    else if (row.Name != b.Name) { changed++; row.ChangedAt = now; }
                    row.Name = b.Name; row.LastSeenAt = now;
                }
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }
            if (total == 0) throw new InvalidOperationException($"{channel} marka listesi boş döndü; mevcut veri korundu.");

            // Tüm sayfalar okunduysa bu turda görülmeyenler kanaldan kalkmıştır.
            var removed = await db.ChannelBrands.Where(b => b.Channel == channel && b.IsActive && b.LastSeenAt < startedAt)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false).SetProperty(b => b.ChangedAt, Now), ct);
            return await FinishAsync(run, added, changed, removed, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { db.ChangeTracker.Clear(); await FailAsync(run, ex, ct); throw; }
    }

    public async Task<CatalogSyncSummary> SyncAttributesAsync(string channel, IChannelCatalogClient client, IReadOnlyList<long> categoryIds, CancellationToken ct = default)
    {
        var run = StartRun(channel, CatalogKind.Attributes);
        try
        {
            int added = 0, changed = 0, removed = 0;
            foreach (var categoryId in categoryIds.Distinct())
            {
                var attributes = await client.GetCategoryAttributesAsync(categoryId, ct);
                var values = new Dictionary<long, IReadOnlyList<CatalogValue>>();
                foreach (var a in attributes)
                    values[a.Id] = a.Values.Count > 0 || a.AllowCustom ? a.Values : await client.GetAttributeValuesAsync(categoryId, a.Id, ct);

                var (a1, c1, r1) = await UpsertAttributesAsync(channel, categoryId, attributes, ct);
                var (a2, c2, r2) = await UpsertValuesAsync(channel, categoryId, values, ct);
                added += a1 + a2; changed += c1 + c2; removed += r1 + r2;
            }
            return await FinishAsync(run, added, changed, removed, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { await FailAsync(run, ex, ct); throw; }
    }

    private async Task<(int Added, int Changed, int Removed)> UpsertAttributesAsync(string channel, long categoryId, IReadOnlyList<CatalogAttribute> fetched, CancellationToken ct)
    {
        var existing = await db.ChannelAttributes.Where(a => a.Channel == channel && a.CategoryExternalId == categoryId).ToDictionaryAsync(a => a.ExternalId, ct);
        var now = Now;
        int added = 0, changed = 0, removed = 0;
        foreach (var a in fetched.GroupBy(x => x.Id).Select(g => g.First()))
        {
            if (!existing.TryGetValue(a.Id, out var row))
            {
                db.ChannelAttributes.Add(new ChannelAttribute
                {
                    Channel = channel, CategoryExternalId = categoryId, ExternalId = a.Id, Name = a.Name, Required = a.Required,
                    AllowCustom = a.AllowCustom, Varianter = a.Varianter, Slicer = a.Slicer, FirstSeenAt = now, LastSeenAt = now, ChangedAt = now,
                });
                added++;
                continue;
            }
            var differs = row.Name != a.Name || row.Required != a.Required || row.AllowCustom != a.AllowCustom || row.Varianter != a.Varianter || row.Slicer != a.Slicer;
            if (!row.IsActive) { row.IsActive = true; added++; row.ChangedAt = now; }
            else if (differs) { changed++; row.ChangedAt = now; }
            row.Name = a.Name; row.Required = a.Required; row.AllowCustom = a.AllowCustom; row.Varianter = a.Varianter; row.Slicer = a.Slicer; row.LastSeenAt = now;
        }
        var seen = fetched.Select(a => a.Id).ToHashSet();
        foreach (var row in existing.Values.Where(r => r.IsActive && !seen.Contains(r.ExternalId))) { row.IsActive = false; row.ChangedAt = now; removed++; }
        await db.SaveChangesAsync(ct);
        return (added, changed, removed);
    }

    private async Task<(int Added, int Changed, int Removed)> UpsertValuesAsync(string channel, long categoryId, Dictionary<long, IReadOnlyList<CatalogValue>> fetched, CancellationToken ct)
    {
        var existing = (await db.ChannelAttributeValues.Where(v => v.Channel == channel && v.CategoryExternalId == categoryId).ToListAsync(ct))
            .ToDictionary(v => (v.AttributeExternalId, v.ExternalId));
        var now = Now;
        int added = 0, changed = 0, removed = 0;
        var seen = new HashSet<(long, long)>();
        foreach (var (attributeId, list) in fetched)
        {
            foreach (var v in list.GroupBy(x => x.Id).Select(g => g.First()))
            {
                var key = (attributeId, v.Id);
                seen.Add(key);
                if (!existing.TryGetValue(key, out var row))
                {
                    db.ChannelAttributeValues.Add(new ChannelAttributeValue
                    {
                        Channel = channel, CategoryExternalId = categoryId, AttributeExternalId = attributeId, ExternalId = v.Id, Name = v.Name,
                        FirstSeenAt = now, LastSeenAt = now, ChangedAt = now,
                    });
                    added++;
                    continue;
                }
                if (!row.IsActive) { row.IsActive = true; added++; row.ChangedAt = now; }
                else if (row.Name != v.Name) { changed++; row.ChangedAt = now; }
                row.Name = v.Name; row.LastSeenAt = now;
            }
        }
        // Yalnızca bu turda değerleri okunabilen özelliklerin kayıtları kaldırılmış sayılır (serbest metinli özelliklerin değeri olmayabilir).
        foreach (var row in existing.Values.Where(r => r.IsActive && fetched.ContainsKey(r.AttributeExternalId) && !seen.Contains((r.AttributeExternalId, r.ExternalId))))
        {
            row.IsActive = false; row.ChangedAt = now; removed++;
        }
        await db.SaveChangesAsync(ct);
        return (added, changed, removed);
    }

    public async Task<List<long>> StaleAttributeCategoriesAsync(string channel, CancellationToken ct = default)
    {
        var cutoff = Now - AttributeTtl;
        return await db.ChannelAttributes.Where(a => a.Channel == channel && a.IsActive)
            .GroupBy(a => a.CategoryExternalId)
            .Where(g => g.Max(a => a.LastSeenAt) < cutoff)
            .Select(g => g.Key).ToListAsync(ct);
    }

    // --- çalıştırma kayıtları ---

    private CatalogSyncRun StartRun(string channel, CatalogKind kind)
    {
        var run = new CatalogSyncRun { Channel = channel, Kind = kind, StartedAt = Now };
        db.CatalogSyncRuns.Add(run);
        return run;
    }

    private async Task<CatalogSyncSummary> FinishAsync(CatalogSyncRun run, int added, int changed, int removed, CancellationToken ct)
    {
        run.Added = added; run.Changed = changed; run.Removed = removed; run.FinishedAt = Now;
        await AttachRunAsync(run, ct);
        await db.SaveChangesAsync(ct);
        return new CatalogSyncSummary(run.Kind, added, changed, removed);
    }

    private async Task FailAsync(CatalogSyncRun run, Exception ex, CancellationToken ct)
    {
        run.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
        run.FinishedAt = Now;
        try
        {
            // Yarım kalan değişiklikleri bırakıp yalnızca hata kaydını yaz.
            db.ChangeTracker.Clear();
            await AttachRunAsync(run, ct);
            await db.SaveChangesAsync(ct);
        }
        catch { /* hata kaydı yazılamazsa asıl hata yine de yukarı çıkar */ }
    }

    /// <summary>Çalıştırma kaydını izlemeye alır; ara kaydetmelerle zaten yazılmışsa güncellenir, yoksa eklenir.</summary>
    private async Task AttachRunAsync(CatalogSyncRun run, CancellationToken ct)
    {
        if (db.Entry(run).State != EntityState.Detached) return;
        if (await db.CatalogSyncRuns.AnyAsync(r => r.Id == run.Id, ct)) db.CatalogSyncRuns.Update(run);
        else db.CatalogSyncRuns.Add(run);
    }
}
