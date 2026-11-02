using Microsoft.AspNetCore.Mvc;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Shop;
using Pazaryeri.Infrastructure.Orders;
using Pazaryeri.Infrastructure.Shop;

namespace Pazaryeri.Api.Controllers;

/// <summary>B2C mağaza kargo ve ödeme ayarları (yönetici): kargo ücreti, ücretsiz kargo eşiği, havale/kapıda ödeme/kart.</summary>
[ApiController]
[Route("api/b2c/settings")]
[RequiresModule(ModuleIds.B2CStore)]
public class B2CSettingsController(CheckoutService checkout) : ControllerBase
{
    [HttpGet]
    public async Task<B2CSettingsDto> Get(CancellationToken ct) => await checkout.GetSettingsAsync(ct);

    [HttpPut]
    public async Task<IActionResult> Save(B2CSettingsInput input, CancellationToken ct)
    {
        try { return Ok(await checkout.SaveSettingsAsync(input, ct)); }
        catch (StoreException e) { return StatusCode(e.Status, new { error = e.Message }); }
    }
}
