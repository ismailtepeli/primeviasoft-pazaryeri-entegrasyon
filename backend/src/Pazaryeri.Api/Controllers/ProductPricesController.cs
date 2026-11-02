using Microsoft.AspNetCore.Mvc;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Products;

namespace Pazaryeri.Api.Controllers;

public record SetPriceInput(decimal Price);

/// <summary>Ürün listesinden fiyat güncelleme: tek ürün (satır içi), seçili ürünlere toplu kural ve Excel.</summary>
[ApiController]
[Route("api/products")]
[CoreModule]
public class ProductPricesController(IProductPriceService prices) : ControllerBase
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpPut("{id:guid}/price")]
    public Task<ActionResult<ProductPriceDto>> SetPrice(Guid id, SetPriceInput input, CancellationToken ct) => Run(() => prices.SetPriceAsync(id, input.Price, ct));

    /// <summary>Seçili ürünlere yüzde/tutar/sabit fiyat uygular.</summary>
    [HttpPut("prices")]
    public Task<ActionResult<PriceChangeResult>> Bulk(BulkPriceInput input, CancellationToken ct) => Run(() => prices.BulkChangeAsync(input, ct));

    /// <summary>Barkod, ürün adı ve mevcut fiyatla dolu Excel; fiyat sütunu düzenlenip geri yüklenir.</summary>
    [HttpGet("price-sheet")]
    public async Task<IActionResult> Sheet(CancellationToken ct) => File(await prices.BuildSheetAsync(ct), XlsxType, "urun-fiyatlari.xlsx");

    [HttpPost("price-import")]
    [RequestSizeLimit(MaxFileBytes + 64 * 1024)]
    public async Task<ActionResult<PriceImportResult>> Import(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "Bir Excel dosyası seçin." });
        if (file.Length > MaxFileBytes) return BadRequest(new { error = "Dosya en fazla 5 MB olabilir." });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { error = "Yalnızca .xlsx dosyaları yüklenebilir." });
        return await Run(async () =>
        {
            await using var stream = file.OpenReadStream();
            return await prices.ImportAsync(stream, ct);
        });
    }

    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (ProductPriceException e) { return StatusCode(e.Status, new { error = e.Message }); }
    }
}
