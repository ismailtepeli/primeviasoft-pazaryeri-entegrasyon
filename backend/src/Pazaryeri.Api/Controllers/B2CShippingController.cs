using Microsoft.AspNetCore.Mvc;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Shop;
using Pazaryeri.Infrastructure.Orders;
using Pazaryeri.Infrastructure.Shop;

namespace Pazaryeri.Api.Controllers;

/// <summary>B2C kargo yönetimi (yönetici): kargo firmaları ve desi ücret kuralları, havale onayı ve siparişi kargoya verme (takip numarası).</summary>
[ApiController]
[Route("api/b2c")]
[RequiresModule(ModuleIds.B2CStore)]
public class B2CShippingController(CheckoutService checkout) : ControllerBase
{
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (StoreException e) { return StatusCode(e.Status, new { error = e.Message }); }
    }

    [HttpGet("carriers")]
    public async Task<IReadOnlyList<ShippingCarrierDto>> List(CancellationToken ct) => await checkout.CarriersAsync(ct);

    [HttpPost("carriers")]
    public Task<IActionResult> Create(ShippingCarrierInput input, CancellationToken ct) => Run(async () => Ok(await checkout.SaveCarrierAsync(null, input, ct)));

    [HttpPut("carriers/{id:guid}")]
    public Task<IActionResult> Update(Guid id, ShippingCarrierInput input, CancellationToken ct) => Run(async () => Ok(await checkout.SaveCarrierAsync(id, input, ct)));

    [HttpDelete("carriers/{id:guid}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken ct) => Run(async () => { await checkout.DeleteCarrierAsync(id, ct); return NoContent(); });

    [HttpPost("orders/{id:guid}/approve-payment")]
    public Task<IActionResult> ApprovePayment(Guid id, CancellationToken ct) => Run(async () => { await checkout.ApprovePaymentAsync(id, ct); return NoContent(); });

    [HttpPost("orders/{id:guid}/ship")]
    public Task<IActionResult> Ship(Guid id, ShipOrderInput input, CancellationToken ct) => Run(async () => { await checkout.ShipAsync(id, input, ct); return NoContent(); });
}
