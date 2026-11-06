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

/// <summary>B2C kargo firmaları, desi ücret kuralları, sepette firma seçimi, havale onayı ve sevk (takip numarası).</summary>
public class ShippingTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Shipping;Trusted_Connection=True;TrustServerCertificate=True";

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

    private static AppDbContext NewDb(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    private static CheckoutService Service(AppDbContext db, Guid tenant)
    {
        var shop = new ShopCatalogService(db, new ModuleService(db, new FixedTenant(tenant), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System));
        var provider = new FakePaymentProvider(Options.Create(new PaymentOptions()));
        return new CheckoutService(db, shop, new StockPoolService(db, new RecordingQueue(db)), provider, TimeProvider.System);
    }

    public async Task InitializeAsync()
    {
        await using var db = NewDb(Guid.Empty);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record World(Guid Tenant, Guid Address, Guid Tee);

    /// <summary>Tişört: 100 ₺, 10 stok, desi 2. Kargo ücreti 30 ₺, 500 ₺ üzeri ücretsiz.</summary>
    private static async Task<World> SeedAsync(string slug)
    {
        await using var root = NewDb(Guid.Empty);
        var tenant = new Tenant { Name = "Firma " + slug, Slug = slug };
        root.Tenants.Add(tenant);
        await root.SaveChangesAsync();
        await using var db = NewDb(tenant.Id);
        db.TenantModules.Add(new TenantModule { ModuleId = ModuleIds.B2CStore, Enabled = true });
        var tee = new Product { Name = "Tişört", Barcode = slug + "-T1", Price = 100m, Stock = 50, B2CVisible = true, VatRate = 20 };
        db.Add(tee);
        await db.SaveChangesAsync();
        db.ProductChannelData.Add(new ProductChannelData { ProductId = tee.Id, Channel = "Trendyol", DimensionalWeight = 2m });
        var customer = new Customer { TenantId = tenant.Id, Email = "a@b.com", FullName = "Ayşe Yılmaz", Phone = "05551234567" };
        var address = new CustomerAddress { TenantId = tenant.Id, CustomerId = customer.Id, Title = "Ev", FullName = "Ayşe Yılmaz", Phone = "05551234567", City = "İstanbul", District = "Kadıköy", AddressLine = "Moda Cd. 1/2", IsDefault = true };
        customer.Addresses.Add(address);
        db.Customers.Add(customer);
        db.B2CSettings.Add(new B2CSettings { ShippingFee = 30m, FreeShippingThreshold = 500m, BankTransferEnabled = true, BankTransferInfo = "TR00 / Firma", CashOnDeliveryEnabled = true });
        await db.SaveChangesAsync();
        return new World(tenant.Id, address.Id, tee.Id);
    }

    private static ShippingCarrierInput Carrier(string name, decimal flat = 40m, string? url = null, params (decimal Desi, decimal Fee)[] rates) =>
        new(name, true, flat, url, rates.Select(r => new ShippingRateDto(r.Desi, r.Fee)).ToList());

    private static CheckoutRequest Req(World w, int qty, Guid? carrier, string method = "CashOnDelivery") =>
        new([new CartLineInput(w.Tee, qty)], w.Address, method, null, carrier);

    // --- seçenekler (saf mantık) ---

    private static ShippingCarrier Mem(string name, decimal flat, int order, params (decimal Desi, decimal Fee)[] rates) => new()
    {
        Id = Guid.NewGuid(), Name = name, Enabled = true, SortOrder = order, FlatFee = flat,
        Rates = rates.Select(r => new ShippingRate { UpToDesi = r.Desi, Fee = r.Fee }).ToList(),
    };

    [Fact]
    public void Firma_yoksa_ayarlardaki_tek_kargo_ucreti_uygulanir_esikte_ucretsiz()
    {
        var s = new B2CSettings { ShippingFee = 30m, FreeShippingThreshold = 500m };

        var one = Assert.Single(CheckoutService.ShippingOptions(s, [], 100m, 0));
        Assert.Equal((null, 30m), (one.CarrierId, one.Fee));
        Assert.Equal(0m, Assert.Single(CheckoutService.ShippingOptions(s, [], 500m, 0)).Fee);
    }

    [Fact]
    public void Desi_araligi_en_kucuk_uygun_ucreti_secer_en_buyuk_araligi_asan_firma_sunulmaz()
    {
        var s = new B2CSettings { FreeShippingThreshold = null };
        var yurtici = Mem("Yurtiçi", 99m, 0, (5m, 40m), (10m, 60m), (20m, 100m));
        var flat = Mem("Sabit", 45m, 1);

        decimal FeeOf(decimal desi, ShippingCarrier c) => CheckoutService.ShippingOptions(s, [yurtici, flat], 100m, desi).FirstOrDefault(o => o.CarrierId == c.Id)?.Fee ?? -1;

        Assert.Equal(40m, FeeOf(0, yurtici));   // desisi girilmemiş ürün ilk aralığa düşer
        Assert.Equal(40m, FeeOf(5m, yurtici));  // sınır dahil
        Assert.Equal(60m, FeeOf(5.01m, yurtici));
        Assert.Equal(100m, FeeOf(20m, yurtici));
        Assert.Equal(-1m, FeeOf(20.5m, yurtici)); // en büyük aralığı aştı: sunulmaz
        Assert.Equal(45m, FeeOf(500m, flat));      // kuralsız firma sabit ücret
    }

    [Fact]
    public void Kapali_firma_sunulmaz_ucretsiz_kargo_tum_firmalarda_gecerlidir_ve_siralama_korunur()
    {
        var s = new B2CSettings { FreeShippingThreshold = 200m };
        var a = Mem("B", 40m, 1);
        var b = Mem("A", 50m, 0);
        var off = Mem("Kapalı", 10m, 2); off.Enabled = false;

        var options = CheckoutService.ShippingOptions(s, [a, b, off], 250m, 1);

        Assert.Equal(["A", "B"], options.Select(o => o.Name));
        Assert.All(options, o => Assert.Equal(0m, o.Fee));
    }

    // --- firma yönetimi ---

    [Fact]
    public async Task Firma_eklenir_guncellenir_silinir_kurallar_sirali_doner()
    {
        var w = await SeedAsync("firma");
        await using var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant);

        var created = await svc.SaveCarrierAsync(null, Carrier("Yurtiçi Kargo", 40m, "https://takip.example.com/?no={takipNo}", (10m, 60m), (5m, 40m)), default);
        Assert.Equal([5m, 10m], created.Rates.Select(r => r.UpToDesi));

        var updated = await svc.SaveCarrierAsync(created.Id, Carrier("Yurtiçi", 45m, null), default);
        Assert.Equal(("Yurtiçi", 45m, null), (updated.Name, updated.FlatFee, updated.TrackingUrlTemplate));
        Assert.Empty(updated.Rates);

        await svc.DeleteCarrierAsync(created.Id, default);
        Assert.Empty(await svc.CarriersAsync(default));
        await Assert.ThrowsAsync<StoreException>(() => svc.DeleteCarrierAsync(created.Id, default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Firma_adi_zorunlu(string name)
    {
        var w = await SeedAsync("adzorunlu");
        await using var db = NewDb(w.Tenant);
        var e = await Assert.ThrowsAsync<StoreException>(() => Service(db, w.Tenant).SaveCarrierAsync(null, Carrier(name), default));
        Assert.Equal(400, e.Status);
    }

    [Theory]
    [InlineData("http://takip.example.com/{takipNo}")]   // https değil
    [InlineData("https://takip.example.com/")]            // yer tutucu yok
    [InlineData("javascript:alert({takipNo})")]
    public async Task Takip_baglantisi_https_olmali_ve_yer_tutucu_icermeli(string template)
    {
        var w = await SeedAsync("sablon" + Math.Abs(template.GetHashCode()));
        await using var db = NewDb(w.Tenant);
        var e = await Assert.ThrowsAsync<StoreException>(() => Service(db, w.Tenant).SaveCarrierAsync(null, Carrier("X", 1m, template), default));
        Assert.Equal(400, e.Status);
    }

    [Fact]
    public async Task Ayni_ad_tekrar_edilemez_ayni_desi_siniri_iki_kez_yazilamaz_negatif_ucret_reddedilir()
    {
        var w = await SeedAsync("tekrar");
        await using var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant);
        await svc.SaveCarrierAsync(null, Carrier("Aras"), default);

        Assert.Equal(409, (await Assert.ThrowsAsync<StoreException>(() => svc.SaveCarrierAsync(null, Carrier("aras"), default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<StoreException>(() => svc.SaveCarrierAsync(null, Carrier("B", 1m, null, (5m, 1m), (5m, 2m)), default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<StoreException>(() => svc.SaveCarrierAsync(null, Carrier("C", -1m), default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<StoreException>(() => svc.SaveCarrierAsync(null, Carrier("D", 1m, null, (0m, 1m)), default))).Status);
    }

    [Fact]
    public async Task Firmalar_kiracilar_arasinda_yalitilidir()
    {
        var a = await SeedAsync("izole-a");
        var b = await SeedAsync("izole-b");
        await using (var dbA = NewDb(a.Tenant)) await Service(dbA, a.Tenant).SaveCarrierAsync(null, Carrier("Sadece A"), default);

        await using var dbB = NewDb(b.Tenant);
        Assert.Empty(await Service(dbB, b.Tenant).CarriersAsync(default));
    }

    // --- sepet ve sipariş ---

    [Fact]
    public async Task Sepet_desiyi_urunlerden_hesaplar_secilen_firmanin_ucretini_yazar()
    {
        var w = await SeedAsync("sepet");
        await using var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant);
        var fast = await svc.SaveCarrierAsync(null, Carrier("Hızlı", 99m, null, (6m, 70m), (50m, 120m)), default);
        var cheap = await svc.SaveCarrierAsync(null, Carrier("Ucuz", 35m), default);

        var q = await svc.QuoteAsync(w.Tenant, [new(w.Tee, 3)], null, default); // 3 × 2 desi = 6 → ilk aralık, 300 ₺ < 500
        Assert.Equal([fast.Id, cheap.Id], q.ShippingOptions!.Select(o => o.CarrierId));
        Assert.Equal((fast.Id, 70m), (q.CarrierId, q.ShippingFee));

        var picked = await svc.QuoteAsync(w.Tenant, [new(w.Tee, 3)], cheap.Id, default);
        Assert.Equal((cheap.Id, 35m, 335m), (picked.CarrierId, picked.ShippingFee, picked.Total));

        var heavy = await svc.QuoteAsync(w.Tenant, [new(w.Tee, 4)], null, default); // 8 desi → ikinci aralık
        Assert.Equal(120m, heavy.ShippingOptions!.First(o => o.CarrierId == fast.Id).Fee);
    }

    [Fact]
    public async Task Siparis_secilen_firmayi_ve_kargo_ucretini_saklar_gecersiz_firma_reddedilir()
    {
        var w = await SeedAsync("siparis");
        await using var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant);
        var c = await svc.SaveCarrierAsync(null, Carrier("Aras Kargo", 42m), default);

        var result = await svc.CheckoutAsync(await CustomerIdAsync(w.Tenant), Req(w, 1, c.Id), default);

        await using var read = NewDb(w.Tenant);
        var order = await read.Orders.AsNoTracking().SingleAsync(o => o.Id == result.OrderId);
        Assert.Equal(("Aras Kargo", 42m), (order.ShipmentCarrier, order.ShippingFee));
        Assert.Equal(142m, order.TotalPrice);

        var e = await Assert.ThrowsAsync<StoreException>(() => svc.CheckoutAsync(order.CustomerId!.Value, Req(w, 1, Guid.NewGuid()), default));
        Assert.Equal(409, e.Status);
    }

    [Fact]
    public async Task Tum_firmalar_kapaliysa_siparis_verilemez()
    {
        var w = await SeedAsync("kapali");
        await using var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant);
        await svc.SaveCarrierAsync(null, new ShippingCarrierInput("Kapalı", false, 10m, null, []), default);

        var customer = await CustomerIdAsync(w.Tenant);
        var e = await Assert.ThrowsAsync<StoreException>(() => svc.CheckoutAsync(customer, Req(w, 1, null), default));
        Assert.Equal(409, e.Status);
    }

    [Fact]
    public async Task Firma_tanimli_degilse_siparis_eskisi_gibi_ayardaki_ucretle_verilir()
    {
        var w = await SeedAsync("firmasiz");
        await using var db = NewDb(w.Tenant);
        var result = await Service(db, w.Tenant).CheckoutAsync(await CustomerIdAsync(w.Tenant), Req(w, 1, null), default);

        await using var read = NewDb(w.Tenant);
        var order = await read.Orders.AsNoTracking().SingleAsync(o => o.Id == result.OrderId);
        Assert.Equal((null, 30m), (order.ShipmentCarrier, order.ShippingFee));
    }

    private static async Task<Guid> CustomerIdAsync(Guid tenant)
    {
        await using var db = NewDb(tenant);
        return await db.Customers.AsNoTracking().Select(c => c.Id).FirstAsync();
    }

    // --- havale onayı ve sevk ---

    [Fact]
    public async Task Sevk_takip_numarasi_ve_firma_bagligiyla_kaydedilir_musteri_detayda_gorur_tekrar_sevk_edilemez()
    {
        var w = await SeedAsync("sevk");
        await using var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant);
        var c = await svc.SaveCarrierAsync(null, Carrier("Yurtiçi", 40m, "https://takip.example.com/?no={takipNo}"), default);
        var customer = await CustomerIdAsync(w.Tenant);
        var placed = await svc.CheckoutAsync(customer, Req(w, 1, c.Id, "CashOnDelivery"), default);

        await svc.ShipAsync(placed.OrderId, new ShipOrderInput(null, null, " TK 123/45 "), default);

        await using var read = NewDb(w.Tenant);
        var order = await read.Orders.AsNoTracking().Include(o => o.Packages).SingleAsync(o => o.Id == placed.OrderId);
        Assert.Equal((OrderStatus.Shipped, "Yurtiçi", "TK 123/45"), (order.Status, order.ShipmentCarrier, order.TrackingNumber));
        Assert.Equal("https://takip.example.com/?no=TK%20123%2F45", order.TrackingUrl);
        Assert.NotNull(order.ShippedAt);
        Assert.All(order.Packages, p => Assert.Equal(OrderStatus.Shipped, p.Status));

        var detail = await new CustomerAccountService(read).OrderAsync(customer, placed.OrderId, default);
        Assert.Equal(("Yurtiçi", "TK 123/45", order.TrackingUrl), (detail.ShipmentCarrier, detail.TrackingNumber, detail.TrackingUrl));
        Assert.False(detail.CanCancel);

        var again = await Assert.ThrowsAsync<StoreException>(() => svc.ShipAsync(placed.OrderId, new ShipOrderInput(null, null, "X"), default));
        Assert.Equal(409, again.Status);
    }

    [Fact]
    public async Task Odemesi_beklenen_havale_siparisi_sevk_edilemez_onaydan_sonra_edilir()
    {
        var w = await SeedAsync("havale");
        await using var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant);
        var placed = await svc.CheckoutAsync(await CustomerIdAsync(w.Tenant), Req(w, 1, null, "BankTransfer"), default);

        var blocked = await Assert.ThrowsAsync<StoreException>(() => svc.ShipAsync(placed.OrderId, new ShipOrderInput(null, "Aras", "A1"), default));
        Assert.Equal(409, blocked.Status);

        await svc.ApprovePaymentAsync(placed.OrderId, default);
        await svc.ApprovePaymentAsync(placed.OrderId, default); // tekrar etkisiz
        await svc.ShipAsync(placed.OrderId, new ShipOrderInput(null, "Aras", "A1"), default);

        await using var read = NewDb(w.Tenant);
        var order = await read.Orders.AsNoTracking().SingleAsync(o => o.Id == placed.OrderId);
        Assert.Equal((PaymentStatus.Paid, OrderStatus.Shipped, "Aras", null), (order.PaymentStatus, order.Status, order.ShipmentCarrier, order.TrackingUrl));
    }

    [Fact]
    public async Task Sevk_dogrulamalari_takip_no_firma_ve_iptal_edilmis_siparis()
    {
        var w = await SeedAsync("dogrula");
        await using var db = NewDb(w.Tenant);
        var svc = Service(db, w.Tenant);
        var customer = await CustomerIdAsync(w.Tenant);
        var placed = await svc.CheckoutAsync(customer, Req(w, 1, null, "CashOnDelivery"), default);

        Assert.Equal(400, (await Assert.ThrowsAsync<StoreException>(() => svc.ShipAsync(placed.OrderId, new ShipOrderInput(null, "Aras", "  "), default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<StoreException>(() => svc.ShipAsync(placed.OrderId, new ShipOrderInput(null, "Aras", new string('9', 101)), default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<StoreException>(() => svc.ShipAsync(placed.OrderId, new ShipOrderInput(null, null, "A1"), default))).Status); // firma yok
        Assert.Equal(400, (await Assert.ThrowsAsync<StoreException>(() => svc.ShipAsync(placed.OrderId, new ShipOrderInput(Guid.NewGuid(), null, "A1"), default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<StoreException>(() => svc.ShipAsync(Guid.NewGuid(), new ShipOrderInput(null, "Aras", "A1"), default))).Status);

        await svc.CancelAsync(customer, placed.OrderId, default);
        Assert.Equal(409, (await Assert.ThrowsAsync<StoreException>(() => svc.ShipAsync(placed.OrderId, new ShipOrderInput(null, "Aras", "A1"), default))).Status);
    }

    [Fact]
    public async Task Baska_kiracinin_siparisi_sevk_edilemez()
    {
        var a = await SeedAsync("sevk-a");
        var b = await SeedAsync("sevk-b");
        Guid orderId;
        await using (var dbA = NewDb(a.Tenant))
            orderId = (await Service(dbA, a.Tenant).CheckoutAsync(await CustomerIdAsync(a.Tenant), Req(a, 1, null), default)).OrderId;

        await using var dbB = NewDb(b.Tenant);
        var e = await Assert.ThrowsAsync<StoreException>(() => Service(dbB, b.Tenant).ShipAsync(orderId, new ShipOrderInput(null, "Aras", "A1"), default));
        Assert.Equal(404, e.Status);
    }
}
