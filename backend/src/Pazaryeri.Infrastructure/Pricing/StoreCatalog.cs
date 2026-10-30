using Microsoft.EntityFrameworkCore;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Pricing;

/// <summary>
/// Bayi sitesinde görünecek ürünlerin tek sorgu kuralı. Ürün görünür sayılır: ürünün B2B bayrağı açık,
/// kategorisi (ya da üst kategorilerinden biri) gizli değil ve bayinin grubu için ürün/kategori kısıtı yok.
/// Bayi sitesi okuma uç noktaları (#69) ürünleri yalnızca buradan alır.
/// </summary>
public class StoreCatalog(AppDbContext db)
{
    public async Task<IQueryable<Product>> VisibleProductsAsync(Guid? groupId, CancellationToken ct)
    {
        var cats = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId, Hidden = c.B2BHidden || !c.IsActive }).ToListAsync(ct);
        var rules = groupId is { } g
            ? await db.GroupVisibilityRules.AsNoTracking().Where(r => r.GroupId == g).Select(r => new { r.ProductId, r.CategoryId }).ToListAsync(ct)
            : [];

        var seeds = cats.Where(c => c.Hidden).Select(c => c.Id).Concat(rules.Where(r => r.CategoryId != null).Select(r => r.CategoryId!.Value)).ToHashSet();
        var hiddenCategories = Descendants(cats.Select(c => (c.Id, c.ParentId)).ToList(), seeds);
        var blockedProducts = rules.Where(r => r.ProductId != null).Select(r => r.ProductId!.Value).ToList();
        var hidden = hiddenCategories.ToList();

        return db.Products.AsNoTracking().Where(p => p.B2BVisible
            && (p.CategoryId == null || !hidden.Contains(p.CategoryId.Value))
            && !blockedProducts.Contains(p.Id));
    }

    /// <summary>Verilen kategorilerin kendisi ve tüm alt kategorileri.</summary>
    public static HashSet<Guid> Descendants(IReadOnlyList<(Guid Id, Guid? ParentId)> all, IReadOnlySet<Guid> roots)
    {
        var result = new HashSet<Guid>(roots);
        bool grew;
        do
        {
            grew = false;
            foreach (var (id, parent) in all)
                if (parent is { } p && result.Contains(p) && result.Add(id)) grew = true;
        } while (grew);
        return result;
    }

    public async Task<B2BSettings> SettingsAsync(CancellationToken ct) =>
        await db.B2BSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new B2BSettings();
}
