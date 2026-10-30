using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Pricing;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Pricing;

public record DealerProductPrice(Guid ProductId, ResolvedPrice Price, int MinOrderQuantity, int PackSize);

/// <summary>Bir bayi için ürünlerin fiyatını <see cref="DealerPricing"/> kuralıyla çözer; bayi sitesi ve yönetici önizlemesi aynı yolu kullanır.</summary>
public class DealerPriceService(AppDbContext db)
{
    public async Task<IReadOnlyList<DealerProductPrice>?> ResolveAsync(Guid dealerId, IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        var dealer = await db.Dealers.AsNoTracking().Where(d => d.Id == dealerId).Select(d => new { d.GroupId }).FirstOrDefaultAsync(ct);
        if (dealer is null) return null;

        // Pasif listeler yok sayılır. Aynı düzeyde birden çok aktif liste varsa en yenisi geçerlidir.
        var dealerList = await db.PriceLists.AsNoTracking().Where(l => l.IsActive && l.DealerId == dealerId).OrderByDescending(l => l.CreatedAt).FirstOrDefaultAsync(ct);
        var groupList = dealer.GroupId is { } gid
            ? await db.PriceLists.AsNoTracking().Where(l => l.IsActive && l.GroupId == gid).OrderByDescending(l => l.CreatedAt).FirstOrDefaultAsync(ct)
            : null;

        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Price, p.VatRate, p.MinOrderQuantity, p.PackSize }).ToListAsync(ct);
        var listIds = new[] { dealerList?.Id, groupList?.Id }.Where(i => i is not null).Select(i => i!.Value).ToList();
        var items = listIds.Count == 0 ? [] : await db.PriceListItems.AsNoTracking()
            .Where(i => listIds.Contains(i.PriceListId) && productIds.Contains(i.ProductId))
            .Select(i => new { i.PriceListId, i.ProductId, i.Price }).ToListAsync(ct);

        static PriceListRule? Rule(PriceList? l) => l is null ? null : new PriceListRule(l.DiscountPercent, l.PricesIncludeVat, l.ShowVatIncluded, l.Currency);
        decimal? Item(PriceList? l, Guid productId) => l is null ? null : items.FirstOrDefault(i => i.PriceListId == l.Id && i.ProductId == productId)?.Price;

        return products.Select(p =>
        {
            var (min, pack) = OrderQuantityRules.Normalize(p.MinOrderQuantity, p.PackSize);
            var price = DealerPricing.Resolve(p.Price, p.VatRate, Rule(dealerList), Item(dealerList, p.Id), Rule(groupList), Item(groupList, p.Id));
            return new DealerProductPrice(p.Id, price, min, pack);
        }).ToList();
    }
}
