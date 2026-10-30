using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Catalog;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Pricing;

namespace Pazaryeri.Tests;

public class CategoryTreeTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_CategoryTree;Trusted_Connection=True;TrustServerCertificate=True";

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

    private static async Task<Guid> TenantAsync(string name)
    {
        await using var db = NewDb(null);
        var t = new Tenant { Name = name };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    // --- saf kurallar ---

    [Theory]
    [InlineData("Çocuk Giyim", "cocuk-giyim")]
    [InlineData("  Kırtasiye & Ofis  ", "kirtasiye-ofis")]
    [InlineData("İş Güvenliği", "is-guvenligi")]
    [InlineData("ŞAPKA", "sapka")]
    [InlineData("---", "")]
    public void Slug_turkce_harfleri_sadelestirir(string name, string expected) => Assert.Equal(expected, CategoryRules.Slugify(name));

    private static (Guid A, Guid B, Guid C, Guid D, List<(Guid, Guid?)> Tree) Sample()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();
        return (a, b, c, d, [(a, null), (b, a), (c, b), (d, null)]); // a > b > c ; d tek
    }

    [Fact]
    public void Derinlik_ve_alt_agac_yuksekligi_hesaplanir()
    {
        var (a, b, c, d, tree) = Sample();
        Assert.Equal((1, 2, 3, 1), (CategoryRules.DepthOf(tree, a), CategoryRules.DepthOf(tree, b), CategoryRules.DepthOf(tree, c), CategoryRules.DepthOf(tree, d)));
        Assert.Equal((3, 2, 1), (CategoryRules.SubtreeHeight(tree, a), CategoryRules.SubtreeHeight(tree, b), CategoryRules.SubtreeHeight(tree, c)));
    }

    [Fact]
    public void Tasima_dongu_ve_derinlik_sinirini_korur()
    {
        var (a, b, c, d, tree) = Sample();
        Assert.NotNull(CategoryRules.ValidateMove(tree, a, a));   // kendisi
        Assert.NotNull(CategoryRules.ValidateMove(tree, a, c));   // kendi alt kategorisi
        Assert.Null(CategoryRules.ValidateMove(tree, c, d));      // başka dala
        Assert.Null(CategoryRules.ValidateMove(tree, c, null));   // köke

        // d'nin altına 3 seviyelik a dalı: derinlik 1 + 3 = 4 olur, geçerli; üstüne 2 seviye daha eklenirse aşılır.
        var deep = new List<(Guid, Guid?)>(tree);
        Guid e = Guid.NewGuid(), f = Guid.NewGuid();
        deep.Add((e, d)); deep.Add((f, e)); // d > e > f
        Assert.NotNull(CategoryRules.ValidateMove(deep, a, f));   // derinlik 3 + 3 = 6 > 5
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("https://x.test/a.jpg", true)]
    [InlineData("ftp://x.test/a.jpg", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("görsel.jpg", false)]
    public void Gorsel_adresi_yalnizca_http_https_olabilir(string? url, bool ok) => Assert.Equal(ok, CategoryRules.ValidateImageUrl(url) is null);

    // --- API ---

    [Fact]
    public async Task Liste_agac_sirasiyla_gelir_toplam_sayilar_alt_kategorileri_kapsar()
    {
        var t = await TenantAsync("Liste");
        await using var db = NewDb(t);
        var c = new CategoriesController(db);
        var giyim = (await c.Create(new CategoryInput("Giyim", null), default)).Value!;
        var kirt = (await c.Create(new CategoryInput("Kırtasiye", null), default)).Value!;
        var tisort = (await c.Create(new CategoryInput("Tişört", giyim.Id), default)).Value!;
        Assert.Equal((0, 1, 0), (giyim.SortOrder, kirt.SortOrder, tisort.SortOrder));
        db.Products.AddRange(new Product { Name = "A", Barcode = "A", CategoryId = tisort.Id }, new Product { Name = "B", Barcode = "B", CategoryId = tisort.Id }, new Product { Name = "C", Barcode = "C", CategoryId = giyim.Id });
        await db.SaveChangesAsync();

        var list = await c.List(default);
        Assert.Equal(["Giyim", "Tişört", "Kırtasiye"], list.Select(x => x.Name).ToList()); // dal içinde derinlemesine
        var g = list.First(x => x.Id == giyim.Id);
        Assert.Equal((1, 3, 1, "giyim"), (g.ProductCount, g.TotalProductCount, g.ChildCount, g.SlugPath));
        Assert.Equal("giyim/tisort", list.First(x => x.Id == tisort.Id).SlugPath);
    }

    [Fact]
    public async Task Siralama_kardesleri_dizer_eksik_ya_da_fazla_liste_reddedilir()
    {
        var t = await TenantAsync("Sira");
        await using var db = NewDb(t);
        var c = new CategoriesController(db);
        var a = (await c.Create(new CategoryInput("A", null), default)).Value!;
        var b = (await c.Create(new CategoryInput("B", null), default)).Value!;
        var x = (await c.Create(new CategoryInput("X", null), default)).Value!;

        var ok = Assert.IsType<List<CategoryDto>>(Assert.IsType<ActionResult<List<CategoryDto>>>(await c.Reorder(new CategoryOrderInput(null, [x.Id, a.Id, b.Id]), default)).Value);
        Assert.Equal(["X", "A", "B"], ok.Select(i => i.Name).ToList());
        Assert.IsType<BadRequestObjectResult>((await c.Reorder(new CategoryOrderInput(null, [x.Id, a.Id]), default)).Result);            // eksik
        Assert.IsType<BadRequestObjectResult>((await c.Reorder(new CategoryOrderInput(null, [x.Id, a.Id, a.Id]), default)).Result);      // tekrar
        Assert.IsType<BadRequestObjectResult>((await c.Reorder(new CategoryOrderInput(a.Id, [b.Id]), default)).Result);                   // başka düzeyin kimliği
    }

    [Fact]
    public async Task Tasima_calisir_dongu_ayni_ad_ve_derinlik_reddedilir()
    {
        var t = await TenantAsync("Tasima");
        await using var db = NewDb(t);
        var c = new CategoriesController(db);
        var a = (await c.Create(new CategoryInput("A", null), default)).Value!;
        var b = (await c.Create(new CategoryInput("B", a.Id), default)).Value!;
        var other = (await c.Create(new CategoryInput("Diğer", null), default)).Value!;
        var dup = (await c.Create(new CategoryInput("B", other.Id), default)).Value!;

        Assert.IsType<BadRequestObjectResult>((await c.Move(a.Id, new CategoryMoveInput(b.Id), default)).Result);     // kendi altına
        Assert.IsType<BadRequestObjectResult>((await c.Move(b.Id, new CategoryMoveInput(other.Id), default)).Result); // hedefte aynı ad
        Assert.IsType<BadRequestObjectResult>((await c.Move(b.Id, new CategoryMoveInput(Guid.NewGuid()), default)).Result);
        Assert.IsType<NotFoundObjectResult>((await c.Move(Guid.NewGuid(), new CategoryMoveInput(null), default)).Result);

        var moved = (await c.Move(b.Id, new CategoryMoveInput(null), default)).Value!;
        Assert.Null(moved.ParentId);
        Assert.Equal("b", moved.SlugPath);
        _ = dup;

        // 5 seviyeye kadar zincir kurulur, altıncısı eklenemez.
        Guid? parent = null;
        for (var i = 0; i < 5; i++) parent = (await c.Create(new CategoryInput($"S{i}", parent), default)).Value!.Id;
        Assert.IsType<BadRequestObjectResult>((await c.Create(new CategoryInput("S5", parent), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Move(a.Id, new CategoryMoveInput(parent), default)).Result);
    }

    [Fact]
    public async Task Ayarlar_aktif_pasif_ve_gorsel_dogrulanir_pasif_kategori_bayi_sitesinde_gorunmez()
    {
        var t = await TenantAsync("Ayar");
        await using var db = NewDb(t);
        var c = new CategoriesController(db);
        var cat = (await c.Create(new CategoryInput("Giyim", null), default)).Value!;
        var dealer = new Dealer { Name = "Bayi", TaxNumber = "1000000001", Email = "b@b.com", Status = DealerStatus.Active };
        db.Dealers.Add(dealer);
        db.Products.Add(new Product { Name = "P", Barcode = "P", CategoryId = cat.Id, B2BVisible = true, Stock = 5, Price = 10m });
        await db.SaveChangesAsync();
        var store = new StoreCatalogService(db, new StoreCatalog(db), new DealerPriceService(db));

        Assert.Single(await store.CategoriesAsync(dealer.Id, default));
        var saved = (await c.Settings(cat.Id, new CategorySettingsInput(false, " https://x.test/g.jpg "), default)).Value!;
        Assert.Equal((false, "https://x.test/g.jpg"), (saved.IsActive, saved.ImageUrl));
        Assert.Empty(await store.CategoriesAsync(dealer.Id, default));
        Assert.Equal(0, (await store.ListAsync(dealer.Id, new StoreQuery(null, null, null, 1, 10), default)).Total); // ürünler de gizlenir

        Assert.IsType<BadRequestObjectResult>((await c.Settings(cat.Id, new CategorySettingsInput(true, "ftp://x"), default)).Result);
        var back = (await c.Settings(cat.Id, new CategorySettingsInput(true, ""), default)).Value!;
        Assert.Equal((true, null), (back.IsActive, back.ImageUrl));
        Assert.Single(await store.CategoriesAsync(dealer.Id, default));
        Assert.IsType<NotFoundObjectResult>((await c.Settings(Guid.NewGuid(), new CategorySettingsInput(true, null), default)).Result);
    }

    [Fact]
    public async Task Bayi_sitesi_kategorileri_sira_ve_gorsel_ile_verir()
    {
        var t = await TenantAsync("Sirali");
        await using var db = NewDb(t);
        var c = new CategoriesController(db);
        var a = (await c.Create(new CategoryInput("Alfa", null), default)).Value!;
        var b = (await c.Create(new CategoryInput("Beta", null), default)).Value!;
        await c.Reorder(new CategoryOrderInput(null, [b.Id, a.Id]), default);
        await c.Settings(b.Id, new CategorySettingsInput(true, "https://x.test/b.jpg"), default);
        var dealer = new Dealer { Name = "Bayi", TaxNumber = "1000000002", Email = "b@b.com", Status = DealerStatus.Active };
        db.Dealers.Add(dealer);
        db.Products.AddRange(new Product { Name = "P1", Barcode = "P1", CategoryId = a.Id, B2BVisible = true, Stock = 5 }, new Product { Name = "P2", Barcode = "P2", CategoryId = b.Id, B2BVisible = true, Stock = 5 });
        await db.SaveChangesAsync();

        var cats = await new StoreCatalogService(db, new StoreCatalog(db), new DealerPriceService(db)).CategoriesAsync(dealer.Id, default);
        Assert.Equal(["Beta", "Alfa"], cats.Select(x => x.Name).ToList());
        Assert.Equal(("https://x.test/b.jpg", "beta"), (cats[0].ImageUrl, cats[0].SlugPath));
    }

    [Fact]
    public async Task Silme_urunleri_hedef_kategoriye_tasiyabilir_alt_kategorisi_olan_silinmez()
    {
        var t = await TenantAsync("Silme");
        await using var db = NewDb(t);
        var c = new CategoriesController(db);
        var a = (await c.Create(new CategoryInput("A", null), default)).Value!;
        var b = (await c.Create(new CategoryInput("B", null), default)).Value!;
        var child = (await c.Create(new CategoryInput("Alt", a.Id), default)).Value!;
        await using (var seed = NewDb(t)) { seed.Products.Add(new Product { Name = "P", Barcode = "P", CategoryId = child.Id }); await seed.SaveChangesAsync(); }

        Assert.IsType<BadRequestObjectResult>(await c.Delete(a.Id, null, default));               // alt kategorisi var
        Assert.IsType<BadRequestObjectResult>(await c.Delete(child.Id, null, default));           // ürünü var, hedef yok
        Assert.IsType<BadRequestObjectResult>(await c.Delete(child.Id, child.Id, default));        // hedef kendisi
        Assert.IsType<BadRequestObjectResult>(await c.Delete(child.Id, Guid.NewGuid(), default));  // hedef yok
        Assert.IsType<NoContentResult>(await c.Delete(child.Id, b.Id, default));
        await using var check = NewDb(t);
        Assert.Equal(b.Id, (await check.Products.SingleAsync()).CategoryId);
        Assert.False(await check.Categories.AnyAsync(x => x.Id == child.Id));
    }

    [Fact]
    public async Task Baska_kiracinin_kategorileri_gorunmez()
    {
        var t1 = await TenantAsync("Bir");
        var t2 = await TenantAsync("Iki");
        await using (var seed = NewDb(t1)) { seed.Categories.Add(new Category { Name = "Gizli" }); await seed.SaveChangesAsync(); }
        await using var db2 = NewDb(t2);
        Assert.Empty(await new CategoriesController(db2).List(default));
    }
}
