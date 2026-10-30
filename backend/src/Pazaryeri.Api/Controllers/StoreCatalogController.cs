using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Pazaryeri.Infrastructure.Auth;
using Pazaryeri.Infrastructure.Pricing;

namespace Pazaryeri.Api.Controllers;

/// <summary>Bayi sitesi ürün okuma uçları: yalnızca bayiye görünen ürünler, bayiye özel fiyat ve stok durumu. Yalnızca bayi token'ı girebilir.</summary>
[ApiController]
[Route("api/store/catalog")]
[Authorize(Policy = AuthPolicies.Dealer)]
[RequiresModule(ModuleIds.B2BStore)]
public class StoreCatalogController(StoreCatalogService catalog) : ControllerBase
{
    private Guid DealerId => Guid.Parse(User.FindFirstValue(AppClaims.DealerId)!);

    [HttpGet("products")]
    public async Task<StoreProductPage> Products([FromQuery] string? q, [FromQuery] Guid? categoryId, [FromQuery] string? sort,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 24, [FromQuery] string[]? brand = null, [FromQuery] decimal? minPrice = null,
        [FromQuery] decimal? maxPrice = null, [FromQuery] bool inStock = false, CancellationToken ct = default) =>
        await catalog.ListAsync(DealerId, new StoreQuery(q, categoryId, sort, page, pageSize, brand, minPrice, maxPrice, inStock), ct);

    [HttpGet("products/{id:guid}")]
    public async Task<ActionResult<StoreProductDetail>> Product(Guid id, CancellationToken ct) =>
        await catalog.DetailAsync(DealerId, id, ct) is { } d ? d : NotFound(new { error = "Ürün bulunamadı." });

    [HttpGet("categories")]
    public async Task<IReadOnlyList<StoreCategory>> Categories(CancellationToken ct) => await catalog.CategoriesAsync(DealerId, ct);
}
