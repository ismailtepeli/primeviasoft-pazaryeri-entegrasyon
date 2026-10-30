using System.Text;

namespace Pazaryeri.Application.Catalog;

/// <summary>Kategori ağacı kuralları: derinlik, taşıma ve adres dostu kod. Saf fonksiyonlardır; ağaç (id, üst) çiftleriyle verilir.</summary>
public static class CategoryRules
{
    public const int MaxDepth = 5;
    public const int MaxImageUrlLength = 500;

    /// <summary>Adres dostu kod: Türkçe harfler sadeleştirilir, küçük harf, boşluk ve işaretler tire olur ("Çocuk Giyim" → "cocuk-giyim").</summary>
    public static string Slugify(string name)
    {
        var map = new Dictionary<char, string> { ['ç'] = "c", ['ğ'] = "g", ['ı'] = "i", ['ö'] = "o", ['ş'] = "s", ['ü'] = "u", ['â'] = "a", ['î'] = "i", ['û'] = "u" };
        var sb = new StringBuilder();
        foreach (var raw in name.Trim().ToLowerInvariant().Replace("İ", "i"))
        {
            var ch = raw;
            if (map.TryGetValue(ch, out var rep)) sb.Append(rep);
            else if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    /// <summary>Kök → kategori yolu için kodlar ("giyim/tisort").</summary>
    public static Dictionary<Guid, string> SlugPaths(IReadOnlyList<(Guid Id, string Name, Guid? ParentId)> all)
    {
        var byId = all.ToDictionary(c => c.Id);
        string PathOf(Guid id)
        {
            var parts = new List<string>();
            for (Guid? cur = id; cur is { } c && byId.TryGetValue(c, out var node) && parts.Count < 20; cur = node.ParentId) parts.Insert(0, Slugify(node.Name));
            return string.Join('/', parts);
        }
        return all.ToDictionary(c => c.Id, c => PathOf(c.Id));
    }

    /// <summary>Kategorinin kökten itibaren derinliği (kök = 1).</summary>
    public static int DepthOf(IReadOnlyList<(Guid Id, Guid? ParentId)> all, Guid id)
    {
        var byId = all.ToDictionary(c => c.Id);
        var depth = 0;
        for (Guid? cur = id; cur is { } c && byId.TryGetValue(c, out var node) && depth < 50; cur = node.ParentId) depth++;
        return depth;
    }

    /// <summary>Kategorinin altındaki en derin seviyenin kendisine göre yüksekliği (yaprak = 1).</summary>
    public static int SubtreeHeight(IReadOnlyList<(Guid Id, Guid? ParentId)> all, Guid id)
    {
        int Height(Guid node, int guard) => guard > 50 ? 1 : 1 + all.Where(c => c.ParentId == node).Select(c => Height(c.Id, guard + 1)).DefaultIfEmpty(0).Max();
        return Height(id, 0);
    }

    /// <summary>Kategori, verilen kökün kendisi ya da altında mı (taşımada döngüyü önlemek için).</summary>
    public static bool IsSelfOrDescendant(IReadOnlyList<(Guid Id, Guid? ParentId)> all, Guid root, Guid candidate)
    {
        var byId = all.ToDictionary(c => c.Id);
        var guard = 0;
        for (Guid? cur = candidate; cur is { } c && guard++ < 50; cur = byId.TryGetValue(c, out var n) ? n.ParentId : null)
            if (c == root) return true;
        return false;
    }

    /// <summary>Kategoriyi newParent altına taşımanın hata metni; geçerliyse null.</summary>
    public static string? ValidateMove(IReadOnlyList<(Guid Id, Guid? ParentId)> all, Guid id, Guid? newParent)
    {
        if (newParent is { } p)
        {
            if (IsSelfOrDescendant(all, id, p)) return "Kategori kendisinin ya da alt kategorisinin altına taşınamaz.";
            if (DepthOf(all, p) + SubtreeHeight(all, id) > MaxDepth) return $"Kategori en fazla {MaxDepth} seviye olabilir.";
        }
        else if (SubtreeHeight(all, id) > MaxDepth) return $"Kategori en fazla {MaxDepth} seviye olabilir.";
        return null;
    }

    public static string? ValidateImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.Trim().Length > MaxImageUrlLength) return $"Görsel adresi en fazla {MaxImageUrlLength} karakter olabilir.";
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
            ? null : "Görsel adresi http ya da https ile başlamalı.";
    }
}
