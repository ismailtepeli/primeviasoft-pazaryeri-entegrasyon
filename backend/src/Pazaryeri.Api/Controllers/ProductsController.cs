using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Products;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Products;

namespace Pazaryeri.Api.Controllers;

/// <param name="Status">"Pending", "Live", "Error", "Missing" veya "Passive".</param>
public record ProductChannelDto(string Channel, string Status, string? ErrorMessage);

/// <param name="CoverUrl">Kapak görselinin adresi; görseli yoksa null.</param>
public record ProductDto(Guid Id, string Name, string Barcode, string? MainId, string? Variant, string? Brand, string? CategoryPath,
    int Stock, decimal Price, int ImageCount, string? CoverUrl, IReadOnlyList<ProductChannelDto> Channels);

public record ProductPage(IReadOnlyList<ProductDto> Items, int Total);

public record ProductDetailDto(Guid Id, string Name, string Barcode, string? MainId, string? Variant, string? Brand, string? Description,
    Guid? CategoryId, string? CategoryPath, int Stock, decimal Price, int VatRate, decimal? Cost, IReadOnlyList<ProductImageDto> Images, IReadOnlyList<ProductChannelDto> Channels);

public record ProductInput(string? Name, string? Barcode, string? MainId, string? Variant, string? Brand, string? Description,
    Guid? CategoryId, int Stock, decimal Price, int VatRate = 20, decimal? Cost = null);

[ApiController]
[Route("api/products")]
public class ProductsController(AppDbContext db) : ControllerBase
{
    public const int MaxPageSize = 100;

    /// <summary>
    /// Katalog listesi. <paramref name="q"/> ad ya da barkodda arar; <paramref name="filter"/>:
    /// "all" (varsayılan), "error" (en az bir kanalda hatalı) veya "pending" (en az bir kanalda onay bekleyen).
    /// </summary>
    [HttpGet]
    public async Task<ProductPage> List([FromQuery] string? q, [FromQuery] string? filter,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        var query = db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.Barcode.Contains(term));
        }

        query = filter?.ToLowerInvariant() switch
        {
            "error" => query.Where(p => p.Listings.Any(l => l.Status == ListingStatus.Error)),
            "pending" => query.Where(p => p.Listings.Any(l => l.Status == ListingStatus.Pending)),
            _ => query,
        };

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new
            {
                p.Id, p.Name, p.Barcode, p.MainId, p.Variant, p.Brand, p.CategoryId, p.Stock, p.Price,
                Images = p.Images.OrderBy(i => i.Position).Select(i => new { i.Id, i.Kind, i.ExternalUrl, i.Position }).ToList(),
                Listings = p.Listings.Select(l => new { l.Channel, l.Status, l.ErrorMessage }).ToList(),
            })
            .ToListAsync(ct);

        var paths = await CategoryPaths.LoadAsync(db, ct);
        var items = rows.Select(r => new ProductDto(r.Id, r.Name, r.Barcode, r.MainId, r.Variant, r.Brand,
            r.CategoryId is { } c ? paths.GetValueOrDefault(c) : null, r.Stock, r.Price, r.Images.Count,
            r.Images.Count == 0 ? null : CoverUrl(r.Images[0].Id, r.Images[0].Kind, r.Images[0].ExternalUrl),
            r.Listings.OrderBy(l => l.Channel).Select(l => new ProductChannelDto(l.Channel, l.Status.ToString(), l.ErrorMessage)).ToList())).ToList();
        return new ProductPage(items, total);
    }

    private static string CoverUrl(Guid id, ImageKind kind, string? external) => kind == ImageKind.Url ? external! : $"/api/images/{id}";

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var p = await db.Products.AsNoTracking().Include(x => x.Images).Include(x => x.Listings).FirstOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? NotFound(new { error = "Ürün bulunamadı." }) : await ToDetail(p, ct);
    }

    [HttpPost]
    public async Task<ActionResult<ProductDetailDto>> Create(ProductInput input, CancellationToken ct)
    {
        var product = new Product();
        if (await Apply(product, input, ct) is { } error) return BadRequest(new { error });
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return await ToDetail(product, ct);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductDetailDto>> Update(Guid id, ProductInput input, CancellationToken ct)
    {
        var product = await db.Products.Include(x => x.Images).Include(x => x.Listings).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return NotFound(new { error = "Ürün bulunamadı." });
        if (await Apply(product, input, ct) is { } error) return BadRequest(new { error });
        await db.SaveChangesAsync(ct);
        return await ToDetail(product, ct);
    }

    /// <summary>Girdiyi doğrulayıp ürüne yazar; geçersizse hata metnini döner.</summary>
    private async Task<string?> Apply(Product product, ProductInput input, CancellationToken ct)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        var barcode = input.Barcode?.Trim() ?? string.Empty;
        if (name.Length == 0) return "Ürün adı boş olamaz.";
        if (name.Length > 500) return "Ürün adı 500 karakteri aşamaz.";
        if (barcode.Length == 0) return "Barkod boş olamaz.";
        if (barcode.Length > 100) return "Barkod 100 karakteri aşamaz.";
        if (input.VatRate is not (0 or 1 or 10 or 20)) return "KDV oranı 0, 1, 10 ya da 20 olmalı.";
        if (input.Cost is { } cost && (cost <= 0 || cost > 100_000_000m)) return "Maliyet sıfırdan büyük olmalı.";
        if (input.Stock < 0) return "Stok negatif olamaz.";
        if (input.Price <= 0) return "Fiyat sıfırdan büyük olmalı.";
        if (input.Price > 9_999_999_999m) return "Fiyat çok büyük.";
        if (input.MainId?.Trim().Length > 100) return "Ürün ana kodu 100 karakteri aşamaz.";
        if (input.Brand?.Trim().Length > 200) return "Marka 200 karakteri aşamaz.";
        if (input.Description?.Length > 5000) return "Açıklama 5000 karakteri aşamaz.";
        if (input.Variant?.Trim().Length > 500) return "Varyant 500 karakteri aşamaz.";

        if (await db.Products.AnyAsync(p => p.Barcode == barcode && p.Id != product.Id, ct)) return "Bu barkod başka bir üründe kullanılıyor.";
        if (input.CategoryId is { } cid && !await db.Categories.AnyAsync(c => c.Id == cid, ct)) return "Seçilen kategori bulunamadı.";

        product.Name = name;
        product.Barcode = barcode;
        product.MainId = Trim(input.MainId, 100);
        product.Variant = Trim(input.Variant, 500);
        product.Brand = Trim(input.Brand, 200);
        product.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        product.CategoryId = input.CategoryId;
        product.VatRate = input.VatRate;
        product.Cost = input.Cost is { } c2 ? decimal.Round(c2, 2) : null;
        product.Stock = input.Stock;
        product.Price = decimal.Round(input.Price, 2);
        return null;
    }

    private static string? Trim(string? value, int max) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > max ? null : value.Trim();

    private async Task<ProductDetailDto> ToDetail(Product p, CancellationToken ct)
    {
        var paths = await CategoryPaths.LoadAsync(db, ct);
        return new ProductDetailDto(p.Id, p.Name, p.Barcode, p.MainId, p.Variant, p.Brand, p.Description, p.CategoryId,
            p.CategoryId is { } c ? paths.GetValueOrDefault(c) : null, p.Stock, p.Price, p.VatRate, p.Cost,
            p.Images.OrderBy(i => i.Position).Select(ProductImageService.ToDto).ToList(),
            p.Listings.OrderBy(l => l.Channel).Select(l => new ProductChannelDto(l.Channel, l.Status.ToString(), l.ErrorMessage)).ToList());
    }
}
