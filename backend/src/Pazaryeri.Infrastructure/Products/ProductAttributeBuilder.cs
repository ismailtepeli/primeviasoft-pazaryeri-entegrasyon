using Pazaryeri.Application.Marketplace;
using Pazaryeri.Domain;

namespace Pazaryeri.Infrastructure.Products;

/// <summary>Ürünün özellik eşlemelerinden kanal özelliklerini (attributes) üretir; zorunlu olup doldurulamayanları bildirir.</summary>
public static class ProductAttributeBuilder
{
    /// <summary>"Beden: M, Renk: Mavi" → { Beden: M, Renk: Mavi }. Anahtar büyük/küçük harf duyarsızdır; ilk ":" ayırıcıdır.</summary>
    public static Dictionary<string, string> ParseVariant(string? variant)
    {
        var result = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        if (string.IsNullOrWhiteSpace(variant)) return result;
        foreach (var part in variant.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var i = part.IndexOf(':');
            if (i <= 0) continue;
            var key = part[..i].Trim();
            var value = part[(i + 1)..].Trim();
            if (key.Length > 0 && value.Length > 0) result.TryAdd(key, value);
        }
        return result;
    }

    /// <param name="channelAttributes">Eşlenen kanal kategorisinin etkin özellikleri.</param>
    /// <param name="mappings">Ürünün kategorisi için kayıtlı özellik eşlemeleri (değerleriyle).</param>
    /// <returns>Üretilen özellikler ve doldurulamayan zorunlu özelliklerin adları.</returns>
    public static (List<ProductAttribute> Attributes, List<string> MissingRequired) Build(
        Product product, IReadOnlyList<ChannelAttribute> channelAttributes, IReadOnlyList<AttributeMapping> mappings)
    {
        var attributes = new List<ProductAttribute>();
        var missing = new List<string>();
        var variant = ParseVariant(product.Variant);

        foreach (var attribute in channelAttributes)
        {
            var built = BuildOne(attribute, mappings.FirstOrDefault(m => m.ExternalAttributeId == attribute.ExternalId), variant);
            if (built is not null) attributes.Add(built);
            else if (attribute.Required) missing.Add(attribute.Name);
        }
        return (attributes, missing);
    }

    private static ProductAttribute? BuildOne(ChannelAttribute attribute, AttributeMapping? mapping, Dictionary<string, string> variant)
    {
        if (mapping is null) return null;
        if (mapping.FixedValueId is { } fixedId) return new ProductAttribute(attribute.ExternalId, fixedId);
        if (!string.IsNullOrWhiteSpace(mapping.FixedCustomValue)) return new ProductAttribute(attribute.ExternalId, null, mapping.FixedCustomValue);

        if (string.IsNullOrWhiteSpace(mapping.SourceAttribute) || !variant.TryGetValue(mapping.SourceAttribute, out var raw)) return null;
        var valueMapping = mapping.Values.FirstOrDefault(v => string.Equals(v.SourceValue, raw, StringComparison.CurrentCultureIgnoreCase));
        if (valueMapping?.ExternalValueId is { } valueId) return new ProductAttribute(attribute.ExternalId, valueId);
        if (!string.IsNullOrWhiteSpace(valueMapping?.CustomValue)) return new ProductAttribute(attribute.ExternalId, null, valueMapping!.CustomValue);
        // Değer eşlenmemiş: serbest metne izin veriliyorsa ürünün kendi değeri olduğu gibi gider.
        return attribute.AllowCustom ? new ProductAttribute(attribute.ExternalId, null, raw) : null;
    }
}
