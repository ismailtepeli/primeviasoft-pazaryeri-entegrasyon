using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Feeds;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Feeds;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Tests;

/// <summary>Dropshipping XML beslemesi: adresten indirme, otomatik tanıma, ürün ekleme/güncelleme, stok sıfırlama ve güvenlik.</summary>
public class ProductFeedTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Feeds;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Yerel, rastgele portlu sahte tedarikçi sunucusu.</summary>
    private sealed class Supplier : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public string Body { get; set; } = "";
        public int Status { get; set; } = 200;
        public string BaseUrl { get; }
        public string FeedUrl => BaseUrl + "/feed.xml";

        private Supplier(WebApplication app, string baseUrl) { _app = app; BaseUrl = baseUrl; }

        public static async Task<Supplier> StartAsync(Func<Supplier, Task>? configure = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            Supplier? self = null;
            app.Run(async ctx =>
            {
                if (ctx.Request.Path == "/gz")
                {
                    using var ms = new MemoryStream();
                    await using (var gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true)) await gz.WriteAsync(Encoding.UTF8.GetBytes(self!.Body));
                    ctx.Response.ContentType = "application/octet-stream"; // Content-Encoding yok: ham .gz dosyası
                    await ctx.Response.Body.WriteAsync(ms.ToArray());
                    return;
                }
                if (ctx.Request.Path == "/redirect") { ctx.Response.Redirect("/feed.xml"); return; }
                ctx.Response.StatusCode = self!.Status;
                ctx.Response.ContentType = "application/xml; charset=utf-8";
                await ctx.Response.WriteAsync(self.Body);
            });
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            self = new Supplier(app, address.TrimEnd('/'));
            if (configure is not null) await configure(self);
            return self;
        }

        public async ValueTask DisposeAsync() { await _app.StopAsync(); await _app.DisposeAsync(); }
    }

    private static AppDbContext NewDb(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    private static ProductFeedService Service(AppDbContext db, bool allowPrivate = true, TimeProvider? time = null) =>
        new(db, new FeedFetcher(Options.Create(new FeedFetcherOptions { AllowPrivateNetwork = allowPrivate })), time ?? TimeProvider.System, NullLogger<ProductFeedService>.Instance);

    public async Task InitializeAsync()
    {
        await using var db = NewDb(Guid.Empty);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> TenantAsync(string slug)
    {
        await using var root = NewDb(Guid.Empty);
        var tenant = new Tenant { Name = "Firma " + slug, Slug = slug };
        root.Tenants.Add(tenant);
        await root.SaveChangesAsync();
        return tenant.Id;
    }

    private static string Feed(params (string Code, string Name, string Price, int Stock)[] items) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><Urunler>" + string.Concat(items.Select(i =>
            $"<Urun><Barkod>{i.Code}</Barkod><UrunAdi>{i.Name}</UrunAdi><Marka>Tedarik Marka</Marka><Kategori>Giyim / Tişört</Kategori><SatisFiyati>{i.Price}</SatisFiyati>"
            + $"<KdvOrani>20</KdvOrani><StokAdedi>{i.Stock}</StokAdedi><Resim1>https://cdn.example.com/{i.Code}-1.jpg</Resim1><Resim2>https://cdn.example.com/{i.Code}-2.jpg</Resim2>"
            + "<Aciklama><![CDATA[<p>Güzel <b>ürün</b></p>]]></Aciklama></Urun>")) + "</Urunler>";

    private static ProductFeedInput Input(string url, string name = "Tedarikçi A", decimal markup = 25, bool includesVat = true, bool zeroMissing = true, bool b2c = false, int interval = 60) =>
        new(name, url, true, null, null, markup, includesVat, 20, 10, true, true, zeroMissing, b2c, false, interval);

    private static async Task<List<Product>> ProductsAsync(Guid tenant)
    {
        await using var db = NewDb(tenant);
        return await db.Products.AsNoTracking().Include(p => p.Images).OrderBy(p => p.Barcode).ToListAsync();
    }

    // --- çözümleme ve kayıt ---

    [Fact]
    public async Task Adresten_indirir_biçimi_tanir_onizleme_dondurur_hicbir_sey_kaydetmez()
    {
        var tenant = await TenantAsync("analiz");
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "Tişört", "100,00", 5), ("B2", "Pantolon", "250,50", 0));
        await using var db = NewDb(tenant);

        var a = await Service(db).AnalyzeAsync(new FeedAnalyzeRequest(supplier.FeedUrl, null, null), default);

        Assert.Equal("Urunler/Urun", a.ItemPath);
        Assert.Equal(2, a.ItemCount);
        Assert.Equal(("B1", "Tişört", 100m), (a.Preview[0].Barcode, a.Preview[0].Name, a.Preview[0].Price));
        Assert.Empty(await db.ProductFeeds.ToListAsync());
        Assert.Empty(await db.Products.ToListAsync());
    }

    [Fact]
    public async Task Yonlendirme_ve_gzip_dosya_desteklenir()
    {
        var tenant = await TenantAsync("gz");
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("G1", "Ürün", "10", 1), ("G2", "Ürün 2", "20", 2));
        await using var db = NewDb(tenant);
        var svc = Service(db);

        Assert.Equal(2, (await svc.AnalyzeAsync(new FeedAnalyzeRequest(supplier.BaseUrl + "/redirect", null, null), default)).ItemCount);
        Assert.Equal(2, (await svc.AnalyzeAsync(new FeedAnalyzeRequest(supplier.BaseUrl + "/gz", null, null), default)).ItemCount);
    }

    [Fact]
    public async Task Kayit_adresten_eslemeyi_kendisi_bulur_adresin_sorgusu_listede_gizlenir()
    {
        var tenant = await TenantAsync("kayit");
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "Tişört", "100", 5), ("B2", "Pantolon", "200", 1));
        await using var db = NewDb(tenant);

        var dto = await Service(db).SaveAsync(null, Input(supplier.FeedUrl + "?key=GIZLI-ANAHTAR"), default);

        Assert.Equal("Urunler/Urun", dto.ItemPath);
        Assert.Equal("Barkod", dto.Mapping[FeedRoles.Barcode][0]);
        Assert.DoesNotContain("GIZLI-ANAHTAR", dto.UrlDisplay);
        Assert.EndsWith("?…", dto.UrlDisplay);
        Assert.Equal(dto.Id, (await Service(db).ListAsync(default)).Single().Id);
    }

    [Fact]
    public async Task Kayit_dogrulamalari()
    {
        var tenant = await TenantAsync("dogrula");
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "Tişört", "100", 5), ("B2", "Pantolon", "200", 1));
        await using var db = NewDb(tenant);
        var svc = Service(db);

        Assert.Equal(400, (await Assert.ThrowsAsync<FeedException>(() => svc.SaveAsync(null, Input(supplier.FeedUrl, name: " "), default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<FeedException>(() => svc.SaveAsync(null, Input(supplier.FeedUrl, interval: 5), default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<FeedException>(() => svc.SaveAsync(null, Input(supplier.FeedUrl, markup: 5000), default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<FeedException>(() => svc.SaveAsync(null, Input("ftp://x/f.xml"), default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<FeedException>(() => svc.SaveAsync(Guid.NewGuid(), Input(supplier.FeedUrl), default))).Status);
        var noMapping = Input(supplier.FeedUrl) with { ItemPath = "Urunler/Urun", Mapping = new() { [FeedRoles.Name] = ["UrunAdi"] } };
        Assert.Equal(422, (await Assert.ThrowsAsync<FeedException>(() => svc.SaveAsync(null, noMapping, default))).Status);
        Assert.Empty(await db.ProductFeeds.ToListAsync());
    }

    // --- içe aktarma ---

    [Fact]
    public async Task Ilk_calistirma_urunleri_kar_marjli_fiyat_maliyet_stok_kategori_gorsel_ve_aciklamayla_ekler()
    {
        var tenant = await TenantAsync("ilk");
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "Tişört", "100,00", 5), ("B2", "Pantolon", "250,50", 0));
        await using var db = NewDb(tenant);
        var svc = Service(db);
        var feed = await svc.SaveAsync(null, Input(supplier.FeedUrl, markup: 25, b2c: true), default);

        var result = await svc.RunAsync(feed.Id, default);

        Assert.Equal(("Success", 2, 0, 0), (result.Status, result.Created, result.Updated, result.Failed));
        var products = await ProductsAsync(tenant);
        var p = products[0];
        Assert.Equal(("B1", "Tişört", "Tedarik Marka", 5, 100m, 125m), (p.Barcode, p.Name, p.Brand, p.Stock, p.Cost, p.Price));
        Assert.Equal(feed.Id, p.FeedId);
        Assert.True(p.B2CVisible);
        Assert.False(p.B2BVisible);
        Assert.Equal("Güzel ürün", p.Description);
        Assert.Equal(["https://cdn.example.com/B1-1.jpg", "https://cdn.example.com/B1-2.jpg"], p.Images.OrderBy(i => i.Position).Select(i => i.ExternalUrl));
        Assert.Equal(313.13m, products[1].Price); // 250,50 × 1,25 = 313,125 → 313,13
        await using var read = NewDb(tenant);
        var category = await read.Categories.AsNoTracking().ToListAsync();
        Assert.Equal(["Giyim", "Tişört"], category.OrderBy(c => c.ParentId.HasValue).Select(c => c.Name));
        Assert.All(products, x => Assert.NotNull(x.CategoryId));
        var saved = (await svc.ListAsync(default)).Single();
        Assert.Equal(("Success", 2, 2), (saved.LastStatus, saved.LastCreated, saved.ProductCount));
        Assert.NotNull(saved.LastRunAt);
        Assert.False(saved.Running);
    }

    [Fact]
    public async Task Fiyat_KDV_haric_gelirse_satis_fiyatina_KDV_eklenir()
    {
        var tenant = await TenantAsync("kdv");
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("K1", "Ürün", "100", 5), ("K2", "Ürün 2", "10", 5));
        await using var db = NewDb(tenant);
        var svc = Service(db);
        var feed = await svc.SaveAsync(null, Input(supplier.FeedUrl, markup: 0, includesVat: false), default);

        await svc.RunAsync(feed.Id, default);

        var p = (await ProductsAsync(tenant))[0];
        Assert.Equal((100m, 120m), (p.Cost, p.Price)); // feed KdvOrani = 20
    }

    [Fact]
    public async Task Ikinci_calistirma_stok_ve_fiyati_gunceller_elle_duzenlenen_alanlari_ezmez()
    {
        var tenant = await TenantAsync("guncel");
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "Tişört", "100", 5), ("B2", "Pantolon", "200", 3));
        await using var db = NewDb(tenant);
        var svc = Service(db);
        var feed = await svc.SaveAsync(null, Input(supplier.FeedUrl, markup: 10), default);
        await svc.RunAsync(feed.Id, default);
        await using (var edit = NewDb(tenant))
        {
            var mine = await edit.Products.FirstAsync(p => p.Barcode == "B1");
            mine.Name = "Benim Başlığım"; mine.Description = "Benim açıklamam";
            await edit.SaveChangesAsync();
        }
        supplier.Body = Feed(("B1", "Tedarikçi Yeni Ad", "150", 9), ("B2", "Pantolon", "200", 0), ("B3", "Yeni Ürün", "50", 4));

        var result = await svc.RunAsync(feed.Id, default);

        Assert.Equal((1, 2, 0), (result.Created, result.Updated, result.Failed));
        var products = (await ProductsAsync(tenant)).ToDictionary(p => p.Barcode);
        Assert.Equal(("Benim Başlığım", "Benim açıklamam", 9, 165m, 150m), (products["B1"].Name, products["B1"].Description, products["B1"].Stock, products["B1"].Price, products["B1"].Cost));
        Assert.Equal(0, products["B2"].Stock);
        Assert.Equal(55m, products["B3"].Price);
    }

    [Fact]
    public async Task Stok_ya_da_fiyat_esitleme_kapaliysa_o_alan_degismez()
    {
        var tenant = await TenantAsync("esit");
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "Tişört", "100", 5), ("B2", "Pantolon", "200", 3));
        await using var db = NewDb(tenant);
        var svc = Service(db);
        var feed = await svc.SaveAsync(null, Input(supplier.FeedUrl, markup: 0), default);
        await svc.RunAsync(feed.Id, default);
        await svc.SaveAsync(feed.Id, Input("", markup: 0) with { SyncPrice = false, SyncStock = true }, default);
        supplier.Body = Feed(("B1", "Tişört", "999", 77), ("B2", "Pantolon", "999", 88));

        await svc.RunAsync(feed.Id, default);

        var p = (await ProductsAsync(tenant))[0];
        Assert.Equal((77, 100m), (p.Stock, p.Price)); // stok güncellendi, fiyat sabit kaldı
    }

    [Fact]
    public async Task Elle_eklenmis_ya_da_baska_beslemenin_urunu_ezilmez_atlanir_ve_raporlanir()
    {
        var tenant = await TenantAsync("catisma");
        await using (var seed = NewDb(tenant))
        {
            seed.Products.Add(new Product { Barcode = "B1", Name = "Elle Eklenen", Price = 500m, Stock = 42 });
            await seed.SaveChangesAsync();
        }
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "Tedarikçi B1", "100", 5), ("B2", "Tedarikçi B2", "200", 3));
        await using var db = NewDb(tenant);
        var svc = Service(db);
        var feed = await svc.SaveAsync(null, Input(supplier.FeedUrl), default);

        var result = await svc.RunAsync(feed.Id, default);

        Assert.Equal(("Partial", 1, 1), (result.Status, result.Created, result.Skipped));
        var manual = (await ProductsAsync(tenant)).First(p => p.Barcode == "B1");
        Assert.Equal(("Elle Eklenen", 500m, 42, null), (manual.Name, manual.Price, manual.Stock, manual.FeedId));
        Assert.Contains("elle eklenmiş", result.Errors.Single().Message);
    }

    [Fact]
    public async Task Beslemeden_kalkan_urunun_stogu_sifirlanir_urun_silinmez_ayar_kapaliysa_dokunulmaz()
    {
        var tenant = await TenantAsync("kalkan");
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "A", "10", 5), ("B2", "B", "20", 6), ("B3", "C", "30", 7));
        await using var db = NewDb(tenant);
        var svc = Service(db);
        var zero = await svc.SaveAsync(null, Input(supplier.FeedUrl, name: "Sıfırlayan"), default);
        await svc.RunAsync(zero.Id, default);
        supplier.Body = Feed(("B1", "A", "10", 5));

        var result = await svc.RunAsync(zero.Id, default);

        Assert.Equal(2, result.ZeroedMissing);
        var products = (await ProductsAsync(tenant)).ToDictionary(p => p.Barcode);
        Assert.Equal((5, 0, 0), (products["B1"].Stock, products["B2"].Stock, products["B3"].Stock));
        Assert.Equal(3, products.Count);

        await svc.SaveAsync(zero.Id, Input("", name: "Sıfırlayan", zeroMissing: false), default);
        await using (var edit = NewDb(tenant)) { (await edit.Products.FirstAsync(p => p.Barcode == "B2")).Stock = 9; await edit.SaveChangesAsync(); }
        Assert.Equal(0, (await svc.RunAsync(zero.Id, default)).ZeroedMissing);
        Assert.Equal(9, (await ProductsAsync(tenant)).First(p => p.Barcode == "B2").Stock);
    }

    [Fact]
    public async Task Yinelenen_barkod_ve_hatali_satirlar_raporlanir_gecerliler_yine_de_eklenir()
    {
        var tenant = await TenantAsync("hata");
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "A", "10", 5), ("B1", "A kopya", "11", 5), ("B3", "C", "0", 7), ("B4", "D", "40", 1));
        await using var db = NewDb(tenant);
        var svc = Service(db);
        var feed = await svc.SaveAsync(null, Input(supplier.FeedUrl), default);

        var result = await svc.RunAsync(feed.Id, default);

        Assert.Equal(("Partial", 2, 1, 1), (result.Status, result.Created, result.Skipped, result.Failed));
        Assert.Equal(["B1", "B4"], (await ProductsAsync(tenant)).Select(p => p.Barcode));
        Assert.Contains(result.Errors, e => e.Item == 2 && e.Message.Contains("yinelenen"));
        Assert.Contains(result.Errors, e => e.Item == 3 && e.Message.Contains("Fiyat"));
        var saved = (await svc.ListAsync(default)).Single();
        Assert.Equal(("Partial", 2, 1, 1), (saved.LastStatus, saved.LastErrors.Count, saved.LastSkipped, saved.LastFailed));
    }

    [Fact]
    public async Task Adres_hata_verirse_besleme_Failed_olur_mevcut_urunler_degismez()
    {
        var tenant = await TenantAsync("kapali");
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "A", "10", 5), ("B2", "B", "20", 6));
        await using var db = NewDb(tenant);
        var svc = Service(db);
        var feed = await svc.SaveAsync(null, Input(supplier.FeedUrl), default);
        await svc.RunAsync(feed.Id, default);

        supplier.Status = 503;
        var e = await Assert.ThrowsAsync<FeedException>(() => svc.RunAsync(feed.Id, default));

        Assert.Contains("503", e.Message);
        var saved = (await svc.ListAsync(default)).Single();
        Assert.Equal("Failed", saved.LastStatus);
        Assert.Contains("503", saved.LastError);
        Assert.False(saved.Running);
        Assert.All(await ProductsAsync(tenant), p => Assert.True(p.Stock > 0));
    }

    [Fact]
    public async Task Bos_ya_da_tanimayan_besleme_stoklari_sifirlamaz_hata_verir()
    {
        var tenant = await TenantAsync("bos");
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "A", "10", 5), ("B2", "B", "20", 6));
        await using var db = NewDb(tenant);
        var svc = Service(db);
        var feed = await svc.SaveAsync(null, Input(supplier.FeedUrl), default);
        await svc.RunAsync(feed.Id, default);

        supplier.Body = "<Urunler></Urunler>";
        await Assert.ThrowsAsync<FeedException>(() => svc.RunAsync(feed.Id, default));
        supplier.Body = "<html><body>Bakımdayız</body></html>";
        await Assert.ThrowsAsync<FeedException>(() => svc.RunAsync(feed.Id, default));

        Assert.All(await ProductsAsync(tenant), p => Assert.True(p.Stock > 0)); // tedarikçi boş dönünce tüm stoklar sıfırlanmaz
    }

    [Fact]
    public async Task Calisan_besleme_ikinci_kez_baslatilamaz_takili_kalan_is_zaman_asimiyla_serbest_kalir()
    {
        var tenant = await TenantAsync("kilit");
        var clock = new Clock(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "A", "10", 5), ("B2", "B", "20", 6));
        await using var db = NewDb(tenant);
        var svc = Service(db, time: clock);
        var feed = await svc.SaveAsync(null, Input(supplier.FeedUrl), default);
        await db.ProductFeeds.Where(f => f.Id == feed.Id).ExecuteUpdateAsync(s => s.SetProperty(f => f.RunningSince, clock.Now.UtcDateTime.AddMinutes(-5)));
        db.ChangeTracker.Clear(); // ExecuteUpdate izlenen kaydı yenilemez; gerçekte her istek kendi bağlamını açar

        Assert.Equal(409, (await Assert.ThrowsAsync<FeedException>(() => svc.RunAsync(feed.Id, default))).Status);

        await db.ProductFeeds.Where(f => f.Id == feed.Id).ExecuteUpdateAsync(s => s.SetProperty(f => f.RunningSince, clock.Now.UtcDateTime.AddHours(-2)));
        db.ChangeTracker.Clear();
        Assert.Equal(2, (await svc.RunAsync(feed.Id, default)).Created);
    }

    [Fact]
    public async Task Zamani_gelen_besleme_calisir_gelmeyen_ve_kapali_olan_calismaz()
    {
        var tenant = await TenantAsync("zamanli");
        var clock = new Clock(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "A", "10", 5), ("B2", "B", "20", 6));
        await using var db = NewDb(tenant);
        var svc = Service(db, time: clock);
        var feed = await svc.SaveAsync(null, Input(supplier.FeedUrl, interval: 60), default);

        Assert.Equal(1, await svc.RunDueAsync(default));          // hiç çalışmamış: zamanı gelmiş
        clock.Now = clock.Now.AddMinutes(30);
        Assert.Equal(0, await svc.RunDueAsync(default));          // 30 dk < 60 dk
        clock.Now = clock.Now.AddMinutes(31);
        Assert.Equal(1, await svc.RunDueAsync(default));          // aralık doldu
        await svc.SaveAsync(feed.Id, Input("", interval: 60) with { Enabled = false }, default);
        clock.Now = clock.Now.AddHours(5);
        Assert.Equal(0, await svc.RunDueAsync(default));          // kapalı
    }

    [Fact]
    public async Task Besleme_silinince_urunler_katalogda_kalir_elle_yonetilen_olur()
    {
        var tenant = await TenantAsync("sil");
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "A", "10", 5), ("B2", "B", "20", 6));
        await using var db = NewDb(tenant);
        var svc = Service(db);
        var feed = await svc.SaveAsync(null, Input(supplier.FeedUrl), default);
        await svc.RunAsync(feed.Id, default);

        await svc.DeleteAsync(feed.Id, default);

        Assert.Empty(await svc.ListAsync(default));
        var products = await ProductsAsync(tenant);
        Assert.Equal(2, products.Count);
        Assert.All(products, p => Assert.Null(p.FeedId));
        Assert.Equal(404, (await Assert.ThrowsAsync<FeedException>(() => svc.DeleteAsync(feed.Id, default))).Status);
    }

    [Fact]
    public async Task Beslemeler_ve_urunler_kiracilar_arasinda_yalitilidir()
    {
        var a = await TenantAsync("izole-a");
        var b = await TenantAsync("izole-b");
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "A", "10", 5), ("B2", "B", "20", 6));
        await using var dbA = NewDb(a);
        var svcA = Service(dbA);
        var feedA = await svcA.SaveAsync(null, Input(supplier.FeedUrl), default);
        await svcA.RunAsync(feedA.Id, default);

        await using var dbB = NewDb(b);
        var svcB = Service(dbB);
        Assert.Empty(await svcB.ListAsync(default));
        Assert.Equal(404, (await Assert.ThrowsAsync<FeedException>(() => svcB.RunAsync(feedA.Id, default))).Status);
        var feedB = await svcB.SaveAsync(null, Input(supplier.FeedUrl), default);
        Assert.Equal(2, (await svcB.RunAsync(feedB.Id, default)).Created); // aynı barkodlar B kiracısında çakışmaz
        Assert.Equal(2, (await ProductsAsync(a)).Count);
        Assert.Equal(2, (await ProductsAsync(b)).Count);
    }

    // --- güvenlik ---

    [Fact]
    public async Task Yerel_ag_adresi_varsayilan_olarak_indirilmez_SSRF()
    {
        var tenant = await TenantAsync("ssrf");
        await using var supplier = await Supplier.StartAsync(); // 127.0.0.1
        supplier.Body = Feed(("B1", "A", "10", 5), ("B2", "B", "20", 6));
        await using var db = NewDb(tenant);
        var svc = Service(db, allowPrivate: false);

        var e = await Assert.ThrowsAsync<FeedException>(() => svc.AnalyzeAsync(new FeedAnalyzeRequest(supplier.FeedUrl, null, null), default));
        Assert.Equal(400, e.Status);
        Assert.Contains("güvenlik", e.Message);
        await Assert.ThrowsAsync<FeedException>(() => svc.SaveAsync(null, Input(supplier.FeedUrl), default));
        await Assert.ThrowsAsync<FeedException>(() => svc.AnalyzeAsync(new FeedAnalyzeRequest("http://169.254.169.254/latest/meta-data/", null, null), default));
        await Assert.ThrowsAsync<FeedException>(() => svc.AnalyzeAsync(new FeedAnalyzeRequest("http://localhost:1/feed.xml", null, null), default));
        await Assert.ThrowsAsync<FeedException>(() => svc.AnalyzeAsync(new FeedAnalyzeRequest("http://[::1]/feed.xml", null, null), default));
        Assert.Empty(await db.ProductFeeds.ToListAsync());
    }

    [Fact]
    public async Task Boyut_siniri_asilirsa_indirme_durdurulur()
    {
        var tenant = await TenantAsync("boyut");
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(Enumerable.Range(1, 200).Select(i => ("B" + i, "Ürün " + i, "10", 1)).ToArray());
        await using var db = NewDb(tenant);
        var fetcher = new FeedFetcher(Options.Create(new FeedFetcherOptions { AllowPrivateNetwork = true, MaxBytes = 2000 }));
        var svc = new ProductFeedService(db, fetcher, TimeProvider.System, NullLogger<ProductFeedService>.Instance);

        var e = await Assert.ThrowsAsync<FeedException>(() => svc.AnalyzeAsync(new FeedAnalyzeRequest(supplier.FeedUrl, null, null), default));
        Assert.Equal(413, e.Status);
    }

    [Fact]
    public async Task Gecici_dosya_indirme_bitince_silinir()
    {
        await using var supplier = await Supplier.StartAsync();
        supplier.Body = Feed(("B1", "A", "10", 5), ("B2", "B", "20", 6));
        var before = Directory.GetFiles(Path.GetTempPath(), "feed-*.tmp").Length;
        var fetcher = new FeedFetcher(Options.Create(new FeedFetcherOptions { AllowPrivateNetwork = true }));

        await using (var stream = await fetcher.DownloadAsync(supplier.FeedUrl, default))
        {
            Assert.True(stream.CanSeek);
            Assert.True(stream.Length > 0);
        }

        Assert.Equal(before, Directory.GetFiles(Path.GetTempPath(), "feed-*.tmp").Length);
    }
}
