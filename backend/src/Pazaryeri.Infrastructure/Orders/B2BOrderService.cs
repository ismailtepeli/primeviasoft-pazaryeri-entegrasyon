using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Pricing;
using Pazaryeri.Application.Stock;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Pricing;

namespace Pazaryeri.Infrastructure.Orders;

public class StoreException(int status, string message, object? details = null) : Exception(message)
{
    public int Status { get; } = status;
    public object? Details { get; } = details;
}

/// <summary>Sepet satırı sorunu: unavailable (artık satışta değil), stock (stok yetmiyor), quantity (minimum/koli kuralına uymuyor), currency.</summary>
public record CartIssue(string Kind, string Text);

public record CartLineView(Guid ProductId, string Name, string Code, string? CoverUrl, int Quantity, StorePrice Price, decimal LineTotal,
    int MinOrderQuantity, int PackSize, StoreStock Stock, CartIssue? Issue, decimal? PriceAtAdd)
{
    /// <summary>Satılabilir kesin stok; stok gösterimi "gizli/seviye" olsa bile sunucu kararları için tutulur, istemciye gönderilmez.</summary>
    [JsonIgnore] public int Available { get; init; }
}

public record CartView(IReadOnlyList<CartLineView> Lines, decimal TotalNet, decimal TotalGross, string Currency, bool HasIssues, int ItemCount);

public record CheckoutInput(string? Address, string? Note, bool AllowPartial);

public record B2BOrderRow(Guid Id, string OrderNumber, DateTime OrderDate, string Status, decimal TotalGross, string Currency, int LineCount);

public record B2BOrderPage(IReadOnlyList<B2BOrderRow> Items, int Total);

public record B2BOrderLine(Guid? ProductId, string Name, string Code, int Quantity, decimal UnitPrice, bool PriceIncludesVat, int VatRate);

public record B2BOrderDetail(Guid Id, string OrderNumber, DateTime OrderDate, string Status, decimal TotalNet, decimal TotalGross, string Currency,
    string? ShippingAddress, string? Note, bool CanCancel, IReadOnlyList<B2BOrderLine> Lines, string? ShipmentCarrier = null, string? TrackingNumber = null,
    DateTime? ShippedAt = null, string? StatusNote = null);

public record ReorderResult(int Added, IReadOnlyList<string> Skipped);

/// <summary>
/// Bayi sitesi sepeti ve siparişi. Sipariş "B2B" kanalında bir <see cref="Order"/> olur; stok ortak havuzdan
/// (<see cref="IStockPoolService"/>) düşer. Pazaryeri satışından farkı: stok yetmiyorsa fazla satış yapılmaz, sipariş reddedilir
/// (ya da bayi kısmi karşılamayı seçer).
/// </summary>
public class B2BOrderService(AppDbContext db, StoreCatalog catalog, DealerPriceService prices, IStockPoolService stock, TimeProvider time)
{
    public const string Channel = "B2B";
    public const int MaxLines = 200;
    public const int MaxQuantity = 100_000;
    public const int PageSize = 20;

    // ---------- sepet ----------

    public async Task<CartView> GetCartAsync(Guid dealerId, CancellationToken ct)
    {
        var cart = await db.Carts.AsNoTracking().Include(c => c.Items).FirstOrDefaultAsync(c => c.DealerId == dealerId, ct);
        return await BuildViewAsync(dealerId, cart?.Items.Select(i => (i.ProductId, i.Quantity, (decimal?)i.PriceAtAdd)).ToList() ?? [], ct);
    }

    /// <summary>Sepete adet ekler (mevcut adede toplanır); kurallara uymuyorsa nedenini söyler.</summary>
    public async Task<CartView> AddAsync(Guid dealerId, Guid productId, int quantity, CancellationToken ct)
    {
        var cart = await GetOrCreateCartAsync(dealerId, ct);
        var existing = cart.Items.FirstOrDefault(i => i.ProductId == productId)?.Quantity ?? 0;
        return await SetQuantityAsync(dealerId, cart, productId, checked(existing + quantity), ct);
    }

    /// <summary>Satırın adedini belirler; 0 satırı kaldırır.</summary>
    public async Task<CartView> SetAsync(Guid dealerId, Guid productId, int quantity, CancellationToken ct)
    {
        var cart = await GetOrCreateCartAsync(dealerId, ct);
        return await SetQuantityAsync(dealerId, cart, productId, quantity, ct);
    }

    public async Task<CartView> ClearAsync(Guid dealerId, CancellationToken ct)
    {
        var cart = await GetOrCreateCartAsync(dealerId, ct);
        db.CartItems.RemoveRange(cart.Items);
        cart.UpdatedAt = Now();
        await db.SaveChangesAsync(ct);
        return await GetCartAsync(dealerId, ct);
    }

    private async Task<CartView> SetQuantityAsync(Guid dealerId, Cart cart, Guid productId, int quantity, CancellationToken ct)
    {
        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (quantity < 0 || quantity > MaxQuantity) throw new StoreException(400, $"Adet 0 ile {MaxQuantity} arasında olmalı.");
        if (quantity == 0)
        {
            if (item is not null) db.CartItems.Remove(item);
        }
        else
        {
            var info = await ResolveAsync(dealerId, [productId], ct);
            if (!info.TryGetValue(productId, out var line)) throw new StoreException(404, "Ürün bulunamadı.");
            var check = OrderQuantityRules.Check(quantity, line.MinOrder, line.Pack);
            if (!check.Valid) throw new StoreException(400, check.Error!, new { suggested = check.Suggested });
            if (quantity > line.Available) throw new StoreException(409, line.Available <= 0 ? "Bu ürün tükendi." : $"Stokta yalnızca {line.Available} adet var.");
            if (item is null)
            {
                if (cart.Items.Count >= MaxLines) throw new StoreException(400, $"Sepette en fazla {MaxLines} farklı ürün olabilir.");
                db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = productId, Quantity = quantity, PriceAtAdd = line.Price.Price.Display });
            }
            else { item.Quantity = quantity; item.PriceAtAdd = line.Price.Price.Display; }
        }
        cart.UpdatedAt = Now();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new StoreException(409, "Sepet aynı anda başka bir işlemde değişti; tekrar deneyin."); }
        db.ChangeTracker.Clear();
        return await GetCartAsync(dealerId, ct);
    }

    private async Task<Cart> GetOrCreateCartAsync(Guid dealerId, CancellationToken ct)
    {
        var cart = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.DealerId == dealerId, ct);
        if (cart is not null) return cart;
        cart = new Cart { DealerId = dealerId, UpdatedAt = Now() };
        db.Carts.Add(cart);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) // eşzamanlı ilk ekleme: diğer istek sepeti oluşturdu
        {
            db.Entry(cart).State = EntityState.Detached;
            cart = await db.Carts.Include(c => c.Items).FirstAsync(c => c.DealerId == dealerId, ct);
        }
        return cart;
    }

    private sealed record Resolved(string Name, string Barcode, string? CoverUrl, StorePrice PriceView, DealerProductPrice Price, int MinOrder, int Pack, int Available, StoreStock Stock, int VatRate, int Safety);

    /// <summary>Bayiye görünen ürünlerin fiyat/stok/kural bilgisi; görünmeyen ürünler sonuçta yoktur.</summary>
    private async Task<Dictionary<Guid, Resolved>> ResolveAsync(Guid dealerId, IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        var group = await db.Dealers.AsNoTracking().Where(d => d.Id == dealerId).Select(d => d.GroupId).FirstOrDefaultAsync(ct);
        var visible = await catalog.VisibleProductsAsync(group, ct);
        var products = await visible.Where(p => productIds.Contains(p.Id)).ToListAsync(ct);
        var resolved = ((await prices.ResolveAsync(dealerId, products.Select(p => p.Id).ToList(), ct)) ?? []).ToDictionary(r => r.ProductId);
        var settings = await catalog.SettingsAsync(ct);
        var ids = products.Select(p => p.Id).ToList();
        var covers = await db.ProductImages.AsNoTracking().Where(i => ids.Contains(i.ProductId) && i.Position == 0)
            .Select(i => new { i.ProductId, i.Id, i.Kind, i.ExternalUrl }).ToListAsync(ct);

        return products.ToDictionary(p => p.Id, p =>
        {
            var r = resolved[p.Id];
            var cover = covers.FirstOrDefault(c => c.ProductId == p.Id);
            var sv = StockDisplay.Compute(p.Stock, p.SafetyStock, settings.StockDisplay, settings.LowStockThreshold);
            return new Resolved(p.Name, p.Barcode, cover is null ? null : cover.Kind == ImageKind.Url ? cover.ExternalUrl : $"/api/images/{cover.Id}",
                new StorePrice(r.Price.Display, r.Price.DisplayIncludesVat, r.Price.Currency, p.VatRate), r, r.MinOrderQuantity, r.PackSize,
                StockDisplay.Available(p.Stock, p.SafetyStock), new StoreStock(sv.Quantity, sv.Label, sv.InStock), p.VatRate, Math.Max(0, p.SafetyStock ?? 0));
        });
    }

    private async Task<CartView> BuildViewAsync(Guid dealerId, IReadOnlyList<(Guid ProductId, int Quantity, decimal? PriceAtAdd)> items, CancellationToken ct)
    {
        var info = await ResolveAsync(dealerId, items.Select(i => i.ProductId).ToList(), ct);
        var names = await db.Products.AsNoTracking().Where(p => items.Select(i => i.ProductId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var lines = new List<CartLineView>();
        decimal net = 0, gross = 0;
        var currency = "TRY";
        foreach (var (productId, qty, added) in items)
        {
            if (!info.TryGetValue(productId, out var r))
            {
                lines.Add(new CartLineView(productId, names.GetValueOrDefault(productId, "Ürün"), "", null, qty, new StorePrice(0, true, "TRY", 0), 0, 1, 1,
                    new StoreStock(null, null, false), new CartIssue("unavailable", "Bu ürün artık satışta değil."), added) { Available = 0 });
                continue;
            }
            CartIssue? issue = null;
            var rule = OrderQuantityRules.Check(qty, r.MinOrder, r.Pack);
            if (r.Available <= 0) issue = new CartIssue("stock", "Bu ürün tükendi.");
            else if (qty > r.Available) issue = new CartIssue("stock", $"Stokta yalnızca {r.Available} adet var.");
            else if (!rule.Valid) issue = new CartIssue("quantity", rule.Error!);
            var unit = r.Price.Price;
            net += unit.Net * qty; gross += unit.Gross * qty;
            currency = unit.Currency;
            lines.Add(new CartLineView(productId, r.Name, r.Barcode, r.CoverUrl, qty, r.PriceView, DealerPricing.Round(unit.Display * qty), r.MinOrder, r.Pack, r.Stock, issue, added) { Available = r.Available });
        }
        if (lines.Where(l => l.Issue?.Kind != "unavailable").Select(l => l.Price.Currency).Distinct().Count() > 1)
            lines = lines.Select(l => l.Issue is null ? l with { Issue = new CartIssue("currency", "Sepette farklı para birimleri var; ayrı sipariş verin.") } : l).ToList();
        return new CartView(lines, DealerPricing.Round(net), DealerPricing.Round(gross), currency, lines.Any(l => l.Issue is not null), lines.Sum(l => l.Quantity));
    }

    // ---------- sipariş verme ----------

    public async Task<B2BOrderDetail> CheckoutAsync(Guid dealerId, CheckoutInput input, CancellationToken ct)
    {
        var dealer = await db.Dealers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dealerId, ct) ?? throw new StoreException(404, "Bayi bulunamadı.");
        var address = DealerRulesClean(input.Address) ?? DealerRulesClean(dealer.Address);
        var note = DealerRulesClean(input.Note);
        if (address is null) throw new StoreException(400, "Teslimat adresi zorunlu.");
        if (address.Length > 1000 || (note?.Length ?? 0) > 1000) throw new StoreException(400, "Adres ve not en fazla 1000 karakter olabilir.");

        var view = await GetCartAsync(dealerId, ct);
        if (view.Lines.Count == 0) throw new StoreException(409, "Sepetiniz boş.");
        if (view.Lines.Any(l => l.Issue?.Kind == "currency")) throw new StoreException(409, "Sepette farklı para birimleri var; ayrı sipariş verin.", new { issues = Issues(view) });

        var ordered = new List<(CartLineView Line, int Quantity)>();
        foreach (var l in view.Lines)
        {
            if (l.Issue is null) { ordered.Add((l, l.Quantity)); continue; }
            if (!input.AllowPartial) continue;
            var q = PartialQuantity(l);
            if (q > 0) ordered.Add((l, q));
        }
        if (!input.AllowPartial && view.HasIssues) throw new StoreException(409, "Sepetteki bazı ürünler siparişe uygun değil.", new { issues = Issues(view) });
        if (ordered.Count == 0) throw new StoreException(409, "Siparişe uygun ürün kalmadı.", new { issues = Issues(view) });

        // Sepeti sahiplen: satır sürümü çakışırsa ikinci eşzamanlı sipariş reddedilir (çift sipariş olmaz).
        var cart = await db.Carts.Include(c => c.Items).FirstAsync(c => c.DealerId == dealerId, ct);
        foreach (var (line, qty) in ordered)
        {
            var item = cart.Items.FirstOrDefault(i => i.ProductId == line.ProductId)
                ?? throw new StoreException(409, "Sepetiniz aynı anda başka bir işlemde; sipariş tekrar verilmedi.");
            if (qty >= item.Quantity) db.CartItems.Remove(item); else item.Quantity -= qty;
        }
        cart.UpdatedAt = Now();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new StoreException(409, "Sepetiniz aynı anda başka bir işlemde; sipariş tekrar verilmedi."); }
        db.ChangeTracker.Clear();

        var number = await NewNumberAsync(ct);
        var deducted = new List<(CartLineView Line, int Quantity, string Reference, bool Applied)>();
        var failed = new List<CartIssue>();
        var index = 0;
        foreach (var (line, qty) in ordered)
        {
            var reference = $"{number}:{++index}";
            var r = (await stock.DeductForSaleAsync(Channel, reference, [new StockLine(line.Code, qty)], ct))[0];
            var safety = await db.Products.AsNoTracking().Where(p => p.Barcode == line.Code).Select(p => p.SafetyStock ?? 0).FirstOrDefaultAsync(ct);
            var ok = r.Outcome == StockOutcome.PoolDisabled || (r.Outcome == StockOutcome.Applied && r.Oversold == 0 && r.StockAfter >= safety);
            deducted.Add((line, qty, reference, r.Outcome == StockOutcome.Applied));
            if (!ok) { failed.Add(new CartIssue("stock", $"{line.Name}: stok yetmedi.")); break; }
        }
        if (failed.Count > 0)
        {
            await RollbackAsync(dealerId, deducted, ordered, ct);
            throw new StoreException(409, "Stok yetmediği için sipariş verilemedi; sepetiniz korundu.", new { issues = failed });
        }

        try
        {
            var order = BuildOrder(dealer, number, address, note, deducted);
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
            return ToDetail(order);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            await RollbackAsync(dealerId, deducted, ordered, ct);
            throw new StoreException(409, "Sipariş kaydedilemedi; stok geri eklendi ve sepetiniz korundu.");
        }
    }

    private static int PartialQuantity(CartLineView l)
    {
        if (l.Issue?.Kind is "unavailable" or "currency") return 0;
        var pack = Math.Max(l.PackSize, 1);
        var q = Math.Min(l.Quantity, l.Available) / pack * pack;
        return q >= OrderQuantityRules.Normalize(l.MinOrderQuantity, pack).Min ? q : 0;
    }

    private static List<CartIssue> Issues(CartView v) => v.Lines.Where(l => l.Issue is not null).Select(l => new CartIssue(l.Issue!.Kind, $"{l.Name}: {l.Issue.Text}")).ToList();

    private Order BuildOrder(Dealer dealer, string number, string address, string? note, List<(CartLineView Line, int Quantity, string Reference, bool Applied)> lines)
    {
        var now = Now();
        decimal net = 0, gross = 0;
        var orderLines = new List<OrderLine>();
        var n = 0;
        foreach (var (line, qty, _, applied) in lines)
        {
            n++;
            var unitNet = line.Price.IncludesVat ? DealerPricing.Round(line.Price.Amount / (1 + line.Price.VatRate / 100m)) : line.Price.Amount;
            var unitGross = line.Price.IncludesVat ? line.Price.Amount : DealerPricing.Round(line.Price.Amount * (1 + line.Price.VatRate / 100m));
            net += unitNet * qty; gross += unitGross * qty;
            orderLines.Add(new OrderLine
            {
                LineId = n.ToString(), Barcode = line.Code, Name = line.Name, Quantity = qty, Price = line.Price.Amount, PriceIncludesVat = line.Price.IncludesVat,
                VatRate = line.Price.VatRate, ProductId = line.ProductId, StockDeducted = applied,
            });
        }
        var package = new OrderPackage { Channel = Channel, PackageId = number, ChannelStatus = "Created", Status = OrderStatus.New, LastModifiedAt = now, TotalPrice = DealerPricing.Round(gross), Lines = orderLines };
        return new Order
        {
            Channel = Channel, OrderNumber = number, Status = OrderStatus.New, OrderDate = now, LastModifiedAt = now, CustomerName = dealer.Name, DealerId = dealer.Id,
            TotalPrice = DealerPricing.Round(gross), TotalNet = DealerPricing.Round(net), Currency = lines[0].Line.Price.Currency, ShippingAddress = address, Note = note, Packages = [package],
        };
    }

    /// <summary>Düşülmüş stoğu geri ekler ve sipariş edilen adetleri sepete geri koyar.</summary>
    private async Task RollbackAsync(Guid dealerId, List<(CartLineView Line, int Quantity, string Reference, bool Applied)> deducted, List<(CartLineView Line, int Quantity)> ordered, CancellationToken ct)
    {
        foreach (var (line, qty, reference, applied) in deducted.Where(d => d.Applied))
            await stock.RestoreAsync(Channel, reference, "iptal:" + reference, [new StockLine(line.Code, qty)], ct);
        db.ChangeTracker.Clear();
        var cart = await db.Carts.Include(c => c.Items).FirstAsync(c => c.DealerId == dealerId, ct);
        foreach (var (line, qty) in ordered)
        {
            var item = cart.Items.FirstOrDefault(i => i.ProductId == line.ProductId);
            if (item is null) db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = line.ProductId, Quantity = qty, PriceAtAdd = line.PriceAtAdd ?? line.Price.Amount });
            else item.Quantity += qty;
        }
        cart.UpdatedAt = Now();
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task<string> NewNumberAsync(CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            var number = $"B2B-{Now():yyMMdd}-{Random.Shared.Next(10000, 99999)}";
            if (!await db.Orders.AnyAsync(o => o.Channel == Channel && o.OrderNumber == number, ct)) return number;
        }
        throw new StoreException(500, "Sipariş numarası üretilemedi.");
    }

    // ---------- geçmiş, iptal, tekrar sipariş ----------

    public async Task<B2BOrderPage> OrdersAsync(Guid dealerId, int page, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        var q = db.Orders.AsNoTracking().Where(o => o.DealerId == dealerId);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id).Skip((page - 1) * PageSize).Take(PageSize)
            .Select(o => new { o.Id, o.OrderNumber, o.OrderDate, o.Status, o.TotalPrice, o.Currency, Lines = o.Packages.SelectMany(p => p.Lines).Count() }).ToListAsync(ct);
        return new B2BOrderPage(rows.Select(o => new B2BOrderRow(o.Id, o.OrderNumber, o.OrderDate, o.Status.ToString(), o.TotalPrice, o.Currency ?? "TRY", o.Lines)).ToList(), total);
    }

    public async Task<B2BOrderDetail> OrderAsync(Guid dealerId, Guid id, CancellationToken ct) => ToDetail(await LoadAsync(dealerId, id, false, ct));

    /// <summary>Bayi yalnızca henüz hazırlanmaya başlamamış (Yeni) siparişi iptal edebilir; stok geri eklenir.</summary>
    public async Task<B2BOrderDetail> CancelAsync(Guid dealerId, Guid id, CancellationToken ct)
    {
        var order = await LoadAsync(dealerId, id, true, ct);
        if (order.Status == OrderStatus.Cancelled) return ToDetail(order);
        if (order.Status != OrderStatus.New) throw new StoreException(409, "Sipariş hazırlanmaya başladı; iptal için firmayla iletişime geçin.");

        foreach (var line in order.Packages.SelectMany(p => p.Lines).Where(l => l.StockDeducted && !l.StockRestored))
        {
            var reference = $"{order.OrderNumber}:{line.LineId}";
            await stock.RestoreAsync(Channel, reference, "iptal:" + reference, [new StockLine(line.Barcode, line.Quantity)], ct);
            await db.OrderLines.Where(l => l.Id == line.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.StockRestored, true), ct);
        }
        db.ChangeTracker.Clear();
        var now = Now();
        await db.OrderPackages.Where(p => p.OrderId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, OrderStatus.Cancelled).SetProperty(p => p.ChannelStatus, "Cancelled").SetProperty(p => p.LastModifiedAt, now), ct);
        await db.Orders.Where(o => o.Id == id && o.Status == OrderStatus.New).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Cancelled).SetProperty(o => o.LastModifiedAt, now), ct);
        return ToDetail(await LoadAsync(dealerId, id, false, ct));
    }

    /// <summary>Eski siparişin satırlarını sepete ekler; satışta olmayan ya da kurala uymayan satırlar atlanır ve nedeni söylenir.</summary>
    public async Task<ReorderResult> ReorderAsync(Guid dealerId, Guid id, CancellationToken ct)
    {
        var order = ToDetail(await LoadAsync(dealerId, id, false, ct));
        var added = 0;
        var skipped = new List<string>();
        foreach (var line in order.Lines)
        {
            if (line.ProductId is not { } pid) { skipped.Add($"{line.Name}: ürün artık yok."); continue; }
            try { await AddAsync(dealerId, pid, line.Quantity, ct); added++; }
            catch (StoreException e) { skipped.Add($"{line.Name}: {e.Message}"); }
        }
        return new ReorderResult(added, skipped);
    }

    private async Task<Order> LoadAsync(Guid dealerId, Guid id, bool tracked, CancellationToken ct)
    {
        var q = db.Orders.Include(o => o.Packages).ThenInclude(p => p.Lines).Where(o => o.Id == id && o.DealerId == dealerId);
        var order = await (tracked ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct);
        return order ?? throw new StoreException(404, "Sipariş bulunamadı.");
    }

    private static B2BOrderDetail ToDetail(Order o) => new(o.Id, o.OrderNumber, o.OrderDate, o.Status.ToString(), o.TotalNet ?? o.TotalPrice, o.TotalPrice, o.Currency ?? "TRY",
        o.ShippingAddress, o.Note, o.Status == OrderStatus.New,
        o.Packages.SelectMany(p => p.Lines).OrderBy(l => int.TryParse(l.LineId, out var n) ? n : 0)
            .Select(l => new B2BOrderLine(l.ProductId, l.Name, l.Barcode, l.Quantity, l.Price, l.PriceIncludesVat, l.VatRate)).ToList(),
        o.ShipmentCarrier, o.TrackingNumber, o.ShippedAt, o.StatusNote);

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private static string? DealerRulesClean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
