using System.Text.Json;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Domain;

namespace Pazaryeri.Infrastructure.Products;

/// <summary>Katalogdaki ürünü Trendyol createProducts kalemine çevirir; kanal için gereken bir veri eksikse nedenleri döner.</summary>
public static class TrendyolProductMapper
{
    public const int MaxTitle = 100;
    public const int MaxDescription = 30000;

    private sealed record AttributeJson(long AttributeId, long? ValueId, string? CustomValue);

    public static (ProductItem? Item, List<string> Missing) Map(Product p, ProductChannelData? data, string? publicBaseUrl, decimal? channelPrice = null, int? channelQuantity = null)
    {
        var missing = new List<string>();
        if (data?.BrandId is null) missing.Add("Trendyol marka eşlemesi yok.");
        if (data?.CategoryId is null) missing.Add("Trendyol kategori eşlemesi yok.");
        if (p.Name.Length > MaxTitle) missing.Add($"Başlık {MaxTitle} karakteri aşıyor (şu an {p.Name.Length}).");
        if (string.IsNullOrWhiteSpace(p.Description)) missing.Add("Açıklama boş.");
        else if (p.Description.Length > MaxDescription) missing.Add($"Açıklama {MaxDescription} karakteri aşıyor.");

        var images = new List<string>();
        foreach (var image in p.Images.OrderBy(i => i.Position).Take(ProductImage.MaxPerProduct))
        {
            if (image.Kind == ImageKind.Url) images.Add(image.ExternalUrl!);
            else if (string.IsNullOrWhiteSpace(publicBaseUrl)) { missing.Add("Yüklenen görsellerin kanaldan erişilebilmesi için Storage:PublicBaseUrl ayarı gerekli."); break; }
            else images.Add($"{publicBaseUrl.TrimEnd('/')}/api/images/{image.Id}");
        }
        if (images.Count == 0 && !missing.Any(m => m.Contains("PublicBaseUrl"))) missing.Add("En az bir ürün görseli gerekli.");

        var attributes = new List<ProductAttribute>();
        if (!string.IsNullOrWhiteSpace(data?.AttributesJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<AttributeJson>>(data.AttributesJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
                attributes.AddRange(parsed.Select(a => new ProductAttribute(a.AttributeId, a.ValueId, a.CustomValue)));
            }
            catch (JsonException) { missing.Add("Kanal özellikleri okunamadı; eşlemeyi yeniden kaydedin."); }
        }

        if (missing.Count > 0) return (null, missing);

        return (new ProductItem(
            Barcode: p.Barcode,
            Title: p.Name,
            ProductMainId: p.MainId ?? p.Barcode,
            StockCode: p.Barcode,
            BrandId: data!.BrandId!.Value,
            CategoryId: data.CategoryId!.Value,
            Description: p.Description!,
            Quantity: channelQuantity ?? p.Stock,
            ListPrice: channelPrice ?? p.Price,
            SalePrice: channelPrice ?? p.Price,
            VatRate: p.VatRate,
            ImageUrls: images,
            Attributes: attributes,
            CargoCompanyId: data.CargoCompanyId,
            DimensionalWeight: data.DimensionalWeight), []);
    }
}
