using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Products;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Products;

public class ProductImageService(AppDbContext db, IImageStore store, ITenantProvider tenant) : IProductImageService
{
    /// <summary>Yüklenen görselin tarayıcıdan okunacağı adres.</summary>
    public static string UrlFor(ProductImage i) => i.Kind == ImageKind.Url ? i.ExternalUrl! : $"/api/images/{i.Id}";

    public static ProductImageDto ToDto(ProductImage i) => new(i.Id, UrlFor(i), i.Position);

    private async Task<List<ProductImage>> LoadAsync(Guid productId, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct)) throw new CatalogException("Ürün bulunamadı.", 404);
        return await db.ProductImages.Where(i => i.ProductId == productId).OrderBy(i => i.Position).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProductImageDto>> AddUploadsAsync(Guid productId, IReadOnlyList<(string FileName, Stream Content)> files, CancellationToken ct = default)
    {
        var images = await LoadAsync(productId, ct);
        if (files.Count == 0) throw new CatalogException("Bir görsel dosyası seçin.");
        if (images.Count + files.Count > ProductImage.MaxPerProduct)
            throw new CatalogException($"Bir ürüne en fazla {ProductImage.MaxPerProduct} görsel eklenebilir (şu an {images.Count}).");

        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Kiracı bilinmiyor.");
        // Önce hepsini doğrula: biri geçersizse hiçbiri kaydedilmez.
        var staged = new List<(ProductImage Image, byte[] Bytes)>();
        foreach (var (name, content) in files)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            if (ms.Length == 0) throw new CatalogException($"'{name}' boş bir dosya.");
            if (ms.Length > ImageSniffer.MaxBytes) throw new CatalogException($"'{name}' 5 MB sınırını aşıyor.");
            var bytes = ms.ToArray();
            var kind = ImageSniffer.Detect(bytes) ?? throw new CatalogException($"'{name}' geçerli bir jpg, png ya da webp görseli değil.");
            staged.Add((new ProductImage { ProductId = productId, Kind = ImageKind.Upload, Extension = kind.Extension, Position = images.Count + staged.Count }, bytes));
        }

        var saved = new List<ProductImage>();
        try
        {
            foreach (var (image, bytes) in staged)
            {
                await store.SaveAsync(tenantId, image.Id, image.Extension!, new MemoryStream(bytes), ct);
                saved.Add(image);
            }
            db.ProductImages.AddRange(staged.Select(s => s.Image));
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            foreach (var i in saved) store.Delete(tenantId, i.Id, i.Extension!); // yarım kalan dosyaları bırakma
            throw;
        }
        return (await LoadAsync(productId, ct)).Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<ProductImageDto>> AddUrlAsync(Guid productId, string url, CancellationToken ct = default)
    {
        var images = await LoadAsync(productId, ct);
        url = (url ?? string.Empty).Trim();
        if (url.Length > 2000) throw new CatalogException("Adres çok uzun.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new CatalogException("Geçerli bir http(s) görsel adresi girin.");
        if (images.Count >= ProductImage.MaxPerProduct)
            throw new CatalogException($"Bir ürüne en fazla {ProductImage.MaxPerProduct} görsel eklenebilir.");

        db.ProductImages.Add(new ProductImage { ProductId = productId, Kind = ImageKind.Url, ExternalUrl = uri.AbsoluteUri, Position = images.Count });
        await db.SaveChangesAsync(ct);
        return (await LoadAsync(productId, ct)).Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<ProductImageDto>> ReorderAsync(Guid productId, IReadOnlyList<Guid> orderedIds, CancellationToken ct = default)
    {
        var images = await LoadAsync(productId, ct);
        if (orderedIds.Count != images.Count || orderedIds.Distinct().Count() != images.Count || !images.All(i => orderedIds.Contains(i.Id)))
            throw new CatalogException("Sıralama listesi ürünün tüm görsellerini bir kez içermeli.");

        var byId = images.ToDictionary(i => i.Id);
        for (var pos = 0; pos < orderedIds.Count; pos++) byId[orderedIds[pos]].Position = pos;
        await db.SaveChangesAsync(ct);
        return images.OrderBy(i => i.Position).Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<ProductImageDto>> DeleteAsync(Guid productId, Guid imageId, CancellationToken ct = default)
    {
        var images = await LoadAsync(productId, ct);
        var target = images.FirstOrDefault(i => i.Id == imageId) ?? throw new CatalogException("Görsel bulunamadı.", 404);

        db.ProductImages.Remove(target);
        var rest = images.Where(i => i.Id != imageId).ToList();
        for (var pos = 0; pos < rest.Count; pos++) rest[pos].Position = pos; // boşluk kalmasın, kapak her zaman 0
        await db.SaveChangesAsync(ct);

        if (target.Kind == ImageKind.Upload && tenant.TenantId is { } tenantId) store.Delete(tenantId, target.Id, target.Extension!);
        return rest.Select(ToDto).ToList();
    }
}
