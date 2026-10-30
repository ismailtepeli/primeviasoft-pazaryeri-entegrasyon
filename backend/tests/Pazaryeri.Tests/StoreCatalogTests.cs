using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Pricing;

namespace Pazaryeri.Tests;

public class StoreCatalogTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_StoreCatalog;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private static AppDbContext NewDb(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    public async Task InitializeAsync()
    {
        await using var db = NewDb(null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static StoreCatalogService Service(AppDbContext db) => new(db, new StoreCatalog(db), new DealerPriceService(db));

    private sealed record World(Guid Tenant, Guid DealerId, Guid GroupId, Guid GoldDealerId, Category Root, Category Child, Category Other);

    /// <summary>Gümüş grubu bayisi (dealer) ve grupsuz bayi (gold adı yanıltıcı değil: grupsuz) ile ürünler.</summary>
    private static async Task<World> SeedAsync()
    {
        await using var root = NewDb(null);
        var tenant = new Tenant { Name = "Katalog" };
        root.Tenants.Add(tenant);
        await root.SaveChangesAsync();

        await using var db = NewDb(tenant.Id);
        var group = new DealerGroup { Name = "Gümüş" };
        var cRoot = new Category { Name = "Giyim" };
        var cOther = new Category { Name = "Kırtasiye" };
        db.AddRange(group, cRoot, cOther);
        await db.SaveChangesAsync();
        var cChild = new Category { Name = "Tişört", ParentId = cRoot.Id };
        db.Categories.Add(cChild);
        var silver = new Dealer { Name = "Gümüş Bayi", TaxNumber = "1000000001", Email = "g@g.com", Status = DealerStatus.Active, GroupId = group.Id };
        var plain = new Dealer { Name = "Grupsuz Bayi", TaxNumber = "1000000002", Email = "p@p.com", Status = DealerStatus.Active };
        db.Dealers.AddRange(silver, plain);
        await db.SaveChangesAsync();

        db.Products.AddRange(
            new Product { Name = "Beyaz Tişört", Barcode = "T1", MainId = "TS", Variant = "Beden: M", CategoryId = cChild.Id, Price = 120m, VatRate = 20, Stock = 50, B2BVisible = true, MinOrderQuantity = 6, PackSize = 3 },
            new Product { Name = "Beyaz Tişört", Barcode = "T2", MainId = "TS", Variant = "Beden: L", CategoryId = cChild.Id, Price = 120m, VatRate = 20, Stock = 0, B2BVisible = true },
            new Product { Name = "Defter", Barcode = "D1", CategoryId = cOther.Id, Price = 60m, VatRate = 20, Stock = 3, SafetyStock = 1, B2BVisible = true },
            new Product { Name = "Kalem", Barcode = "K1", CategoryId = cOther.Id, Price = 12m, VatRate = 20, Stock = 100, B2BVisible = true },
            new Product { Name = "Gizli Ürün", Barcode = "G1", Price = 5m, Stock = 10, B2BVisible = false });
        db.PriceLists.Add(new PriceList { Name = "Gümüş liste", GroupId = group.Id, DiscountPercent = 10m, ShowVatIncluded = true });
        await db.SaveChangesAsync();
        return new World(tenant.Id, silver.Id, group.Id, plain.Id, cRoot, cChild, cOther);
    }

    [Fact]
    public async Task Liste_yalnizca_gorunur_urunleri_bayiye_ozel_fiyat_ve_stok_durumuyla_doner()
    {
        var w = await SeedAsync();
        await using var db = NewDb(w.Tenant);
        var page = await Service(db).ListAsync(w.DealerId, new StoreQuery(null, null, null, 1, 24), default);

        Assert.Equal(4, page.Total); // gizli ürün yok
        Assert.DoesNotContain(page.Items, i => i.Code == "G1");
        var defter = page.Items.Single(i => i.Code == "D1");
        Assert.Equal((54m, true, "TRY"), (defter.Price.Amount, defter.Price.IncludesVat, defter.Price.Currency)); // grup iskontosu %10, KDV dahil gösterim
        Assert.Equal("Az kaldı", defter.Stock.Label);   // 3 - güvenlik 1 = 2 ≤ eşik 5
        Assert.True(defter.Stock.InStock);
        Assert.Equal("Tükendi", page.Items.Single(i => i.Code == "T2").Stock.Label);
        Assert.Equal("Stokta var", page.Items.Single(i => i.Code == "K1").Stock.Label);
        var tee = page.Items.Single(i => i.Code == "T1");
        Assert.Equal((6, 3), (tee.MinOrderQuantity, tee.PackSize));
        Assert.Equal("Giyim > Tişört", tee.CategoryPath);
    }

    [Fact]
    public async Task Grupsuz_bayi_genel_fiyati_gorur()
    {
        var w = await SeedAsync();
        await using var db = NewDb(w.Tenant);
        var page = await Service(db).ListAsync(w.GoldDealerId, new StoreQuery("Defter", null, null, 1, 24), default);
        Assert.Equal(60m, page.Items.Single().Price.Amount);
    }

    [Fact]
    public async Task Grup_kisiti_ve_gizli_kategori_listede_ve_detayda_uygulanir()
    {
        var w = await SeedAsync();
        await using var db = NewDb(w.Tenant);
        var kalem = await db.Products.SingleAsync(p => p.Barcode == "K1");
        db.GroupVisibilityRules.Add(new GroupVisibilityRule { GroupId = w.GroupId, ProductId = kalem.Id });
        db.GroupVisibilityRules.Add(new GroupVisibilityRule { GroupId = w.GroupId, CategoryId = w.Root.Id }); // tişörtler alt kategoriyle gizlenir
        await db.SaveChangesAsync();

        var svc = Service(db);
        var page = await svc.ListAsync(w.DealerId, new StoreQuery(null, null, null, 1, 24), default);
        Assert.Equal(["D1"], page.Items.Select(i => i.Code).ToList());
        Assert.Null(await svc.DetailAsync(w.DealerId, kalem.Id, default));
        Assert.NotNull(await svc.DetailAsync(w.GoldDealerId, kalem.Id, default)); // grupsuz bayi etkilenmez
        Assert.Equal(4, (await svc.ListAsync(w.GoldDealerId, new StoreQuery(null, null, null, 1, 24), default)).Total);
    }

    [Fact]
    public async Task Arama_kategori_siralama_ve_sayfalama_calisir()
    {
        var w = await SeedAsync();
        await using var db = NewDb(w.Tenant);
        var svc = Service(db);

        Assert.Equal(2, (await svc.ListAsync(w.DealerId, new StoreQuery(null, w.Root.Id, null, 1, 24), default)).Total); // üst kategori alt kategoriyi kapsar
        Assert.Equal(2, (await svc.ListAsync(w.DealerId, new StoreQuery(null, w.Other.Id, null, 1, 24), default)).Total);
        Assert.Equal(["T1", "T2"], (await svc.ListAsync(w.DealerId, new StoreQuery("tişört", null, null, 1, 24), default)).Items.Select(i => i.Code).OrderBy(c => c).ToList());
        Assert.Equal(["K1"], (await svc.ListAsync(w.DealerId, new StoreQuery("K1", null, null, 1, 24), default)).Items.Select(i => i.Code).ToList()); // barkodla

        var byPrice = await svc.ListAsync(w.DealerId, new StoreQuery(null, null, "price", 1, 24), default);
        Assert.Equal(["K1", "D1"], byPrice.Items.Take(2).Select(i => i.Code).ToList()); // 10,8 → 54
        var byPriceDesc = await svc.ListAsync(w.DealerId, new StoreQuery(null, null, "price_desc", 1, 24), default);
        Assert.Equal("K1", byPriceDesc.Items.Last().Code);
        var nameDesc = await svc.ListAsync(w.DealerId, new StoreQuery(null, null, "name_desc", 1, 24), default);
        Assert.Equal("Kalem", nameDesc.Items.First().Name);
        Assert.Equal("Beyaz Tişört", (await svc.ListAsync(w.DealerId, new StoreQuery(null, null, "bilinmeyen", 1, 24), default)).Items.First().Name); // geçersiz sıralama adadır

        var p1 = await svc.ListAsync(w.DealerId, new StoreQuery(null, null, null, 1, 3), default);
        var p2 = await svc.ListAsync(w.DealerId, new StoreQuery(null, null, null, 2, 3), default);
        Assert.Equal((4, 3, 1), (p1.Total, p1.Items.Count, p2.Items.Count));
        Assert.Empty(p1.Items.Select(i => i.Id).Intersect(p2.Items.Select(i => i.Id)));
        Assert.Equal(1, (await svc.ListAsync(w.DealerId, new StoreQuery(null, null, null, 0, 0), default)).Items.Count); // sayfa ve boyut güvenli aralığa çekilir
    }

    [Fact]
    public async Task Detay_gorsel_sirali_varyant_ve_kirinti_doner()
    {
        var w = await SeedAsync();
        await using var db = NewDb(w.Tenant);
        var tee = await db.Products.SingleAsync(p => p.Barcode == "T1");
        db.ProductImages.AddRange(
            new ProductImage { ProductId = tee.Id, Position = 1, Kind = ImageKind.Url, ExternalUrl = "https://x.test/2.jpg" },
            new ProductImage { ProductId = tee.Id, Position = 0, Kind = ImageKind.Url, ExternalUrl = "https://x.test/1.jpg" });
        tee.Description = "Pamuklu";
        await db.SaveChangesAsync();

        var d = (await Service(db).DetailAsync(w.DealerId, tee.Id, default))!;
        Assert.Equal(["https://x.test/1.jpg", "https://x.test/2.jpg"], d.Images);
        Assert.Equal(["Giyim", "Tişört"], d.Breadcrumb.Select(c => c.Name).ToList());
        Assert.Equal(2, d.Variants.Count);
        Assert.Equal(("Pamuklu", 108m), (d.Description, d.Price.Amount));
        Assert.Contains(d.Variants, v => v.Code == "T2" && !v.InStock);
        Assert.Equal((6, 3), (d.MinOrderQuantity, d.PackSize));

        var list = await Service(db).ListAsync(w.DealerId, new StoreQuery("T1", null, null, 1, 5), default);
        Assert.Equal("https://x.test/1.jpg", list.Items.Single().CoverUrl);
        Assert.Null(await Service(db).DetailAsync(w.DealerId, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Stok_gosterimi_ayara_gore_degisir()
    {
        var w = await SeedAsync();
        await using var db = NewDb(w.Tenant);
        db.B2BSettings.Add(new B2BSettings { StockDisplay = StockDisplayMode.Exact });
        await db.SaveChangesAsync();
        var defter = (await Service(db).ListAsync(w.DealerId, new StoreQuery("D1", null, null, 1, 5), default)).Items.Single();
        Assert.Equal((2, true), (defter.Stock.Quantity, defter.Stock.InStock));

        var s = await db.B2BSettings.SingleAsync();
        s.StockDisplay = StockDisplayMode.Hidden;
        await db.SaveChangesAsync();
        var hidden = (await Service(db).ListAsync(w.DealerId, new StoreQuery("D1", null, null, 1, 5), default)).Items.Single();
        Assert.Equal((null, null, true), (hidden.Stock.Quantity, hidden.Stock.Label, hidden.Stock.InStock));
    }

    [Fact]
    public async Task Kategori_agaci_yalnizca_gorunur_urunu_olanlari_ust_kategori_toplamiyla_doner()
    {
        var w = await SeedAsync();
        await using var db = NewDb(w.Tenant);
        var svc = Service(db);
        var cats = await svc.CategoriesAsync(w.DealerId, default);
        Assert.Equal(("Giyim", 2, null), (cats.Single(c => c.Id == w.Root.Id).Name, cats.Single(c => c.Id == w.Root.Id).ProductCount, cats.Single(c => c.Id == w.Root.Id).ParentId));
        Assert.Equal(w.Root.Id, cats.Single(c => c.Id == w.Child.Id).ParentId);
        Assert.Equal(2, cats.Single(c => c.Id == w.Other.Id).ProductCount);

        // Kırtasiyeyi gizle: kategori menüden kalkar.
        (await db.Categories.SingleAsync(c => c.Id == w.Other.Id)).B2BHidden = true;
        await db.SaveChangesAsync();
        Assert.DoesNotContain(await svc.CategoriesAsync(w.DealerId, default), c => c.Id == w.Other.Id);
    }

    private static async Task SeedBrandsAsync(World w)
    {
        await using var db = NewDb(w.Tenant);
        foreach (var (barcode, brand) in new[] { ("T1", "Nike"), ("T2", "Nike"), ("D1", "Faber"), ("K1", "Faber") })
            (await db.Products.SingleAsync(p => p.Barcode == barcode)).Brand = brand;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Marka_susgeci_ve_facet_sayilari_marka_secimine_ragmen_digerlerini_gosterir()
    {
        var w = await SeedAsync();
        await SeedBrandsAsync(w);
        await using var db = NewDb(w.Tenant);
        var svc = Service(db);

        var all = await svc.ListAsync(w.DealerId, new StoreQuery(null, null, null, 1, 24), default);
        Assert.Equal([("Faber", 2), ("Nike", 2)], all.Facets!.Brands.Select(b => (b.Name, b.Count)).ToList());

        var nike = await svc.ListAsync(w.DealerId, new StoreQuery(null, null, null, 1, 24, ["Nike"]), default);
        Assert.Equal(["T1", "T2"], nike.Items.Select(i => i.Code).OrderBy(c => c).ToList());
        Assert.Equal(2, nike.Facets!.Brands.Count); // Faber de listede kalır

        var both = await svc.ListAsync(w.DealerId, new StoreQuery(null, null, null, 1, 24, ["Nike", "Faber"]), default);
        Assert.Equal(4, both.Total);
        Assert.Equal(0, (await svc.ListAsync(w.DealerId, new StoreQuery(null, null, null, 1, 24, ["Yok"]), default)).Total);

        // Kategori daraltınca markalar da daralır.
        var cat = await svc.ListAsync(w.DealerId, new StoreQuery(null, w.Other.Id, null, 1, 24), default);
        Assert.Equal([("Faber", 2)], cat.Facets!.Brands.Select(b => (b.Name, b.Count)).ToList());
    }

    [Fact]
    public async Task Fiyat_araligi_bayiye_ozel_fiyatla_uygulanir_ve_aralik_bilgisi_doner()
    {
        var w = await SeedAsync();
        await using var db = NewDb(w.Tenant);
        var svc = Service(db);

        var all = await svc.ListAsync(w.DealerId, new StoreQuery(null, null, null, 1, 24), default);
        Assert.Equal((10.8m, 108m), (all.Facets!.PriceMin, all.Facets.PriceMax)); // 12 → 10,8 ; 120 → 108 (grup %10)

        var mid = await svc.ListAsync(w.DealerId, new StoreQuery(null, null, "price", 1, 24, null, 50m, 100m), default);
        Assert.Equal(["D1"], mid.Items.Select(i => i.Code).ToList());      // yalnızca 54 ₺
        Assert.Equal(1, mid.Total);
        Assert.Equal(2, (await svc.ListAsync(w.DealerId, new StoreQuery(null, null, null, 1, 24, null, 100m, null), default)).Total); // iki tişört
        Assert.Equal(1, (await svc.ListAsync(w.DealerId, new StoreQuery(null, null, null, 1, 24, null, null, 10.8m), default)).Total);  // sınır dahil

        // Grupsuz bayide aynı aralık genel fiyatla değerlendirilir.
        var plain = await Service(db).ListAsync(w.GoldDealerId, new StoreQuery(null, null, null, 1, 24, null, 100m, null), default);
        Assert.Equal(2, plain.Total);
        Assert.Equal(0, (await Service(db).ListAsync(w.GoldDealerId, new StoreQuery(null, null, null, 1, 24, null, 65m, 100m), default)).Total); // defter 60 ₺ (genel fiyat) aralık dışı
    }

    [Fact]
    public async Task Yalnizca_stokta_olanlar_guvenlik_stogu_dusulerek_suzulur_ve_sayfalama_toplami_dogrudur()
    {
        var w = await SeedAsync();
        await using var db = NewDb(w.Tenant);
        var svc = Service(db);
        var inStock = await svc.ListAsync(w.DealerId, new StoreQuery(null, null, null, 1, 24, null, null, null, true), default);
        Assert.DoesNotContain(inStock.Items, i => i.Code == "T2");  // stok 0
        Assert.Equal(3, inStock.Total);

        await using (var admin = NewDb(w.Tenant)) { (await admin.Products.SingleAsync(p => p.Barcode == "D1")).Stock = 1; await admin.SaveChangesAsync(); } // 1 - güvenlik 1 = 0
        var after = await svc.ListAsync(w.DealerId, new StoreQuery(null, null, null, 1, 2, null, null, null, true), default);
        Assert.Equal((2, 2), (after.Total, after.Items.Count));          // T1, K1; sayfa boyu 2
        Assert.DoesNotContain(after.Items, i => i.Code == "D1");
    }

    [Fact]
    public async Task Baska_kiracinin_bayisi_ve_urunleri_karismaz()
    {
        var w = await SeedAsync();
        await using var other = NewDb(null);
        var t2 = new Tenant { Name = "Diğer" };
        other.Tenants.Add(t2);
        await other.SaveChangesAsync();
        await using var db2 = NewDb(t2.Id);
        Assert.Equal(0, (await Service(db2).ListAsync(w.DealerId, new StoreQuery(null, null, null, 1, 24), default)).Total);
    }
}
