using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Catalog;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Products;

namespace Pazaryeri.Api.Controllers;

public record ChannelValueDto(long Id, string Name);

public record ValueMappingDto(string SourceValue, long? ExternalValueId, string? ExternalValueName, string? CustomValue);

public record AttributeMappingDto(string? SourceAttribute, long? FixedValueId, string? FixedValueName, string? FixedCustomValue, IReadOnlyList<ValueMappingDto> Values);

/// <param name="Status">"Mapped" (eşlendi), "Missing" (zorunlu ama eksik) veya "Optional" (isteğe bağlı, eşlenmemiş).</param>
public record AttributeRowDto(long AttributeId, string Name, bool Required, bool AllowCustom, bool Varianter, int ValueCount, string Status, AttributeMappingDto? Mapping);

/// <summary>Kategorideki ürünlerin Varyant alanından çıkan kendi özellik adı ve gördüğümüz değerleri.</summary>
public record SourceAttributeDto(string Name, IReadOnlyList<string> Values);

/// <param name="ChannelCategory">Kategorinin eşlendiği kanal kategorisi; kategori eşlemesi yoksa null.</param>
/// <param name="NeedsSync">Kanal kategorisinin özellikleri henüz katalogda yok; katalog senkronu gerekir.</param>
public record AttributeMappingList(ChannelCategoryDto? ChannelCategory, bool NeedsSync, int RequiredTotal, int RequiredMapped,
    IReadOnlyList<AttributeRowDto> Rows, IReadOnlyList<SourceAttributeDto> Sources);

public record ValueMappingInput(string? SourceValue, long? ExternalValueId, string? CustomValue);

public record SaveAttributeMappingInput(string? Channel, Guid? CategoryId, long? ExternalAttributeId, string? SourceAttribute,
    long? FixedValueId, string? FixedCustomValue, List<ValueMappingInput>? Values);

public static class AttributeMappingStatus
{
    public const string Mapped = "Mapped";
    public const string Missing = "Missing";
    public const string Optional = "Optional";

    /// <summary>Sabit değer ya da ürün özelliği + tüm gözlenen değerlerin eşlenmesi "eşlendi" sayılır.</summary>
    public static string Of(ChannelAttribute attribute, AttributeMapping? mapping, IReadOnlyList<string>? observedSourceValues)
    {
        if (mapping is null) return attribute.Required ? Missing : Optional;
        if (mapping.FixedValueId is not null || !string.IsNullOrWhiteSpace(mapping.FixedCustomValue)) return Mapped;
        if (string.IsNullOrWhiteSpace(mapping.SourceAttribute)) return attribute.Required ? Missing : Optional;

        // Eşlenmemiş değer: serbest metinli özellikte ürünün kendi değeri gider (sorun değil); listeli özellikte eksik sayılır.
        if (attribute.AllowCustom) return Mapped;
        var mappedValues = mapping.Values.Where(v => v.ExternalValueId is not null || !string.IsNullOrWhiteSpace(v.CustomValue))
            .Select(v => v.SourceValue).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        return (observedSourceValues ?? []).All(mappedValues.Contains) ? Mapped : Missing;
    }
}

[ApiController]
[Route("api")]
public class AttributeMappingController(AppDbContext db) : ControllerBase
{
    public const int ValueLimit = 100;

    private static bool Supported(string? channel) => channel is not null && CatalogWeeklySync.Channels.Contains(channel);

    /// <summary>Kanal kategorisindeki özellik değerlerinde ad araması (en fazla 100 sonuç).</summary>
    [HttpGet("catalog/{channel}/categories/{categoryId:long}/attributes/{attributeId:long}/values")]
    public async Task<ActionResult<List<ChannelValueDto>>> Values(string channel, long categoryId, long attributeId, [FromQuery] string? q, CancellationToken ct)
    {
        if (!Supported(channel)) return BadRequest(new { error = $"{channel} için katalog desteklenmiyor." });
        var query = db.ChannelAttributeValues.AsNoTracking()
            .Where(v => v.Channel == channel && v.CategoryExternalId == categoryId && v.AttributeExternalId == attributeId && v.IsActive);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(v => v.Name.Contains(q.Trim()));
        return await query.OrderBy(v => v.Name).Take(ValueLimit).Select(v => new ChannelValueDto(v.ExternalId, v.Name)).ToListAsync(ct);
    }

    [HttpGet("mappings/attributes")]
    public async Task<ActionResult<AttributeMappingList>> List([FromQuery] string? channel, [FromQuery] Guid categoryId, CancellationToken ct)
    {
        if (!Supported(channel)) return BadRequest(new { error = "Geçerli bir kanal seçin." });
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, ct)) return NotFound(new { error = "Kategori bulunamadı." });

        var categoryMapping = await db.CategoryMappings.AsNoTracking().FirstOrDefaultAsync(m => m.Channel == channel && m.CategoryId == categoryId, ct);
        var channelCategory = categoryMapping is null ? null
            : await db.ChannelCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Channel == channel && c.ExternalId == categoryMapping.ExternalCategoryId, ct);
        if (categoryMapping is null || channelCategory is null)
            return new AttributeMappingList(null, false, 0, 0, [], []);

        var categoryDto = new ChannelCategoryDto(channelCategory.ExternalId, channelCategory.Name, channelCategory.Name, channelCategory.IsLeaf, !channelCategory.IsLeaf, channelCategory.IsActive);
        var attributes = await db.ChannelAttributes.AsNoTracking()
            .Where(a => a.Channel == channel && a.CategoryExternalId == channelCategory.ExternalId && a.IsActive)
            .OrderByDescending(a => a.Required).ThenBy(a => a.Name).ToListAsync(ct);
        if (attributes.Count == 0) return new AttributeMappingList(categoryDto, true, 0, 0, [], await SourcesAsync(categoryId, ct));

        var mappings = await db.AttributeMappings.AsNoTracking().Include(m => m.Values).Where(m => m.Channel == channel && m.CategoryId == categoryId).ToListAsync(ct);
        var valueNames = await db.ChannelAttributeValues.AsNoTracking()
            .Where(v => v.Channel == channel && v.CategoryExternalId == channelCategory.ExternalId).ToDictionaryAsync(v => (v.AttributeExternalId, v.ExternalId), v => v.Name, ct);
        var valueCounts = await db.ChannelAttributeValues.Where(v => v.Channel == channel && v.CategoryExternalId == channelCategory.ExternalId && v.IsActive)
            .GroupBy(v => v.AttributeExternalId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var sources = await SourcesAsync(categoryId, ct);
        var sourceValues = sources.ToDictionary(s => s.Name, s => s.Values, StringComparer.CurrentCultureIgnoreCase);

        string? NameOf(long attributeId, long? valueId) => valueId is { } v && valueNames.TryGetValue((attributeId, v), out var n) ? n : null;
        var rows = attributes.Select(a =>
        {
            var m = mappings.FirstOrDefault(x => x.ExternalAttributeId == a.ExternalId);
            IReadOnlyList<string>? observed = m?.SourceAttribute is { } s && sourceValues.TryGetValue(s, out var vals) ? vals : null;
            var dto = m is null ? null : new AttributeMappingDto(m.SourceAttribute, m.FixedValueId, NameOf(a.ExternalId, m.FixedValueId), m.FixedCustomValue,
                m.Values.OrderBy(v => v.SourceValue).Select(v => new ValueMappingDto(v.SourceValue, v.ExternalValueId, NameOf(a.ExternalId, v.ExternalValueId), v.CustomValue)).ToList());
            return new AttributeRowDto(a.ExternalId, a.Name, a.Required, a.AllowCustom, a.Varianter, valueCounts.GetValueOrDefault(a.ExternalId),
                AttributeMappingStatus.Of(a, m, observed), dto);
        }).ToList();

        var required = rows.Where(r => r.Required).ToList();
        return new AttributeMappingList(categoryDto, false, required.Count, required.Count(r => r.Status == AttributeMappingStatus.Mapped), rows, sources);
    }

    private async Task<List<SourceAttributeDto>> SourcesAsync(Guid categoryId, CancellationToken ct)
    {
        var variants = await db.Products.AsNoTracking().Where(p => p.CategoryId == categoryId && p.Variant != null).Select(p => p.Variant).ToListAsync(ct);
        var found = new Dictionary<string, SortedSet<string>>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var variant in variants)
            foreach (var (key, value) in ProductAttributeBuilder.ParseVariant(variant))
            {
                if (!found.TryGetValue(key, out var set)) found[key] = set = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
                if (set.Count < 200) set.Add(value);
            }
        return found.OrderBy(kv => kv.Key, StringComparer.CurrentCultureIgnoreCase).Select(kv => new SourceAttributeDto(kv.Key, kv.Value.ToList())).ToList();
    }

    [HttpPut("mappings/attributes")]
    public async Task<IActionResult> Save(SaveAttributeMappingInput input, CancellationToken ct)
    {
        if (!Supported(input.Channel)) return BadRequest(new { error = "Geçerli bir kanal seçin." });
        if (input.CategoryId is not { } categoryId || input.ExternalAttributeId is not { } attributeId) return BadRequest(new { error = "Kategori ve kanal özelliği gerekli." });
        var channel = input.Channel!;

        var categoryMapping = await db.CategoryMappings.AsNoTracking().FirstOrDefaultAsync(m => m.Channel == channel && m.CategoryId == categoryId, ct);
        if (categoryMapping is null) return BadRequest(new { error = "Önce kategoriyi bir kanal kategorisiyle eşleyin." });
        var attribute = await db.ChannelAttributes.AsNoTracking().FirstOrDefaultAsync(a =>
            a.Channel == channel && a.CategoryExternalId == categoryMapping.ExternalCategoryId && a.ExternalId == attributeId && a.IsActive, ct);
        if (attribute is null) return BadRequest(new { error = "Bu özellik kanal kategorisinde bulunamadı; katalogu senkronlayın." });

        var source = input.SourceAttribute?.Trim();
        var fixedCustom = input.FixedCustomValue?.Trim();
        var hasFixed = input.FixedValueId is not null || !string.IsNullOrEmpty(fixedCustom);
        if (hasFixed && !string.IsNullOrEmpty(source)) return BadRequest(new { error = "Sabit değer ile ürün özelliği birlikte seçilemez." });
        if (!hasFixed && string.IsNullOrEmpty(source)) return BadRequest(new { error = "Sabit bir değer ya da ürün özelliği seçin." });
        if (source?.Length > 100) return BadRequest(new { error = "Ürün özellik adı 100 karakteri aşamaz." });

        var validIds = (await db.ChannelAttributeValues.AsNoTracking()
            .Where(v => v.Channel == channel && v.CategoryExternalId == categoryMapping.ExternalCategoryId && v.AttributeExternalId == attributeId && v.IsActive)
            .Select(v => v.ExternalId).ToListAsync(ct)).ToHashSet();

        string? Check(long? valueId, string? custom)
        {
            if (valueId is not null && !validIds.Contains(valueId.Value)) return "Seçilen değer kanalın değer listesinde yok.";
            if (!string.IsNullOrEmpty(custom))
            {
                if (!attribute.AllowCustom) return $"'{attribute.Name}' serbest metin kabul etmez; listeden bir değer seçin.";
                if (custom.Length > 200) return "Serbest metin 200 karakteri aşamaz.";
            }
            return null;
        }
        if (hasFixed && Check(input.FixedValueId, fixedCustom) is { } fixedError) return BadRequest(new { error = fixedError });
        if (hasFixed && input.FixedValueId is not null && !string.IsNullOrEmpty(fixedCustom)) return BadRequest(new { error = "Sabit değer için liste ya da serbest metinden yalnızca biri seçilebilir." });

        var valueInputs = new List<ValueMappingInput>();
        if (!hasFixed)
        {
            var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var v in input.Values ?? [])
            {
                var sv = v.SourceValue?.Trim();
                var custom = v.CustomValue?.Trim();
                if (string.IsNullOrEmpty(sv)) return BadRequest(new { error = "Değer eşlemesinde ürün değeri boş olamaz." });
                if (sv.Length > 200) return BadRequest(new { error = "Ürün değeri 200 karakteri aşamaz." });
                if (!seen.Add(sv)) return BadRequest(new { error = $"'{sv}' değeri birden fazla kez eşlenmiş." });
                if (v.ExternalValueId is null && string.IsNullOrEmpty(custom)) continue; // henüz eşlenmemiş satır: kaydedilmez
                if (v.ExternalValueId is not null && !string.IsNullOrEmpty(custom)) return BadRequest(new { error = $"'{sv}' için liste ya da serbest metinden yalnızca biri seçilebilir." });
                if (Check(v.ExternalValueId, custom) is { } err) return BadRequest(new { error = $"'{sv}': {err}" });
                valueInputs.Add(new ValueMappingInput(sv, v.ExternalValueId, string.IsNullOrEmpty(custom) ? null : custom));
            }
        }

        var mapping = await db.AttributeMappings.Include(m => m.Values).FirstOrDefaultAsync(m => m.Channel == channel && m.CategoryId == categoryId && m.ExternalAttributeId == attributeId, ct);
        if (mapping is null)
        {
            mapping = new AttributeMapping { Channel = channel, CategoryId = categoryId, ExternalAttributeId = attributeId };
            db.AttributeMappings.Add(mapping);
        }
        mapping.SourceAttribute = hasFixed ? null : source;
        mapping.FixedValueId = hasFixed ? input.FixedValueId : null;
        mapping.FixedCustomValue = hasFixed && !string.IsNullOrEmpty(fixedCustom) ? fixedCustom : null;
        // Satırlar yerinde güncellenir (aynı anahtarlı sil+ekle, benzersiz dizinle çakışır); listede olmayanlar silinir.
        foreach (var existing in mapping.Values.Where(e => !valueInputs.Any(v => string.Equals(v.SourceValue, e.SourceValue, StringComparison.CurrentCultureIgnoreCase))).ToList())
        {
            mapping.Values.Remove(existing);
            db.AttributeValueMappings.Remove(existing);
        }
        foreach (var v in valueInputs)
        {
            var row = mapping.Values.FirstOrDefault(e => string.Equals(e.SourceValue, v.SourceValue, StringComparison.CurrentCultureIgnoreCase));
            if (row is null)
            {
                // Anahtarı önceden atanmış yeni kayıt, izlenen üst kaydın koleksiyonuna eklenirse EF onu mevcut sanıp UPDATE atar; açıkça Add edilir.
                row = new AttributeValueMapping { AttributeMappingId = mapping.Id, SourceValue = v.SourceValue! };
                db.AttributeValueMappings.Add(row);
            }
            row.ExternalValueId = v.ExternalValueId;
            row.CustomValue = v.CustomValue;
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("mappings/attributes")]
    public async Task<IActionResult> Remove([FromQuery] string? channel, [FromQuery] Guid categoryId, [FromQuery] long externalAttributeId, CancellationToken ct)
    {
        if (!Supported(channel)) return BadRequest(new { error = "Geçerli bir kanal seçin." });
        var mapping = await db.AttributeMappings.FirstOrDefaultAsync(m => m.Channel == channel && m.CategoryId == categoryId && m.ExternalAttributeId == externalAttributeId, ct);
        if (mapping is null) return NotFound(new { error = "Eşleme bulunamadı." });
        db.AttributeMappings.Remove(mapping);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
