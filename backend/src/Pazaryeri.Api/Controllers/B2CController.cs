using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

public record SalePriceInput(decimal? SalePrice);

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
