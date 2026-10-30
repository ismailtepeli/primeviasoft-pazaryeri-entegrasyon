using Microsoft.AspNetCore.Mvc;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Dealers;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Orders;
using Pazaryeri.Infrastructure.Orders;

namespace Pazaryeri.Api.Controllers;

public record DealerTermInput(int PaymentTermDays);

/// <summary>Yönetici tarafında B2B siparişleri: liste, ayrıntı ve onay, ret, sevk, teslim, iptal işlemleri.</summary>
[ApiController]
[Route("api/b2b/orders")]
[RequiresModule(ModuleIds.B2BStore)]
public class B2BOrdersAdminController(B2BAdminOrderService orders) : ControllerBase
{
    [HttpGet]
    public Task<ActionResult<B2BAdminOrderPage>> List([FromQuery] string? tab, [FromQuery] string? q, [FromQuery] Guid? dealerId, [FromQuery] int page = 1, CancellationToken ct = default) =>
        Run(() => orders.ListAsync(tab, q, dealerId, page, ct));

    [HttpGet("{id:guid}")]
    public Task<ActionResult<B2BAdminOrderDetail>> Get(Guid id, CancellationToken ct) => Run(() => orders.GetAsync(id, ct));

    [HttpPost("{id:guid}/approve")]
    public Task<ActionResult<B2BAdminOrderDetail>> Approve(Guid id, B2BOrderActionInput? input, CancellationToken ct) => Run(() => orders.ActAsync(id, B2BAction.Approve, input ?? new(null, null, null), ct));

    [HttpPost("{id:guid}/reject")]
    public Task<ActionResult<B2BAdminOrderDetail>> Reject(Guid id, B2BOrderActionInput input, CancellationToken ct) => Run(() => orders.ActAsync(id, B2BAction.Reject, input, ct));

    [HttpPost("{id:guid}/ship")]
    public Task<ActionResult<B2BAdminOrderDetail>> Ship(Guid id, B2BOrderActionInput input, CancellationToken ct) => Run(() => orders.ActAsync(id, B2BAction.Ship, input, ct));

    [HttpPost("{id:guid}/deliver")]
    public Task<ActionResult<B2BAdminOrderDetail>> Deliver(Guid id, CancellationToken ct) => Run(() => orders.ActAsync(id, B2BAction.Deliver, new(null, null, null), ct));

    [HttpPost("{id:guid}/cancel")]
    public Task<ActionResult<B2BAdminOrderDetail>> Cancel(Guid id, B2BOrderActionInput? input, CancellationToken ct) => Run(() => orders.ActAsync(id, B2BAction.Cancel, input ?? new(null, null, null), ct));

    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (StoreException e) { return StatusCode(e.Status, new { error = e.Message }); }
    }
}

/// <summary>Bayi cari hesabı: bakiye, vadesi geçen tutar, ödeme kayıtları ve vade günü.</summary>
[ApiController]
[Route("api/b2b/dealers/{dealerId:guid}")]
[RequiresModule(ModuleIds.B2BStore)]
public class DealerAccountController(DealerAccountService accounts) : ControllerBase
{
    [HttpGet("account")]
    public Task<ActionResult<DealerAccountView>> Get(Guid dealerId, CancellationToken ct) => Run(() => accounts.GetAsync(dealerId, ct));

    [HttpPost("payments")]
    public Task<ActionResult<DealerAccountView>> AddPayment(Guid dealerId, PaymentInput input, CancellationToken ct) => Run(() => accounts.AddPaymentAsync(dealerId, input, ct));

    [HttpDelete("payments/{paymentId:guid}")]
    public Task<ActionResult<DealerAccountView>> RemovePayment(Guid dealerId, Guid paymentId, CancellationToken ct) => Run(() => accounts.RemovePaymentAsync(dealerId, paymentId, ct));

    [HttpPut("term")]
    public Task<ActionResult<DealerAccountView>> SetTerm(Guid dealerId, DealerTermInput input, CancellationToken ct) => Run(() => accounts.SetTermAsync(dealerId, input.PaymentTermDays, ct));

    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (StoreException e) { return StatusCode(e.Status, new { error = e.Message }); }
    }
}
