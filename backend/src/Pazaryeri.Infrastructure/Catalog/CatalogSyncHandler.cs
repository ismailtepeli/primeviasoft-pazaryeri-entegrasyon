using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Catalog;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Catalog;

/// <summary>
/// CatalogSync işi: kanalın kategori ağacını ve markalarını yeniler; ardından kullanımdaki (bir ürüne eşlenmiş) ya da
/// bir haftadan eski kategorilerin özelliklerini ve değerlerini çeker. Her kategorinin özelliklerini toptan çekmek
/// on binlerce istek tutacağı için özellikler yalnızca kullanımdaki kategoriler için yenilenir.
/// </summary>
public class CatalogSyncHandler(
    AppDbContext db,
    IChannelCredentialService credentials,
    IMarketplaceClientFactory clients,
    IChannelCatalogService catalog) : ISyncJobHandler
{
    public SyncJobType Type => SyncJobType.CatalogSync;

    public async Task HandleAsync(SyncJob job, CancellationToken ct)
    {
        var creds = await credentials.GetForClientAsync(job.Channel, ct)
            ?? throw new InvalidOperationException($"{job.Channel} için kayıtlı API bilgisi yok.");
        if (clients.Create(creds) is not IChannelCatalogClient client)
            throw new NotSupportedException($"{job.Channel} için katalog senkronu desteklenmiyor.");

        await catalog.SyncCategoriesAsync(job.Channel, client, ct);
        await catalog.SyncBrandsAsync(job.Channel, client, ct);

        var inUse = await db.ProductChannelData.Where(d => d.Channel == job.Channel && d.CategoryId != null)
            .Select(d => d.CategoryId!.Value).Distinct().ToListAsync(ct);
        // Kategori eşlemesiyle seçilen kanal kategorilerinin özellikleri de özellik eşlemesi için gereklidir.
        inUse = inUse.Union(await db.CategoryMappings.Where(m => m.Channel == job.Channel).Select(m => m.ExternalCategoryId).Distinct().ToListAsync(ct)).ToList();
        var cached = await db.ChannelAttributes.Where(a => a.Channel == job.Channel && inUse.Contains(a.CategoryExternalId))
            .Select(a => a.CategoryExternalId).Distinct().ToListAsync(ct);
        var categories = inUse.Except(cached).Concat(await catalog.StaleAttributeCategoriesAsync(job.Channel, ct)).Distinct().ToList();
        if (categories.Count > 0) await catalog.SyncAttributesAsync(job.Channel, client, categories, ct);
    }
}
