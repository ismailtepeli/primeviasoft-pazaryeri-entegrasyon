using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Auth;
using Pazaryeri.Application.Dealers;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Notifications;
using Pazaryeri.Application.Orders;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Auth;
using Pazaryeri.Infrastructure.Dealers;
using Pazaryeri.Infrastructure.Modules;
using Pazaryeri.Infrastructure.Notifications;
using Pazaryeri.Infrastructure.Orders;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Pricing;
using Pazaryeri.Infrastructure.Stock;

namespace Pazaryeri.Tests;

public class NotificationTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Notifications;Trusted_Connection=True;TrustServerCertificate=True";

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

    private static NotificationService Notifier(AppDbContext db) => new(db);

    private static B2BOrderService Orders(AppDbContext db) =>
        new(db, new StoreCatalog(db), new DealerPriceService(db), new StockPoolService(db, new RecordingQueue(db)), TimeProvider.System, Notifier(db));

    private static B2BAdminOrderService AdminOrders(AppDbContext db) =>
        new(db, new StockPoolService(db, new RecordingQueue(db)), TimeProvider.System, Notifier(db));

    public async Task InitializeAsync()
    {
        await using var db = NewDb(null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record World(Guid Tenant, Guid Dealer, Guid Other, Guid Pen);

    private static async Task<World> SeedAsync(string name, int stock = 10)
    {
        await using var root = NewDb(null);
        var tenant = new Tenant { Name = name };
        root.Tenants.Add(tenant);
        await root.SaveChangesAsync();
        await using var db = NewDb(tenant.Id);
        var dealer = new Dealer { Name = "Mavi", TaxNumber = "1000000001", Email = "m@m.com", Status = DealerStatus.Active, Address = "İstanbul" };
        var other = new Dealer { Name = "Diğer", TaxNumber = "1000000002", Email = "d@d.com", Status = DealerStatus.Active, Address = "Ankara" };
        var pen = new Product { Name = "Kalem", Barcode = "PEN", Price = 10m, VatRate = 20, Stock = stock, B2BVisible = true, MinOrderQuantity = 1, PackSize = 1 };
        db.AddRange(dealer, other, pen);
        await db.SaveChangesAsync();
        return new World(tenant.Id, dealer.Id, other.Id, pen.Id);
    }

    private static async Task<Guid> OrderAsync(World w, Guid dealer, int qty)
    {
        await using var db = NewDb(w.Tenant);
        var s = Orders(db);
        await s.AddAsync(dealer, w.Pen, qty, default);
        return (await s.CheckoutAsync(dealer, new CheckoutInput(null, null, false), default)).Id;
    }

    // --- yayım ve okuma ---

    [Fact]
    public async Task Siparis_verilince_yoneticiye_ve_bayiye_bildirim_dusar_bayiye_siparis_baglantisi_verilir()
    {
        var w = await SeedAsync("Yeni");
        var orderId = await OrderAsync(w, w.Dealer, 3);

        await using var db = NewDb(w.Tenant);
        var n = Notifier(db);
        var admin = await n.ListAsync(NotificationAudience.Admin, null, default);
        var item = Assert.Single(admin.Items);
        Assert.Equal((NotificationKinds.B2BOrderNew, 1, false), (item.Kind, admin.Unread, item.Read));
        Assert.Contains("Mavi", item.Body);

        var dealer = await n.ListAsync(NotificationAudience.Dealer, w.Dealer, default);
        var mine = Assert.Single(dealer.Items);
        Assert.Equal((NotificationKinds.OrderReceived, $"/siparis/{orderId}"), (mine.Kind, mine.Link));
        Assert.Empty((await n.ListAsync(NotificationAudience.Dealer, w.Other, default)).Items); // başka bayi görmez
    }

    [Fact]
    public async Task Okundu_isaretleme_yalnizca_kendi_bildirimine_islenir_ve_tumunu_okundu_yapar()
    {
        var w = await SeedAsync("Okundu");
        await OrderAsync(w, w.Dealer, 1);
        await OrderAsync(w, w.Other, 1);

        await using var db = NewDb(w.Tenant);
        var n = Notifier(db);
        var theirs = (await n.ListAsync(NotificationAudience.Dealer, w.Other, default)).Items.Single();
        Assert.False(await n.MarkReadAsync(NotificationAudience.Dealer, w.Dealer, theirs.Id, default)); // başkasının bildirimi
        Assert.Equal(1, await n.UnreadCountAsync(NotificationAudience.Dealer, w.Other, default));

        var mine = (await n.ListAsync(NotificationAudience.Dealer, w.Dealer, default)).Items.Single();
        Assert.True(await n.MarkReadAsync(NotificationAudience.Dealer, w.Dealer, mine.Id, default));
        Assert.Equal(0, await n.UnreadCountAsync(NotificationAudience.Dealer, w.Dealer, default));

        Assert.Equal(2, await n.UnreadCountAsync(NotificationAudience.Admin, null, default));
        await n.MarkAllReadAsync(NotificationAudience.Admin, null, default);
        Assert.Equal(0, await n.UnreadCountAsync(NotificationAudience.Admin, null, default));
        Assert.Equal(1, await n.UnreadCountAsync(NotificationAudience.Dealer, w.Other, default)); // diğer kitle etkilenmez
    }

    [Fact]
    public async Task Ayni_anahtar_tek_bildirim_uretir_ve_dis_adres_baglantisi_atilir()
    {
        var w = await SeedAsync("Anahtar");
        await using var db = NewDb(w.Tenant);
        var n = Notifier(db);
        await n.NotifyAdminAsync("x", "Başlık", null, "/siparisler", "k1");
        await n.NotifyAdminAsync("x", "Başlık", null, "/siparisler", "k1");
        await n.NotifyAdminAsync("x", "Dış bağlantı", null, "https://kotu.example/", "k2");
        await n.NotifyAdminAsync("x", "Protokol göreli", null, "//kotu.example/", "k3");

        var items = (await n.ListAsync(NotificationAudience.Admin, null, default)).Items;
        Assert.Equal(3, items.Count);
        Assert.Null(items.Single(i => i.Title == "Dış bağlantı").Link);
        Assert.Null(items.Single(i => i.Title == "Protokol göreli").Link);
        Assert.Equal("/siparisler", items.Single(i => i.Title == "Başlık").Link);
    }

    [Fact]
    public async Task Kiracilar_birbirinin_bildirimini_gormez()
    {
        var a = await SeedAsync("KiracıA");
        var b = await SeedAsync("KiracıB");
        await OrderAsync(a, a.Dealer, 1);

        await using var dbB = NewDb(b.Tenant);
        Assert.Empty((await Notifier(dbB).ListAsync(NotificationAudience.Admin, null, default)).Items);
    }

    // --- süreç değişiklikleri ---

    [Fact]
    public async Task Yonetici_siparisi_onaylayip_sevk_edince_bayiye_bildirim_gider_tekrar_islem_cift_bildirim_yazmaz()
    {
        var w = await SeedAsync("Surec");
        var id = await OrderAsync(w, w.Dealer, 2);

        await using (var db = NewDb(w.Tenant)) await AdminOrders(db).ActAsync(id, B2BAction.Approve, new B2BOrderActionInput(null, null, null), default);
        await using (var db = NewDb(w.Tenant)) await AdminOrders(db).ActAsync(id, B2BAction.Ship, new B2BOrderActionInput(null, "Yurtiçi", "TR123"), default);

        await using var check = NewDb(w.Tenant);
        var items = (await Notifier(check).ListAsync(NotificationAudience.Dealer, w.Dealer, default)).Items;
        Assert.Contains(items, i => i.Kind == NotificationKinds.OrderApproved);
        var shipped = Assert.Single(items, i => i.Kind == NotificationKinds.OrderShipped);
        Assert.Contains("TR123", shipped.Body);
        Assert.Equal($"/siparis/{id}", shipped.Link);
    }

    [Fact]
    public async Task Reddedilen_siparis_gerekceyi_bayiye_iletir()
    {
        var w = await SeedAsync("Ret");
        var id = await OrderAsync(w, w.Dealer, 2);
        await using (var db = NewDb(w.Tenant)) await AdminOrders(db).ActAsync(id, B2BAction.Reject, new B2BOrderActionInput("Ürün üretimden kalktı", null, null), default);

        await using var check = NewDb(w.Tenant);
        var rejected = (await Notifier(check).ListAsync(NotificationAudience.Dealer, w.Dealer, default)).Items.Single(i => i.Kind == NotificationKinds.OrderRejected);
        Assert.Equal("Ürün üretimden kalktı", rejected.Body);
    }

    [Fact]
    public async Task Bayi_siparisi_iptal_edince_yoneticiye_bildirim_gider()
    {
        var w = await SeedAsync("Iptal");
        var id = await OrderAsync(w, w.Dealer, 2);
        await using (var db = NewDb(w.Tenant)) await Orders(db).CancelAsync(w.Dealer, id, default);

        await using var check = NewDb(w.Tenant);
        Assert.Contains((await Notifier(check).ListAsync(NotificationAudience.Admin, null, default)).Items, i => i.Kind == NotificationKinds.B2BOrderCancelled);
    }

    [Fact]
    public async Task Stok_yetmeyen_siparis_bildirim_uretmez_yalnizca_basarili_siparis_bildirilir()
    {
        var w = await SeedAsync("Kacan", stock: 3);
        await using (var db = NewDb(w.Tenant)) await Orders(db).AddAsync(w.Dealer, w.Pen, 3, default);
        await using (var db = NewDb(w.Tenant)) await Orders(db).AddAsync(w.Other, w.Pen, 3, default);
        await using (var db = NewDb(w.Tenant)) await Orders(db).CheckoutAsync(w.Dealer, new CheckoutInput(null, null, false), default);
        await using (var db = NewDb(w.Tenant))
            await Assert.ThrowsAsync<StoreException>(() => Orders(db).CheckoutAsync(w.Other, new CheckoutInput(null, null, false), default));

        await using var check = NewDb(w.Tenant);
        var admin = (await Notifier(check).ListAsync(NotificationAudience.Admin, null, default)).Items;
        Assert.Equal(1, admin.Count(i => i.Kind == NotificationKinds.B2BOrderNew));
        Assert.Contains(admin, i => i.Kind == NotificationKinds.B2BOrderNew && i.Title.Contains("B2B"));
        Assert.DoesNotContain((await Notifier(check).ListAsync(NotificationAudience.Dealer, w.Other, default)).Items, i => i.Kind == NotificationKinds.OrderReceived);
    }

    [Fact]
    public async Task Bayi_basvurusu_yoneticiye_bildirim_dusurur()
    {
        await using var root = NewDb(null);
        var tenant = new Tenant { Name = "Basvuru", Slug = "basvuru" };
        root.Tenants.Add(tenant);
        await root.SaveChangesAsync();
        await using (var seed = NewDb(tenant.Id)) { seed.TenantModules.Add(new TenantModule { ModuleId = ModuleIds.Dealers, Enabled = true }); await seed.SaveChangesAsync(); }

        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new AmbientProvider()))
        {
            var modules = new ModuleService(db, new FixedTenant(tenant.Id), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);
            var auth = new DealerAuthService(db, new JwtTokenService(Options.Create(new JwtOptions { Key = new string('k', 40) })), modules, TimeProvider.System, notifier: Notifier(db));
            var result = await auth.ApplyAsync(new DealerApplication("basvuru", "Mavi Ticaret", "1234567890", "Kadıköy", "Ayşe", "ayse@mavi.com", "0555", "İstanbul", "Adres", "Parola123"), default);
            Assert.Equal(DealerAuthResult.Ok, result.Result);
        }

        await using var check = NewDb(tenant.Id);
        var item = Assert.Single((await Notifier(check).ListAsync(NotificationAudience.Admin, null, default)).Items);
        Assert.Equal((NotificationKinds.DealerApplication, "/bayiler"), (item.Kind, item.Link));
        Assert.Contains("Mavi Ticaret", item.Body);
    }
}
