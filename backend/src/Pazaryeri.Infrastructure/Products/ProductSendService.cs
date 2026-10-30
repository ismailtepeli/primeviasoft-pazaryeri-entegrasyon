using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Products;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Products;

public class ProductSendService(AppDbContext db, IJobQueue queue, ProductChannelPreparer preparer) : IProductSendService
{
    /// <summary>Şimdilik ürün gönderimi yalnızca bu kanallarda uygulandı.</summary>
    public static readonly string[] SupportedChannels = ["Trendyol"];

    public const int MaxProductsPerCall = 5000;

    public async Task<ProductSendResult> EnqueueAsync(string channel, IReadOnlyList<Guid> productIds, CancellationToken ct = default)
    {
        if (!Pazaryeri.Domain.SupportedChannels.All.Contains(channel)) throw new CatalogException("Bilinmeyen kanal.");
        if (!SupportedChannels.Contains(channel)) throw new CatalogException($"{channel} için ürün gönderimi henüz desteklenmiyor.");

        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0) throw new CatalogException("Gönderilecek ürün seçin.");
        if (ids.Count > MaxProductsPerCall) throw new CatalogException($"Tek seferde en fazla {MaxProductsPerCall} ürün gönderilebilir.");

        var status = await db.ChannelConnections.Where(c => c.Channel == channel).Select(c => (ConnectionStatus?)c.Status).FirstOrDefaultAsync(ct);
        if (status is null) throw new CatalogException($"{channel} bağlı değil; önce Mağazalar ekranından bağlayın.");
        if (status != ConnectionStatus.Connected) throw new CatalogException($"{channel} bağlantısı doğrulanmadı; Mağazalar ekranından bağlantıyı test edin.");

        var prepared = await preparer.PrepareAsync(channel, ids, ct);
        if (prepared.Count == 0) throw new CatalogException("Seçilen ürünler bulunamadı.", 404);

        // Eksik verisi olanlar (eşlenmemiş kategori, zorunlu özellik, açıklama, görsel…) kuyruğa alınmaz;
        // nedenleriyle "Eksik" durumuna yazılır ve Ürünler ekranında görünür.
        var blocked = prepared.Where(p => p.Item is null).ToList();
        foreach (var (product, _, reasons) in blocked)
        {
            var listing = preparer.ListingOf(product, channel);
            listing.Status = ListingStatus.Missing;
            listing.ErrorMessage = string.Join((char)10, reasons);
        }
        if (blocked.Count > 0) await db.SaveChangesAsync(ct);

        var ready = prepared.Where(p => p.Item is not null).OrderBy(p => p.Product.Name).Select(p => p.Product.Id).ToList();
        var jobs = 0;
        foreach (var chunk in ready.Chunk(IProductSendService.MaxItemsPerRequest))
        {
            var payload = JsonSerializer.Serialize(new ProductSendPayload([.. chunk]));
            await queue.EnqueueAsync(SyncJobType.ProductSend, channel, ct: ct, payload: payload);
            jobs++;
        }
        return new ProductSendResult(jobs, ready.Count, ids.Count - prepared.Count, blocked.Count);
    }
}
