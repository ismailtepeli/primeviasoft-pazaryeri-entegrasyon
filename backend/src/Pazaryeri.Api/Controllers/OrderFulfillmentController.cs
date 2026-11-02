using Microsoft.AspNetCore.Mvc;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Pazaryeri.Infrastructure.Orders;

namespace Pazaryeri.Api.Controllers;

public record InvoiceInput(string? InvoiceNumber);

/// <summary>Pazaryeri siparişinin durumunu kanala bildirir (hazırlanıyor, faturalandı).</summary>
[ApiController]
[Route("api/orders/{id:guid}")]
[RequiresModule(ModuleIds.Orders)]
public class OrderFulfillmentController(OrderFulfillmentService fulfillment) : ControllerBase
{
    [HttpPost("picking")]
    public Task<ActionResult<FulfillmentResult>> Picking(Guid id, CancellationToken ct) => Run(() => fulfillment.ActAsync(id, FulfillmentAction.Picking, null, ct));

    [HttpPost("invoice")]
    public Task<ActionResult<FulfillmentResult>> Invoice(Guid id, InvoiceInput input, CancellationToken ct) => Run(() => fulfillment.ActAsync(id, FulfillmentAction.Invoice, input.InvoiceNumber, ct));

    private async Task<ActionResult<FulfillmentResult>> Run(Func<Task<FulfillmentResult>> action)
    {
        try { return Ok(await action()); }
        catch (StoreException e) { return StatusCode(e.Status, new { error = e.Message }); }
    }
}
