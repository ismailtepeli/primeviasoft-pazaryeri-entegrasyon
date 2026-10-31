using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Orders;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Pricing;
using Pazaryeri.Infrastructure.Stock;

namespace Pazaryeri.Tests;

public class B2BOrderTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_B2BOrders;Trusted_Connection=True;TrustServerCertificate=True";

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

    private static B2BOrderService Service(AppDbContext db) =>
        new(db, new StoreCatalog(db), new DealerPriceService(db), new StockPoolService(db, new RecordingQueue(db)), TimeProvider.System);

    public async Task InitializeAsync()
    {
        await using var db = NewDb(null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record World(Guid Tenant, Guid DealerId, Guid OtherDealerId, Guid Pen, Guid Notebook, Guid Gift);

    /// <summary>Kalem (stok 100, min 10, koli 5, 10 ₺ + %20 KDV), Defter (stok 8, güvenlik stoğu 3), Hediye (B2B'de gizli).</summary>
    private static async Task<World> SeedAsync(string name, int penStock = 100)
    {
        await using var root = NewDb(null);
        var tenant = new Tenant { Name = name };
        root.Tenants.Add(tenant);
        await root.SaveChangesAsync();
        await using var db = NewDb(tenant.Id);
        var dealer = new Dealer { Name = "Mavi", TaxNumber = "1000000001", Email = "m@m.com", Status = DealerStatus.Active, Address = "İstanbul, Kadıköy" };
        var other = new Dealer { Name = "Diğer", TaxNumber = "1000000002", Email = "d@d.com", Status = DealerStatus.Active, Address = "Ankara" };
        var pen = new Product { Name = "Kalem", Barcode = "PEN", Price = 12m, VatRate = 20, Stock = penStock, B2BVisible = true, MinOrderQuantity = 10, PackSize = 5 };
        var notebook = new Product { Name = "Defter", Barcode = "NB", Price = 60m, VatRate = 20, Stock = 8, SafetyStock = 3, B2BVisible = true };
        var gift = new Product { Name = "Hediye", Barcode = "GF", Price = 5m, Stock = 50, B2BVisible = false };
        db.AddRange(dealer, other, pen, notebook, gift);
        await db.SaveChangesAsync();
        return new World(tenant.Id, dealer.Id, other.Id, pen.Id, notebook.Id, gift.Id);
    }

    private static async Task<int> StockOfAsync(Guid tenant, string barcode)
    {
        await using var db = NewDb(tenant);
        return await db.Products.Where(p => p.Barcode == barcode).Select(p => p.Stock).SingleAsync();
    }

    private static async Task<StoreException> FailAsync(Func<Task> action) => await Assert.ThrowsAsync<StoreException>(action);

    // --- sepet ---

    [Fact]
    public async Task Sepete_ekleme_adetleri_toplar_ve_fiyatla_toplami_hesaplar()
    {
        var w = await SeedAsync("Sepet");
        await using var db = NewDb(w.Tenant);
        var s = Service(db);

        var cart = await s.AddAsync(w.DealerId, w.Pen, 10, default);
        cart = await s.AddAsync(w.DealerId, w.Pen, 5, default); // 10 + 5 = 15, koli katı
        var line = cart.Lines.Single();
        Assert.Equal((15, 12m, false), (line.Quantity, line.Price.Amount, cart.HasIssues));
        Assert.Equal((150m, 180m, 15), (cart.TotalNet, cart.TotalGross, cart.ItemCount)); // 15 × 12 KDV hariç? Genel fiyat KDV dahil: brüt 180, net 150
    }

    [Fact]
    public async Task Sepet_kurallari_minimum_koli_stok_gorunurluk_ve_guvenlik_stogu()
    {
        var w = await SeedAsync("Kural");
        await using var db = NewDb(w.Tenant);
        var s = Service(db);

        Assert.Equal(400, (await FailAsync(() => s.AddAsync(w.DealerId, w.Pen, 5, default))).Status);    // minimum 10
        Assert.Equal(400, (await FailAsync(() => s.AddAsync(w.DealerId, w.Pen, 12, default))).Status);   // koli 5'in katı değil
        Assert.Equal(409, (await FailAsync(() => s.AddAsync(w.DealerId, w.Notebook, 6, default))).Status); // satılabilir 8-3=5
        Assert.Equal(404, (await FailAsync(() => s.AddAsync(w.DealerId, w.Gift, 1, default))).Status);   // gizli ürün
        Assert.Equal(404, (await FailAsync(() => s.AddAsync(w.DealerId, Guid.NewGuid(), 1, default))).Status);
        Assert.Equal(400, (await FailAsync(() => s.AddAsync(w.DealerId, w.Notebook, -1, default))).Status);
        Assert.Empty((await s.GetCartAsync(w.DealerId, default)).Lines);

        Assert.Single((await s.AddAsync(w.DealerId, w.Notebook, 5, default)).Lines);
    }

    [Fact]
    public async Task Sepet_guncelleme_silme_ve_bayiler_arasi_yalitim()
    {
        var w = await SeedAsync("Yalitim");
        await using var db = NewDb(w.Tenant);
        var s = Service(db);
        await s.AddAsync(w.DealerId, w.Pen, 10, default);
        var updated = await s.SetAsync(w.DealerId, w.Pen, 20, default);
        Assert.Equal(20, updated.Lines.Single().Quantity);

        Assert.Empty((await s.GetCartAsync(w.OtherDealerId, default)).Lines); // başka bayinin sepeti ayrı
        Assert.Empty((await s.SetAsync(w.DealerId, w.Pen, 0, default)).Lines);
        await s.AddAsync(w.DealerId, w.Pen, 10, default);
        Assert.Empty((await s.ClearAsync(w.DealerId, default)).Lines);
    }

    [Fact]
    public async Task Sonradan_gizlenen_urun_stoku_biten_urun_ve_fiyat_degisimi_sepette_uyari_verir()
    {
        var w = await SeedAsync("Uyari");
        await using var db = NewDb(w.Tenant);
        var s = Service(db);
        await s.AddAsync(w.DealerId, w.Pen, 10, default);
        await s.AddAsync(w.DealerId, w.Notebook, 5, default);

        await using var admin = NewDb(w.Tenant);
        (await admin.Products.SingleAsync(p => p.Id == w.Pen)).B2BVisible = false;                // artık satışta değil
        var nb = await admin.Products.SingleAsync(p => p.Id == w.Notebook);
        nb.Stock = 4; nb.Price = 66m;                                                              // stok düştü ve fiyat değişti
        await admin.SaveChangesAsync();

        var cart = await s.GetCartAsync(w.DealerId, default);
        Assert.True(cart.HasIssues);
        Assert.Equal("unavailable", cart.Lines.Single(l => l.ProductId == w.Pen).Issue!.Kind);
        var notebook = cart.Lines.Single(l => l.ProductId == w.Notebook);
        Assert.Equal("stock", notebook.Issue!.Kind);
        Assert.Equal((66m, 60m), (notebook.Price.Amount, notebook.PriceAtAdd)); // ekranda "fiyat değişti" uyarısı için
    }

    // --- sipariş ---

    [Fact]
    public async Task Siparis_B2B_kanalinda_olusur_stoku_dusurur_sepeti_bosaltir_ve_toplamlari_yazar()
    {
        var w = await SeedAsync("Siparis");
        await using var db = NewDb(w.Tenant);
        var s = Service(db);
        await s.AddAsync(w.DealerId, w.Pen, 20, default);
        await s.AddAsync(w.DealerId, w.Notebook, 2, default);

        var order = await s.CheckoutAsync(w.DealerId, new CheckoutInput(null, "Acil", false), default);
        Assert.StartsWith("B2B-", order.OrderNumber);
        Assert.Equal(("New", "İstanbul, Kadıköy", "Acil", true), (order.Status, order.ShippingAddress, order.Note, order.CanCancel)); // adres bayi kaydından
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(20 * 12m + 2 * 60m, order.TotalGross);
        Assert.Equal(Math.Round((20 * 12m + 2 * 60m) / 1.2m, 2), order.TotalNet);

        Assert.Equal(80, await StockOfAsync(w.Tenant, "PEN"));
        Assert.Equal(6, await StockOfAsync(w.Tenant, "NB"));
        Assert.Empty((await s.GetCartAsync(w.DealerId, default)).Lines);

        await using var check = NewDb(w.Tenant);
        var saved = await check.Orders.Include(o => o.Packages).ThenInclude(p => p.Lines).SingleAsync();
        Assert.Equal(("B2B", w.DealerId, OrderStatus.New, "Mavi"), (saved.Channel, saved.DealerId, saved.Status, saved.CustomerName));
        Assert.All(saved.Packages.Single().Lines, l => Assert.True(l.StockDeducted));
    }

    [Fact]
    public async Task Bos_sepet_ve_adres_eksikligi_reddedilir()
    {
        var w = await SeedAsync("Bos");
        await using var db = NewDb(w.Tenant);
        var s = Service(db);
        Assert.Equal(409, (await FailAsync(() => s.CheckoutAsync(w.DealerId, new CheckoutInput(null, null, false), default))).Status);

        await using (var admin = NewDb(w.Tenant)) { (await admin.Dealers.SingleAsync(d => d.Id == w.DealerId)).Address = null; await admin.SaveChangesAsync(); }
        await s.AddAsync(w.DealerId, w.Pen, 10, default);
        Assert.Equal(400, (await FailAsync(() => s.CheckoutAsync(w.DealerId, new CheckoutInput(" ", null, false), default))).Status);
        Assert.Single((await s.GetCartAsync(w.DealerId, default)).Lines); // sepet korunur
        Assert.NotNull(await s.CheckoutAsync(w.DealerId, new CheckoutInput("Adres 1", null, false), default));
    }

    [Fact]
    public async Task Kayitli_adres_bilgisi_doner_adresi_olmayan_bayi_adres_girmek_zorundadir_ve_istenirse_kaydedilir()
    {
        var w = await SeedAsync("AdresKayit");
        await using var db = NewDb(w.Tenant);
        var s = Service(db);

        Assert.Equal("İstanbul, Kadıköy", (await s.CheckoutInfoAsync(w.DealerId, default)).SavedAddress);   // kayıtlı adres var
        Assert.Null((await s.CheckoutInfoAsync(Guid.NewGuid(), default)).SavedAddress);   // bilinmeyen bayi: adres yok

        await using (var admin = NewDb(w.Tenant)) { (await admin.Dealers.SingleAsync(d => d.Id == w.DealerId)).Address = "  "; await admin.SaveChangesAsync(); }
        Assert.Null((await s.CheckoutInfoAsync(w.DealerId, default)).SavedAddress);                         // boşluk adres sayılmaz

        // adres yok ve girilmedi: zorunlu
        await s.AddAsync(w.DealerId, w.Pen, 10, default);
        Assert.Equal(400, (await FailAsync(() => s.CheckoutAsync(w.DealerId, new CheckoutInput(null, null, false, SaveAddress: true), default))).Status);

        // adres girildi, kaydet seçili değil: siparişte kalır, bayi kaydı boş
        var first = await s.CheckoutAsync(w.DealerId, new CheckoutInput("Ankara, Çankaya", null, false), default);
        Assert.Equal("Ankara, Çankaya", first.ShippingAddress);
        Assert.Null((await s.CheckoutInfoAsync(w.DealerId, default)).SavedAddress);

        // adres girildi ve kaydet seçili: bayinin kayıtlı adresi olur, sonraki sipariş boş adresle verilebilir
        await s.AddAsync(w.DealerId, w.Pen, 10, default);
        await s.CheckoutAsync(w.DealerId, new CheckoutInput("İzmir, Konak", null, false, SaveAddress: true), default);
        Assert.Equal("İzmir, Konak", (await s.CheckoutInfoAsync(w.DealerId, default)).SavedAddress);
        await s.AddAsync(w.DealerId, w.Pen, 10, default);
        Assert.Equal("İzmir, Konak", (await s.CheckoutAsync(w.DealerId, new CheckoutInput(null, null, false), default)).ShippingAddress);

        // kayıtlı adres doluyken kaydet seçilse de mevcut adres ezilmez
        await s.AddAsync(w.DealerId, w.Pen, 10, default);
        var third = await s.CheckoutAsync(w.DealerId, new CheckoutInput("Bursa, Nilüfer", null, false, SaveAddress: true), default);
        Assert.Equal("Bursa, Nilüfer", third.ShippingAddress);                                               // bu siparişin adresi girilen
        Assert.Equal("İzmir, Konak", (await s.CheckoutInfoAsync(w.DealerId, default)).SavedAddress);        // kayıtlı adres korunur
    }

    [Fact]
    public async Task Stok_yetmezse_siparis_reddedilir_stok_ve_sepet_degismez_kismi_karsilama_secenegi_calisir()
    {
        var w = await SeedAsync("Kismi");
        await using var db = NewDb(w.Tenant);
        var s = Service(db);
        await s.AddAsync(w.DealerId, w.Pen, 50, default);
        await s.AddAsync(w.DealerId, w.Notebook, 5, default);
        await using (var admin = NewDb(w.Tenant)) { (await admin.Products.SingleAsync(p => p.Id == w.Pen)).Stock = 23; await admin.SaveChangesAsync(); } // 50 istendi, 23 var

        var rejected = await FailAsync(() => s.CheckoutAsync(w.DealerId, new CheckoutInput(null, null, false), default));
        Assert.Equal(409, rejected.Status);
        Assert.Equal((23, 8), (await StockOfAsync(w.Tenant, "PEN"), await StockOfAsync(w.Tenant, "NB")));
        Assert.Equal(2, (await s.GetCartAsync(w.DealerId, default)).Lines.Count);

        // Kısmi: kalem 23 → koli katı 20 (min 10 ≥); defter eksiksiz.
        var order = await s.CheckoutAsync(w.DealerId, new CheckoutInput(null, null, true), default);
        Assert.Equal(20, order.Lines.Single(l => l.Code == "PEN").Quantity);
        Assert.Equal(3, await StockOfAsync(w.Tenant, "PEN"));
        var rest = await s.GetCartAsync(w.DealerId, default);
        Assert.Equal((1, 30), (rest.Lines.Count, rest.Lines.Single().Quantity)); // karşılanamayan 30 adet sepette kalır
    }

    [Fact]
    public async Task Kismi_karsilamada_hic_urun_kalmazsa_reddedilir()
    {
        var w = await SeedAsync("Hic");
        await using var db = NewDb(w.Tenant);
        var s = Service(db);
        await s.AddAsync(w.DealerId, w.Pen, 10, default);
        await using (var admin = NewDb(w.Tenant)) { (await admin.Products.SingleAsync(p => p.Id == w.Pen)).Stock = 4; await admin.SaveChangesAsync(); }
        Assert.Equal(409, (await FailAsync(() => s.CheckoutAsync(w.DealerId, new CheckoutInput(null, null, true), default))).Status);
        Assert.Equal(4, await StockOfAsync(w.Tenant, "PEN"));
    }

    [Fact]
    public async Task Son_stok_icin_eszamanli_iki_siparisten_yalnizca_biri_olusur_asiri_satis_olmaz()
    {
        var w = await SeedAsync("Yaris", penStock: 10);
        var dealers = new[] { w.DealerId, w.OtherDealerId };
        foreach (var d in dealers)
        {
            await using var db = NewDb(w.Tenant);
            await Service(db).AddAsync(d, w.Pen, 10, default);
        }

        var results = await Task.WhenAll(dealers.Select(async d =>
        {
            await using var db = NewDb(w.Tenant);
            try { await Service(db).CheckoutAsync(d, new CheckoutInput(null, null, false), default); return true; }
            catch (StoreException) { return false; }
        }));

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(0, await StockOfAsync(w.Tenant, "PEN")); // negatife düşmez, fazla satış yok
        await using var check = NewDb(w.Tenant);
        Assert.Equal(1, await check.Orders.CountAsync());
        // Kaybeden bayinin sepeti korunur.
        var loser = results[0] ? w.OtherDealerId : w.DealerId;
        Assert.Single((await Service(check).GetCartAsync(loser, default)).Lines);
    }

    [Fact]
    public async Task Ayni_sepetten_eszamanli_cift_siparis_tek_siparis_uretir()
    {
        var w = await SeedAsync("Cift");
        await using (var db = NewDb(w.Tenant)) await Service(db).AddAsync(w.DealerId, w.Pen, 10, default);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = NewDb(w.Tenant);
            try { await Service(db).CheckoutAsync(w.DealerId, new CheckoutInput(null, null, false), default); return true; }
            catch (StoreException) { return false; }
        }));

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(90, await StockOfAsync(w.Tenant, "PEN")); // yalnızca bir kez düştü
        await using var check = NewDb(w.Tenant);
        Assert.Equal(1, await check.Orders.CountAsync());
    }

    // --- geçmiş, iptal, tekrar sipariş ---

    [Fact]
    public async Task Gecmis_yalnizca_kendi_siparisleri_gosterir_detay_baska_bayiye_404()
    {
        var w = await SeedAsync("Gecmis");
        await using var db = NewDb(w.Tenant);
        var s = Service(db);
        await s.AddAsync(w.DealerId, w.Pen, 10, default);
        var order = await s.CheckoutAsync(w.DealerId, new CheckoutInput(null, null, false), default);
        await s.AddAsync(w.OtherDealerId, w.Pen, 10, default);
        await s.CheckoutAsync(w.OtherDealerId, new CheckoutInput(null, null, false), default);

        var page = await s.OrdersAsync(w.DealerId, 1, default);
        Assert.Equal((1, order.OrderNumber), (page.Total, page.Items.Single().OrderNumber));
        Assert.Equal(order.Id, (await s.OrderAsync(w.DealerId, order.Id, default)).Id);
        Assert.Equal(404, (await FailAsync(() => s.OrderAsync(w.OtherDealerId, order.Id, default))).Status);
        Assert.Equal(404, (await FailAsync(() => s.CancelAsync(w.OtherDealerId, order.Id, default))).Status);
    }

    [Fact]
    public async Task Iptal_stogu_geri_ekler_iki_kez_iptal_stogu_cifte_eklemez_hazirlanan_iptal_edilemez()
    {
        var w = await SeedAsync("Iptal");
        await using var db = NewDb(w.Tenant);
        var s = Service(db);
        await s.AddAsync(w.DealerId, w.Pen, 20, default);
        var order = await s.CheckoutAsync(w.DealerId, new CheckoutInput(null, null, false), default);
        Assert.Equal(80, await StockOfAsync(w.Tenant, "PEN"));

        var cancelled = await s.CancelAsync(w.DealerId, order.Id, default);
        Assert.Equal(("Cancelled", false), (cancelled.Status, cancelled.CanCancel));
        Assert.Equal(100, await StockOfAsync(w.Tenant, "PEN"));

        await using var db2 = NewDb(w.Tenant);
        await Service(db2).CancelAsync(w.DealerId, order.Id, default); // ikinci iptal etkisiz
        Assert.Equal(100, await StockOfAsync(w.Tenant, "PEN"));

        await s.AddAsync(w.DealerId, w.Pen, 10, default);
        var second = await s.CheckoutAsync(w.DealerId, new CheckoutInput(null, null, false), default);
        await using (var admin = NewDb(w.Tenant)) { (await admin.Orders.SingleAsync(o => o.Id == second.Id)).Status = OrderStatus.Preparing; await admin.SaveChangesAsync(); }
        Assert.Equal(409, (await FailAsync(() => Service(NewDb(w.Tenant)).CancelAsync(w.DealerId, second.Id, default))).Status);
        Assert.Equal(90, await StockOfAsync(w.Tenant, "PEN"));
    }

    [Fact]
    public async Task Tekrar_siparis_satirlari_sepete_ekler_olmayanlari_nedeniyle_atlar()
    {
        var w = await SeedAsync("Tekrar");
        await using var db = NewDb(w.Tenant);
        var s = Service(db);
        await s.AddAsync(w.DealerId, w.Pen, 20, default);
        await s.AddAsync(w.DealerId, w.Notebook, 2, default);
        var order = await s.CheckoutAsync(w.DealerId, new CheckoutInput(null, null, false), default);
        await using (var admin = NewDb(w.Tenant)) { (await admin.Products.SingleAsync(p => p.Id == w.Notebook)).B2BVisible = false; await admin.SaveChangesAsync(); }

        var result = await s.ReorderAsync(w.DealerId, order.Id, default);
        Assert.Equal(1, result.Added);
        Assert.Single(result.Skipped);
        Assert.Equal(20, (await s.GetCartAsync(w.DealerId, default)).Lines.Single().Quantity);
    }

    [Fact]
    public async Task Ortak_stok_havuzu_kapaliysa_siparis_stogu_etkilemez()
    {
        var w = await SeedAsync("Havuz");
        await using (var admin = NewDb(w.Tenant)) { admin.StockSettings.Add(new StockSettings { DeductAcrossChannels = false }); await admin.SaveChangesAsync(); }
        await using var db = NewDb(w.Tenant);
        var s = Service(db);
        await s.AddAsync(w.DealerId, w.Pen, 10, default);
        await s.CheckoutAsync(w.DealerId, new CheckoutInput(null, null, false), default);
        Assert.Equal(100, await StockOfAsync(w.Tenant, "PEN"));
    }
}
