using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Shop;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Modules;
using Pazaryeri.Infrastructure.Orders;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Shop;
using Pazaryeri.Infrastructure.Stock;

namespace Pazaryeri.Tests;

/// <summary>B2C sepet fiyatlama, sipariş, ödeme yöntemleri, stok ayırma, iptal ve kart ödemesi süre dolumu.</summary>
public class CheckoutTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Checkout;Trusted_Connection=True;TrustServerCertificate=True";

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

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static AppDbContext NewDb(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    private static CheckoutService Service(AppDbContext db, Guid tenant, bool fake = false, TimeProvider? time = null)
    {
        var shop = new ShopCatalogService(db, new ModuleService(db, new FixedTenant(tenant), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System));
        var provider = new FakePaymentProvider(Options.Create(new PaymentOptions { Provider = fake ? FakePaymentProvider.Name : null }));
        return new CheckoutService(db, shop, new StockPoolService(db, new RecordingQueue(db)), provider, time ?? TimeProvider.System);
    }

    public async Task InitializeAsync()
    {
        await using var db = NewDb(Guid.Empty);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record World(Guid Tenant, Guid Customer, Guid Address, Guid Tee, Guid Cap, Guid Hidden, Guid Out);

    private static async Task<World> SeedAsync(string slug, int teeStock = 10, bool bank = true, string? bankInfo = "TR00 0000 0000 0000 0000 0000 00 / Firma A.Ş.")
    {
        await using var root = NewDb(Guid.Empty);
        var tenant = new Tenant { Name = "Firma " + slug, Slug = slug };
        root.Tenants.Add(tenant);
        await root.SaveChangesAsync();
        await using var db = NewDb(tenant.Id);
        db.TenantModules.Add(new TenantModule { ModuleId = ModuleIds.B2CStore, Enabled = true });
        var tee = new Product { Name = "Tişört", Barcode = slug + "-T1", Price = 100m, B2CSalePrice = 80m, Stock = teeStock, B2CVisible = true, VatRate = 20 };
        var cap = new Product { Name = "Şapka", Barcode = slug + "-C1", Price = 50m, Stock = 100, SafetyStock = 98, B2CVisible = true, VatRate = 20 };
        var hidden = new Product { Name = "Görünmez", Barcode = slug + "-H1", Price = 10m, Stock = 10, B2CVisible = false };
        var outp = new Product { Name = "Tükenen", Barcode = slug + "-O1", Price = 30m, Stock = 0, B2CVisible = true };
        db.AddRange(tee, cap, hidden, outp);
        var customer = new Customer { TenantId = tenant.Id, Email = "a@b.com", FullName = "Ayşe Yılmaz", Phone = "05551234567" };
        var address = new CustomerAddress { TenantId = tenant.Id, CustomerId = customer.Id, Title = "Ev", FullName = "Ayşe Yılmaz", Phone = "05551234567", City = "İstanbul", District = "Kadıköy", AddressLine = "Moda Cd. 1/2", IsDefault = true };
        customer.Addresses.Add(address);
        db.Customers.Add(customer);
        db.B2CSettings.Add(new B2CSettings { ShippingFee = 30m, FreeShippingThreshold = 500m, BankTransferEnabled = bank, BankTransferInfo = bankInfo });
        await db.SaveChangesAsync();
        return new World(tenant.Id, customer.Id, address.Id, tee.Id, cap.Id, hidden.Id, outp.Id);
    }

    private static CheckoutRequest Req(World w, string method = "BankTransfer", params (Guid Id, int Qty)[] lines) =>
        new(lines.Select(l => new CartLineInput(l.Id, l.Qty)).ToList(), w.Address, method, null);

    private static async Task<int> StockAsync(Guid tenant, Guid productId)
    {
        await using var db = NewDb(tenant);
        return await db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.Stock).FirstAsync();
    }

    // --- fiyatlama ---

    [Fact]
    public async Task Fiyatlama_sunucuda_kampanya_fiyatiyla_ve_kargoyla_hesaplanir_ucretsiz_kargo_esigi_uygulanir()
    {
        var w = await SeedAsync("fiyat");
        await using var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant);

        var q = await svc.QuoteAsync(w.Tenant, [new(w.Tee, 2)], null, default);
        Assert.Equal(80m, q.Lines[0].UnitPrice);
        Assert.Equal(100m, q.Lines[0].ListPrice);
        Assert.Equal(160m, q.Subtotal);
        Assert.Equal(30m, q.ShippingFee);
        Assert.Equal(190m, q.Total);
        Assert.Equal(340m, q.FreeShippingRemaining);
        Assert.False(q.HasIssues);

        var free = await svc.QuoteAsync(w.Tenant, [new(w.Tee, 7)], null, default); // 560 ≥ 500
        Assert.Equal(0m, free.ShippingFee);
        Assert.Equal(560m, free.Total);
        Assert.Null(free.FreeShippingRemaining);
    }

    [Fact]
    public async Task Fiyatlama_stok_yetersiz_tukenen_ve_gorunmeyen_urunleri_sorun_olarak_isaretler_ve_toplama_katmaz()
    {
        var w = await SeedAsync("sorun");
        await using var db = NewDb(w.Tenant);
        var q = await Service(db, w.Tenant).QuoteAsync(w.Tenant, [new(w.Tee, 11), new(w.Cap, 3), new(w.Hidden, 1), new(w.Out, 1), new(Guid.NewGuid(), 1)], null, default);

        Assert.True(q.HasIssues);
        Assert.Equal(5, q.Lines.Count);
        Assert.All(q.Lines, l => Assert.NotNull(l.Issue));
        Assert.Equal(10, q.Lines.First(l => l.ProductId == w.Tee).Issue!.Available);
        Assert.Equal("unavailable", q.Lines.First(l => l.ProductId == w.Hidden).Issue!.Kind);
        Assert.Equal(0m, q.Subtotal);
        Assert.Equal(0m, q.ShippingFee); // boş sepete kargo yazılmaz
    }

    [Fact]
    public async Task Fiyatlama_bos_ya_da_asiri_sepeti_reddeder_ayni_urunun_satirlarini_birlestirir()
    {
        var w = await SeedAsync("sinir");
        await using var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant);
        await Assert.ThrowsAsync<StoreException>(() => svc.QuoteAsync(w.Tenant, [], null, default));
        await Assert.ThrowsAsync<StoreException>(() => svc.QuoteAsync(w.Tenant, [new(w.Tee, 0), new(w.Cap, -3)], null, default));
        var many = Enumerable.Range(0, CheckoutService.MaxLines + 1).Select(_ => new CartLineInput(Guid.NewGuid(), 1)).ToList();
        await Assert.ThrowsAsync<StoreException>(() => svc.QuoteAsync(w.Tenant, many, null, default));

        var q = await svc.QuoteAsync(w.Tenant, [new(w.Tee, 1), new(w.Tee, 2)], null, default);
        Assert.Single(q.Lines);
        Assert.Equal(3, q.Lines[0].Quantity);
    }

    [Fact]
    public async Task Baska_kiracinin_urunu_sepette_gorunmez()
    {
        var a = await SeedAsync("kira-a");
        var b = await SeedAsync("kira-b");
        await using var db = NewDb(a.Tenant);
        var q = await Service(db, a.Tenant).QuoteAsync(a.Tenant, [new(b.Tee, 1)], null, default);
        Assert.Equal("unavailable", q.Lines[0].Issue!.Kind);
    }

    // --- sipariş ---

    [Fact]
    public async Task Havale_siparisi_stogu_ayirir_toplami_sunucuda_hesaplar_ve_adresi_siparise_yazar()
    {
        var w = await SeedAsync("havale");
        await using var db = NewDb(w.Tenant);
        var result = await Service(db, w.Tenant).CheckoutAsync(w.Customer, Req(w, "BankTransfer", (w.Tee, 3)), default);

        Assert.StartsWith("B2C-", result.OrderNumber);
        Assert.Equal(270m, result.Total); // 3 x 80 + 30 kargo
        Assert.Equal("Pending", result.PaymentStatus);
        Assert.Contains("Firma A.Ş.", result.PaymentInfo);
        Assert.Null(result.PaymentUrl);
        Assert.Equal(7, await StockAsync(w.Tenant, w.Tee));

        await using var check = NewDb(w.Tenant);
        var order = await check.Orders.Include(o => o.Packages).ThenInclude(p => p.Lines).SingleAsync();
        Assert.Equal("B2C", order.Channel);
        Assert.Equal(w.Customer, order.CustomerId);
        Assert.Equal(OrderStatus.New, order.Status);
        Assert.Equal(PaymentMethod.BankTransfer, order.PaymentMethod);
        Assert.Equal(30m, order.ShippingFee);
        Assert.Contains("Moda Cd. 1/2", order.ShippingAddress);
        var line = order.Packages.Single().Lines.Single();
        Assert.Equal(80m, line.Price);
        Assert.True(line.StockDeducted);
    }

    [Fact]
    public async Task Siparis_baska_musterinin_adresiyle_ya_da_kapali_odeme_yontemiyle_verilemez()
    {
        var w = await SeedAsync("guvenlik");
        Guid otherAddress;
        await using (var db = NewDb(w.Tenant))
        {
            var other = new Customer { TenantId = w.Tenant, Email = "x@y.com", FullName = "Başka" };
            var a = new CustomerAddress { TenantId = w.Tenant, CustomerId = other.Id, Title = "Ev", FullName = "Başka", Phone = "05550000000", City = "Ankara", District = "Çankaya", AddressLine = "Başka sk. 5" };
            other.Addresses.Add(a);
            db.Customers.Add(other);
            await db.SaveChangesAsync();
            otherAddress = a.Id;
        }
        await using var run = NewDb(w.Tenant);
        var svc = Service(run, w.Tenant);

        var steal = await Assert.ThrowsAsync<StoreException>(() => svc.CheckoutAsync(w.Customer, Req(w, "BankTransfer", (w.Tee, 1)) with { AddressId = otherAddress }, default));
        Assert.Equal(400, steal.Status);
        var cod = await Assert.ThrowsAsync<StoreException>(() => svc.CheckoutAsync(w.Customer, Req(w, "CashOnDelivery", (w.Tee, 1)), default)); // kapıda ödeme kapalı
        Assert.Equal(409, cod.Status);
        var card = await Assert.ThrowsAsync<StoreException>(() => svc.CheckoutAsync(w.Customer, Req(w, "Card", (w.Tee, 1)), default));
        Assert.Equal(409, card.Status);
        var bad = await Assert.ThrowsAsync<StoreException>(() => svc.CheckoutAsync(w.Customer, Req(w, "Bitcoin", (w.Tee, 1)), default));
        Assert.Equal(400, bad.Status);
        Assert.Equal(10, await StockAsync(w.Tenant, w.Tee)); // hiçbirinde stok ayrılmadı
    }

    [Fact]
    public async Task Kapida_odeme_ucreti_toplama_eklenir_ve_odeme_teslimatta_alinacak_olarak_isaretlenir()
    {
        var w = await SeedAsync("kapida");
        await using (var db = NewDb(w.Tenant))
            await Service(db, w.Tenant).SaveSettingsAsync(new B2CSettingsInput(30m, 500m, true, "IBAN", true, 15m, false), default);
        await using var run = NewDb(w.Tenant);
        var result = await Service(run, w.Tenant).CheckoutAsync(w.Customer, Req(w, "CashOnDelivery", (w.Tee, 1)), default);

        Assert.Equal(125m, result.Total); // 80 + 30 kargo + 15 kapıda ödeme
        Assert.Equal("OnDelivery", result.PaymentStatus);
        await using var check = NewDb(w.Tenant);
        Assert.Equal(15m, (await check.Orders.SingleAsync()).PaymentFee);
    }

    [Fact]
    public async Task Stok_yetmeyen_siparis_hic_stok_tutmaz_ve_kalan_adedi_soyler()
    {
        var w = await SeedAsync("yetmez", teeStock: 2);
        await using var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant);
        var ex = await Assert.ThrowsAsync<StoreException>(() => svc.CheckoutAsync(w.Customer, Req(w, "BankTransfer", (w.Tee, 3)), default));
        Assert.Equal(409, ex.Status);
        Assert.Equal(2, await StockAsync(w.Tenant, w.Tee));
        await using var check = NewDb(w.Tenant);
        Assert.Empty(await check.Orders.ToListAsync());
    }

    [Fact]
    public async Task Besi_ayni_anda_siparis_dortluk_stoktan_yalnizca_dordunu_alir()
    {
        var w = await SeedAsync("yaris", teeStock: 4);
        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var db = NewDb(w.Tenant);
            try { await Service(db, w.Tenant).CheckoutAsync(w.Customer, Req(w, "BankTransfer", (w.Tee, 1)), default); return true; }
            catch (StoreException) { return false; }
        }).ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(4, results.Count(r => r));
        Assert.Equal(0, await StockAsync(w.Tenant, w.Tee));
        await using var check = NewDb(w.Tenant);
        Assert.Equal(4, await check.Orders.CountAsync());
    }

    // --- iptal ---

    [Fact]
    public async Task Musteri_yeni_siparisini_iptal_eder_stok_geri_eklenir_ikinci_iptal_etkisiz()
    {
        var w = await SeedAsync("iptal");
        await using var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant);
        var result = await svc.CheckoutAsync(w.Customer, Req(w, "BankTransfer", (w.Tee, 4)), default);
        Assert.Equal(6, await StockAsync(w.Tenant, w.Tee));

        await svc.CancelAsync(w.Customer, result.OrderId, default);
        await using var again = NewDb(w.Tenant);
        await Service(again, w.Tenant).CancelAsync(w.Customer, result.OrderId, default);

        Assert.Equal(10, await StockAsync(w.Tenant, w.Tee));
        await using var check = NewDb(w.Tenant);
        var order = await check.Orders.Include(o => o.Packages).SingleAsync();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.Failed, order.PaymentStatus);
        Assert.All(order.Packages, p => Assert.Equal(OrderStatus.Cancelled, p.Status));
    }

    [Fact]
    public async Task Hazirlanmaya_baslayan_ya_da_baskasinin_siparisi_iptal_edilemez()
    {
        var w = await SeedAsync("iptalyok");
        await using var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant);
        var result = await svc.CheckoutAsync(w.Customer, Req(w, "BankTransfer", (w.Tee, 1)), default);

        await Assert.ThrowsAsync<StoreException>(() => svc.CancelAsync(Guid.NewGuid(), result.OrderId, default)); // başka müşteri: 404
        await db.Orders.Where(o => o.Id == result.OrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Preparing));
        db.ChangeTracker.Clear();
        var ex = await Assert.ThrowsAsync<StoreException>(() => svc.CancelAsync(w.Customer, result.OrderId, default));
        Assert.Equal(409, ex.Status);
        Assert.Equal(9, await StockAsync(w.Tenant, w.Tee));
    }

    // --- kart ödemesi ---

    [Fact]
    public async Task Kart_odemesi_saglayici_yokken_sunulmaz_ve_ayarla_da_acilamaz()
    {
        var w = await SeedAsync("kartyok");
        await using var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant, fake: false);
        var ex = await Assert.ThrowsAsync<StoreException>(() => svc.SaveSettingsAsync(new B2CSettingsInput(30m, null, true, "IBAN", false, 0, true), default));
        Assert.Equal(409, ex.Status);
        var q = await svc.QuoteAsync(w.Tenant, [new(w.Tee, 1)], null, default);
        Assert.DoesNotContain(q.PaymentOptions, o => o.Method == "Card");
    }

    private static async Task<(CheckoutResult Order, CheckoutService Svc, AppDbContext Db)> CardOrderAsync(World w, TimeProvider? time = null, int qty = 2)
    {
        var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant, fake: true, time);
        await svc.SaveSettingsAsync(new B2CSettingsInput(30m, 500m, true, "IBAN", false, 0, true), default);
        var result = await svc.CheckoutAsync(w.Customer, Req(w, "Card", (w.Tee, qty)), default);
        return (result, svc, db);
    }

    [Fact]
    public async Task Kart_siparisi_odeme_bekler_basarili_sonuc_odendi_yapar_tekrar_cagri_etkisizdir()
    {
        var w = await SeedAsync("kartok");
        var (result, svc, db) = await CardOrderAsync(w);
        await using var _ = db;
        Assert.Equal("Pending", result.PaymentStatus);
        Assert.Equal($"/odeme/test/{result.OrderId}", result.PaymentUrl);
        Assert.Equal(8, await StockAsync(w.Tenant, w.Tee));

        Assert.Equal("Paid", await svc.ConfirmCardAsync(w.Customer, result.OrderId, true, default));
        db.ChangeTracker.Clear();
        Assert.Equal("Paid", await svc.ConfirmCardAsync(w.Customer, result.OrderId, false, default)); // ödenmiş sipariş sonradan "başarısız" yapılamaz
        await using var check = NewDb(w.Tenant);
        var order = await check.Orders.SingleAsync();
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(OrderStatus.New, order.Status);
        Assert.Null(order.PaymentExpiresAt);
        Assert.Equal(8, await StockAsync(w.Tenant, w.Tee));
        var cancel = await Assert.ThrowsAsync<StoreException>(() => svc.CancelAsync(w.Customer, result.OrderId, default)); // ödemesi alınmış sipariş müşteri tarafından iptal edilmez
        Assert.Equal(409, cancel.Status);
    }

    [Fact]
    public async Task Kart_odemesi_basarisiz_olursa_siparis_iptal_olur_ve_stok_geri_gelir()
    {
        var w = await SeedAsync("kartfail");
        var (result, svc, db) = await CardOrderAsync(w);
        await using var _ = db;
        Assert.Equal("Failed", await svc.ConfirmCardAsync(w.Customer, result.OrderId, false, default));

        Assert.Equal(10, await StockAsync(w.Tenant, w.Tee));
        await using var check = NewDb(w.Tenant);
        var order = await check.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.Failed, order.PaymentStatus);
    }

    [Fact]
    public async Task Suresi_dolan_kart_siparisi_iptal_edilir_stok_doner_suresi_dolmayan_dokunulmaz()
    {
        var w = await SeedAsync("kartsure");
        var clock = new Clock(DateTimeOffset.UtcNow);
        var (result, svc, db) = await CardOrderAsync(w, clock, qty: 3);
        await using var _ = db;
        Assert.Equal(0, await svc.ExpireCardOrdersAsync(default)); // henüz dolmadı
        Assert.Equal(7, await StockAsync(w.Tenant, w.Tee));

        clock.Now = clock.Now.AddMinutes(CheckoutService.CardMinutes + 1);
        Assert.Equal(1, await svc.ExpireCardOrdersAsync(default));
        Assert.Equal(0, await svc.ExpireCardOrdersAsync(default)); // ikinci turda etkisiz

        Assert.Equal(10, await StockAsync(w.Tenant, w.Tee));
        await using var check = NewDb(w.Tenant);
        var order = await check.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.Expired, order.PaymentStatus);
        Assert.Equal("Expired", await svc.ConfirmCardAsync(w.Customer, result.OrderId, true, default)); // süresi dolmuş sipariş sonradan ödenmiş olmaz
        await using var after = NewDb(w.Tenant);
        Assert.Equal(PaymentStatus.Expired, (await after.Orders.SingleAsync()).PaymentStatus);
    }

    // --- ayarlar ---

    [Fact]
    public async Task Ayarlar_dogrulanir_havale_acikken_bilgi_zorunlu_negatif_ucret_reddedilir()
    {
        var w = await SeedAsync("ayar");
        await using var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant);
        await Assert.ThrowsAsync<StoreException>(() => svc.SaveSettingsAsync(new B2CSettingsInput(30m, null, true, "  ", false, 0, false), default));
        await Assert.ThrowsAsync<StoreException>(() => svc.SaveSettingsAsync(new B2CSettingsInput(-1m, null, true, "IBAN", false, 0, false), default));
        await Assert.ThrowsAsync<StoreException>(() => svc.SaveSettingsAsync(new B2CSettingsInput(30m, 0m, true, "IBAN", false, 0, false), default));
        await Assert.ThrowsAsync<StoreException>(() => svc.SaveSettingsAsync(new B2CSettingsInput(30m, null, true, new string('x', 1001), false, 0, false), default));

        var saved = await svc.SaveSettingsAsync(new B2CSettingsInput(19.999m, 250m, true, " IBAN ", true, 10m, false), default);
        Assert.Equal(20m, saved.ShippingFee);
        Assert.Equal("IBAN", saved.BankTransferInfo);
        Assert.False(saved.CardProviderAvailable);
    }
}
