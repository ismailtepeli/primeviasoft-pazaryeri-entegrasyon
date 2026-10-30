using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Application.Products;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Products;

/// <summary>
/// ProductSend işi: ürünleri kanala göndermeden önce eksik veri denetler (eksikse "Eksik" durumu ve nedenler yazılır),
/// kalanları createProducts ile yollar, batch kimliğini işe kaydeder ve ürünleri "Onayda" yapar.
/// Kanalın item bazlı sonucunu <see cref="ProductBatchPoller"/> sonradan işler.
/// </summary>
public class ProductSendHandler(
    AppDbContext db,
    IChannelCredentialService credentials,
    IMarketplaceClientFactory clients,
    IConfiguration config,
    TimeProvider time) : ISyncJobHandler
{
    public SyncJobType Type => SyncJobType.ProductSend;

    public async Task HandleAsync(SyncJob job, CancellationToken ct)
    {
        var payload = string.IsNullOrWhiteSpace(job.Payload) ? null : JsonSerializer.Deserialize<ProductSendPayload>(job.Payload);
        if (payload is null || payload.ProductIds.Count == 0) throw new InvalidOperationException("Gönderim işinde ürün listesi yok.");

        var creds = await credentials.GetForClientAsync(job.Channel, ct)
            ?? throw new InvalidOperationException($"{job.Channel} için kayıtlı API bilgisi yok.");

        var products = await db.Products.Include(p => p.Images).Include(p => p.Listings)
            .Where(p => payload.ProductIds.Contains(p.Id)).ToListAsync(ct);
        var data = (await db.ProductChannelData.Where(d => d.Channel == job.Channel && payload.ProductIds.Contains(d.ProductId)).ToListAsync(ct))
            .ToDictionary(d => d.ProductId);
        var publicBaseUrl = config["Storage:PublicBaseUrl"];

        var items = new List<ProductItem>();
        var sent = new List<Product>();
        var missing = 0;
        foreach (var product in products)
        {
            var (item, reasons) = TrendyolProductMapper.Map(product, data.GetValueOrDefault(product.Id), publicBaseUrl);
            var listing = product.Listings.FirstOrDefault(l => l.Channel == job.Channel);
            if (listing is null)
            {
                listing = new ProductListing { ProductId = product.Id, Channel = job.Channel };
                db.ProductListings.Add(listing);
                product.Listings.Add(listing);
            }

            if (item is null)
            {
                listing.Status = ListingStatus.Missing;
                listing.ErrorMessage = string.Join('\n', reasons);
                missing++;
            }
            else
            {
                items.Add(item);
                sent.Add(product);
            }
        }
        job.AffectedCount = missing;

        if (items.Count > 0)
        {
            var batch = await clients.Create(creds).SendProductsAsync(items, ct);
            job.BatchId = batch.BatchId;
            job.BatchPending = true;
            job.BatchSentAt = time.GetUtcNow().UtcDateTime;
            foreach (var listing in sent.Select(p => p.Listings.First(l => l.Channel == job.Channel)))
            {
                listing.Status = ListingStatus.Pending;
                listing.ErrorMessage = null;
            }
        }
        await db.SaveChangesAsync(ct); // batch kimliği kaybolmasın
    }
}
