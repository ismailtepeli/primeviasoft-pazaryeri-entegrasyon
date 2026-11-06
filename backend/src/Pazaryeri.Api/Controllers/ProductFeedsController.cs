using Microsoft.AspNetCore.Mvc;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Feeds;
using Pazaryeri.Infrastructure.Feeds;

namespace Pazaryeri.Api.Controllers;

/// <summary>Dropshipping XML beslemeleri (yönetici): adresi çözümleme/önizleme, besleme kaydı, elle çalıştırma. Ürünler barkoda göre eklenir ve güncel tutulur.</summary>
[ApiController]
[Route("api/feeds")]
[CoreModule]
public class ProductFeedsController(ProductFeedService feeds) : ControllerBase
{
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (FeedException e) { return StatusCode(e.Status, new { error = e.Message }); }
    }

    [HttpGet]
    public async Task<IReadOnlyList<ProductFeedDto>> List(CancellationToken ct) => await feeds.ListAsync(ct);

    /// <summary>Adresi indirir, XML biçimini tanır ve alan eşlemesi ile ilk ürünlerin önizlemesini döner; hiçbir şey kaydedilmez.</summary>
    [HttpPost("analyze")]
    public Task<IActionResult> Analyze(FeedAnalyzeRequest input, CancellationToken ct) => Run(async () => Ok(await feeds.AnalyzeAsync(input, ct)));

    /// <summary>Kayıtlı beslemeyi (adresi gizli tutarak) yeniden çözümler; eşleme düzenleme ekranı için.</summary>
    [HttpPost("{id:guid}/analyze")]
    public Task<IActionResult> AnalyzeStored(Guid id, FeedAnalyzeRequest input, CancellationToken ct) => Run(async () => Ok(await feeds.AnalyzeStoredAsync(id, input, ct)));

    [HttpPost]
    public Task<IActionResult> Create(ProductFeedInput input, CancellationToken ct) => Run(async () => Ok(await feeds.SaveAsync(null, input, ct)));

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, ProductFeedInput input, CancellationToken ct) => Run(async () => Ok(await feeds.SaveAsync(id, input, ct)));

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken ct) => Run(async () => { await feeds.DeleteAsync(id, ct); return NoContent(); });

    /// <summary>Beslemeyi hemen çalıştırır (ürünleri ekler/günceller).</summary>
    [HttpPost("{id:guid}/run")]
    public Task<IActionResult> RunNow(Guid id, CancellationToken ct) => Run(async () => Ok(await feeds.RunAsync(id, ct)));
}
