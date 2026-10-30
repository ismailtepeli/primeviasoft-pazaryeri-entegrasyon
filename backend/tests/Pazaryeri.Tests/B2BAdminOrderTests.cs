using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Dealers;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Orders;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Orders;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Pricing;
using Pazaryeri.Infrastructure.Stock;

namespace Pazaryeri.Tests;

public class B2BAdminOrderTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_B2BAdmin;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private sealed class RecordingQueue(AppDbContext db) : IJobQueue
    {
        public async Task<SyncJob> EnqueueAsync(SyncJobType type, string channel, string? batchId = null, CancellationToken ct = default, string? payload = null)
        {
            var job = new SyncJob { Type = type, Channel = channel };
            db.SyncJobs.Add(job);
            await db.SaveChangesAsync(ct);
            return job;
        }
    }

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }

    private static AppDbContext NewDb(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    private static B2BAdminOrderService Admin(AppDbContext db) => new(db, new StockPoolService(db, new RecordingQueue(db)), TimeProvider.System);

    private static B2BOrderService Store(AppDbContext db) =>
        new(db, new StoreCatalog(db), new DealerPriceService(db), new StockPoolService(db, new RecordingQueue(db)), TimeProvider.System);

    public async Task InitializeAsync()
    {
        await using var db = NewDb(null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record World(Guid Tenant, Guid DealerId, Guid OtherDealerId, Guid Product);

    private static async Task<World> SeedAsync(string name, int stock = 100)
    {
        await using var root = NewDb(null);
        var tenant = new Tenant { Name = name };
        root.Tenants.Add(tenant);
        await root.SaveChangesAsync();
        await using var db = NewDb(tenant.Id);
        var a = new Dealer { Name = "Mavi", TaxNumber = "1000000001", Email = "m@m.com", Status = DealerStatus.Active, Address = "İstanbul" };
        var b = new Dealer { Name = "Diğer", TaxNumber = "1000000002", Email = "d@d.com", Status = DealerStatus.Active, Address = "Ankara" };
        var p = new Product { Name = "Kalem", Barcode = "PEN", Price = 12m, VatRate = 20, Stock = stock, B2BVisible = true };
        db.AddRange(a, b, p);
        await db.SaveChangesAsync();
        return new World(tenant.Id, a.Id, b.Id, p.Id);
    }

    private static async Task<Guid> PlaceOrderAsync(World w, Guid dealer, int qty = 10)
    {
        await using var db = NewDb(w.Tenant);
        var s = Store(db);
        await s.AddAsync(dealer, w.Product, qty, default);
        return (await s.CheckoutAsync(dealer, new CheckoutInput(null, null, false), default)).Id;
    }

    private static async Task<int> StockAsync(Guid tenant)
    {
        await using var db = NewDb(tenant);
        return await db.Products.Select(p => p.Stock).SingleAsync();
    }

    private static async Task<StoreException> FailAsync(Func<Task> action) => await Assert.ThrowsAsync<StoreException>(action);

    private static readonly B2BOrderActionInput None = new(null, null, null);

    // --- saf kurallar ---

    [Theory]
    [InlineData(OrderStatus.New, B2BAction.Approve, OrderStatus.Preparing)]
    [InlineData(OrderStatus.New, B2BAction.Reject, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.New, B2BAction.Cancel, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Preparing, B2BAction.Ship, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Preparing, B2BAction.Cancel, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Shipped, B2BAction.Deliver, OrderStatus.Delivered)]
    public void Gecerli_gecisler(OrderStatus from, B2BAction action, OrderStatus to) => Assert.Equal(to, B2BOrderFlow.Next(from, action));

    [Theory]
    [InlineData(OrderStatus.New, B2BAction.Ship)]
    [InlineData(OrderStatus.New, B2BAction.Deliver)]
    [InlineData(OrderStatus.Preparing, B2BAction.Approve)]
    [InlineData(OrderStatus.Preparing, B2BAction.Reject)]
    [InlineData(OrderStatus.Shipped, B2BAction.Cancel)]
    [InlineData(OrderStatus.Shipped, B2BAction.Ship)]
    [InlineData(OrderStatus.Delivered, B2BAction.Cancel)]
    [InlineData(OrderStatus.Cancelled, B2BAction.Approve)]
    [InlineData(OrderStatus.Returned, B2BAction.Deliver)]
    public void Gecersiz_gecisler_reddedilir(OrderStatus from, B2BAction action) => Assert.Null(B2BOrderFlow.Next(from, action));

    [Fact]
    public void Izinli_islemler_ve_stok_iadesi_ve_sevk_dogrulamasi()
    {
        Assert.Equal([B2BAction.Approve, B2BAction.Reject, B2BAction.Cancel], B2BOrderFlow.Allowed(OrderStatus.New));
        Assert.Equal([B2BAction.Deliver], B2BOrderFlow.Allowed(OrderStatus.Shipped));
        Assert.Empty(B2BOrderFlow.Allowed(OrderStatus.Delivered));
        Assert.True(B2BOrderFlow.RestoresStock(B2BAction.Reject) && B2BOrderFlow.RestoresStock(B2BAction.Cancel));
        Assert.False(B2BOrderFlow.RestoresStock(B2BAction.Ship));
        Assert.Null(B2BOrderFlow.ValidateShipment("Yurtiçi", "TR123"));
        Assert.NotNull(B2BOrderFlow.ValidateShipment(" ", "TR123"));
        Assert.NotNull(B2BOrderFlow.ValidateShipment("Yurtiçi", ""));
        Assert.NotNull(B2BOrderFlow.ValidateShipment(new string('x', 101), "1"));
    }

    [Fact]
    public void Cari_odemeleri_en_eski_siparisten_kapatir_ve_vadesi_gecenleri_bulur()
    {
        var now = new DateTime(2026, 6, 30);
        var orders = new[] { new AccountOrder(new DateTime(2026, 5, 1), 100m), new AccountOrder(new DateTime(2026, 6, 1), 200m), new AccountOrder(new DateTime(2026, 6, 25), 50m) };

        var none = DealerAccount.Compute(orders, [], 30, now);
        Assert.Equal((350m, 0m, 350m, 100m, new DateTime(2026, 5, 1)), (none.TotalOrders, none.TotalPayments, none.Balance, none.Overdue, none.OldestUnpaidDate!.Value)); // 1 Mayıs + 30 gün < 30 Haz; 1 Haz + 30 = 1 Tem > 30 Haz

        var partial = DealerAccount.Compute(orders, [150m], 30, now); // ilk sipariş kapanır, ikincisinden 50 kapanır
        Assert.Equal((200m, 0m, new DateTime(2026, 6, 1)), (partial.Balance, partial.Overdue, partial.OldestUnpaidDate!.Value));

        var zeroTerm = DealerAccount.Compute(orders, [150m], 0, now);
        Assert.Equal(200m, zeroTerm.Overdue);

        var overpaid = DealerAccount.Compute(orders, [400m], 0, now);
        Assert.Equal((-50m, 0m, null), (overpaid.Balance, overpaid.Overdue, overpaid.OldestUnpaidDate)); // bayi alacaklı

        Assert.Equal(new AccountSummary(0, 0, 0, 0, null), DealerAccount.Compute([], [], 0, now));
    }

    [Fact]
    public void Odeme_ve_vade_dogrulamalari()
    {
        Assert.Null(DealerAccount.ValidatePayment(10m, null));
        Assert.NotNull(DealerAccount.ValidatePayment(0m, null));
        Assert.NotNull(DealerAccount.ValidatePayment(-1m, null));
        Assert.NotNull(DealerAccount.ValidatePayment(200_000_000m, null));
        Assert.NotNull(DealerAccount.ValidatePayment(10m, new string('x', 301)));
        Assert.Null(DealerAccount.ValidateTerm(0));
        Assert.NotNull(DealerAccount.ValidateTerm(-1));
        Assert.NotNull(DealerAccount.ValidateTerm(366));
    }

    // --- yönetici akışı ---

    [Fact]
    public async Task Onay_sevk_teslim_akisi_sevk_bilgisi_bayiye_gorunur()
    {
        var w = await SeedAsync("Akis");
        var id = await PlaceOrderAsync(w, w.DealerId);
        await using var db = NewDb(w.Tenant);
        var admin = Admin(db);

        var pending = await admin.GetAsync(id, default);
        Assert.Equal(("New", "Mavi", true), (pending.Status, pending.Dealer.Name, pending.Actions.Contains("Approve")));

        var approved = await admin.ActAsync(id, B2BAction.Approve, None, default);
        Assert.Equal("Preparing", approved.Status);
        Assert.NotNull(approved.ApprovedAt);
        Assert.Equal(["Ship", "Cancel"], approved.Actions);

        Assert.Equal(400, (await FailAsync(() => admin.ActAsync(id, B2BAction.Ship, new(null, "Yurtiçi", null), default))).Status); // takip no yok
        var shipped = await admin.ActAsync(id, B2BAction.Ship, new(null, " Yurtiçi Kargo ", "TR12345"), default);
        Assert.Equal(("Shipped", "Yurtiçi Kargo", "TR12345"), (shipped.Status, shipped.ShipmentCarrier, shipped.TrackingNumber));
        Assert.NotNull(shipped.ShippedAt);

        var delivered = await admin.ActAsync(id, B2BAction.Deliver, None, default);
        Assert.Equal(("Delivered", 0), (delivered.Status, delivered.Actions.Count));

        // Bayi tarafı sevk bilgisini görür.
        await using var storeDb = NewDb(w.Tenant);
        var dealerView = await Store(storeDb).OrderAsync(w.DealerId, id, default);
        Assert.Equal(("Delivered", "Yurtiçi Kargo", "TR12345", false), (dealerView.Status, dealerView.ShipmentCarrier, dealerView.TrackingNumber, dealerView.CanCancel));

        // Paket durumu da senkron.
        await using var check = NewDb(w.Tenant);
        Assert.All(await check.OrderPackages.ToListAsync(), p => Assert.Equal(OrderStatus.Delivered, p.Status));
    }

    [Fact]
    public async Task Gecersiz_gecis_409_olur_sevk_edilmis_siparis_iptal_edilemez()
    {
        var w = await SeedAsync("Gecersiz");
        var id = await PlaceOrderAsync(w, w.DealerId);
        await using var db = NewDb(w.Tenant);
        var admin = Admin(db);
        Assert.Equal(409, (await FailAsync(() => admin.ActAsync(id, B2BAction.Ship, new(null, "A", "1"), default))).Status);   // önce onay
        Assert.Equal(409, (await FailAsync(() => admin.ActAsync(id, B2BAction.Deliver, None, default))).Status);
        await admin.ActAsync(id, B2BAction.Approve, None, default);
        Assert.Equal(409, (await FailAsync(() => admin.ActAsync(id, B2BAction.Approve, None, default))).Status);              // ikinci onay
        await admin.ActAsync(id, B2BAction.Ship, new(null, "A", "1"), default);
        Assert.Equal(409, (await FailAsync(() => admin.ActAsync(id, B2BAction.Cancel, None, default))).Status);
        Assert.Equal(90, await StockAsync(w.Tenant)); // sevkte stok düşük kalır
        Assert.Equal(404, (await FailAsync(() => admin.GetAsync(Guid.NewGuid(), default))).Status);
    }

    [Fact]
    public async Task Ret_gerekce_ister_stogu_geri_ekler_ve_gerekce_bayiye_gorunur()
    {
        var w = await SeedAsync("Ret");
        var id = await PlaceOrderAsync(w, w.DealerId);
        Assert.Equal(90, await StockAsync(w.Tenant));
        await using var db = NewDb(w.Tenant);
        var admin = Admin(db);

        Assert.Equal(400, (await FailAsync(() => admin.ActAsync(id, B2BAction.Reject, new(" ", null, null), default))).Status);
        Assert.Equal(400, (await FailAsync(() => admin.ActAsync(id, B2BAction.Reject, new(new string('x', 501), null, null), default))).Status);
        Assert.Equal(90, await StockAsync(w.Tenant)); // hatalı istek stoğa dokunmaz

        var rejected = await admin.ActAsync(id, B2BAction.Reject, new("Kredi limiti aşıldı", null, null), default);
        Assert.Equal(("Cancelled", "Kredi limiti aşıldı"), (rejected.Status, rejected.StatusNote));
        Assert.Equal(100, await StockAsync(w.Tenant));

        await using var storeDb = NewDb(w.Tenant);
        Assert.Equal("Kredi limiti aşıldı", (await Store(storeDb).OrderAsync(w.DealerId, id, default)).StatusNote);
    }

    [Fact]
    public async Task Hazirlanan_siparis_yonetici_tarafindan_iptal_edilir_stok_bir_kez_geri_gelir()
    {
        var w = await SeedAsync("Iptal");
        var id = await PlaceOrderAsync(w, w.DealerId, 20);
        await using var db = NewDb(w.Tenant);
        var admin = Admin(db);
        await admin.ActAsync(id, B2BAction.Approve, None, default);
        Assert.Equal(80, await StockAsync(w.Tenant));
        var cancelled = await admin.ActAsync(id, B2BAction.Cancel, new("Stok sayımı", null, null), default);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(100, await StockAsync(w.Tenant));
        Assert.Equal(409, (await FailAsync(() => admin.ActAsync(id, B2BAction.Cancel, None, default))).Status); // ikinci iptal geçişi reddedilir
        Assert.Equal(100, await StockAsync(w.Tenant));
    }

    [Fact]
    public async Task Es_zamanli_iki_onay_isleminden_yalnizca_biri_basarili_olur()
    {
        var w = await SeedAsync("Yaris");
        var id = await PlaceOrderAsync(w, w.DealerId);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = NewDb(w.Tenant);
            try { await Admin(db).ActAsync(id, B2BAction.Approve, None, default); return true; }
            catch (StoreException) { return false; }
        }));
        Assert.Equal(1, results.Count(r => r));
    }

    [Fact]
    public async Task Liste_sekme_arama_ve_bayi_suzer_sayaclar_bayi_icindir_baska_kiracinin_siparisi_gorunmez()
    {
        var w = await SeedAsync("Liste");
        var a1 = await PlaceOrderAsync(w, w.DealerId);
        var a2 = await PlaceOrderAsync(w, w.DealerId);
        var b1 = await PlaceOrderAsync(w, w.OtherDealerId);
        await using var db = NewDb(w.Tenant);
        var admin = Admin(db);
        await admin.ActAsync(a2, B2BAction.Approve, None, default);
        await admin.ActAsync(b1, B2BAction.Reject, new("Olmaz", null, null), default);

        var all = await admin.ListAsync(null, null, null, 1, default);
        Assert.Equal((3, 1, 1, 0, 0, 1), (all.Total, all.Counts.Pending, all.Counts.Preparing, all.Counts.Shipped, all.Counts.Delivered, all.Counts.Cancelled));
        Assert.Equal([a1], (await admin.ListAsync("pending", null, null, 1, default)).Items.Select(i => i.Id).ToList());
        Assert.Equal([b1], (await admin.ListAsync("cancelled", null, null, 1, default)).Items.Select(i => i.Id).ToList());
        Assert.Equal(2, (await admin.ListAsync(null, null, w.DealerId, 1, default)).Total);
        Assert.Equal(1, (await admin.ListAsync(null, "Diğer", null, 1, default)).Total);
        Assert.Equal(400, (await FailAsync(() => admin.ListAsync("yok", null, null, 1, default))).Status);

        var other = await new TenantHelper().NewTenantAsync();
        await using var db2 = NewDb(other);
        Assert.Equal(0, (await Admin(db2).ListAsync(null, null, null, 1, default)).Total);
        Assert.Equal(404, (await FailAsync(() => Admin(db2).GetAsync(a1, default))).Status);
    }

    private sealed class TenantHelper
    {
        public async Task<Guid> NewTenantAsync()
        {
            await using var root = NewDb(null);
            var t = new Tenant { Name = "Baska" };
            root.Tenants.Add(t);
            await root.SaveChangesAsync();
            return t.Id;
        }
    }

    [Fact]
    public async Task Pazaryeri_siparisi_B2B_yonetiminde_gorunmez_ve_islem_gormez()
    {
        var w = await SeedAsync("Ayri");
        Guid external;
        await using (var seed = NewDb(w.Tenant))
        {
            var o = new Order { Channel = "Trendyol", OrderNumber = "T1", CustomerName = "X", OrderDate = DateTime.UtcNow, Status = OrderStatus.New };
            seed.Orders.Add(o);
            await seed.SaveChangesAsync();
            external = o.Id;
        }
        await using var db = NewDb(w.Tenant);
        Assert.Equal(0, (await Admin(db).ListAsync(null, null, null, 1, default)).Total);
        Assert.Equal(404, (await FailAsync(() => Admin(db).ActAsync(external, B2BAction.Approve, None, default))).Status);
    }

    // --- cari hesap ---

    [Fact]
    public async Task Cari_hesap_iptal_siparisleri_saymaz_odemeyi_ekler_siler_ve_vadeyi_ayarlar()
    {
        var w = await SeedAsync("Cari");
        var o1 = await PlaceOrderAsync(w, w.DealerId, 10); // 120
        var o2 = await PlaceOrderAsync(w, w.DealerId, 20); // 240
        await using var db = NewDb(w.Tenant);
        var accounts = new DealerAccountService(db, TimeProvider.System);
        await Admin(db).ActAsync(o2, B2BAction.Reject, new("x", null, null), default);

        var account = await accounts.GetAsync(w.DealerId, default);
        Assert.Equal((120m, 120m, 0), (account.Summary.TotalOrders, account.Summary.Balance, account.Payments.Count));

        var paid = await accounts.AddPaymentAsync(w.DealerId, new PaymentInput(50m, null, " Havale "), default);
        Assert.Equal((70m, "Havale"), (paid.Summary.Balance, paid.Payments.Single().Note));

        Assert.Equal(400, (await FailAsync(() => accounts.AddPaymentAsync(w.DealerId, new PaymentInput(0m, null, null), default))).Status);
        Assert.Equal(400, (await FailAsync(() => accounts.AddPaymentAsync(w.DealerId, new PaymentInput(5m, DateTime.UtcNow.AddDays(10), null), default))).Status);
        Assert.Equal(404, (await FailAsync(() => accounts.AddPaymentAsync(Guid.NewGuid(), new PaymentInput(5m, null, null), default))).Status);

        var termed = await accounts.SetTermAsync(w.DealerId, 30, default);
        Assert.Equal(30, termed.PaymentTermDays);
        Assert.Equal(400, (await FailAsync(() => accounts.SetTermAsync(w.DealerId, 400, default))).Status);

        // Bir ay sonra vadesi geçmiş: sabit saatle hesapla.
        var later = new DealerAccountService(db, new FixedTime(DateTime.UtcNow.AddDays(45)));
        Assert.Equal(70m, (await later.GetAsync(w.DealerId, default)).Summary.Overdue);

        var removed = await accounts.RemovePaymentAsync(w.DealerId, paid.Payments.Single().Id, default);
        Assert.Equal(120m, removed.Summary.Balance);
        Assert.Equal(404, (await FailAsync(() => accounts.RemovePaymentAsync(w.OtherDealerId, paid.Payments.Single().Id, default))).Status); // başka bayinin ödemesi silinemez
        _ = o1;
    }

    [Fact]
    public async Task Genel_bakis_ozeti_B2B_onay_bekleyen_siparisleri_sayar()
    {
        var w = await SeedAsync("Kpi");
        var a = await PlaceOrderAsync(w, w.DealerId);
        await PlaceOrderAsync(w, w.OtherDealerId);
        await using var db = NewDb(w.Tenant);
        var summary = await new DashboardController(db, TimeProvider.System).Summary(default);
        Assert.Equal(2, summary.B2BPendingApproval);
        await Admin(db).ActAsync(a, B2BAction.Approve, None, default);
        Assert.Equal(1, (await new DashboardController(db, TimeProvider.System).Summary(default)).B2BPendingApproval);
    }

    [Fact]
    public async Task Siparis_listesi_kanal_suzgeci_calisir()
    {
        var w = await SeedAsync("Kanal");
        await PlaceOrderAsync(w, w.DealerId);
        await using (var seed = NewDb(w.Tenant)) { seed.Orders.Add(new Order { Channel = "Trendyol", OrderNumber = "T9", CustomerName = "X", OrderDate = DateTime.UtcNow, Status = OrderStatus.New }); await seed.SaveChangesAsync(); }
        await using var db = NewDb(w.Tenant);
        var orders = new OrdersController(db, TimeProvider.System);
        Assert.Equal(2, (await orders.List(null, null, 1, 20, null, default)).Value!.Total);
        Assert.Equal(1, (await orders.List(null, null, 1, 20, "B2B", default)).Value!.Total);
        Assert.Equal(1, (await orders.List(null, null, 1, 20, "Trendyol", default)).Value!.Total);
    }
}
