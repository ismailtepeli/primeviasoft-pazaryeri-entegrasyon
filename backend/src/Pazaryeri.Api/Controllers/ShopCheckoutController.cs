using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Shop;
using Pazaryeri.Infrastructure.Auth;
using Pazaryeri.Infrastructure.Orders;
using Pazaryeri.Infrastructure.Shop;

namespace Pazaryeri.Api.Controllers;

public record FakePaymentInput(bool Success);

/// <summary>Sepet fiyatlama (herkese açık) ve B2C sipariş (müşteri oturumu gerekir). Fiyat, stok ve kargo her zaman sunucuda hesaplanır.</summary>
[ApiController]
[Route("api/shop")]
[CoreModule]
[EnableRateLimiting(StoreRateLimiting.ShopPolicy)]
public class ShopCheckoutController(CheckoutService checkout, ShopCatalogService shop, IPaymentProviderInfo providerInfo) : ControllerBase
{
    private Guid CustomerId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (StoreException e) { return StatusCode(e.Status, new { error = e.Message, details = e.Details }); }
    }

    /// <summary>Sepet satırlarını (ürün, adet) fiyatlar: güncel fiyat, stok durumu, kargo, toplam ve ödeme seçenekleri.</summary>
    [HttpPost("cart/quote")]
    [AllowAnonymous]
    public Task<IActionResult> Quote([FromQuery] string? slug, CartQuoteRequest input, CancellationToken ct) => Run(async () =>
        await shop.ResolveAsync(slug, ct) is { } tenant ? Ok(await checkout.QuoteAsync(tenant, input.Lines, input.CarrierId, ct)) : NotFound(new { error = "Mağaza bulunamadı." }));

    [HttpPost("checkout")]
    [Authorize(Policy = AuthPolicies.Customer)]
    [RequiresModule(ModuleIds.B2CStore)]
    public Task<IActionResult> Checkout(CheckoutRequest input, CancellationToken ct) => Run(async () => Ok(await checkout.CheckoutAsync(CustomerId, input, ct)));

    [HttpPost("me/orders/{id:guid}/cancel")]
    [Authorize(Policy = AuthPolicies.Customer)]
    [RequiresModule(ModuleIds.B2CStore)]
    public Task<IActionResult> Cancel(Guid id, CancellationToken ct) => Run(async () => { await checkout.CancelAsync(CustomerId, id, ct); return NoContent(); });

    /// <summary>
    /// Deneme ödeme sayfasının sonucu. Yalnızca <c>Payments:Provider=fake</c> iken vardır; gerçek sağlayıcıda bu uç bulunmaz
    /// (ödeme sonucu sağlayıcının doğrulanmış geri bildiriminden gelir).
    /// </summary>
    [HttpPost("me/orders/{id:guid}/fake-payment")]
    [Authorize(Policy = AuthPolicies.Customer)]
    [RequiresModule(ModuleIds.B2CStore)]
    public Task<IActionResult> FakePayment(Guid id, FakePaymentInput input, CancellationToken ct) => Run(async () =>
        providerInfo.IsFake ? Ok(new { paymentStatus = await checkout.ConfirmCardAsync(CustomerId, id, input.Success, ct) }) : NotFound());
}
