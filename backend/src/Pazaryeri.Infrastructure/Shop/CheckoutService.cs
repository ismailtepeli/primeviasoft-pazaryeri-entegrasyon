using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Notifications;
using Pazaryeri.Application.Shop;
using Pazaryeri.Application.Stock;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Orders;
using Pazaryeri.Infrastructure.Persistence;
using Quartz;

namespace Pazaryeri.Infrastructure.Shop;

/// <summary>
/// B2C sepet fiyatlama ve sipariş. Sepet istemcide tutulur; sunucu yalnızca (ürün, adet) alır ve fiyat, stok, kargo ve ödeme
/// seçeneklerini kendisi hesaplar (istemciden fiyat kabul edilmez). Stok B2B ile aynı atomik düşümle (ya hep ya hiç) ayrılır;
/// kart ödemesi süresinde tamamlanmazsa sipariş iptal edilip stok geri eklenir.
/// </summary>
public class CheckoutService(AppDbContext db, ShopCatalogService shop, IStockPoolService stock, IPaymentProvider payments, TimeProvider time, INotifier? notifier = null)
{
    public const string Channel = CustomerAccountService.OrderChannel;
    public const int MaxLines = 50;
    public const int MaxQuantity = 99;
    public const int CardMinutes = 30;
    public const string Currency = "TRY";

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    // ---------- ayarlar ----------

    public async Task<B2CSettings> SettingsAsync(CancellationToken ct) => await db.B2CSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new B2CSettings();

    private B2CSettingsDto ToDto(B2CSettings s) => new(s.ShippingFee, s.FreeShippingThreshold, s.BankTransferEnabled, s.BankTransferInfo, s.CashOnDeliveryEnabled, s.CashOnDeliveryFee, s.CardEnabled, payments.Available);

    public async Task<B2CSettingsDto> GetSettingsAsync(CancellationToken ct) => ToDto(await SettingsAsync(ct));

    public async Task<B2CSettingsDto> SaveSettingsAsync(B2CSettingsInput input, CancellationToken ct)
    {
        if (input.ShippingFee < 0 || input.ShippingFee > 100_000) throw new StoreException(400, "Kargo ücreti 0 ile 100.000 arasında olmalı.");
        if (input.FreeShippingThreshold is { } t && (t <= 0 || t > 100_000_000)) throw new StoreException(400, "Ücretsiz kargo tutarı sıfırdan büyük olmalı; kapatmak için boş bırakın.");
        if (input.CashOnDeliveryFee < 0 || input.CashOnDeliveryFee > 100_000) throw new StoreException(400, "Kapıda ödeme ücreti 0 ile 100.000 arasında olmalı.");
        var info = string.IsNullOrWhiteSpace(input.BankTransferInfo) ? null : input.BankTransferInfo.Trim();
        if (info is not null && info.Length > B2CSettings.MaxInfoLength) throw new StoreException(400, $"Havale bilgisi en fazla {B2CSettings.MaxInfoLength} karakter olabilir.");
        if (input.BankTransferEnabled && info is null) throw new StoreException(400, "Havale açıksa müşterinin göreceği banka/IBAN bilgisini yazın.");
        if (input.CardEnabled && !payments.Available) throw new StoreException(409, "Kart ödemesi için bir ödeme sağlayıcısı yapılandırılmamış.");

        var s = await db.B2CSettings.FirstOrDefaultAsync(ct);
        if (s is null) { s = new B2CSettings(); db.B2CSettings.Add(s); }
        s.ShippingFee = decimal.Round(input.ShippingFee, 2);
        s.FreeShippingThreshold = input.FreeShippingThreshold is { } th ? decimal.Round(th, 2) : null;
        s.BankTransferEnabled = input.BankTransferEnabled; s.BankTransferInfo = info;
        s.CashOnDeliveryEnabled = input.CashOnDeliveryEnabled; s.CashOnDeliveryFee = decimal.Round(input.CashOnDeliveryFee, 2);
        s.CardEnabled = input.CardEnabled;
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    private IReadOnlyList<PaymentOption> Options(B2CSettings s)
    {
        var list = new List<PaymentOption>();
        if (s.BankTransferEnabled) list.Add(new PaymentOption(nameof(PaymentMethod.BankTransfer), "Havale / EFT", 0, s.BankTransferInfo));
        if (s.CashOnDeliveryEnabled) list.Add(new PaymentOption(nameof(PaymentMethod.CashOnDelivery), "Kapıda ödeme", s.CashOnDeliveryFee, null));
        if (s.CardEnabled && payments.Available) list.Add(new PaymentOption(nameof(PaymentMethod.Card), "Kredi / banka kartı", 0, null));
        return list;
    }

    // ---------- sepet ----------

    private static List<CartLineInput> Merge(IReadOnlyList<CartLineInput>? lines)
    {
        if (lines is null || lines.Count == 0) throw new StoreException(400, "Sepetiniz boş.");
        var merged = lines.GroupBy(l => l.ProductId).Select(g => new CartLineInput(g.Key, (int)Math.Min(g.Sum(x => (long)Math.Clamp(x.Quantity, 0, MaxQuantity)), MaxQuantity))).Where(l => l.Quantity > 0).ToList();
        if (merged.Count == 0) throw new StoreException(400, "Sepetiniz boş.");
        if (merged.Count > MaxLines) throw new StoreException(400, $"Sepette en fazla {MaxLines} farklı ürün olabilir.");
        return merged;
    }

    /// <summary>Herkese açık fiyatlama; kiracı slug çözümünden gelir.</summary>
    public async Task<CartQuote> QuoteAsync(Guid tenantId, IReadOnlyList<CartLineInput>? input, CancellationToken ct)
    {
        var lines = Merge(input);
        using var _ = AmbientTenant.Use(tenantId);
        var products = (await shop.CartProductsAsync(tenantId, lines.Select(l => l.ProductId).ToList(), ct)).ToDictionary(p => p.Id);
        var settings = await SettingsAsync(ct);
        return BuildQuote(lines, products, settings, null);
    }

    private CartQuote BuildQuote(List<CartLineInput> lines, Dictionary<Guid, ShopCartProduct> products, B2CSettings settings, PaymentMethod? method)
    {
        var rows = new List<CartQuoteLine>();
        decimal subtotal = 0;
        foreach (var l in lines)
        {
            if (!products.TryGetValue(l.ProductId, out var p))
            {
                rows.Add(new CartQuoteLine(l.ProductId, "Ürün", null, l.Quantity, 0, null, 0, new CartQuoteIssue("unavailable", "Bu ürün artık satışta değil.")));
                continue;
            }
            CartQuoteIssue? issue = null;
            if (p.Available <= 0) issue = new CartQuoteIssue("stock", "Stok tükendi.", 0);
            else if (l.Quantity > p.Available) issue = new CartQuoteIssue("stock", $"Stokta yalnızca {p.Available} adet var.", p.Available);
            var total = decimal.Round(p.UnitPrice * l.Quantity, 2);
            if (issue is null) subtotal += total;
            rows.Add(new CartQuoteLine(p.Id, p.Name, p.CoverUrl, l.Quantity, p.UnitPrice, p.ListPrice, total, issue));
        }
        var free = settings.FreeShippingThreshold is { } t && subtotal >= t;
        var shipping = free || subtotal == 0 ? 0 : settings.ShippingFee;
        decimal? remaining = settings.FreeShippingThreshold is { } th && !free && subtotal > 0 ? decimal.Round(th - subtotal, 2) : null;
        var fee = method == PaymentMethod.CashOnDelivery ? settings.CashOnDeliveryFee : 0;
        return new CartQuote(rows, subtotal, shipping, remaining, subtotal + shipping + fee, Currency, rows.Any(r => r.Issue is not null), Options(settings));
    }

    // ---------- sipariş ----------

    /// <summary>Oturumdaki müşterinin siparişini oluşturur ve stoğu ayırır. Kiracı müşterinin token'ından gelir.</summary>
    public async Task<CheckoutResult> CheckoutAsync(Guid customerId, CheckoutRequest request, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().Include(c => c.Addresses).FirstOrDefaultAsync(c => c.Id == customerId && c.IsActive, ct)
            ?? throw new StoreException(404, "Hesap bulunamadı.");
        var address = customer.Addresses.FirstOrDefault(a => a.Id == request.AddressId) ?? throw new StoreException(400, "Teslimat adresi seçin.");
        if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, true, out var method) || !Enum.IsDefined(method)) throw new StoreException(400, "Ödeme yöntemi seçin.");
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note?.Length > 500) throw new StoreException(400, "Sipariş notu en fazla 500 karakter olabilir.");

        var lines = Merge(request.Lines);
        var products = (await shop.CartProductsAsync(customer.TenantId, lines.Select(l => l.ProductId).ToList(), ct)).ToDictionary(p => p.Id);
        var settings = await SettingsAsync(ct);
        if (!Options(settings).Any(o => o.Method == method.ToString())) throw new StoreException(409, "Bu ödeme yöntemi şu an kullanılamıyor.");
        var quote = BuildQuote(lines, products, settings, method);
        if (quote.HasIssues)
            throw new StoreException(409, "Sepetinizdeki bazı ürünler siparişe uygun değil.", new { issues = quote.Lines.Where(l => l.Issue is not null).Select(l => new { l.ProductId, l.Name, l.Issue }).ToList() });

        var number = await NewNumberAsync(ct);
        var barcodes = lines.Select(l => products[l.ProductId].Barcode).ToList();
        var safeties = await db.Products.AsNoTracking().Where(p => barcodes.Contains(p.Barcode)).ToDictionaryAsync(p => p.Barcode, p => Math.Max(0, p.SafetyStock ?? 0), StringComparer.OrdinalIgnoreCase, ct);
        var references = lines.Select((_, i) => $"{number}:{i + 1}").ToList();
        var reserve = await stock.DeductAllOrNothingAsync(Channel,
            lines.Select((l, i) => new StockReserveLine(products[l.ProductId].Barcode, l.Quantity, references[i], safeties.GetValueOrDefault(products[l.ProductId].Barcode))).ToList(), ct);
        if (!reserve.Success)
        {
            var issues = reserve.Shortages.Select(sh => new { sh.Barcode, text = sh.Available > 0 ? $"Stokta yalnızca {sh.Available} adet kaldı." : "Stok tükendi.", sh.Available }).ToList();
            throw new StoreException(409, "Stok yetmediği için sipariş verilemedi; sepetinizi güncelleyin.", new { issues });
        }

        var now = Now();
        var fee = method == PaymentMethod.CashOnDelivery ? settings.CashOnDeliveryFee : 0;
        decimal net = 0;
        var orderLines = new List<OrderLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            var p = products[lines[i].ProductId];
            net += decimal.Round(p.UnitPrice / (1 + p.VatRate / 100m), 2) * lines[i].Quantity;
            orderLines.Add(new OrderLine
            {
                LineId = (i + 1).ToString(), Barcode = p.Barcode, Name = p.Name, Quantity = lines[i].Quantity, Price = p.UnitPrice, PriceIncludesVat = true, VatRate = p.VatRate,
                ProductId = p.Id, StockDeducted = reserve.Applied,
            });
        }
        net += decimal.Round((quote.ShippingFee + fee) / 1.2m, 2);
        var paymentStatus = method == PaymentMethod.CashOnDelivery ? PaymentStatus.OnDelivery : PaymentStatus.Pending;
        var order = new Order
        {
            Channel = Channel, OrderNumber = number, Status = OrderStatus.New, OrderDate = now, LastModifiedAt = now, CustomerName = customer.FullName, CustomerId = customer.Id,
            TotalPrice = quote.Total, TotalNet = decimal.Round(net, 2), Currency = Currency, Note = note, ShippingFee = quote.ShippingFee, PaymentFee = fee > 0 ? fee : null,
            ShippingAddress = FormatAddress(address), PaymentMethod = method, PaymentStatus = paymentStatus,
            PaymentExpiresAt = method == PaymentMethod.Card ? now.AddMinutes(CardMinutes) : null,
            Packages = [new OrderPackage { Channel = Channel, PackageId = number, ChannelStatus = "Created", Status = OrderStatus.New, LastModifiedAt = now, TotalPrice = quote.Total, Lines = orderLines }],
        };
        try
        {
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (reserve.Applied)
                for (var i = 0; i < lines.Count; i++)
                    await stock.RestoreAsync(Channel, references[i], "iptal:" + references[i], [new StockLine(products[lines[i].ProductId].Barcode, lines[i].Quantity)], ct);
            throw new StoreException(409, "Sipariş kaydedilemedi; stok geri eklendi, tekrar deneyin.");
        }

        string? url = null;
        if (method == PaymentMethod.Card) url = await payments.StartAsync(order.Id, number, quote.Total, Currency, ct);
        if (notifier is not null)
            await notifier.NotifyAdminAsync(NotificationKinds.B2COrderNew, $"Yeni B2C siparişi {number}", $"{customer.FullName} · {orderLines.Count} kalem · {quote.Total:0.00} {Currency} · {MethodText(method)}", "/siparisler", $"b2c-new:{order.Id}", ct);
        return new CheckoutResult(order.Id, number, method.ToString(), paymentStatus.ToString(), quote.Total, method == PaymentMethod.BankTransfer ? settings.BankTransferInfo : null, url);
    }

    public static string MethodText(PaymentMethod m) => m switch { PaymentMethod.BankTransfer => "Havale/EFT", PaymentMethod.CashOnDelivery => "Kapıda ödeme", _ => "Kart" };

    private static string FormatAddress(CustomerAddress a) =>
        $"{a.FullName}, {a.Phone}\n{a.AddressLine}\n{a.District} / {a.City}{(string.IsNullOrWhiteSpace(a.PostalCode) ? "" : " " + a.PostalCode)}";

    private async Task<string> NewNumberAsync(CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            var number = $"B2C-{Now():yyMMdd}-{Random.Shared.Next(10000, 99999)}";
            if (!await db.Orders.AnyAsync(o => o.Channel == Channel && o.OrderNumber == number, ct)) return number;
        }
        throw new StoreException(500, "Sipariş numarası üretilemedi.");
    }

    // ---------- ödeme sonucu, iptal, süre dolumu ----------

    private Task<Order?> MineAsync(Guid customerId, Guid orderId, CancellationToken ct) =>
        db.Orders.Include(o => o.Packages).ThenInclude(p => p.Lines).FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == customerId && o.Channel == Channel, ct);

    /// <summary>
    /// Kart ödemesinin sonucunu işler (gerçek sağlayıcıda webhook/geri dönüş doğrulamasından sonra çağrılır). Yalnızca bekleyen kart ödemesi
    /// değişir; tekrar çağrı etkisizdir. Başarısızlıkta sipariş iptal edilir ve stok geri eklenir.
    /// </summary>
    public async Task<string> ConfirmCardAsync(Guid customerId, Guid orderId, bool success, CancellationToken ct)
    {
        var order = await MineAsync(customerId, orderId, ct) ?? throw new StoreException(404, "Sipariş bulunamadı.");
        if (order.PaymentMethod != PaymentMethod.Card) throw new StoreException(409, "Bu sipariş kart ile ödenmiyor.");
        if (order.PaymentStatus != PaymentStatus.Pending) return order.PaymentStatus?.ToString() ?? "";
        if (success)
        {
            var now = Now();
            var paid = await db.Orders.Where(o => o.Id == orderId && o.PaymentStatus == PaymentStatus.Pending && o.Status == OrderStatus.New)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentStatus, PaymentStatus.Paid).SetProperty(o => o.PaymentExpiresAt, (DateTime?)null).SetProperty(o => o.LastModifiedAt, now), ct);
            if (paid == 0) throw new StoreException(409, "Sipariş artık ödenemez; süresi dolmuş ya da iptal edilmiş.");
            return nameof(PaymentStatus.Paid);
        }
        await CancelOrderAsync(order, PaymentStatus.Failed, ct);
        return nameof(PaymentStatus.Failed);
    }

    /// <summary>Müşteri, henüz hazırlanmaya başlamamış ve ücreti alınmamış siparişini iptal eder; stok geri eklenir.</summary>
    public async Task CancelAsync(Guid customerId, Guid orderId, CancellationToken ct)
    {
        var order = await MineAsync(customerId, orderId, ct) ?? throw new StoreException(404, "Sipariş bulunamadı.");
        if (order.Status == OrderStatus.Cancelled) return;
        if (!CanCancel(order)) throw new StoreException(409, order.PaymentStatus == PaymentStatus.Paid ? "Ödemesi alınmış sipariş için iptal talebini firmayla iletişime geçerek iletin." : "Sipariş hazırlanmaya başladı; iptal için firmayla iletişime geçin.");
        var number = order.OrderNumber; var name = order.CustomerName; var id = order.Id;
        await CancelOrderAsync(order, order.PaymentStatus == PaymentStatus.Pending ? PaymentStatus.Failed : order.PaymentStatus, ct);
        if (notifier is not null)
            await notifier.NotifyAdminAsync(NotificationKinds.B2COrderCancelled, $"Müşteri siparişi iptal etti: {number}", name, "/siparisler", $"b2c-cancel:{id}", ct);
    }

    public static bool CanCancel(Order o) => o.Status == OrderStatus.New && o.PaymentStatus != PaymentStatus.Paid;

    private async Task CancelOrderAsync(Order order, PaymentStatus? payment, CancellationToken ct)
    {
        foreach (var line in order.Packages.SelectMany(p => p.Lines).Where(l => l.StockDeducted && !l.StockRestored))
        {
            var reference = $"{order.OrderNumber}:{line.LineId}";
            await stock.RestoreAsync(Channel, reference, "iptal:" + reference, [new StockLine(line.Barcode, line.Quantity)], ct);
            await db.OrderLines.Where(l => l.Id == line.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.StockRestored, true), ct);
        }
        var id = order.Id;
        db.ChangeTracker.Clear();
        var now = Now();
        await db.OrderPackages.Where(p => p.OrderId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, OrderStatus.Cancelled).SetProperty(p => p.ChannelStatus, "Cancelled").SetProperty(p => p.LastModifiedAt, now), ct);
        await db.Orders.Where(o => o.Id == id && o.Status == OrderStatus.New)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Cancelled).SetProperty(o => o.PaymentStatus, payment).SetProperty(o => o.PaymentExpiresAt, (DateTime?)null).SetProperty(o => o.LastModifiedAt, now), ct);
    }

    /// <summary>Süresi dolmuş bekleyen kart siparişlerini iptal eder ve stoklarını geri ekler; iptal edilen sayıyı döner. Kiracı bağlamı kurulmuş olmalı.</summary>
    public async Task<int> ExpireCardOrdersAsync(CancellationToken ct)
    {
        var now = Now();
        var ids = await db.Orders.AsNoTracking().Where(o => o.Channel == Channel && o.PaymentMethod == PaymentMethod.Card && o.PaymentStatus == PaymentStatus.Pending
            && o.Status == OrderStatus.New && o.PaymentExpiresAt != null && o.PaymentExpiresAt <= now).Select(o => o.Id).Take(200).ToListAsync(ct);
        var cancelled = 0;
        foreach (var id in ids)
        {
            db.ChangeTracker.Clear();
            var order = await db.Orders.Include(o => o.Packages).ThenInclude(p => p.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order is { PaymentStatus: PaymentStatus.Pending, Status: OrderStatus.New }) { await CancelOrderAsync(order, PaymentStatus.Expired, ct); cancelled++; }
        }
        return cancelled;
    }
}

/// <summary>Bekleyen kart siparişlerinin süresini dolduran periyodik iş (5 dk).</summary>
public class B2CPaymentExpiryJob(IServiceScopeFactory scopes, ILogger<B2CPaymentExpiryJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken ct)
    {
        List<Guid> tenants;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
            tenants = await db.Orders.IgnoreQueryFilters().AsNoTracking().Where(o => o.Channel == CheckoutService.Channel && o.PaymentMethod == PaymentMethod.Card
                && o.PaymentStatus == PaymentStatus.Pending && o.Status == OrderStatus.New && o.PaymentExpiresAt != null && o.PaymentExpiresAt <= now).Select(o => o.TenantId).Distinct().ToListAsync(ct);
        }
        foreach (var tenantId in tenants)
        {
            try
            {
                using var _ = AmbientTenant.Use(tenantId);
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CheckoutService>().ExpireCardOrdersAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "B2C ödeme süresi dolumu işlenemedi (kiracı {Tenant}).", tenantId);
            }
        }
    }
}
