using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Dealers;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Pricing;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Pricing;

namespace Pazaryeri.Api.Controllers;

public record PriceListDto(Guid Id, string Name, string Currency, Guid? GroupId, string? GroupName, Guid? DealerId, string? DealerName,
    decimal? DiscountPercent, bool PricesIncludeVat, bool ShowVatIncluded, bool IsActive, int ItemCount);

public record PriceListInput(string? Name, string? Currency, Guid? GroupId, Guid? DealerId, decimal? DiscountPercent,
    bool PricesIncludeVat, bool ShowVatIncluded, bool IsActive);

/// <summary>MinOrderQuantity/PackSize ürünün etkin değerleridir; ListMinOrderQuantity/ListPackSize bu listede kalem düzeyinde geçersiz kılınanlardır (boşsa ürünün değeri).</summary>
public record PriceListItemRow(Guid ProductId, string Name, string Barcode, decimal GeneralPrice, decimal? ListPrice, int MinOrderQuantity, int PackSize,
    int? ListMinOrderQuantity = null, int? ListPackSize = null);

public record PriceListItemPage(IReadOnlyList<PriceListItemRow> Items, int Total);

public record PriceListItemInput(decimal? Price, int? MinOrderQuantity = null, int? PackSize = null);

public record QuantityRuleInput(int MinOrderQuantity, int PackSize);

public record PricePreviewDto(Guid ProductId, string Name, decimal Net, decimal Gross, decimal Display, bool DisplayIncludesVat,
    string Currency, string Source, int MinOrderQuantity, int PackSize);

/// <summary>Fiyat listeleri: bayiye özel ya da bayi grubuna bağlı liste, ürün fiyatları, iskonto, minimum ve koli adedi.</summary>
[ApiController]
[Route("api/price-lists")]
[RequiresModule(ModuleIds.PriceLists)]
public class PriceListsController(AppDbContext db, DealerPriceService prices, PriceListImportService import) : ControllerBase
{
    public const int MaxPageSize = 100;
    private static readonly string[] Currencies = ["TRY", "USD", "EUR"];

    [HttpGet]
    public async Task<List<PriceListDto>> List(CancellationToken ct) =>
        await db.PriceLists.AsNoTracking().OrderBy(l => l.Name).Select(l => new PriceListDto(l.Id, l.Name, l.Currency, l.GroupId,
            db.DealerGroups.Where(g => g.Id == l.GroupId).Select(g => g.Name).FirstOrDefault(), l.DealerId,
            db.Dealers.Where(d => d.Id == l.DealerId).Select(d => d.Name).FirstOrDefault(),
            l.DiscountPercent, l.PricesIncludeVat, l.ShowVatIncluded, l.IsActive, l.Items.Count)).ToListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<PriceListDto>> Create(PriceListInput input, CancellationToken ct)
    {
        var error = await ValidateAsync(input, null, ct);
        if (error is not null) return error;
        var list = new PriceList();
        Apply(list, input);
        db.PriceLists.Add(list);
        await db.SaveChangesAsync(ct);
        return Created($"api/price-lists/{list.Id}", await One(list.Id, ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PriceListDto>> Update(Guid id, PriceListInput input, CancellationToken ct)
    {
        var list = await db.PriceLists.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (list is null) return NotFound(new { error = "Fiyat listesi bulunamadı." });
        var error = await ValidateAsync(input, id, ct);
        if (error is not null) return error;
        Apply(list, input);
        await db.SaveChangesAsync(ct);
        return await One(id, ct);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var list = await db.PriceLists.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (list is null) return NotFound(new { error = "Fiyat listesi bulunamadı." });
        db.PriceLists.Remove(list);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Listenin ürün fiyatları: tüm ürünler gelir, listede fiyatı olmayanın ListPrice alanı boştur. Arama ad/barkod, yalnızca fiyatlılar filtresi.</summary>
    [HttpGet("{id:guid}/items")]
    public async Task<ActionResult<PriceListItemPage>> Items(Guid id, [FromQuery] string? q, [FromQuery] bool onlyPriced = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!await db.PriceLists.AnyAsync(l => l.Id == id, ct)) return NotFound(new { error = "Fiyat listesi bulunamadı." });
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);
        var query = db.Products.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) { var t = q.Trim(); query = query.Where(p => p.Name.Contains(t) || p.Barcode.Contains(t)); }
        if (onlyPriced) query = query.Where(p => db.PriceListItems.Any(i => i.PriceListId == id && i.ProductId == p.Id));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(p => p.Name).ThenBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new PriceListItemRow(p.Id, p.Name, p.Barcode, p.Price,
                db.PriceListItems.Where(i => i.PriceListId == id && i.ProductId == p.Id).Select(i => (decimal?)i.Price).FirstOrDefault(),
                p.MinOrderQuantity, p.PackSize,
                db.PriceListItems.Where(i => i.PriceListId == id && i.ProductId == p.Id).Select(i => i.MinOrderQuantity).FirstOrDefault(),
                db.PriceListItems.Where(i => i.PriceListId == id && i.ProductId == p.Id).Select(i => i.PackSize).FirstOrDefault())).ToListAsync(ct);
        // Eski ya da elle girilmiş geçersiz değerler ekranda etkin değerleriyle görünür.
        rows = rows.Select(r => { var (min, pack) = OrderQuantityRules.Normalize(r.MinOrderQuantity, r.PackSize); return r with { MinOrderQuantity = min, PackSize = pack }; }).ToList();
        return new PriceListItemPage(rows, total);
    }

    /// <summary>
    /// Ürüne liste fiyatı yazar; fiyat boş gönderilirse kalem silinir (iskonto/genel fiyata dönülür, kalemin adet ayarı da gider).
    /// İsteğe bağlı minimum/koli bu listede ürünün değerini geçersiz kılar; boş bırakılırsa ürünün değeri kullanılır.
    /// </summary>
    [HttpPut("{id:guid}/items/{productId:guid}")]
    public async Task<IActionResult> SetItem(Guid id, Guid productId, PriceListItemInput input, CancellationToken ct)
    {
        if (!await db.PriceLists.AnyAsync(l => l.Id == id, ct)) return NotFound(new { error = "Fiyat listesi bulunamadı." });
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct)) return NotFound(new { error = "Ürün bulunamadı." });
        var item = await db.PriceListItems.FirstOrDefaultAsync(i => i.PriceListId == id && i.ProductId == productId, ct);
        if (input.Price is null)
        {
            if (input.MinOrderQuantity is not null || input.PackSize is not null) return BadRequest(new { error = "Adet ayarı için önce liste fiyatı girin." });
            if (item is not null) db.PriceListItems.Remove(item);
        }
        else
        {
            if (input.Price < 0 || input.Price > 100_000_000m) return BadRequest(new { error = "Fiyat 0 ile 100.000.000 arasında olmalı." });
            if (OrderQuantityRules.ValidateOverrides(input.MinOrderQuantity, input.PackSize) is { } qe) return BadRequest(new { error = qe });
            var price = DealerPricing.Round(input.Price.Value);
            if (item is null) db.PriceListItems.Add(new PriceListItem { PriceListId = id, ProductId = productId, Price = price, MinOrderQuantity = input.MinOrderQuantity, PackSize = input.PackSize });
            else { item.Price = price; item.MinOrderQuantity = input.MinOrderQuantity; item.PackSize = input.PackSize; }
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Ürünün varsayılan minimum sipariş ve koli adedi; listede kalemi geçersiz kılmıyorsa tüm listeler ve genel fiyat için geçerlidir.</summary>
    [HttpPut("products/{productId:guid}/quantity")]
    public async Task<IActionResult> SetQuantity(Guid productId, QuantityRuleInput input, CancellationToken ct)
    {
        if (OrderQuantityRules.ValidateSettings(input.MinOrderQuantity, input.PackSize) is { } error) return BadRequest(new { error });
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return NotFound(new { error = "Ürün bulunamadı." });
        product.MinOrderQuantity = input.MinOrderQuantity;
        product.PackSize = input.PackSize;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Excel'den toplu fiyat yükleme (Barkod, Fiyat; isteğe bağlı Minimum, Koli).</summary>
    [HttpPost("{id:guid}/import")]
    [RequestSizeLimit(5_000_000)]
    public async Task<ActionResult<PriceImportResult>> Import(Guid id, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "Bir Excel dosyası seçin." });
        try
        {
            await using var stream = file.OpenReadStream();
            return await import.ImportAsync(id, stream, ct);
        }
        catch (PriceImportException e) { return e.Message.Contains("bulunamadı") ? NotFound(new { error = e.Message }) : BadRequest(new { error = e.Message }); }
    }

    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Boş içe aktarma şablonu (Barkod, Fiyat, Minimum, Koli başlıklı .xlsx).</summary>
    [HttpGet("template")]
    public FileContentResult Template() => File(PriceListImportService.Template(), XlsxType, "fiyat-listesi-sablon.xlsx");

    /// <summary>Listenin fiyatlı ürünlerini içe aktarmayla aynı biçimde Excel'e aktarır.</summary>
    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var result = await import.ExportAsync(id, ct);
        if (result is null) return NotFound(new { error = "Fiyat listesi bulunamadı." });
        var safe = new string(result.Value.Name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');
        return File(result.Value.File, XlsxType, $"fiyat-listesi-{(safe.Length == 0 ? "liste" : safe)}.xlsx");
    }

    /// <summary>Bir bayinin göreceği fiyatların önizlemesi; bayi sitesiyle aynı çözümleme kuralını kullanır.</summary>
    [HttpGet("preview")]
    public async Task<ActionResult<List<PricePreviewDto>>> Preview([FromQuery] Guid dealerId, [FromQuery] string? q, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) { var t = q.Trim(); query = query.Where(p => p.Name.Contains(t) || p.Barcode.Contains(t)); }
        var products = await query.OrderBy(p => p.Name).Take(50).Select(p => new { p.Id, p.Name }).ToListAsync(ct);
        var resolved = await prices.ResolveAsync(dealerId, products.Select(p => p.Id).ToList(), ct);
        if (resolved is null) return NotFound(new { error = "Bayi bulunamadı." });
        return products.Select(p => (Product: p, Price: resolved.First(r => r.ProductId == p.Id)))
            .Select(x => new PricePreviewDto(x.Product.Id, x.Product.Name, x.Price.Price.Net, x.Price.Price.Gross, x.Price.Price.Display,
                x.Price.Price.DisplayIncludesVat, x.Price.Price.Currency, x.Price.Price.Source.ToString(), x.Price.MinOrderQuantity, x.Price.PackSize)).ToList();
    }

    private async Task<PriceListDto> One(Guid id, CancellationToken ct) => (await List(ct)).First(l => l.Id == id);

    private static void Apply(PriceList list, PriceListInput input)
    {
        list.Name = input.Name!.Trim();
        list.Currency = (input.Currency ?? "TRY").Trim().ToUpperInvariant();
        list.GroupId = input.GroupId;
        list.DealerId = input.DealerId;
        list.DiscountPercent = input.DiscountPercent is > 0 ? input.DiscountPercent : null;
        list.PricesIncludeVat = input.PricesIncludeVat;
        list.ShowVatIncluded = input.ShowVatIncluded;
        list.IsActive = input.IsActive;
    }

    private async Task<ActionResult?> ValidateAsync(PriceListInput i, Guid? selfId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(i.Name)) return BadRequest(new { error = "Liste adı zorunlu." });
        if (DealerRules.ValidateLength(i.Name, 120, "Liste adı") is { } len) return BadRequest(new { error = len });
        if (!Currencies.Contains((i.Currency ?? "TRY").Trim().ToUpperInvariant())) return BadRequest(new { error = "Para birimi TRY, USD ya da EUR olmalı." });
        if (i.GroupId is not null && i.DealerId is not null) return BadRequest(new { error = "Liste bir bayi grubuna ya da tek bir bayiye bağlanır, ikisine birden değil." });
        if (i.DiscountPercent is < 0 or > 100) return BadRequest(new { error = "İskonto 0 ile 100 arasında olmalı." });
        if (i.GroupId is { } g && !await db.DealerGroups.AnyAsync(x => x.Id == g, ct)) return BadRequest(new { error = "Bayi grubu bulunamadı." });
        if (i.DealerId is { } d && !await db.Dealers.AnyAsync(x => x.Id == d, ct)) return BadRequest(new { error = "Bayi bulunamadı." });
        var name = i.Name.Trim();
        if (await db.PriceLists.AnyAsync(l => l.Name == name && l.Id != selfId, ct)) return Conflict(new { error = "Bu adla bir fiyat listesi zaten var." });
        return null;
    }
}
