using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Products;

/// <param name="Item">Kanala gönderilecek kalem; ürün gönderilemiyorsa null.</param>
/// <param name="Missing">Gönderilemeyen ürünün nedenleri (ör. kategori eşlemesi yok, zorunlu özellik eksik).</param>
public record PreparedProduct(Product Product, ProductItem? Item, IReadOnlyList<string> Missing);

/// <summary>
/// Katalogdaki ürünleri kanala göndermeye hazırlar: kategori eşlemesi, marka adı eşleşmesi ve özellik eşlemelerini uygular,
/// eksik zorunlu veriyi nedenleriyle bildirir. Gönderim kuyruğa alınırken ve işlenirken aynı kurallar kullanılır.
/// </summary>
public class ProductChannelPreparer(AppDbContext db, IConfiguration config)
{
    /// <summary>"Zorunlu özellik eksik: Renk, Beden." biçiminde neden metni.</summary>
    public static string MissingRequiredMessage(IEnumerable<string> names) => $"Zorunlu özellik eksik: {string.Join(", ", names)}.";

    public async Task<List<PreparedProduct>> PrepareAsync(string channel, IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        var products = await db.Products.Include(p => p.Images).Include(p => p.Listings).Where(p => productIds.Contains(p.Id)).ToListAsync(ct);
        var data = (await db.ProductChannelData.Where(d => d.Channel == channel && productIds.Contains(d.ProductId)).ToListAsync(ct)).ToDictionary(d => d.ProductId);
        var publicBaseUrl = config["Storage:PublicBaseUrl"];

        // Kategori eşlemesi (kendi kategori → kanal kategorisi) ve marka adı eşleşmesi, ürüne özel veride yoksa kullanılır.
        var categoryIds = products.Where(p => p.CategoryId != null).Select(p => p.CategoryId!.Value).Distinct().ToList();
        var mapped = await db.CategoryMappings.Where(m => m.Channel == channel && categoryIds.Contains(m.CategoryId))
            .ToDictionaryAsync(m => m.CategoryId, m => m.ExternalCategoryId, ct);
        var brandNames = products.Where(p => !string.IsNullOrWhiteSpace(p.Brand)).Select(p => p.Brand!.Trim()).Distinct().ToList();
        var brands = (await db.ChannelBrands.Where(b => b.Channel == channel && b.IsActive && brandNames.Contains(b.Name)).ToListAsync(ct))
            .GroupBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).ToDictionary(g => g.Key, g => g.First().ExternalId, StringComparer.CurrentCultureIgnoreCase);

        // Özellik eşlemeleri: ürünün kategorisinin eşlendiği kanal kategorisinin özellikleri + kayıtlı eşlemeler.
        var attributeMappings = await db.AttributeMappings.Include(m => m.Values).Where(m => m.Channel == channel && categoryIds.Contains(m.CategoryId)).ToListAsync(ct);
        var channelAttributes = (await db.ChannelAttributes.Where(a => a.Channel == channel && a.IsActive && mapped.Values.Contains(a.CategoryExternalId)).OrderBy(a => a.Name).ToListAsync(ct))
            .GroupBy(a => a.CategoryExternalId).ToDictionary(g => g.Key, g => g.ToList());

        // Fiyat kuralıyla hesaplanmış kanal fiyatı varsa katalog fiyatı yerine o gönderilir.
        var prices = await db.ChannelPrices.Where(c => c.Channel == channel && productIds.Contains(c.ProductId)).ToDictionaryAsync(c => c.ProductId, c => c.Price, ct);

        var result = new List<PreparedProduct>(products.Count);
        foreach (var product in products)
        {
            var own = data.GetValueOrDefault(product.Id);
            var requiredMissing = new List<string>();
            string? attributesJson = own?.AttributesJson;
            // Ürüne özel öznitelik verisi varsa ona güvenilir; yoksa özellik eşlemelerinden üretilir. Kanal kategorisinin özellikleri
            // katalogda henüz yoksa zorunluluk bilinemez, bu durumda engellenmez (kanal kendi hatasını bildirir).
            if (attributesJson is null && product.CategoryId is { } pc && mapped.TryGetValue(pc, out var ext) && channelAttributes.TryGetValue(ext, out var attrs))
            {
                var (built, missingNames) = ProductAttributeBuilder.Build(product, attrs, attributeMappings.Where(m => m.CategoryId == pc).ToList());
                requiredMissing.AddRange(missingNames);
                if (built.Count > 0)
                    attributesJson = JsonSerializer.Serialize(built.Select(a => new { attributeId = a.AttributeId, valueId = a.ValueId, customValue = a.CustomValue }),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }

            var effective = new ProductChannelData
            {
                ProductId = product.Id, Channel = channel,
                BrandId = own?.BrandId ?? (product.Brand is { } b && brands.TryGetValue(b.Trim(), out var brandId) ? brandId : null),
                CategoryId = own?.CategoryId ?? (product.CategoryId is { } c && mapped.TryGetValue(c, out var externalId) ? externalId : null),
                CargoCompanyId = own?.CargoCompanyId, DimensionalWeight = own?.DimensionalWeight, AttributesJson = attributesJson,
            };
            var (item, reasons) = TrendyolProductMapper.Map(product, effective, publicBaseUrl, prices.TryGetValue(product.Id, out var channelPrice) ? channelPrice : null);
            if (requiredMissing.Count > 0)
            {
                reasons = [.. reasons, MissingRequiredMessage(requiredMissing)];
                item = null;
            }
            result.Add(new PreparedProduct(product, item, reasons));
        }
        return result;
    }

    /// <summary>Ürünün kanal kaydını döner; yoksa oluşturur.</summary>
    public ProductListing ListingOf(Product product, string channel)
    {
        var listing = product.Listings.FirstOrDefault(l => l.Channel == channel);
        if (listing is null)
        {
            listing = new ProductListing { ProductId = product.Id, Channel = channel };
            db.ProductListings.Add(listing);
            product.Listings.Add(listing);
        }
        return listing;
    }
}
