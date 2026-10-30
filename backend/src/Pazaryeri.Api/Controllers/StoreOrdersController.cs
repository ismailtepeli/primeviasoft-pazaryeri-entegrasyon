using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Pazaryeri.Infrastructure.Auth;
using Pazaryeri.Infrastructure.Orders;

namespace Pazaryeri.Api.Controllers;

public record CartAddInput(Guid ProductId, int Quantity);

public record CartSetInput(int Quantity);

/// <summary>Bayi sitesi sepeti ve siparişleri (api/store). Yalnızca bayi token'ı girebilir; bayi yalnızca kendi firmasının verisini görür.</summary>
[ApiController]
[Route("api/store")]
[Authorize(Policy = AuthPolicies.Dealer)]
[RequiresModule(ModuleIds.B2BStore)]
public class StoreOrdersController(B2BOrderService orders) : ControllerBase
{
    private Guid DealerId => Guid.Parse(User.FindFirstValue(AppClaims.DealerId)!);

    [HttpGet("cart")]
    public Task<CartView> Cart(CancellationToken ct) => orders.GetCartAsync(DealerId, ct);

    [HttpPost("cart/items")]
    public Task<ActionResult<CartView>> AddItem(CartAddInput input, CancellationToken ct) => Run(() => orders.AddAsync(DealerId, input.ProductId, input.Quantity, ct));

    [HttpPut("cart/items/{productId:guid}")]
    public Task<ActionResult<CartView>> SetItem(Guid productId, CartSetInput input, CancellationToken ct) => Run(() => orders.SetAsync(DealerId, productId, input.Quantity, ct));

    [HttpDelete("cart")]
    public Task<ActionResult<CartView>> Clear(CancellationToken ct) => Run(() => orders.ClearAsync(DealerId, ct));

    [HttpPost("orders")]
    public async Task<ActionResult<B2BOrderDetail>> Checkout(CheckoutInput input, CancellationToken ct)
    {
        var result = await Run(() => orders.CheckoutAsync(DealerId, input, ct));
        return result.Result is null ? Created($"api/store/orders/{result.Value!.Id}", result.Value) : result;
    }

    [HttpGet("orders")]
    public Task<B2BOrderPage> Orders([FromQuery] int page = 1, CancellationToken ct = default) => orders.OrdersAsync(DealerId, page, ct);

    [HttpGet("orders/{id:guid}")]
    public Task<ActionResult<B2BOrderDetail>> Order(Guid id, CancellationToken ct) => Run(() => orders.OrderAsync(DealerId, id, ct));

    [HttpPost("orders/{id:guid}/cancel")]
    public Task<ActionResult<B2BOrderDetail>> Cancel(Guid id, CancellationToken ct) => Run(() => orders.CancelAsync(DealerId, id, ct));

    [HttpPost("orders/{id:guid}/reorder")]
    public Task<ActionResult<ReorderResult>> Reorder(Guid id, CancellationToken ct) => Run(() => orders.ReorderAsync(DealerId, id, ct));

    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (StoreException e) { return StatusCode(e.Status, new { error = e.Message, details = e.Details }); }
    }
}
