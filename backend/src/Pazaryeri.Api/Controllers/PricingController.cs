using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Pricing;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

/// <param name="Rounding">"None", "NearestWhole", "UpWhole", "Ending90" veya "Ending99".</param>
/// <param name="Saved">Kural daha önce kaydedildi mi; false ise gösterilen değerler varsayılandır.</param>
public record PricingRuleDto(string Channel, decimal CommissionPercent, decimal ShippingCost, decimal ProfitPercent, string Rounding, bool Saved);

public record PricingRuleInput(decimal? CommissionPercent, decimal? ShippingCost, decimal? ProfitPercent, string? Rounding);

/// <param name="Updated">Fiyatı yeni hesaplanan ya da değişen ürün sayısı.</param>
/// <param name="Unchanged">Fiyatı zaten aynı olan ürün sayısı.</param>
/// <param name="NoCost">Maliyeti girilmediği için hesaplanamayan ürün sayısı.</param>
public record ApplyPricingResult(int Updated, int Unchanged, int NoCost);

[ApiController]
[Route("api/pricing")]
public class PricingController(AppDbContext db, TimeProvider time) : ControllerBase
{
    private static readonly string[] Channels = [.. SupportedChannels.All];

    [HttpGet("rules")]
    public async Task<List<PricingRuleDto>> Rules(CancellationToken ct)
    {
        var saved = await db.PricingRules.AsNoTracking().ToDictionaryAsync(r => r.Channel, ct);
        return Channels.Select(c => saved.TryGetValue(c, out var r) ? ToDto(r, true) : new PricingRuleDto(c, 0, 0, 0, nameof(PriceRounding.None), false)).ToList();
    }

    private static PricingRuleDto ToDto(PricingRule r, bool saved) =>
        new(r.Channel, r.CommissionPercent, r.ShippingCost, r.ProfitPercent, r.Rounding.ToString(), saved);

    [HttpPut("rules/{channel}")]
    public async Task<ActionResult<PricingRuleDto>> Save(string channel, PricingRuleInput input, CancellationToken ct)
    {
        if (!Channels.Contains(channel)) return BadRequest(new { error = "Bilinmeyen kanal." });
        if (input.CommissionPercent is null || input.ShippingCost is null || input.ProfitPercent is null) return BadRequest(new { error = "Komisyon, kargo ve kâr değerlerini girin." });
        if (!Enum.TryParse<PriceRounding>(input.Rounding, out var rounding) || !Enum.IsDefined(rounding)) return BadRequest(new { error = "Geçersiz yuvarlama seçimi." });
        if (PriceCalculator.Validate(input.CommissionPercent.Value, input.ShippingCost.Value, input.ProfitPercent.Value) is { } error) return BadRequest(new { error });

        var rule = await db.PricingRules.FirstOrDefaultAsync(r => r.Channel == channel, ct);
        if (rule is null) db.PricingRules.Add(rule = new PricingRule { Channel = channel });
        rule.CommissionPercent = decimal.Round(input.CommissionPercent.Value, 2);
        rule.ShippingCost = decimal.Round(input.ShippingCost.Value, 2);
        rule.ProfitPercent = decimal.Round(input.ProfitPercent.Value, 2);
        rule.Rounding = rounding;
        rule.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return ToDto(rule, true);
    }

    /// <summary>Kayıtlı kuralı, maliyeti olan tüm ürünlere uygular; yalnızca değişen kanal fiyatları yazılır.</summary>
    [HttpPost("apply/{channel}")]
    public async Task<ActionResult<ApplyPricingResult>> Apply(string channel, CancellationToken ct)
    {
        if (!Channels.Contains(channel)) return BadRequest(new { error = "Bilinmeyen kanal." });
        var rule = await db.PricingRules.AsNoTracking().FirstOrDefaultAsync(r => r.Channel == channel, ct);
        if (rule is null) return BadRequest(new { error = $"{channel} için önce fiyat kuralını kaydedin." });

        var products = await db.Products.AsNoTracking().Select(p => new { p.Id, p.Cost }).ToListAsync(ct);
        var existing = await db.ChannelPrices.Where(c => c.Channel == channel).ToDictionaryAsync(c => c.ProductId, ct);
        var now = time.GetUtcNow().UtcDateTime;
        int updated = 0, unchanged = 0, noCost = 0;

        foreach (var p in products)
        {
            if (p.Cost is not { } cost) { noCost++; continue; }
            var price = PriceCalculator.Calculate(cost, rule).Price;
            if (existing.TryGetValue(p.Id, out var current))
            {
                if (current.Price == price) { unchanged++; continue; }
                current.Price = price;
                current.CalculatedAt = now;
            }
            else
            {
                db.ChannelPrices.Add(new ChannelPrice { ProductId = p.Id, Channel = channel, Price = price, CalculatedAt = now });
            }
            updated++;
        }
        await db.SaveChangesAsync(ct);
        return new ApplyPricingResult(updated, unchanged, noCost);
    }
}
