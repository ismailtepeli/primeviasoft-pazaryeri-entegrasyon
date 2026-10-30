using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Stock;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

/// <param name="Saved">Kural daha önce kaydedildi mi; false ise gösterilen değerler varsayılandır.</param>
public record StockRuleDto(string Channel, int SafetyStock, decimal AllocationPercent, bool Saved);

public record StockRuleInput(int? SafetyStock, decimal? AllocationPercent);

public record StockSettingsDto(bool DeductAcrossChannels);

/// <param name="SafetyStock">Ürüne özel güvenlik stoğu; null ise kanalın varsayılanı geçerli.</param>
/// <param name="EffectiveSafety">Gerçekte uygulanan güvenlik stoğu.</param>
/// <param name="ChannelQuantity">Seçili kanala açılan miktar.</param>
public record StockRowDto(Guid ProductId, string Name, string Barcode, int Stock, int? SafetyStock, int EffectiveSafety, int ChannelQuantity);

public record StockPage(string Channel, IReadOnlyList<StockRowDto> Items, int Total);

public record SafetyInput(int? SafetyStock);

/// <param name="Type">"Sale", "Restore" veya "Adjustment".</param>
/// <param name="Quantity">Stoğa yansıyan işaretli adet (satışta negatif).</param>
/// <param name="Oversold">Stok yetmediği için düşülemeyen adet.</param>
public record StockMovementDto(Guid Id, DateTime At, Guid ProductId, string Name, string Barcode, string Channel, string Type, int Quantity, int Oversold, int StockAfter, string Reference);

public record StockMovementPage(IReadOnlyList<StockMovementDto> Items, int Total);

[ApiController]
[Route("api/stock")]
public class StockController(AppDbContext db, TimeProvider time) : ControllerBase
{
    public const int MaxPageSize = 100;
    private static readonly string[] Channels = [.. SupportedChannels.All];

    // --- ayarlar ---

    [CoreModule]
    [HttpGet("settings")]
    public async Task<StockSettingsDto> GetSettings(CancellationToken ct) =>
        new((await db.StockSettings.AsNoTracking().FirstOrDefaultAsync(ct))?.DeductAcrossChannels ?? true);

    [CoreModule]
    [HttpPut("settings")]
    public async Task<StockSettingsDto> SaveSettings(StockSettingsDto input, CancellationToken ct)
    {
        var settings = await db.StockSettings.FirstOrDefaultAsync(ct);
        if (settings is null) db.StockSettings.Add(settings = new StockSettings());
        settings.DeductAcrossChannels = input.DeductAcrossChannels;
        await db.SaveChangesAsync(ct);
        return new StockSettingsDto(settings.DeductAcrossChannels);
    }

    // --- kanal kuralları ---

    [RequiresModule(ModuleIds.StockPrice)]
    [HttpGet("rules")]
    public async Task<List<StockRuleDto>> Rules(CancellationToken ct)
    {
        var saved = await db.StockRules.AsNoTracking().ToDictionaryAsync(r => r.Channel, ct);
        return Channels.Select(c => saved.TryGetValue(c, out var r) ? new StockRuleDto(c, r.SafetyStock, r.AllocationPercent, true) : new StockRuleDto(c, 0, 100, false)).ToList();
    }

    [RequiresModule(ModuleIds.StockPrice)]
    [HttpPut("rules/{channel}")]
    public async Task<ActionResult<StockRuleDto>> SaveRule(string channel, StockRuleInput input, CancellationToken ct)
    {
        if (!Channels.Contains(channel)) return BadRequest(new { error = "Bilinmeyen kanal." });
        if (input.SafetyStock is null || input.AllocationPercent is null) return BadRequest(new { error = "Güvenlik stoğu ve kanala açılan payı girin." });
        if (StockCalculator.Validate(input.SafetyStock.Value, input.AllocationPercent.Value) is { } error) return BadRequest(new { error });

        var rule = await db.StockRules.FirstOrDefaultAsync(r => r.Channel == channel, ct);
        if (rule is null) db.StockRules.Add(rule = new StockRule { Channel = channel });
        rule.SafetyStock = input.SafetyStock.Value;
        rule.AllocationPercent = decimal.Round(input.AllocationPercent.Value, 2);
        rule.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return new StockRuleDto(channel, rule.SafetyStock, rule.AllocationPercent, true);
    }

    // --- ürün bazında dağıtım ---

    /// <summary>Ürünlerin fiziksel stoğu, güvenlik stoğu ve seçili kanala açılan miktarı (sunucu taraflı sayfalama).</summary>
    [RequiresModule(ModuleIds.StockPrice)]
    [HttpGet("products")]
    public async Task<ActionResult<StockPage>> Products([FromQuery] string? channel, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (channel is null || !Channels.Contains(channel)) return BadRequest(new { error = "Geçerli bir kanal seçin." });
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        var query = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.Barcode.Contains(term));
        }

        var rule = await db.StockRules.AsNoTracking().FirstOrDefaultAsync(r => r.Channel == channel, ct);
        var ruleSafety = rule?.SafetyStock ?? 0;
        var percent = rule?.AllocationPercent ?? 100;

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(p => p.Name).ThenBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new { p.Id, p.Name, p.Barcode, p.Stock, p.SafetyStock }).ToListAsync(ct);

        var items = rows.Select(p =>
        {
            var safety = StockCalculator.EffectiveSafety(p.SafetyStock, ruleSafety);
            return new StockRowDto(p.Id, p.Name, p.Barcode, p.Stock, p.SafetyStock, safety, StockCalculator.ChannelQuantity(p.Stock, safety, percent));
        }).ToList();
        return new StockPage(channel, items, total);
    }

    /// <summary>Stok defteri: satışlar, iptal/iade geri eklemeleri ve elle düzeltmeler (en yeni önce).</summary>
    [CoreModule]
    [HttpGet("movements")]
    public async Task<StockMovementPage> Movements([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);
        var query = db.StockMovements.AsNoTracking().Join(db.Products, m => m.ProductId, p => p.Id, (m, p) => new { m, p.Name, p.Barcode });
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.Barcode.Contains(term) || x.m.Reference.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.m.CreatedAt).ThenByDescending(x => x.m.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(x => new StockMovementDto(x.m.Id, x.m.CreatedAt, x.m.ProductId, x.Name, x.Barcode, x.m.Channel, x.m.Type.ToString(), x.m.Quantity,
            x.m.Type == StockMovementType.Sale ? x.m.Requested + x.m.Quantity : 0, x.m.StockAfter, x.m.Reference)).ToList();
        return new StockMovementPage(items, total);
    }

    /// <summary>Ürüne özel güvenlik stoğunu ayarlar; null göndermek kanalın varsayılanına döndürür.</summary>
    [RequiresModule(ModuleIds.StockPrice)]
    [HttpPut("products/{id:guid}/safety")]
    public async Task<IActionResult> SetSafety(Guid id, SafetyInput input, CancellationToken ct)
    {
        if (input.SafetyStock is { } s && (s < 0 || s > StockCalculator.MaxStock)) return BadRequest(new { error = "Güvenlik stoğu 0 ile 10.000.000 arasında olmalı." });
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound(new { error = "Ürün bulunamadı." });
        product.SafetyStock = input.SafetyStock;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
