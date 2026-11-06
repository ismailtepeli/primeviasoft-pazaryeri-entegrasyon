using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Pricing;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Pricing;

namespace Pazaryeri.Infrastructure.Shop;

/// <param name="Price">Satış fiyatı (KDV dahil); kampanya fiyatı varsa o.</param>
/// <param name="ListPrice">Kampanya varsa üstü çizili normal fiyat; yoksa null.</param>
public record ShopProductRow(Guid Id, string Name, string? Brand, string? CategoryPath, string? CoverUrl, decimal Price, decimal? ListPrice,
    string Currency, bool InStock, string StockLabel);

public record ShopFacets(IReadOnlyList<BrandFacet> Brands, decimal? PriceMin, decimal? PriceMax);

public record ShopProductPage(IReadOnlyList<ShopProductRow> Items, int Total, ShopFacets Facets);

public record ShopVariant(Guid Id, string? Variant, bool InStock);

public record ShopProductDetail(Guid Id, string Name, string? Brand, string? Description, IReadOnlyList<StoreCrumb> Breadcrumb, IReadOnlyList<string> Images,
    decimal Price, decimal? ListPrice, string Currency, int VatRate, bool InStock, string StockLabel, IReadOnlyList<ShopVariant> Variants);

public record ShopInfo(string Name);

/// <summary>Sepetteki ürünün sunucuda doğrulanmış hâli (<see cref="ShopCatalogService.CartProductsAsync"/>).</summary>
public record ShopCartProduct(Guid Id, string Name, string Barcode, decimal UnitPrice, decimal? ListPrice, int VatRate, int Available, string? CoverUrl, decimal? Desi = null);

public record ShopQuery(string? Search, Guid? CategoryId, string? Sort, int Page, int PageSize,
    IReadOnlyList<string>? Brands = null, decimal? MinPrice = null, decimal? MaxPrice = null, bool InStock = false);

/// <summary>
/// B2C mağazanın herkese açık ürün okuma tarafı. Oturum yoktur: kiracı slug'tan bulunur ve B2C mağaza modülü o kiracıda açık olmalıdır.
/// Ürün görünür sayılır: B2C bayrağı açık, kategorisi (ya da üst kategorilerinden biri) pasif/gizli değil. Fiyatlar KDV dahildir,
/// kampanya fiyatı varsa normal fiyat üstü çizili gösterilir; kesin stok adedi sızdırılmaz (Stokta var / Az kaldı / Tükendi).
/// </summary>
public class ShopCatalogService(AppDbContext db, IModuleService modules)
{
    public const int MaxPageSize = 60;
    public const int LowStock = 5;
    public static readonly string[] Sorts = ["name", "name_desc", "price", "price_desc"];

    /// <summary>Slug'a ait etkin kiracı ve B2C mağaza açıksa kiracı kimliği; aksi hâlde null (mağaza yokmuş gibi davranılır).</summary>
    public async Task<Guid?> ResolveAsync(string? slug, CancellationToken ct = default)
    {
        var key = slug?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(key)) return null;
        var tenant = await db.Tenants.AsNoTracking().Where(t => t.Slug == key && t.IsActive).Select(t => new { t.Id, t.Name }).FirstOrDefaultAsync(ct);
        if (tenant is null || !await modules.IsEnabledAsync(ModuleIds.B2CStore, tenant.Id, ct)) return null;
        return tenant.Id;
    }

    public async Task<ShopInfo?> InfoAsync(string? slug, CancellationToken ct = default)
    {
        if (await ResolveAsync(slug, ct) is not { } id) return null;
        return new ShopInfo(await db.Tenants.AsNoTracking().Where(t => t.Id == id).Select(t => t.Name).FirstAsync(ct));
    }

    private async Task<IQueryable<Product>> VisibleAsync(CancellationToken ct)
    {
        var cats = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId, Hidden = c.B2CHidden || !c.IsActive }).ToListAsync(ct);
        var hidden = StoreCatalog.Descendants(cats.Select(c => (c.Id, c.ParentId)).ToList(), cats.Where(c => c.Hidden).Select(c => c.Id).ToHashSet()).ToList();
        return db.Products.AsNoTracking().Where(p => p.B2CVisible && p.Price > 0 && (p.CategoryId == null || !hidden.Contains(p.CategoryId.Value)));
    }

    private static decimal Effective(Product p) => p.B2CSalePrice is { } s && s > 0 && s < p.Price ? s : p.Price;

    public async Task<ShopProductPage> ListAsync(Guid tenantId, ShopQuery q, CancellationToken ct = default)
    {
        using var _ = AmbientTenant.Use(tenantId);
        var pageSize = Math.Clamp(q.PageSize, 1, MaxPageSize);
        var page = Math.Max(q.Page, 1);
        var visible = await VisibleAsync(ct);

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var t = q.Search.Trim();
            visible = visible.Where(p => p.Name.Contains(t) || (p.Brand != null && p.Brand.Contains(t)) || (p.MainId != null && p.MainId.Contains(t)));
        }
        if (q.CategoryId is { } cid)
        {
            var all = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync(ct);
            var ids = StoreCatalog.Descendants(all.Select(c => (c.Id, c.ParentId)).ToList(), new HashSet<Guid> { cid }).ToList();
            visible = visible.Where(p => p.CategoryId != null && ids.Contains(p.CategoryId.Value));
        }
        if (q.InStock) visible = visible.Where(p => p.Stock - (p.SafetyStock ?? 0) > 0);

        var brandCounts = await visible.Where(p => p.Brand != null && p.Brand != "").GroupBy(p => p.Brand!).Select(g => new { Name = g.Key, N = g.Count() }).ToListAsync(ct);
        var brands = brandCounts.OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).Select(b => new BrandFacet(b.Name, b.N)).ToList();
        var chosen = q.Brands?.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim()).Distinct().ToList() ?? [];
        if (chosen.Count > 0) visible = visible.Where(p => p.Brand != null && chosen.Contains(p.Brand));

        // Etkin fiyat (kampanya varsa o) üzerinden süzme ve sıralama veritabanında yapılır.
        var priced = visible.Select(p => new { P = p, Eff = p.B2CSalePrice != null && p.B2CSalePrice > 0 && p.B2CSalePrice < p.Price ? p.B2CSalePrice.Value : p.Price });
        var range = await priced.GroupBy(_ => 1).Select(g => new { Min = g.Min(x => x.Eff), Max = g.Max(x => x.Eff) }).FirstOrDefaultAsync(ct);
        if (q.MinPrice is { } lo) priced = priced.Where(x => x.Eff >= lo);
        if (q.MaxPrice is { } hi) priced = priced.Where(x => x.Eff <= hi);

        var sort = Sorts.Contains(q.Sort) ? q.Sort! : "name";
        var total = await priced.CountAsync(ct);
        var ordered = sort switch
        {
            "price" => priced.OrderBy(x => x.Eff).ThenBy(x => x.P.Id),
            "price_desc" => priced.OrderByDescending(x => x.Eff).ThenBy(x => x.P.Id),
            "name_desc" => priced.OrderByDescending(x => x.P.Name).ThenBy(x => x.P.Id),
            _ => priced.OrderBy(x => x.P.Name).ThenBy(x => x.P.Id),
        };
        var products = (await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct)).Select(x => x.P).ToList();

        var paths = await CategoryPathsAsync(ct);
        var ids2 = products.Select(p => p.Id).ToList();
        var covers = await db.ProductImages.AsNoTracking().Where(i => ids2.Contains(i.ProductId) && i.Position == 0)
            .Select(i => new { i.ProductId, i.Id, i.Kind, i.ExternalUrl }).ToListAsync(ct);
        var rows = products.Select(p =>
        {
            var cover = covers.FirstOrDefault(c => c.ProductId == p.Id);
            var (inStock, label) = StockOf(p);
            var price = Effective(p);
            return new ShopProductRow(p.Id, p.Name, p.Brand, p.CategoryId is { } c ? paths.GetValueOrDefault(c) : null,
                cover is null ? null : ImageUrl(cover.Id, cover.Kind, cover.ExternalUrl), price, price < p.Price ? p.Price : null, "TRY", inStock, label);
        }).ToList();
        return new ShopProductPage(rows, total, new ShopFacets(brands, range?.Min, range?.Max));
    }

    public async Task<ShopProductDetail?> DetailAsync(Guid tenantId, Guid productId, CancellationToken ct = default)
    {
        using var _ = AmbientTenant.Use(tenantId);
        var visible = await VisibleAsync(ct);
        var p = await visible.FirstOrDefaultAsync(x => x.Id == productId, ct);
        if (p is null) return null;

        var images = await db.ProductImages.AsNoTracking().Where(i => i.ProductId == p.Id).OrderBy(i => i.Position).Select(i => new { i.Id, i.Kind, i.ExternalUrl }).ToListAsync(ct);
        var crumbs = new List<StoreCrumb>();
        if (p.CategoryId is { } cid)
        {
            var all = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.Name, c.ParentId }).ToListAsync(ct);
            var slugs = Pazaryeri.Application.Catalog.CategoryRules.SlugPaths(all.Select(c => (c.Id, c.Name, c.ParentId)).ToList());
            for (Guid? cur = cid; cur is { } id && crumbs.Count < 20;)
            {
                var node = all.FirstOrDefault(c => c.Id == id);
                if (node is null) break;
                crumbs.Insert(0, new StoreCrumb(node.Id, node.Name, slugs[node.Id]));
                cur = node.ParentId;
            }
        }
        var variants = new List<ShopVariant>();
        if (!string.IsNullOrWhiteSpace(p.MainId))
            variants = (await visible.Where(x => x.MainId == p.MainId).OrderBy(x => x.Variant).ThenBy(x => x.Barcode).ToListAsync(ct))
                .Select(s => new ShopVariant(s.Id, s.Variant, StockDisplay.Available(s.Stock, s.SafetyStock) > 0)).ToList();

        var (inStock, label) = StockOf(p);
        var price = Effective(p);
        return new ShopProductDetail(p.Id, p.Name, p.Brand, p.Description, crumbs, images.Select(i => ImageUrl(i.Id, i.Kind, i.ExternalUrl)).ToList(),
            price, price < p.Price ? p.Price : null, "TRY", p.VatRate, inStock, label, variants);
    }

    /// <summary>Sepet fiyatlaması için görünür ürünler: etkin (kampanyalı) birim fiyat, satılabilir stok, barkod ve kapak. Görünmeyen ürün dönmez.</summary>
    public async Task<IReadOnlyList<ShopCartProduct>> CartProductsAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        using var _ = AmbientTenant.Use(tenantId);
        var visible = await VisibleAsync(ct);
        var products = await visible.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var covers = await db.ProductImages.AsNoTracking().Where(i => ids.Contains(i.ProductId) && i.Position == 0).Select(i => new { i.ProductId, i.Id, i.Kind, i.ExternalUrl }).ToListAsync(ct);
        var coverBy = covers.GroupBy(c => c.ProductId).ToDictionary(g => g.Key, g => ImageUrl(g.First().Id, g.First().Kind, g.First().ExternalUrl));
        // Desi, ürünün kanal verisindeki desi değerinden (pazaryeri için girilmiş) alınır; birden çok kanalda varsa en büyüğü; girilmemişse 0 sayılır.
        var desiBy = (await db.ProductChannelData.AsNoTracking().Where(d => ids.Contains(d.ProductId) && d.DimensionalWeight != null).Select(d => new { d.ProductId, d.DimensionalWeight }).ToListAsync(ct))
            .GroupBy(d => d.ProductId).ToDictionary(g => g.Key, g => g.Max(d => d.DimensionalWeight));
        return products.Select(p =>
        {
            var price = Effective(p);
            return new ShopCartProduct(p.Id, p.Name, p.Barcode, price, price < p.Price ? p.Price : null, p.VatRate, StockDisplay.Available(p.Stock, p.SafetyStock), coverBy.GetValueOrDefault(p.Id), desiBy.GetValueOrDefault(p.Id));
        }).ToList();
    }

    /// <summary>Görünür ürünü olan kategoriler (üst kategorileri de dahil); ürün sayıları alt kategorileri kapsar.</summary>
    public async Task<IReadOnlyList<StoreCategory>> CategoriesAsync(Guid tenantId, CancellationToken ct = default)
    {
        using var _ = AmbientTenant.Use(tenantId);
        var visible = await VisibleAsync(ct);
        var direct = await visible.Where(p => p.CategoryId != null).GroupBy(p => p.CategoryId!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        var all = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.Name, c.ParentId, c.SortOrder, c.ImageUrl }).ToListAsync(ct);
        var byId = all.ToDictionary(c => c.Id);
        var slugs = Pazaryeri.Application.Catalog.CategoryRules.SlugPaths(all.Select(c => (c.Id, c.Name, c.ParentId)).ToList());

        var totals = new Dictionary<Guid, int>();
        foreach (var (id, n) in direct)
        {
            var depth = 0;
            for (Guid? cur = id; cur is { } c && byId.ContainsKey(c) && depth++ < 20; cur = byId[c].ParentId) totals[c] = totals.GetValueOrDefault(c) + n;
        }
        return all.Where(c => totals.ContainsKey(c.Id)).OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new StoreCategory(c.Id, c.Name, c.ParentId is { } p && totals.ContainsKey(p) ? p : null, totals[c.Id], c.ImageUrl, slugs[c.Id])).ToList();
    }

    private static (bool InStock, string Label) StockOf(Product p)
    {
        var available = StockDisplay.Available(p.Stock, p.SafetyStock);
        return available <= 0 ? (false, StockDisplay.OutOfStock) : available <= LowStock ? (true, StockDisplay.Low) : (true, StockDisplay.InStock);
    }

    private async Task<Dictionary<Guid, string>> CategoryPathsAsync(CancellationToken ct)
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

    private static string ImageUrl(Guid id, ImageKind kind, string? external) => kind == ImageKind.Url ? external! : $"/api/images/{id}";
}
