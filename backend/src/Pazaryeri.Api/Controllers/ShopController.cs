using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Pazaryeri.Infrastructure.Pricing;
using Pazaryeri.Infrastructure.Shop;

namespace Pazaryeri.Api.Controllers;

/// <summary>
/// B2C mağaza vitrini (api/shop): herkese açık, oturumsuz, salt okunur. Kiracı her istekte slug ile verilir; B2C mağaza modülü
/// o kiracıda kapalıysa (ya da kiracı yoksa) 404 döner. Modül kapısı kiracıyı oturumdan bilemediği için serviste uygulanır.
/// </summary>
[ApiController]
[Route("api/shop")]
[AllowAnonymous]
[CoreModule]
[EnableRateLimiting(StoreRateLimiting.ShopPolicy)]
public class ShopController(ShopCatalogService shop) : ControllerBase
{
    private IActionResult NotFoundShop() => NotFound(new { error = "Mağaza bulunamadı." });

    [HttpGet("info")]
    public async Task<IActionResult> Info([FromQuery] string? slug, CancellationToken ct)
    {
        var info = await shop.InfoAsync(slug, ct);
        if (info is null) return NotFoundShop();
        Response.Headers.CacheControl = "public, max-age=30";
        return Ok(info);
    }

    [HttpGet("categories")]
    public async Task<IActionResult> Categories([FromQuery] string? slug, CancellationToken ct) =>
        await shop.ResolveAsync(slug, ct) is { } tenant ? Ok(await shop.CategoriesAsync(tenant, ct)) : NotFoundShop();

    [HttpGet("products")]
    public async Task<IActionResult> Products([FromQuery] string? slug, [FromQuery] string? q, [FromQuery] Guid? categoryId, [FromQuery] string? sort,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 24, [FromQuery] string[]? brand = null, [FromQuery] decimal? minPrice = null,
        [FromQuery] decimal? maxPrice = null, [FromQuery] bool inStock = false, CancellationToken ct = default) =>
        await shop.ResolveAsync(slug, ct) is { } tenant
            ? Ok(await shop.ListAsync(tenant, new ShopQuery(q, categoryId, sort, page, pageSize, brand, minPrice, maxPrice, inStock), ct))
            : NotFoundShop();

    [HttpGet("products/{id:guid}")]
    public async Task<IActionResult> Product(Guid id, [FromQuery] string? slug, CancellationToken ct)
    {
        if (await shop.ResolveAsync(slug, ct) is not { } tenant) return NotFoundShop();
        return await shop.DetailAsync(tenant, id, ct) is { } d ? Ok(d) : NotFound(new { error = "Ürün bulunamadı." });
    }
}
