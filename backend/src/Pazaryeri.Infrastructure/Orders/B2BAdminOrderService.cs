using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Dealers;
using Pazaryeri.Application.Notifications;
using Pazaryeri.Application.Orders;
using Pazaryeri.Application.Stock;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Orders;

public record B2BAdminOrderRow(Guid Id, string OrderNumber, DateTime OrderDate, Guid? DealerId, string DealerName, string Status, decimal TotalGross, string Currency, int LineCount);

public record B2BAdminOrderPage(IReadOnlyList<B2BAdminOrderRow> Items, int Total, B2BOrderCounts Counts);

public record B2BOrderCounts(int Pending, int Preparing, int Shipped, int Delivered, int Cancelled);

/// <summary>Yönetici satırı: bayinin aynı ürünü bir önceki siparişinde hangi fiyata aldığı (iptal ve ret hariç); önceki sipariş yoksa boş.</summary>
public record B2BAdminOrderLine(Guid? ProductId, string Name, string Code, int Quantity, decimal UnitPrice, bool PriceIncludesVat, int VatRate,
    decimal? PreviousUnitPrice = null, bool PreviousPriceIncludesVat = false, DateTime? PreviousDate = null, decimal? PreviousChangePercent = null);

public record B2BAdminDealer(Guid? Id, string Name, string? TaxNumber, string? ContactName, string? Email, string? Phone);

public record B2BAdminOrderDetail(Guid Id, string OrderNumber, DateTime OrderDate, string Status, decimal TotalNet, decimal TotalGross, string Currency,
    string? ShippingAddress, string? Note, B2BAdminDealer Dealer, IReadOnlyList<B2BAdminOrderLine> Lines, string? ShipmentCarrier, string? TrackingNumber,
    DateTime? ShippedAt, DateTime? ApprovedAt, string? StatusNote, IReadOnlyList<string> Actions);

public record B2BOrderActionInput(string? Reason, string? Carrier, string? TrackingNumber);

/// <summary>Yöneticinin B2B siparişleri: liste, ayrıntı ve onay/ret/sevk/teslim/iptal geçişleri (kurallar <see cref="B2BOrderFlow"/>'da).</summary>
public class B2BAdminOrderService(AppDbContext db, IStockPoolService stock, TimeProvider time, INotifier? notifier = null)
{
    public const int PageSize = 20;

    public static OrderStatus[]? StatusesForTab(string? tab) => tab?.ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "pending" => [OrderStatus.New],
        "preparing" => [OrderStatus.Preparing],
        "shipped" => [OrderStatus.Shipped],
        "delivered" => [OrderStatus.Delivered],
        "cancelled" => [OrderStatus.Cancelled, OrderStatus.Returned],
        _ => throw new StoreException(400, "Bilinmeyen sekme."),
    };

    public async Task<B2BAdminOrderPage> ListAsync(string? tab, string? q, Guid? dealerId, int page, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        var statuses = StatusesForTab(tab);
        var all = db.Orders.AsNoTracking().Where(o => o.Channel == B2BOrderService.Channel);
        if (dealerId is { } d) all = all.Where(o => o.DealerId == d);
        var by = await all.GroupBy(o => o.Status).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        int C(params OrderStatus[] s) => s.Sum(x => by.GetValueOrDefault(x));
        var counts = new B2BOrderCounts(C(OrderStatus.New), C(OrderStatus.Preparing), C(OrderStatus.Shipped), C(OrderStatus.Delivered), C(OrderStatus.Cancelled, OrderStatus.Returned));

        var query = all;
        if (statuses is not null) query = query.Where(o => statuses.Contains(o.Status));
        if (!string.IsNullOrWhiteSpace(q)) { var t = q.Trim(); query = query.Where(o => o.OrderNumber.Contains(t) || o.CustomerName.Contains(t)); }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id).Skip((page - 1) * PageSize).Take(PageSize)
            .Select(o => new { o.Id, o.OrderNumber, o.OrderDate, o.DealerId, o.CustomerName, o.Status, o.TotalPrice, o.Currency, Lines = o.Packages.SelectMany(p => p.Lines).Count() }).ToListAsync(ct);
        return new B2BAdminOrderPage(rows.Select(o => new B2BAdminOrderRow(o.Id, o.OrderNumber, o.OrderDate, o.DealerId, o.CustomerName, o.Status.ToString(), o.TotalPrice, o.Currency ?? "TRY", o.Lines)).ToList(), total, counts);
    }

    public async Task<B2BAdminOrderDetail> GetAsync(Guid id, CancellationToken ct) => await ToDetailAsync(await LoadAsync(id, ct), ct);

    public async Task<B2BAdminOrderDetail> ActAsync(Guid id, B2BAction action, B2BOrderActionInput input, CancellationToken ct)
    {
        var order = await LoadAsync(id, ct);
        var next = B2BOrderFlow.Next(order.Status, action) ?? throw new StoreException(409, "Bu işlem siparişin şu anki durumunda yapılamaz.");

        var reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim();
        if (action == B2BAction.Reject && reason is null) throw new StoreException(400, "Reddetme gerekçesi zorunlu.");
        if (reason is { Length: > B2BOrderFlow.MaxReasonLength }) throw new StoreException(400, $"Gerekçe en fazla {B2BOrderFlow.MaxReasonLength} karakter olabilir.");
        if (action == B2BAction.Ship && B2BOrderFlow.ValidateShipment(input.Carrier, input.TrackingNumber) is { } shipError) throw new StoreException(400, shipError);

        var now = time.GetUtcNow().UtcDateTime;
        var carrier = input.Carrier?.Trim();
        var tracking = input.TrackingNumber?.Trim();
        var current = order.Status;

        // Durumu koşullu güncelleyerek sahiplen: aynı anda gelen ikinci işlem 0 satır etkiler ve reddedilir.
        var claimed = await db.Orders.Where(o => o.Id == id && o.Status == current).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Status, next).SetProperty(o => o.LastModifiedAt, now)
            .SetProperty(o => o.ApprovedAt, action == B2BAction.Approve ? now : order.ApprovedAt)
            .SetProperty(o => o.ShipmentCarrier, action == B2BAction.Ship ? carrier : order.ShipmentCarrier)
            .SetProperty(o => o.TrackingNumber, action == B2BAction.Ship ? tracking : order.TrackingNumber)
            .SetProperty(o => o.ShippedAt, action == B2BAction.Ship ? now : order.ShippedAt)
            .SetProperty(o => o.StatusNote, B2BOrderFlow.RestoresStock(action) ? reason : order.StatusNote), ct);
        if (claimed == 0) throw new StoreException(409, "Sipariş aynı anda başka bir işlemle değişti; yenileyip tekrar deneyin.");
        await db.OrderPackages.Where(p => p.OrderId == id).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.Status, next).SetProperty(p => p.LastModifiedAt, now).SetProperty(p => p.ChannelStatus, next.ToString()), ct);

        if (B2BOrderFlow.RestoresStock(action))
        {
            foreach (var line in order.Packages.SelectMany(p => p.Lines).Where(l => l.StockDeducted && !l.StockRestored))
            {
                var reference = $"{order.OrderNumber}:{line.LineId}";
                await stock.RestoreAsync(B2BOrderService.Channel, reference, "iptal:" + reference, [new StockLine(line.Barcode, line.Quantity)], ct);
                await db.OrderLines.Where(l => l.Id == line.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.StockRestored, true), ct);
            }
        }
        db.ChangeTracker.Clear();
        if (notifier is not null && order.DealerId is { } dealerId && Message(action, order.OrderNumber, reason, carrier, tracking) is var (kind, title, body))
            await notifier.NotifyDealerAsync(dealerId, kind, title, body, $"/siparis/{id}", $"b2b-{action}:{id}", ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Süreç değişikliğinde bayiye gidecek bildirim (tür, başlık, ayrıntı).</summary>
    private static (string Kind, string Title, string? Body) Message(B2BAction action, string number, string? reason, string? carrier, string? tracking) => action switch
    {
        B2BAction.Approve => (NotificationKinds.OrderApproved, $"Siparişiniz onaylandı: {number}", "Hazırlanıyor."),
        B2BAction.Reject => (NotificationKinds.OrderRejected, $"Siparişiniz reddedildi: {number}", reason),
        B2BAction.Ship => (NotificationKinds.OrderShipped, $"Siparişiniz kargoya verildi: {number}", string.Join(" · ", new[] { carrier, tracking is null ? null : $"Takip no: {tracking}" }.Where(x => !string.IsNullOrEmpty(x)))),
        B2BAction.Deliver => (NotificationKinds.OrderDelivered, $"Siparişiniz teslim edildi: {number}", null),
        _ => (NotificationKinds.OrderCancelled, $"Siparişiniz iptal edildi: {number}", reason),
    };

    private async Task<Order> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Orders.AsNoTracking().Include(o => o.Packages).ThenInclude(p => p.Lines)
            .FirstOrDefaultAsync(o => o.Id == id && o.Channel == B2BOrderService.Channel, ct) ?? throw new StoreException(404, "Sipariş bulunamadı.");

    private sealed record PreviousPrice(string Barcode, decimal Price, bool IncludesVat, int VatRate, DateTime Date);

    /// <summary>Aynı bayinin bu siparişten önceki, iptal/iade olmayan B2B siparişlerindeki her barkodun en son birim fiyatı.</summary>
    private async Task<Dictionary<string, PreviousPrice>> PreviousPricesAsync(Order o, CancellationToken ct)
    {
        if (o.DealerId is not { } dealerId) return [];
        var codes = o.Packages.SelectMany(p => p.Lines).Select(l => l.Barcode).Distinct().ToList();
        var rows = await db.Orders.AsNoTracking()
            .Where(x => x.Channel == B2BOrderService.Channel && x.DealerId == dealerId && x.Id != o.Id && x.OrderDate < o.OrderDate
                && x.Status != OrderStatus.Cancelled && x.Status != OrderStatus.Returned)
            .SelectMany(x => x.Packages.SelectMany(p => p.Lines).Where(l => codes.Contains(l.Barcode)),
                (x, l) => new { l.Barcode, l.Price, l.PriceIncludesVat, l.VatRate, x.OrderDate }).ToListAsync(ct);
        return rows.GroupBy(r => r.Barcode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.OrderDate).Select(r => new PreviousPrice(r.Barcode, r.Price, r.PriceIncludesVat, r.VatRate, r.OrderDate)).First(), StringComparer.OrdinalIgnoreCase);
    }

    private static B2BAdminOrderLine WithPrevious(OrderLine l, Dictionary<string, PreviousPrice> previous)
    {
        var line = new B2BAdminOrderLine(l.ProductId, l.Name, l.Barcode, l.Quantity, l.Price, l.PriceIncludesVat, l.VatRate);
        if (!previous.TryGetValue(l.Barcode, out var p)) return line;
        return line with { PreviousUnitPrice = p.Price, PreviousPriceIncludesVat = p.IncludesVat, PreviousDate = p.Date, PreviousChangePercent = ChangePercent(l.Price, l.PriceIncludesVat, l.VatRate, p) };
    }

    /// <summary>Fiyatlar KDV dahil/hariç farklı kurulduysa karşılaştırma KDV dahil birim fiyat üzerinden yapılır.</summary>
    private static decimal? ChangePercent(decimal price, bool includesVat, int vatRate, PreviousPrice p)
    {
        static decimal Gross(decimal amount, bool incl, int vat) => incl ? amount : amount * (1 + vat / 100m);
        var before = Gross(p.Price, p.IncludesVat, p.VatRate);
        return before <= 0 ? null : Math.Round((Gross(price, includesVat, vatRate) - before) / before * 100m, 1);
    }

    private async Task<B2BAdminOrderDetail> ToDetailAsync(Order o, CancellationToken ct)
    {
        var dealer = o.DealerId is { } did
            ? await db.Dealers.AsNoTracking().Where(d => d.Id == did).Select(d => new B2BAdminDealer(d.Id, d.Name, d.TaxNumber, d.ContactName, d.Email, d.Phone)).FirstOrDefaultAsync(ct)
            : null;
        var previous = await PreviousPricesAsync(o, ct);
        return new B2BAdminOrderDetail(o.Id, o.OrderNumber, o.OrderDate, o.Status.ToString(), o.TotalNet ?? o.TotalPrice, o.TotalPrice, o.Currency ?? "TRY", o.ShippingAddress, o.Note,
            dealer ?? new B2BAdminDealer(null, o.CustomerName, null, null, null, null),
            o.Packages.SelectMany(p => p.Lines).OrderBy(l => int.TryParse(l.LineId, out var n) ? n : 0)
                .Select(l => WithPrevious(l, previous)).ToList(),
            o.ShipmentCarrier, o.TrackingNumber, o.ShippedAt, o.ApprovedAt, o.StatusNote,
            B2BOrderFlow.Allowed(o.Status).Select(a => a.ToString()).ToList());
    }
}

public record PaymentRow(Guid Id, decimal Amount, DateTime PaidAt, string? Note);

public record DealerAccountView(Guid DealerId, string DealerName, int PaymentTermDays, AccountSummary Summary, IReadOnlyList<PaymentRow> Payments);

public record PaymentInput(decimal Amount, DateTime? PaidAt, string? Note);

/// <summary>Bayi cari hesabı: siparişler (iptal/iade hariç) ve elle girilen ödemeler; vade günü bayi başına tutulur.</summary>
public class DealerAccountService(AppDbContext db, TimeProvider time)
{
    public async Task<DealerAccountView> GetAsync(Guid dealerId, CancellationToken ct)
    {
        var dealer = await db.Dealers.AsNoTracking().Where(d => d.Id == dealerId).Select(d => new { d.Name, d.PaymentTermDays }).FirstOrDefaultAsync(ct)
            ?? throw new StoreException(404, "Bayi bulunamadı.");
        var orders = await db.Orders.AsNoTracking().Where(o => o.DealerId == dealerId && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned)
            .Select(o => new AccountOrder(o.OrderDate, o.TotalPrice)).ToListAsync(ct);
        var payments = await db.DealerPayments.AsNoTracking().Where(p => p.DealerId == dealerId).OrderByDescending(p => p.PaidAt).ThenByDescending(p => p.Id)
            .Select(p => new PaymentRow(p.Id, p.Amount, p.PaidAt, p.Note)).ToListAsync(ct);
        var summary = DealerAccount.Compute(orders, payments.Select(p => p.Amount), dealer.PaymentTermDays, time.GetUtcNow().UtcDateTime);
        return new DealerAccountView(dealerId, dealer.Name, dealer.PaymentTermDays, summary, payments);
    }

    public async Task<DealerAccountView> AddPaymentAsync(Guid dealerId, PaymentInput input, CancellationToken ct)
    {
        if (DealerAccount.ValidatePayment(input.Amount, input.Note) is { } error) throw new StoreException(400, error);
        if (!await db.Dealers.AnyAsync(d => d.Id == dealerId, ct)) throw new StoreException(404, "Bayi bulunamadı.");
        var paidAt = input.PaidAt ?? time.GetUtcNow().UtcDateTime;
        if (paidAt > time.GetUtcNow().UtcDateTime.AddDays(1)) throw new StoreException(400, "Ödeme tarihi ileri bir tarih olamaz.");
        db.DealerPayments.Add(new DealerPayment { DealerId = dealerId, Amount = decimal.Round(input.Amount, 2), PaidAt = paidAt, Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim() });
        await db.SaveChangesAsync(ct);
        return await GetAsync(dealerId, ct);
    }

    public async Task<DealerAccountView> RemovePaymentAsync(Guid dealerId, Guid paymentId, CancellationToken ct)
    {
        var removed = await db.DealerPayments.Where(p => p.Id == paymentId && p.DealerId == dealerId).ExecuteDeleteAsync(ct);
        if (removed == 0) throw new StoreException(404, "Ödeme kaydı bulunamadı.");
        return await GetAsync(dealerId, ct);
    }

    public async Task<DealerAccountView> SetTermAsync(Guid dealerId, int termDays, CancellationToken ct)
    {
        if (DealerAccount.ValidateTerm(termDays) is { } error) throw new StoreException(400, error);
        var updated = await db.Dealers.Where(d => d.Id == dealerId).ExecuteUpdateAsync(s => s.SetProperty(d => d.PaymentTermDays, termDays), ct);
        if (updated == 0) throw new StoreException(404, "Bayi bulunamadı.");
        return await GetAsync(dealerId, ct);
    }
}
