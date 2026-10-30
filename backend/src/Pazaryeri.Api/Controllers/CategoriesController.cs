using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Catalog;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

/// <param name="Path">"Giyim > Tişört" gibi üstten alta tam yol.</param>
public record CategoryDto(Guid Id, string Name, Guid? ParentId, string Path, int ProductCount, int TotalProductCount = 0, int SortOrder = 0,
    bool IsActive = true, string? ImageUrl = null, string SlugPath = "", int ChildCount = 0);

public record CategoryInput(string? Name, Guid? ParentId);

public record CategorySettingsInput(bool IsActive, string? ImageUrl);

public record CategoryMoveInput(Guid? ParentId);

public record CategoryOrderInput(Guid? ParentId, List<Guid>? Ids);

public static class CategoryPaths
{
    public const string Separator = " > ";

    /// <summary>Kiracının tüm kategorileri için id → tam yol.</summary>
    public static async Task<Dictionary<Guid, string>> LoadAsync(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.Name, c.ParentId }).ToListAsync(ct);
        return Build(rows.Select(c => (c.Id, c.Name, c.ParentId)).ToList());
    }

    public static Dictionary<Guid, string> Build(IReadOnlyList<(Guid Id, string Name, Guid? ParentId)> all)
    {
        var byId = all.ToDictionary(c => c.Id);
        string PathOf(Guid id)
        {
            var parts = new List<string>();
            for (Guid? cur = id; cur is { } c && byId.TryGetValue(c, out var node) && parts.Count < 20; cur = node.ParentId) parts.Insert(0, node.Name);
            return string.Join(Separator, parts);
        }
        return all.ToDictionary(c => c.Id, c => PathOf(c.Id));
    }
}

[ApiController]
[Route("api/categories")]
[CoreModule]
public class CategoriesController(AppDbContext db) : ControllerBase
{
    public const int MaxDepth = CategoryRules.MaxDepth;

    /// <summary>Tüm kategoriler ağaç sırasıyla (kök, alt; kardeşler SortOrder sonra ada göre); sayılar doğrudan ve alt kategorilerle toplam.</summary>
    [HttpGet]
    public async Task<List<CategoryDto>> List(CancellationToken ct)
    {
        var all = await db.Categories.AsNoTracking().ToListAsync(ct);
        return await ToDtosAsync(all, ct);
    }

    private async Task<List<CategoryDto>> ToDtosAsync(List<Category> all, CancellationToken ct)
    {
        var tuples = all.Select(c => (c.Id, c.Name, c.ParentId)).ToList();
        var paths = CategoryPaths.Build(tuples);
        var slugs = CategoryRules.SlugPaths(tuples);
        var direct = await db.Products.Where(p => p.CategoryId != null).GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        var children = all.ToLookup(c => c.ParentId);
        var ordered = new List<Category>();
        void Walk(Guid? parent, int guard)
        {
            if (guard > 20) return;
            foreach (var c in children[parent].OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)) { ordered.Add(c); Walk(c.Id, guard + 1); }
        }
        Walk(null, 0);
        int Total(Guid id, int guard) => direct.GetValueOrDefault(id) + (guard > 20 ? 0 : children[id].Sum(c => Total(c.Id, guard + 1)));
        return ordered.Select(c => new CategoryDto(c.Id, c.Name, c.ParentId, paths[c.Id], direct.GetValueOrDefault(c.Id), Total(c.Id, 0), c.SortOrder,
            c.IsActive, c.ImageUrl, slugs[c.Id], children[c.Id].Count())).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(CategoryInput input, CancellationToken ct)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length == 0) return BadRequest(new { error = "Kategori adı boş olamaz." });
        if (name.Length > 200) return BadRequest(new { error = "Kategori adı 200 karakteri aşamaz." });
        if (name.Contains('>')) return BadRequest(new { error = "Kategori adı '>' karakterini içeremez." });

        if (input.ParentId is { } pid)
        {
            if (!await db.Categories.AnyAsync(c => c.Id == pid, ct)) return BadRequest(new { error = "Üst kategori bulunamadı." });
            var paths = await CategoryPaths.LoadAsync(db, ct);
            if (paths[pid].Split(CategoryPaths.Separator).Length >= MaxDepth) return BadRequest(new { error = $"Kategori en fazla {MaxDepth} seviye olabilir." });
        }
        if (await db.Categories.AnyAsync(c => c.ParentId == input.ParentId && c.Name == name, ct))
            return BadRequest(new { error = "Bu kategori zaten var." });

        var siblings = await db.Categories.Where(c => c.ParentId == input.ParentId).Select(c => (int?)c.SortOrder).MaxAsync(ct);
        var category = new Category { Name = name, ParentId = input.ParentId, SortOrder = (siblings ?? -1) + 1 };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        var path = (await CategoryPaths.LoadAsync(db, ct))[category.Id];
        return new CategoryDto(category.Id, category.Name, category.ParentId, path, 0, 0, category.SortOrder, true, null, CategoryRules.Slugify(category.Name), 0);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> Rename(Guid id, CategoryInput input, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return NotFound(new { error = "Kategori bulunamadı." });
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length == 0) return BadRequest(new { error = "Kategori adı boş olamaz." });
        if (name.Length > 200) return BadRequest(new { error = "Kategori adı 200 karakteri aşamaz." });
        if (name.Contains('>')) return BadRequest(new { error = "Kategori adı '>' karakterini içeremez." });
        if (await db.Categories.AnyAsync(c => c.ParentId == category.ParentId && c.Name == name && c.Id != id, ct))
            return BadRequest(new { error = "Bu kategori zaten var." });

        category.Name = name; // yalnızca ad değişir; üst kategori sabit (taşıma desteklenmez)
        await db.SaveChangesAsync(ct);
        var path = (await CategoryPaths.LoadAsync(db, ct))[id];
        return new CategoryDto(id, name, category.ParentId, path, await db.Products.CountAsync(p => p.CategoryId == id, ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid? moveProductsTo = null, CancellationToken ct = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return NotFound(new { error = "Kategori bulunamadı." });
        if (await db.Categories.AnyAsync(c => c.ParentId == id, ct)) return BadRequest(new { error = "Alt kategorisi olan kategori silinemez." });
        var productCount = await db.Products.CountAsync(p => p.CategoryId == id, ct);
        if (productCount > 0)
        {
            if (moveProductsTo is not { } target) return BadRequest(new { error = $"Kategoride {productCount} ürün var; silmek için ürünleri başka bir kategoriye taşıyın ya da taşıma hedefi seçin." });
            if (target == id || !await db.Categories.AnyAsync(c => c.Id == target, ct)) return BadRequest(new { error = "Taşıma hedefi geçerli bir kategori olmalı." });
            await db.Products.Where(p => p.CategoryId == id).ExecuteUpdateAsync(u => u.SetProperty(p => p.CategoryId, target), ct);
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Kategorinin aktif/pasif durumu ve görseli; görsel boş gönderilirse temizlenir.</summary>
    [HttpPut("{id:guid}/settings")]
    public async Task<ActionResult<CategoryDto>> Settings(Guid id, CategorySettingsInput input, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return NotFound(new { error = "Kategori bulunamadı." });
        if (CategoryRules.ValidateImageUrl(input.ImageUrl) is { } error) return BadRequest(new { error });
        category.IsActive = input.IsActive;
        category.ImageUrl = string.IsNullOrWhiteSpace(input.ImageUrl) ? null : input.ImageUrl.Trim();
        await db.SaveChangesAsync(ct);
        return (await List(ct)).First(c => c.Id == id);
    }

    /// <summary>Kategoriyi başka bir üst kategorinin (ya da köke) altına taşır; sona eklenir. Döngü ve en çok 5 seviye korunur.</summary>
    [HttpPut("{id:guid}/move")]
    public async Task<ActionResult<CategoryDto>> Move(Guid id, CategoryMoveInput input, CancellationToken ct)
    {
        var all = await db.Categories.ToListAsync(ct);
        var category = all.FirstOrDefault(c => c.Id == id);
        if (category is null) return NotFound(new { error = "Kategori bulunamadı." });
        if (input.ParentId is { } pid && all.All(c => c.Id != pid)) return BadRequest(new { error = "Üst kategori bulunamadı." });
        if (CategoryRules.ValidateMove(all.Select(c => (c.Id, c.ParentId)).ToList(), id, input.ParentId) is { } error) return BadRequest(new { error });
        if (all.Any(c => c.ParentId == input.ParentId && c.Name == category.Name && c.Id != id)) return BadRequest(new { error = "Hedefte aynı adlı bir kategori var." });

        category.ParentId = input.ParentId;
        category.SortOrder = all.Where(c => c.ParentId == input.ParentId && c.Id != id).Select(c => c.SortOrder).DefaultIfEmpty(-1).Max() + 1;
        await db.SaveChangesAsync(ct);
        return (await List(ct)).First(c => c.Id == id);
    }

    /// <summary>Bir üst kategorinin (ya da kökün) çocuklarını verilen sıraya dizer; liste tam olarak o çocukları içermelidir.</summary>
    [HttpPut("order")]
    public async Task<ActionResult<List<CategoryDto>>> Reorder(CategoryOrderInput input, CancellationToken ct)
    {
        var ids = input.Ids ?? [];
        var siblings = await db.Categories.Where(c => c.ParentId == input.ParentId).ToListAsync(ct);
        if (ids.Count != ids.Distinct().Count() || ids.Count != siblings.Count || !siblings.Select(c => c.Id).ToHashSet().SetEquals(ids))
            return BadRequest(new { error = "Sıralama, üst kategorinin tüm alt kategorilerini içermeli." });
        for (var i = 0; i < ids.Count; i++) siblings.First(c => c.Id == ids[i]).SortOrder = i;
        await db.SaveChangesAsync(ct);
        return await List(ct);
    }
}
