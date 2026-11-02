using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

public record SalePriceInput(decimal? SalePrice);

/// <summary>Toplu kampanya: Mode "percent" (normal fiyattan yüzde indirim), "price" (sabit kampanya fiyatı) ya da "clear" (kampanyayı kaldır).</summary>
public record BulkSaleInput(List<Guid>? ProductIds, string? Mode, decimal Value);

public record BulkSaleResult(int Updated, int Cleared, IReadOnlyList<string> Skipped);

/// <summary>B2C mağazada neyin görüneceği (yönetici): ürün bayrağı ve kampanya fiyatı. B2B görünürlüğünden bağımsızdır.</summary>
[ApiController]
[Route("api/b2c")]
[RequiresModule(ModuleIds.B2CStore)]
public class B2CController(AppDbContext db) : ControllerBase
{
    public const int MaxBulk = 1000;

    /// <summary>Seçili ürünlerde "B2C mağazada göster" bayrağını toplu açar/kapatır; bulunamayan kimlikler sayılmaz.</summary>
    [HttpPut("products/visibility")]
    public async Task<ActionResult<object>> SetProductVisibility(ProductVisibilityInput input, CancellationToken ct)
    {
        var ids = input.ProductIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0) return BadRequest(new { error = "En az bir ürün seçin." });
        if (ids.Count > MaxBulk) return BadRequest(new { error = $"Tek seferde en fazla {MaxBulk} ürün değiştirilebilir." });
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        foreach (var p in products) p.B2CVisible = input.Visible;
        await db.SaveChangesAsync(ct);
        return new { updated = products.Count };
    }

    /// <summary>
    /// Seçili ürünlere kampanya fiyatı uygular ya da kaldırır. Yüzde 1-90 arası olmalı; sabit fiyat normal fiyattan düşük ve sıfırdan büyük olmalıdır.
    /// Kurala uymayan ürünler atlanır ve adlarıyla bildirilir, diğerleri güncellenir.
    /// </summary>
    [HttpPut("products/sale")]
    public async Task<ActionResult<BulkSaleResult>> SetSale(BulkSaleInput input, CancellationToken ct)
    {
        var ids = input.ProductIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0) return BadRequest(new { error = "En az bir ürün seçin." });
        if (ids.Count > MaxBulk) return BadRequest(new { error = $"Tek seferde en fazla {MaxBulk} ürün değiştirilebilir." });
        var mode = input.Mode?.Trim().ToLowerInvariant();
        if (mode is not ("percent" or "price" or "clear")) return BadRequest(new { error = "Kampanya türü geçersiz." });
        if (mode == "percent" && (input.Value < 1 || input.Value > 90)) return BadRequest(new { error = "İndirim yüzdesi 1 ile 90 arasında olmalı." });
        if (mode == "price" && (input.Value <= 0 || input.Value > 9_999_999_999m)) return BadRequest(new { error = "Kampanya fiyatı sıfırdan büyük olmalı." });

        var products = await db.Products.Where(p => ids.Contains(p.Id)).OrderBy(p => p.Name).ToListAsync(ct);
        int updated = 0, cleared = 0;
        var skipped = new List<string>();
        foreach (var p in products)
        {
            if (mode == "clear") { if (p.B2CSalePrice is not null) { p.B2CSalePrice = null; cleared++; } continue; }
            var price = mode == "percent" ? decimal.Round(p.Price * (1 - input.Value / 100m), 2, MidpointRounding.AwayFromZero) : decimal.Round(input.Value, 2);
            if (price <= 0 || price >= p.Price) { skipped.Add(p.Name); continue; }
            p.B2CSalePrice = price;
            updated++;
        }
        await db.SaveChangesAsync(ct);
        return new BulkSaleResult(updated, cleared, skipped);
    }

    /// <summary>B2C mağaza için kategori listesi: gizli mi ve kaç ürünü B2C'de görünür.</summary>
    [HttpGet("categories")]
    public async Task<List<CategoryVisibilityDto>> Categories(CancellationToken ct)
    {
        var paths = await CategoryPaths.LoadAsync(db, ct);
        var counts = await db.Products.Where(p => p.B2CVisible && p.CategoryId != null).GroupBy(p => p.CategoryId!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        var all = await db.Categories.AsNoTracking().ToListAsync(ct);
        return all.OrderBy(c => paths[c.Id], StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new CategoryVisibilityDto(c.Id, paths[c.Id], c.B2CHidden, counts.GetValueOrDefault(c.Id))).ToList();
    }

    /// <summary>Kampanya fiyatını (KDV dahil) belirler; boş gönderilirse kampanya kalkar. Normal fiyattan düşük olmalıdır.</summary>
    [HttpPut("products/{id:guid}/sale-price")]
    public async Task<ActionResult<object>> SetSalePrice(Guid id, SalePriceInput input, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound(new { error = "Ürün bulunamadı." });
        if (input.SalePrice is { } price)
        {
            price = decimal.Round(price, 2);
            if (price <= 0) return BadRequest(new { error = "Kampanya fiyatı sıfırdan büyük olmalı." });
            if (price >= product.Price) return BadRequest(new { error = "Kampanya fiyatı normal fiyattan düşük olmalı." });
            product.B2CSalePrice = price;
        }
        else product.B2CSalePrice = null;
        await db.SaveChangesAsync(ct);
        return new { id = product.Id, salePrice = product.B2CSalePrice };
    }

    /// <summary>Kategoriyi (ve alt kategorilerini) B2C mağazada gizler ya da gösterir.</summary>
    [HttpPut("categories/{id:guid}/hidden")]
    public async Task<ActionResult<object>> SetCategoryHidden(Guid id, CategoryHiddenInput input, CancellationToken ct)
    {
        var updated = await db.Categories.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.B2CHidden, input.Hidden), ct);
        return updated == 0 ? NotFound(new { error = "Kategori bulunamadı." }) : new { id, hidden = input.Hidden };
    }
}
