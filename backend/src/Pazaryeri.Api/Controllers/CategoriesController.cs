using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

/// <param name="Path">"Giyim > Tişört" gibi üstten alta tam yol.</param>
public record CategoryDto(Guid Id, string Name, Guid? ParentId, string Path, int ProductCount);

public record CategoryInput(string? Name, Guid? ParentId);

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
public class CategoriesController(AppDbContext db) : ControllerBase
{
    public const int MaxDepth = 5;

    /// <summary>Tüm kategoriler, yola göre sıralı (üst kategori altlarından önce gelir).</summary>
    [HttpGet]
    public async Task<List<CategoryDto>> List(CancellationToken ct)
    {
        var paths = await CategoryPaths.LoadAsync(db, ct);
        var counts = await db.Products.Where(p => p.CategoryId != null).GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        var all = await db.Categories.AsNoTracking().ToListAsync(ct);
        return all.OrderBy(c => paths[c.Id], StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new CategoryDto(c.Id, c.Name, c.ParentId, paths[c.Id], counts.GetValueOrDefault(c.Id))).ToList();
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

        var category = new Category { Name = name, ParentId = input.ParentId };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        var path = (await CategoryPaths.LoadAsync(db, ct))[category.Id];
        return new CategoryDto(category.Id, category.Name, category.ParentId, path, 0);
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
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return NotFound(new { error = "Kategori bulunamadı." });
        if (await db.Categories.AnyAsync(c => c.ParentId == id, ct)) return BadRequest(new { error = "Alt kategorisi olan kategori silinemez." });
        if (await db.Products.AnyAsync(p => p.CategoryId == id, ct)) return BadRequest(new { error = "Ürünleri olan kategori silinemez; önce ürünleri başka kategoriye taşıyın." });

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
