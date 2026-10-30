using Microsoft.AspNetCore.Mvc;
using Pazaryeri.Application.Products;

namespace Pazaryeri.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductImportController(IProductImportService import) : ControllerBase
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Başlıkları ve örnek satırları hazır Excel şablonu.</summary>
    [HttpGet("template")]
    public IActionResult Template() => File(import.BuildTemplate(), XlsxType, "urun-sablonu.xlsx");

    /// <summary>Excel'deki ürünleri yükler; geçerli satırlar kaydedilir, geçersizler satır numarasıyla raporlanır.</summary>
    [HttpPost("import")]
    [RequestSizeLimit(MaxFileBytes + 64 * 1024)]
    public async Task<ActionResult<ProductImportResult>> Import(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "Bir Excel dosyası seçin." });
        if (file.Length > MaxFileBytes) return BadRequest(new { error = "Dosya en fazla 5 MB olabilir." });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Yalnızca .xlsx dosyaları yüklenebilir." });

        try
        {
            await using var stream = file.OpenReadStream();
            return await import.ImportAsync(stream, ct);
        }
        catch (ProductImportException e)
        {
            return BadRequest(new { error = e.Message });
        }
    }
}
