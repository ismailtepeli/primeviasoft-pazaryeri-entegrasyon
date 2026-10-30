using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

public record B2BSettingsDto(string StockDisplay, int LowStockThreshold);

public record ProductVisibilityInput(List<Guid>? ProductIds, bool Visible);

public record CategoryVisibilityDto(Guid Id, string Path, bool Hidden, int ProductCount);

public record CategoryHiddenInput(bool Hidden);

public record RestrictionInput(Guid? GroupId, Guid? ProductId, Guid? CategoryId);

public record RestrictionDto(Guid Id, Guid GroupId, string GroupName, Guid? ProductId, string? ProductName, Guid? CategoryId, string? CategoryPath);

/// <summary>B2B bayi sitesinde neyin görüneceği: ürün bayrağı, kategori gizleme, bayi grubu kısıtları ve stok gösterimi.</summary>
[ApiController]
[Route("api/b2b")]
[RequiresModule(ModuleIds.B2BStore)]
public class B2BController(AppDbContext db) : ControllerBase
{
    public const int MaxBulk = 1000;

    [HttpGet("settings")]
    public async Task<B2BSettingsDto> GetSettings(CancellationToken ct)
    {
        var s = await db.B2BSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new B2BSettings();
        return new B2BSettingsDto(s.StockDisplay.ToString(), s.LowStockThreshold);
    }

    [HttpPut("settings")]
    public async Task<ActionResult<B2BSettingsDto>> SaveSettings(B2BSettingsDto input, CancellationToken ct)
    {
        if (!Enum.TryParse<StockDisplayMode>(input.StockDisplay, true, out var mode)) return BadRequest(new { error = "Stok gösterimi Exact, Level ya da Hidden olmalı." });
        if (input.LowStockThreshold is < 0 or > 100000) return BadRequest(new { error = "Az kaldı eşiği 0 ile 100000 arasında olmalı." });
        var s = await db.B2BSettings.FirstOrDefaultAsync(ct);
        if (s is null) db.B2BSettings.Add(s = new B2BSettings());
        s.StockDisplay = mode;
        s.LowStockThreshold = input.LowStockThreshold;
        await db.SaveChangesAsync(ct);
        return new B2BSettingsDto(s.StockDisplay.ToString(), s.LowStockThreshold);
    }

    /// <summary>Seçili ürünlerde "B2B sitesinde göster" bayrağını toplu açar/kapatır; bulunamayan kimlikler sayılmaz.</summary>
    [HttpPut("products/visibility")]
    public async Task<ActionResult<object>> SetProductVisibility(ProductVisibilityInput input, CancellationToken ct)
    {
        var ids = input.ProductIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0) return BadRequest(new { error = "En az bir ürün seçin." });
        if (ids.Count > MaxBulk) return BadRequest(new { error = $"Tek seferde en fazla {MaxBulk} ürün değiştirilebilir." });
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        foreach (var p in products) p.B2BVisible = input.Visible;
        await db.SaveChangesAsync(ct);
        return new { updated = products.Count };
    }

    [HttpGet("categories")]
    public async Task<List<CategoryVisibilityDto>> Categories(CancellationToken ct)
    {
        var paths = await CategoryPaths.LoadAsync(db, ct);
        var counts = await db.Products.Where(p => p.CategoryId != null).GroupBy(p => p.CategoryId!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        var all = await db.Categories.AsNoTracking().ToListAsync(ct);
        return all.OrderBy(c => paths[c.Id], StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new CategoryVisibilityDto(c.Id, paths[c.Id], c.B2BHidden, counts.GetValueOrDefault(c.Id))).ToList();
    }

    [HttpPut("categories/{id:guid}/hidden")]
    public async Task<IActionResult> SetCategoryHidden(Guid id, CategoryHiddenInput input, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return NotFound(new { error = "Kategori bulunamadı." });
        category.B2BHidden = input.Hidden;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("restrictions")]
    public async Task<List<RestrictionDto>> Restrictions(CancellationToken ct)
    {
        var paths = await CategoryPaths.LoadAsync(db, ct);
        var rows = await db.GroupVisibilityRules.AsNoTracking().Select(r => new
        {
            r.Id, r.GroupId, GroupName = db.DealerGroups.Where(g => g.Id == r.GroupId).Select(g => g.Name).FirstOrDefault(),
            r.ProductId, ProductName = db.Products.Where(p => p.Id == r.ProductId).Select(p => p.Name).FirstOrDefault(), r.CategoryId,
        }).ToListAsync(ct);
        return rows.OrderBy(r => r.GroupName).ThenBy(r => r.ProductName)
            .Select(r => new RestrictionDto(r.Id, r.GroupId, r.GroupName ?? "", r.ProductId, r.ProductName, r.CategoryId, r.CategoryId is { } c && paths.TryGetValue(c, out var path) ? path : null)).ToList();
    }

    /// <summary>Bir gruba bir ürünü ya da bir kategoriyi (alt kategorileriyle) gizler; aynı kısıt tekrar eklenirse mevcut döner.</summary>
    [HttpPost("restrictions")]
    public async Task<ActionResult<RestrictionDto>> AddRestriction(RestrictionInput input, CancellationToken ct)
    {
        if (input.GroupId is not { } groupId) return BadRequest(new { error = "Bayi grubu seçin." });
        if ((input.ProductId is null) == (input.CategoryId is null)) return BadRequest(new { error = "Ürün ya da kategoriden yalnızca birini seçin." });
        if (!await db.DealerGroups.AnyAsync(g => g.Id == groupId, ct)) return BadRequest(new { error = "Bayi grubu bulunamadı." });
        if (input.ProductId is { } pid && !await db.Products.AnyAsync(p => p.Id == pid, ct)) return BadRequest(new { error = "Ürün bulunamadı." });
        if (input.CategoryId is { } cid && !await db.Categories.AnyAsync(c => c.Id == cid, ct)) return BadRequest(new { error = "Kategori bulunamadı." });

        var rule = await db.GroupVisibilityRules.FirstOrDefaultAsync(r => r.GroupId == groupId && r.ProductId == input.ProductId && r.CategoryId == input.CategoryId, ct);
        if (rule is null)
        {
            db.GroupVisibilityRules.Add(rule = new GroupVisibilityRule { GroupId = groupId, ProductId = input.ProductId, CategoryId = input.CategoryId });
            await db.SaveChangesAsync(ct);
        }
        return (await Restrictions(ct)).First(r => r.Id == rule.Id);
    }

    [HttpDelete("restrictions/{id:guid}")]
    public async Task<IActionResult> RemoveRestriction(Guid id, CancellationToken ct)
    {
        var rule = await db.GroupVisibilityRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound(new { error = "Kısıt bulunamadı." });
        db.GroupVisibilityRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
