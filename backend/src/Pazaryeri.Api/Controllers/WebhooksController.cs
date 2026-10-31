using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Application.Modules;
using Pazaryeri.Infrastructure.Marketplace;
using Pazaryeri.Infrastructure.Orders;

namespace Pazaryeri.Api.Controllers;

/// <summary>Kanalların çağırdığı webhook alıcısı (kimliksiz; gizli anahtarla doğrulanır). Hemen 200 döner, işi kuyruğa bırakır.</summary>
[ApiController]
[Route("webhooks/trendyol/{tenantId:guid}")]
[AllowAnonymous]
[CoreModule]
[EnableRateLimiting(StoreRateLimiting.WebhookPolicy)]
public class WebhooksController(WebhookReceiver receiver) : ControllerBase
{
    public const string TrendyolChannel = "Trendyol";
    public const int MaxBodyBytes = 64 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(Guid tenantId, CancellationToken ct)
    {
        string body;
        using (var reader = new StreamReader(Request.Body)) body = await reader.ReadToEndAsync(ct);
        var outcome = await receiver.ReceiveAsync(tenantId, TrendyolChannel, Request.Headers.Authorization.ToString(), Request.Headers["x-api-key"].ToString(), body, ct);
        return outcome == WebhookOutcome.Unauthorized ? Unauthorized() : Ok(new { status = outcome.ToString() });
    }
}

/// <summary>Yönetici: kanalda webhook kaydı ve durumu.</summary>
[ApiController]
[Route("api/webhooks/{channel}")]
[RequiresModule(ModuleIds.Orders)]
public class WebhooksAdminController(WebhookRegistrationService registration) : ControllerBase
{
    private static bool Supported(string channel) => string.Equals(channel, WebhooksController.TrendyolChannel, StringComparison.OrdinalIgnoreCase);

    [HttpGet]
    public async Task<ActionResult<WebhookStatus>> Status(string channel, CancellationToken ct) =>
        Supported(channel) ? await registration.StatusAsync(WebhooksController.TrendyolChannel, ct) : NotFound(new { error = "Bu kanal webhook desteklemiyor." });

    [HttpPost]
    public async Task<ActionResult<WebhookStatus>> Register(string channel, CancellationToken ct)
    {
        if (!Supported(channel)) return NotFound(new { error = "Bu kanal webhook desteklemiyor." });
        try { return await registration.RegisterAsync(WebhooksController.TrendyolChannel, ct); }
        catch (InvalidOperationException e) { return Conflict(new { error = e.Message }); }
        catch (MarketplaceApiException e) { return StatusCode(StatusCodes.Status502BadGateway, new { error = $"Kanal webhook kaydını reddetti ({e.StatusCode})." }); }
    }
}
