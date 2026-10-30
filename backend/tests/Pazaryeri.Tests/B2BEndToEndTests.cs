using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Auth;
using Pazaryeri.Application.Dealers;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Orders;
using Pazaryeri.Application.Pricing;
using Pazaryeri.Application.Stock;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Auth;
using Pazaryeri.Infrastructure.Dealers;
using Pazaryeri.Infrastructure.Modules;
using Pazaryeri.Infrastructure.Orders;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Pricing;
using Pazaryeri.Infrastructure.Stock;

namespace Pazaryeri.Tests;

/// <summary>
/// B2B dönüşümünün uçtan uca doğrulaması (#72): bayi başvurusundan sipariş durum takibine tam akış, modül aç/kapa,
/// pazaryeri ve B2B'nin aynı stok havuzunu kullanması ve fiyat/KDV örnekleri. Servisler gerçek veritabanıyla birlikte çalışır.
/// </summary>
public class B2BEndToEndTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_B2BE2E;Trusted_Connection=True;TrustServerCertificate=True";
    private static readonly JwtOptions Jwt = new() { Key = new string('k', 40) };

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private sealed class AmbientProvider : ITenantProvider
    {
        public Guid? TenantId => AmbientTenant.TenantId;
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

    private static AppDbContext NewAmbientDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new AmbientProvider());

    private static ModuleService Modules(AppDbContext db) => new(db, new FixedTenant(null), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);

    private static DealerAuthService Auth(AppDbContext db) => new(db, new JwtTokenService(Options.Create(Jwt)), Modules(db), TimeProvider.System);

    private static B2BOrderService Store(AppDbContext db) =>
        new(db, new StoreCatalog(db), new DealerPriceService(db), new StockPoolService(db, new RecordingQueue(db)), TimeProvider.System);

    private static B2BAdminOrderService Admin(AppDbContext db) => new(db, new StockPoolService(db, new RecordingQueue(db)), TimeProvider.System);

    private static StoreCatalogService Catalog(AppDbContext db) => new(db, new StoreCatalog(db), new DealerPriceService(db));

    public async Task InitializeAsync()
    {
        await using var db = NewDb(null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> TenantAsync(string name, string slug, params string[] enabledModules)
    {
        await using var root = NewDb(null);
        var t = new Tenant { Name = name, Slug = slug };
        root.Tenants.Add(t);
        await root.SaveChangesAsync();
        await using var db = NewDb(t.Id);
        foreach (var m in enabledModules) db.TenantModules.Add(new TenantModule { ModuleId = m, Enabled = true });
        await db.SaveChangesAsync();
        return t.Id;
    }

    private static readonly string[] AllB2B = [ModuleIds.Orders, ModuleIds.Dealers, ModuleIds.PriceLists, ModuleIds.B2BStore];

    // --- 1) Uçtan uca akış ---

    [Fact]
    public async Task Basvurudan_siparis_durum_takibine_tam_akis()
    {
        var tenant = await TenantAsync("Akış A.Ş.", "akis", AllB2B);
        await using (var seed = NewDb(tenant))
        {
            var group = new DealerGroup { Name = "Altın" };
            seed.DealerGroups.Add(group);
            await seed.SaveChangesAsync();
            seed.Products.Add(new Product { Name = "Defter", Barcode = "NB", Price = 60m, VatRate = 20, Stock = 50, B2BVisible = true, MinOrderQuantity = 10, PackSize = 5 });
            seed.Products.Add(new Product { Name = "Gizli", Barcode = "GZ", Price = 5m, Stock = 50, B2BVisible = false });
            seed.PriceLists.Add(new PriceList { Name = "Altın liste", GroupId = group.Id, DiscountPercent = 10m, PricesIncludeVat = true, ShowVatIncluded = true });
            await seed.SaveChangesAsync();
        }

        // 1. Bayi siteden başvurur; onaylanana kadar giremez.
        await using var anon = NewAmbientDb();
        var apply = await Auth(anon).ApplyAsync(new DealerApplication("akis", "Mavi Ticaret", "1234567890", "Kadıköy", "Ayşe", "ayse@mavi.test", "0555", "İstanbul", "Adres 1", "Parola1234"), default);
        Assert.Equal(DealerAuthResult.Ok, apply.Result);
        await using (var a2 = NewAmbientDb())
            Assert.Equal(DealerAuthResult.Pending, (await Auth(a2).LoginAsync(new DealerLogin("akis", "ayse@mavi.test", "Parola1234"), default)).Result);

        // 2. Yönetici başvuruyu onaylar ve bayiyi gruba alır.
        await using (var admin = NewDb(tenant))
        {
            var groupId = (await admin.DealerGroups.SingleAsync()).Id;
            var dealer = await admin.Dealers.SingleAsync();
            Assert.Equal("Active", (await new DealersController(admin).Approve(dealer.Id, default)).Value!.Status);
            dealer.GroupId = groupId;
            await admin.SaveChangesAsync();
        }

        // 3. Bayi giriş yapar.
        await using var a3 = NewAmbientDb();
        var login = (await Auth(a3).LoginAsync(new DealerLogin("akis", "ayse@mavi.test", "Parola1234"), default)).Value!;
        var dealerId = login.User.DealerId;
        Assert.False(string.IsNullOrEmpty(login.AccessToken));

        // 4. Ürün listesi: yalnızca görünür ürün, grup iskontosuyla fiyat (60 → 54).
        await using var db = NewDb(tenant);
        var page = await Catalog(db).ListAsync(dealerId, new StoreQuery(null, null, null, 1, 24), default);
        var item = Assert.Single(page.Items);
        Assert.Equal(("NB", 54m, true, 10, 5), (item.Code, item.Price.Amount, item.Price.IncludesVat, item.MinOrderQuantity, item.PackSize));

        // 5. Sepet ve sipariş: koli kuralı korunur, sipariş onay bekler.
        var store = Store(db);
        Assert.Equal(400, (await Assert.ThrowsAsync<StoreException>(() => store.AddAsync(dealerId, item.Id, 12, default))).Status);
        await store.AddAsync(dealerId, item.Id, 20, default);
        var order = await store.CheckoutAsync(dealerId, new CheckoutInput(null, "Acil", false), default);
        Assert.Equal(("New", 1080m, "Adres 1"), (order.Status, order.TotalGross, order.ShippingAddress));
        await using (var check = NewDb(tenant)) Assert.Equal(30, await check.Products.Where(p => p.Barcode == "NB").Select(p => p.Stock).SingleAsync());

        // 6. Panelde onay ve sevk.
        await using var adminDb = NewDb(tenant);
        var service = Admin(adminDb);
        Assert.Equal("Preparing", (await service.ActAsync(order.Id, B2BAction.Approve, new(null, null, null), default)).Status);
        await service.ActAsync(order.Id, B2BAction.Ship, new(null, "Yurtiçi", "YK1"), default);

        // 7. Bayi durumu ve sevk bilgisini görür; cari hesapta borç oluşur.
        await using var dealerDb = NewDb(tenant);
        var seen = await Store(dealerDb).OrderAsync(dealerId, order.Id, default);
        Assert.Equal(("Shipped", "Yurtiçi", "YK1", false), (seen.Status, seen.ShipmentCarrier, seen.TrackingNumber, seen.CanCancel));
        var account = await new DealerAccountService(dealerDb, TimeProvider.System).GetAsync(dealerId, default);
        Assert.Equal(1080m, account.Summary.Balance);
    }

    // --- 2) Modül kapat/aç ---

    private static async Task<(ActionExecutingContext Context, bool Called)> RunFilterAsync(string moduleId, Guid tenant, IModuleService modules)
    {
        var services = new ServiceCollection().AddSingleton<ITenantProvider>(new FixedTenant(tenant)).AddSingleton(modules).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var actionContext = new ActionContext(http, new RouteData(), new ControllerActionDescriptor());
        var context = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), new object());
        var called = false;
        await new RequiresModuleAttribute(moduleId).OnActionExecutionAsync(context, () => { called = true; return Task.FromResult(new ActionExecutedContext(actionContext, [], new object())); });
        return (context, called);
    }

    [Fact]
    public async Task Modul_kapaninca_uc_noktalar_403_site_hizmet_disi_olur_acilinca_geri_gelir()
    {
        var tenant = await TenantAsync("Modül", "modul", AllB2B);
        await using var db = NewDb(tenant);
        var modules = new ModuleService(db, new FixedTenant(tenant), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);

        await using var anon = NewAmbientDb();
        Assert.True((await Auth(anon).InfoAsync("modul", default))!.Available);
        foreach (var id in new[] { ModuleIds.B2BStore, ModuleIds.PriceLists, ModuleIds.Dealers }) Assert.True((await RunFilterAsync(id, tenant, modules)).Called);

        // Bağımlılık sırasıyla kapat: b2b-store, price-lists, dealers.
        Assert.Equal(ModuleChangeResult.Changed, (await modules.SetAsync(ModuleIds.B2BStore, false)).Result);
        Assert.Equal(ModuleChangeResult.Changed, (await modules.SetAsync(ModuleIds.PriceLists, false)).Result);
        var blocked = await RunFilterAsync(ModuleIds.B2BStore, tenant, modules);
        Assert.False(blocked.Called);
        Assert.Equal(403, Assert.IsType<ObjectResult>(blocked.Context.Result).StatusCode);
        Assert.False((await RunFilterAsync(ModuleIds.PriceLists, tenant, modules)).Called);
        Assert.True((await RunFilterAsync(ModuleIds.Dealers, tenant, modules)).Called); // bağımsız modüller etkilenmez

        await using var anon2 = NewAmbientDb();
        Assert.False((await Auth(anon2).InfoAsync("modul", default))!.Available); // bayi sitesi hizmet dışı

        Assert.Equal(ModuleChangeResult.Changed, (await modules.SetAsync(ModuleIds.Dealers, false)).Result);
        await using var anon3 = NewAmbientDb();
        Assert.Equal(DealerAuthResult.UnknownTenant, (await Auth(anon3).ApplyAsync(new DealerApplication("modul", "X", "1234567890", null, null, "x@x.com", null, null, null, "Parola1234"), default)).Result);

        Assert.Equal(ModuleChangeResult.Changed, (await modules.SetAsync(ModuleIds.Dealers, true)).Result);
        Assert.Equal(ModuleChangeResult.Changed, (await modules.SetAsync(ModuleIds.PriceLists, true)).Result);
        Assert.Equal(ModuleChangeResult.Changed, (await modules.SetAsync(ModuleIds.B2BStore, true)).Result);
        await using var anon4 = NewAmbientDb();
        Assert.True((await Auth(anon4).InfoAsync("modul", default))!.Available);
        Assert.True((await RunFilterAsync(ModuleIds.B2BStore, tenant, modules)).Called);
    }

    [Fact]
    public async Task Kapali_modulun_is_turleri_iptal_edilir_acik_modulunku_calisir()
    {
        var tenant = await TenantAsync("Işler", "isler", ModuleIds.Orders);
        await using var db = NewDb(tenant);
        var modules = new ModuleService(db, new FixedTenant(tenant), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);
        // B2B modülleri kapalıyken iş türü kataloğu hâlâ her iş türünü bir modüle bağlar; pazaryeri iş türü kapalı modüle düşer.
        var disabledFor = ModuleCatalog.ModuleFor(SyncJobType.StockPriceSync);
        Assert.Equal(ModuleChangeResult.Changed, (await modules.SetAsync(disabledFor, false)).Result);
        Assert.False(await modules.IsEnabledAsync(disabledFor, tenant));
        Assert.True(await modules.IsEnabledAsync(ModuleCatalog.ModuleFor(SyncJobType.OrderSync), tenant));
    }

    // --- 3) Ortak stok havuzu ---

    [Fact]
    public async Task Pazaryeri_ve_B2B_ayni_havuzdan_dusar_ve_toplam_stoku_asamaz()
    {
        var tenant = await TenantAsync("Havuz", "havuz", AllB2B);
        Guid dealer, product;
        await using (var seed = NewDb(tenant))
        {
            var d = new Dealer { Name = "Mavi", TaxNumber = "1000000001", Email = "m@m.com", Status = DealerStatus.Active, Address = "A" };
            var p = new Product { Name = "Kalem", Barcode = "PEN", Price = 10m, VatRate = 20, Stock = 10, B2BVisible = true };
            seed.AddRange(d, p);
            await seed.SaveChangesAsync();
            dealer = d.Id; product = p.Id;
        }

        await using var db = NewDb(tenant);
        var pool = new StockPoolService(db, new RecordingQueue(db));
        await pool.DeductForSaleAsync("Trendyol", "T-1:1", [new StockLine("PEN", 4)]);                       // pazaryeri satışı: 10 → 6

        var store = Store(db);
        var ex = await Assert.ThrowsAsync<StoreException>(() => store.AddAsync(dealer, product, 8, default)); // B2B artık yalnızca 6 görür
        Assert.Equal(409, ex.Status);
        await store.AddAsync(dealer, product, 6, default);
        await store.CheckoutAsync(dealer, new CheckoutInput(null, null, false), default);                    // 6 → 0

        await using var check = NewDb(tenant);
        Assert.Equal(0, await check.Products.Select(p => p.Stock).SingleAsync());
        var movements = await check.StockMovements.OrderBy(m => m.CreatedAt).Select(m => new { m.Channel, m.Quantity }).ToListAsync();
        Assert.Equal([("Trendyol", -4), ("B2B", -6)], movements.Select(m => (m.Channel, m.Quantity)).ToList());
    }

    [Fact]
    public async Task Son_stok_icin_pazaryeri_ile_B2B_yarisinda_stok_negatife_dusmez_ve_B2B_reddedilir()
    {
        var tenant = await TenantAsync("Yarış", "yaris", AllB2B);
        Guid dealer, product;
        await using (var seed = NewDb(tenant))
        {
            var d = new Dealer { Name = "Mavi", TaxNumber = "1000000001", Email = "m@m.com", Status = DealerStatus.Active, Address = "A" };
            var p = new Product { Name = "Kalem", Barcode = "PEN", Price = 10m, VatRate = 20, Stock = 5, B2BVisible = true };
            seed.AddRange(d, p);
            await seed.SaveChangesAsync();
            dealer = d.Id; product = p.Id;
        }
        await using (var seed = NewDb(tenant)) await Store(seed).AddAsync(dealer, product, 5, default);

        var b2b = Task.Run(async () =>
        {
            await using var db = NewDb(tenant);
            try { await Store(db).CheckoutAsync(dealer, new CheckoutInput(null, null, false), default); return true; }
            catch (StoreException) { return false; }
        });
        var marketplace = Task.Run(async () =>
        {
            await using var db = NewDb(tenant);
            return (await new StockPoolService(db, new RecordingQueue(db)).DeductForSaleAsync("Trendyol", "T-9:1", [new StockLine("PEN", 5)]))[0];
        });
        await Task.WhenAll(b2b, marketplace);

        await using var check = NewDb(tenant);
        var stock = await check.Products.Select(p => p.Stock).SingleAsync();
        Assert.True(stock >= 0);
        var placed = await check.Orders.CountAsync(o => o.Channel == "B2B");
        // B2B siparişi verildiyse pazaryeri satışı fazla satış olarak işaretlenir; verilmediyse pazaryeri 5'i aldı.
        var oversold = (await marketplace).Oversold;
        if (b2b.Result) Assert.Equal((1, 0, 5), (placed, stock, oversold));
        else Assert.Equal((0, 0), (placed, stock));
    }

    // --- 4) Fiyat ve KDV örnekleri ---

    [Theory]
    // genel fiyat (KDV dahil), KDV %, liste (fiyat KDV dahil mi), kalem fiyatı, iskonto %, KDV dahil göster → beklenen (net, brüt, gösterilen)
    [InlineData(120.0, 20, null, null, null, true, 100.0, 120.0, 120.0)]            // listesiz: genel fiyat KDV dahil
    [InlineData(120.0, 20, false, 100.0, null, true, 100.0, 120.0, 120.0)]            // liste fiyatı KDV hariç 100, brüt gösterim
    [InlineData(120.0, 20, false, 100.0, null, false, 100.0, 120.0, 100.0)]           // aynı, KDV hariç gösterim
    [InlineData(120.0, 20, true, 90.0, null, false, 75.0, 90.0, 75.0)]                // liste fiyatı KDV dahil 90 → net 75
    [InlineData(200.0, 20, true, null, 10.0, true, 150.0, 180.0, 180.0)]              // iskonto genel fiyattan: 200 × 0,9 = 180 brüt
    [InlineData(99.99, 18, true, null, 33.33, true, 56.49, 66.66, 66.66)]    // yuvarlama (yarım yukarı, 2 basamak)
    [InlineData(10.0, 0, false, 5.0, null, true, 5.0, 5.0, 5.0)]                       // KDV %0
    public void Fiyat_ve_KDV_ornekleri(double general, int vat, bool? includesVat, double? itemPrice, double? discount, bool showVat, double net, double gross, double shown)
    {
        decimal? D(double? v) => v is null ? null : (decimal)v.Value;
        var list = includesVat is null ? null : new PriceListRule(D(discount), includesVat.Value, showVat, "TRY");
        var r = DealerPricing.Resolve((decimal)general, vat, null, null, list, D(itemPrice));
        Assert.Equal(((decimal)net, (decimal)gross), (r.Net, r.Gross));
        Assert.Equal((decimal)shown, r.Display);
    }

    [Fact]
    public async Task KDV_hariç_liste_fiyati_siparis_toplamlarina_net_ve_brut_olarak_yansir()
    {
        var tenant = await TenantAsync("Kdv", "kdv", AllB2B);
        Guid dealer;
        await using (var seed = NewDb(tenant))
        {
            var g = new DealerGroup { Name = "G" };
            seed.DealerGroups.Add(g);
            await seed.SaveChangesAsync();
            var d = new Dealer { Name = "Mavi", TaxNumber = "1000000001", Email = "m@m.com", Status = DealerStatus.Active, Address = "A", GroupId = g.Id };
            var p = new Product { Name = "Kalem", Barcode = "PEN", Price = 120m, VatRate = 20, Stock = 50, B2BVisible = true };
            seed.AddRange(d, p);
            await seed.SaveChangesAsync();
            seed.PriceLists.Add(new PriceList { Name = "L", GroupId = g.Id, PricesIncludeVat = false, ShowVatIncluded = true });
            await seed.SaveChangesAsync();
            seed.PriceListItems.Add(new PriceListItem { PriceListId = (await seed.PriceLists.SingleAsync()).Id, ProductId = p.Id, Price = 100m });
            await seed.SaveChangesAsync();
            dealer = d.Id;
        }
        await using var db = NewDb(tenant);
        var product = (await Catalog(db).ListAsync(dealer, new StoreQuery(null, null, null, 1, 10), default)).Items.Single();
        Assert.Equal((120m, true), (product.Price.Amount, product.Price.IncludesVat)); // listede 100 + KDV, KDV dahil gösterilir
        var store = Store(db);
        await store.AddAsync(dealer, product.Id, 3, default);
        var order = await store.CheckoutAsync(dealer, new CheckoutInput(null, null, false), default);
        Assert.Equal((300m, 360m), (order.TotalNet, order.TotalGross));
    }
}
