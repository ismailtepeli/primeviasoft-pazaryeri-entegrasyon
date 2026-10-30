using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Pricing;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Pricing;

public record StorePrice(decimal Amount, bool IncludesVat, string Currency, int VatRate);

public record StoreStock(int? Quantity, string? Label, bool InStock);

public record StoreProductRow(Guid Id, string Name, string Code, string? MainId, string? Variant, string? Brand, string? CategoryPath,
    string? CoverUrl, StorePrice Price, StoreStock Stock, int MinOrderQuantity, int PackSize);

public record StoreProductPage(IReadOnlyList<StoreProductRow> Items, int Total);

public record StoreVariant(Guid Id, string? Variant, string Code, bool InStock);

public record StoreCrumb(Guid Id, string Name);

public record StoreProductDetail(Guid Id, string Name, string Code, string? MainId, string? Variant, string? Brand, string? Description,
    IReadOnlyList<StoreCrumb> Breadcrumb, IReadOnlyList<string> Images, StorePrice Price, StoreStock Stock, int MinOrderQuantity, int PackSize,
    IReadOnlyList<StoreVariant> Variants);

public record StoreCategory(Guid Id, string Name, Guid? ParentId, int ProductCount, string? ImageUrl, string SlugPath);

public record StoreQuery(string? Search, Guid? CategoryId, string? Sort, int Page, int PageSize);

/// <summary>Bayi sitesinin ürün okuma tarafı: görünürlük (<see cref="StoreCatalog"/>), bayiye özel fiyat (<see cref="DealerPriceService"/>) ve stok durumu tek yerde birleşir.</summary>
public class StoreCatalogService(AppDbContext db, StoreCatalog catalog, DealerPriceService prices)
{
    public const int MaxPageSize = 100;
    /// <summary>Fiyata göre sıralamada çözümlenen en fazla ürün sayısı; fazlası için sıralama ada düşer.</summary>
    public const int MaxPriceSort = 5000;
    public static readonly string[] Sorts = ["name", "name_desc", "price", "price_desc"];

    private async Task<Guid?> GroupOfAsync(Guid dealerId, CancellationToken ct) =>
        await db.Dealers.AsNoTracking().Where(d => d.Id == dealerId).Select(d => d.GroupId).FirstOrDefaultAsync(ct);

    public async Task<StoreProductPage> ListAsync(Guid dealerId, StoreQuery q, CancellationToken ct)
    {
        var pageSize = Math.Clamp(q.PageSize, 1, MaxPageSize);
        var page = Math.Max(q.Page, 1);
        var visible = await catalog.VisibleProductsAsync(await GroupOfAsync(dealerId, ct), ct);

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var t = q.Search.Trim();
            visible = visible.Where(p => p.Name.Contains(t) || p.Barcode.Contains(t) || (p.MainId != null && p.MainId.Contains(t)) || (p.Brand != null && p.Brand.Contains(t)));
        }
        if (q.CategoryId is { } cid)
        {
            var all = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync(ct);
            var ids = StoreCatalog.Descendants(all.Select(c => (c.Id, c.ParentId)).ToList(), new HashSet<Guid> { cid }).ToList();
            visible = visible.Where(p => p.CategoryId != null && ids.Contains(p.CategoryId.Value));
        }

        var total = await visible.CountAsync(ct);
        var sort = Sorts.Contains(q.Sort) ? q.Sort! : "name";
        var byPrice = sort is "price" or "price_desc" && total <= MaxPriceSort;

        List<Product> products;
        Dictionary<Guid, DealerProductPrice> resolved;
        if (byPrice)
        {
            var everything = await visible.ToListAsync(ct);
            resolved = ((await prices.ResolveAsync(dealerId, everything.Select(p => p.Id).ToList(), ct)) ?? []).ToDictionary(r => r.ProductId);
            Func<Product, decimal> key = p => resolved[p.Id].Price.Gross;
            var ordered = sort == "price" ? everything.OrderBy(key) : everything.OrderByDescending(key);
            products = ordered.ThenBy(p => p.Name).ThenBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }
        else
        {
            var ordered = sort == "name_desc" ? visible.OrderByDescending(p => p.Name).ThenBy(p => p.Id) : visible.OrderBy(p => p.Name).ThenBy(p => p.Id);
            products = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            resolved = ((await prices.ResolveAsync(dealerId, products.Select(p => p.Id).ToList(), ct)) ?? []).ToDictionary(r => r.ProductId);
        }

        var settings = await catalog.SettingsAsync(ct);
        var paths = await CategoryPaths(ct);
        var productIds = products.Select(p => p.Id).ToList();
        var covers = await db.ProductImages.AsNoTracking().Where(i => productIds.Contains(i.ProductId) && i.Position == 0)
            .Select(i => new { i.ProductId, i.Id, i.Kind, i.ExternalUrl }).ToListAsync(ct);

        var rows = products.Select(p =>
        {
            var cover = covers.FirstOrDefault(c => c.ProductId == p.Id);
            return new StoreProductRow(p.Id, p.Name, p.Barcode, p.MainId, p.Variant, p.Brand,
                p.CategoryId is { } c ? paths.GetValueOrDefault(c) : null, cover is null ? null : ImageUrl(cover.Id, cover.Kind, cover.ExternalUrl),
                ToPrice(resolved[p.Id], p.VatRate), ToStock(p, settings), resolved[p.Id].MinOrderQuantity, resolved[p.Id].PackSize);
        }).ToList();
        return new StoreProductPage(rows, total);
    }

    /// <summary>Ürün bayiye görünmüyorsa (bayrak kapalı, kategori/grup kısıtı) null; sitede yokmuş gibi davranılır.</summary>
    public async Task<StoreProductDetail?> DetailAsync(Guid dealerId, Guid productId, CancellationToken ct)
    {
        var visible = await catalog.VisibleProductsAsync(await GroupOfAsync(dealerId, ct), ct);
        var p = await visible.FirstOrDefaultAsync(x => x.Id == productId, ct);
        if (p is null) return null;

        var settings = await catalog.SettingsAsync(ct);
        var resolved = (await prices.ResolveAsync(dealerId, [p.Id], ct))!.Single();
        var images = await db.ProductImages.AsNoTracking().Where(i => i.ProductId == p.Id).OrderBy(i => i.Position)
            .Select(i => new { i.Id, i.Kind, i.ExternalUrl }).ToListAsync(ct);

        var crumbs = new List<StoreCrumb>();
        if (p.CategoryId is { } cid)
        {
            var all = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.Name, c.ParentId }).ToListAsync(ct);
            for (Guid? cur = cid; cur is { } id && crumbs.Count < 20; )
            {
                var node = all.FirstOrDefault(c => c.Id == id);
                if (node is null) break;
                crumbs.Insert(0, new StoreCrumb(node.Id, node.Name));
                cur = node.ParentId;
            }
        }

        var variants = new List<StoreVariant>();
        if (!string.IsNullOrWhiteSpace(p.MainId))
        {
            var siblings = await visible.Where(x => x.MainId == p.MainId).OrderBy(x => x.Variant).ThenBy(x => x.Barcode).ToListAsync(ct);
            variants = siblings.Select(s => new StoreVariant(s.Id, s.Variant, s.Barcode, StockDisplay.Available(s.Stock, s.SafetyStock) > 0)).ToList();
        }

        return new StoreProductDetail(p.Id, p.Name, p.Barcode, p.MainId, p.Variant, p.Brand, p.Description, crumbs,
            images.Select(i => ImageUrl(i.Id, i.Kind, i.ExternalUrl)).ToList(), ToPrice(resolved, p.VatRate), ToStock(p, settings),
            resolved.MinOrderQuantity, resolved.PackSize, variants);
    }

    /// <summary>Bayiye görünen ürünü olan kategoriler (üst kategorileri de dahil), ürün sayıları alt kategorileri kapsar.</summary>
    public async Task<IReadOnlyList<StoreCategory>> CategoriesAsync(Guid dealerId, CancellationToken ct)
    {
        var visible = await catalog.VisibleProductsAsync(await GroupOfAsync(dealerId, ct), ct);
        var direct = await visible.Where(p => p.CategoryId != null).GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        var all = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.Name, c.ParentId, c.SortOrder, c.ImageUrl }).ToListAsync(ct);
        var byId = all.ToDictionary(c => c.Id);
        var slugs = Pazaryeri.Application.Catalog.CategoryRules.SlugPaths(all.Select(c => (c.Id, c.Name, c.ParentId)).ToList());

        var totals = new Dictionary<Guid, int>();
        foreach (var (id, n) in direct)
        {
            var depth = 0;
            for (Guid? cur = id; cur is { } c && byId.ContainsKey(c) && depth++ < 20; cur = byId[c].ParentId)
                totals[c] = totals.GetValueOrDefault(c) + n;
        }

        return all.Where(c => totals.ContainsKey(c.Id)).OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new StoreCategory(c.Id, c.Name, c.ParentId is { } p && totals.ContainsKey(p) ? p : null, totals[c.Id], c.ImageUrl, slugs[c.Id])).ToList();
    }

    private async Task<Dictionary<Guid, string>> CategoryPaths(CancellationToken ct)
    {
        var rows = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.Name, c.ParentId }).ToListAsync(ct);
        var byId = rows.ToDictionary(c => c.Id);
        string PathOf(Guid id)
        {
            var parts = new List<string>();
            for (Guid? cur = id; cur is { } c && byId.TryGetValue(c, out var node) && parts.Count < 20; cur = node.ParentId) parts.Insert(0, node.Name);
            return string.Join(" > ", parts);
        }
        return rows.ToDictionary(c => c.Id, c => PathOf(c.Id));
    }

    private static StorePrice ToPrice(DealerProductPrice r, int vat) =>
        new(r.Price.Display, r.Price.DisplayIncludesVat, r.Price.Currency, vat);

    private static StoreStock ToStock(Product p, B2BSettings s)
    {
        var v = StockDisplay.Compute(p.Stock, p.SafetyStock, s.StockDisplay, s.LowStockThreshold);
        return new StoreStock(v.Quantity, v.Label, v.InStock);
    }

    private static string ImageUrl(Guid id, ImageKind kind, string? external) => kind == ImageKind.Url ? external! : $"/api/images/{id}";
}
