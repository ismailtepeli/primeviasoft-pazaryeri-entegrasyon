using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Catalog;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

/// <param name="Path">"Giyim > Tişört" gibi üstten alta tam yol.</param>
/// <param name="HasChildren">Alt kategorisi var; seçilemez, içine girilebilir.</param>
/// <param name="IsActive">Kanalda hâlâ var mı (kaldırılmışsa false).</param>
public record ChannelCategoryDto(long ExternalId, string Name, string Path, bool IsLeaf, bool HasChildren, bool IsActive);

/// <param name="Mapped">Eşlenen kanal kategorisi; eşleme yoksa null.</param>
public record CategoryMappingRow(Guid CategoryId, string Path, int ProductCount, ChannelCategoryDto? Mapped);

public record CategoryMappingList(string Channel, int Total, int MappedCount, IReadOnlyList<CategoryMappingRow> Rows);

public record SaveCategoryMappingInput(string? Channel, Guid? CategoryId, long? ExternalCategoryId);

[ApiController]
[Route("api")]
public class CategoryMappingController(AppDbContext db) : ControllerBase
{
    public const int SearchLimit = 50;

    private static bool Supported(string? channel) => channel is not null && CatalogWeeklySync.Channels.Contains(channel);

    private async Task<(Dictionary<long, ChannelCategory> ById, Dictionary<long, string> Paths)> LoadChannelAsync(string channel, CancellationToken ct)
    {
        var all = await db.ChannelCategories.AsNoTracking().Where(c => c.Channel == channel).ToDictionaryAsync(c => c.ExternalId, ct);
        var paths = new Dictionary<long, string>(all.Count);
        string PathOf(long id)
        {
            if (paths.TryGetValue(id, out var cached)) return cached;
            var parts = new List<string>();
            for (long? cur = id; cur is { } c && all.TryGetValue(c, out var node) && parts.Count < 20; cur = node.ParentExternalId) parts.Insert(0, node.Name);
            return paths[id] = string.Join(CategoryPaths.Separator, parts);
        }
        foreach (var id in all.Keys) PathOf(id);
        return (all, paths);
    }

    private static ChannelCategoryDto ToDto(ChannelCategory c, Dictionary<long, string> paths) =>
        new(c.ExternalId, c.Name, paths.GetValueOrDefault(c.ExternalId, c.Name), c.IsLeaf, !c.IsLeaf, c.IsActive);

    /// <summary>
    /// Seçici için kanal kategorileri: <paramref name="q"/> (en az 2 karakter) ad araması yapar; yoksa
    /// <paramref name="parentId"/> altındaki çocukları (boşsa kökleri) listeler.
    /// </summary>
    [HttpGet("catalog/{channel}/categories")]
    public async Task<ActionResult<List<ChannelCategoryDto>>> Browse(string channel, [FromQuery] string? q, [FromQuery] long? parentId, CancellationToken ct)
    {
        if (!Supported(channel)) return BadRequest(new { error = $"{channel} için katalog desteklenmiyor." });
        var (all, paths) = await LoadChannelAsync(channel, ct);

        IEnumerable<ChannelCategory> rows;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            if (term.Length < 2) return BadRequest(new { error = "Aramak için en az 2 karakter girin." });
            rows = all.Values.Where(c => c.IsActive && c.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(c => c.IsLeaf).ThenBy(c => paths[c.ExternalId], StringComparer.CurrentCultureIgnoreCase).Take(SearchLimit);
        }
        else
        {
            rows = all.Values.Where(c => c.IsActive && c.ParentExternalId == parentId)
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase);
        }
        return rows.Select(c => ToDto(c, paths)).ToList();
    }

    /// <summary>Kiracının kategorileri ve her birinin kanaldaki eşlemesi.</summary>
    [HttpGet("mappings/categories")]
    public async Task<ActionResult<CategoryMappingList>> List([FromQuery] string? channel, CancellationToken ct)
    {
        if (!Supported(channel)) return BadRequest(new { error = "Geçerli bir kanal seçin." });
        var own = await CategoryPaths.LoadAsync(db, ct);
        var counts = await db.Products.Where(p => p.CategoryId != null).GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        var maps = await db.CategoryMappings.AsNoTracking().Where(m => m.Channel == channel).ToDictionaryAsync(m => m.CategoryId, m => m.ExternalCategoryId, ct);
        var (all, paths) = await LoadChannelAsync(channel!, ct);

        var rows = own.OrderBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase)
            .Select(kv => new CategoryMappingRow(kv.Key, kv.Value, counts.GetValueOrDefault(kv.Key),
                maps.TryGetValue(kv.Key, out var ext) && all.TryGetValue(ext, out var c) ? ToDto(c, paths) : null))
            .ToList();
        return new CategoryMappingList(channel!, rows.Count, rows.Count(r => r.Mapped is { IsActive: true }), rows);
    }

    [HttpPut("mappings/categories")]
    public async Task<ActionResult<CategoryMappingRow>> Save(SaveCategoryMappingInput input, CancellationToken ct)
    {
        if (!Supported(input.Channel)) return BadRequest(new { error = "Geçerli bir kanal seçin." });
        if (input.CategoryId is not { } categoryId || input.ExternalCategoryId is not { } externalId) return BadRequest(new { error = "Kategori ve kanal kategorisi gerekli." });

        var path = (await CategoryPaths.LoadAsync(db, ct)).GetValueOrDefault(categoryId);
        if (path is null) return NotFound(new { error = "Kategori bulunamadı." });

        var channelCategory = await db.ChannelCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Channel == input.Channel && c.ExternalId == externalId, ct);
        if (channelCategory is null || !channelCategory.IsActive) return BadRequest(new { error = "Kanal kategorisi bulunamadı; katalogu senkronlayın." });
        if (!channelCategory.IsLeaf) return BadRequest(new { error = "Yalnızca en alt seviye kategori seçilebilir." });

        var mapping = await db.CategoryMappings.FirstOrDefaultAsync(m => m.Channel == input.Channel && m.CategoryId == categoryId, ct);
        if (mapping is null) db.CategoryMappings.Add(new CategoryMapping { Channel = input.Channel!, CategoryId = categoryId, ExternalCategoryId = externalId });
        else mapping.ExternalCategoryId = externalId;
        await db.SaveChangesAsync(ct);

        var (_, paths) = await LoadChannelAsync(input.Channel!, ct);
        var count = await db.Products.CountAsync(p => p.CategoryId == categoryId, ct);
        return new CategoryMappingRow(categoryId, path, count, ToDto(channelCategory, paths));
    }

    [HttpDelete("mappings/categories")]
    public async Task<IActionResult> Remove([FromQuery] string? channel, [FromQuery] Guid categoryId, CancellationToken ct)
    {
        if (!Supported(channel)) return BadRequest(new { error = "Geçerli bir kanal seçin." });
        var mapping = await db.CategoryMappings.FirstOrDefaultAsync(m => m.Channel == channel && m.CategoryId == categoryId, ct);
        if (mapping is null) return NotFound(new { error = "Eşleme bulunamadı." });
        db.CategoryMappings.Remove(mapping);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
