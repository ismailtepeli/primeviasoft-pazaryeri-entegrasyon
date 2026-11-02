using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Modules;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Modules;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Shop;

namespace Pazaryeri.Tests;

public class ShopCatalogTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Shop;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class AmbientProvider : ITenantProvider
    {
        public Guid? TenantId => AmbientTenant.TenantId;
    }

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private static AppDbContext NewDb(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, tenantId is null ? new AmbientProvider() : new FixedTenant(tenantId));

    private static ShopCatalogService Shop(AppDbContext db) =>
        new(db, new ModuleService(db, new AmbientProvider(), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System));

    public async Task InitializeAsync()
    {
        await using var db = NewDb(Guid.Empty);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record World(Guid Tenant, string Slug, Guid Tee, Guid Polo, Guid Notebook, Guid Hidden, Guid Out, Guid HiddenCat);

    private static async Task<World> SeedAsync(string slug, bool shopOn = true, bool active = true)
    {
        await using var root = NewDb(Guid.Empty);
        var tenant = new Tenant { Name = "Firma " + slug, Slug = slug, IsActive = active };
        root.Tenants.Add(tenant);
        await root.SaveChangesAsync();
        await using var db = NewDb(tenant.Id);
        if (shopOn) db.TenantModules.Add(new TenantModule { ModuleId = ModuleIds.B2CStore, Enabled = true });
        var giyim = new Category { Name = "Giyim" };
        var gizli = new Category { Name = "Gizli Kat", B2CHidden = true };
        db.Categories.AddRange(giyim, gizli);
        await db.SaveChangesAsync();
        var tee = new Product { Name = "Tişört", Barcode = "T1", Brand = "Demo", CategoryId = giyim.Id, Price = 100m, B2CSalePrice = 80m, Stock = 50, B2CVisible = true };
        var polo = new Product { Name = "Polo", Barcode = "T2", Brand = "Demo", CategoryId = giyim.Id, Price = 200m, B2CSalePrice = 250m, Stock = 3, B2CVisible = true }; // kampanya normalden pahalı: yok sayılır
        var nb = new Product { Name = "Defter", Barcode = "N1", Brand = "Kağıt", Price = 40m, Stock = 100, SafetyStock = 98, B2CVisible = true, MainId = "DEF", Variant = "A5" };
        var nb2 = new Product { Name = "Defter", Barcode = "N2", Brand = "Kağıt", Price = 45m, Stock = 100, B2CVisible = true, MainId = "DEF", Variant = "A4" };
        var hidden = new Product { Name = "Görünmez", Barcode = "H1", Price = 10m, Stock = 10, B2CVisible = false, B2BVisible = true };
        var inHiddenCat = new Product { Name = "Gizli Kategoride", Barcode = "H2", Price = 10m, Stock = 10, B2CVisible = true, CategoryId = gizli.Id };
        var outOfStock = new Product { Name = "Tükenen", Barcode = "O1", Price = 30m, Stock = 0, B2CVisible = true };
        db.AddRange(tee, polo, nb, nb2, hidden, inHiddenCat, outOfStock);
        await db.SaveChangesAsync();
        return new World(tenant.Id, slug, tee.Id, polo.Id, nb.Id, hidden.Id, outOfStock.Id, gizli.Id);
    }

    private static ShopQuery Q(string? search = null, string? sort = null, decimal? min = null, decimal? max = null, bool inStock = false, Guid? category = null) =>
        new(search, category, sort, 1, 50, null, min, max, inStock);

    // --- kiracı ve modül kapısı ---

    [Fact]
    public async Task Magaza_yalnizca_etkin_kiracida_ve_B2C_modulu_aciksa_bulunur()
    {
        var ok = await SeedAsync("acik");
        await SeedAsync("kapali", shopOn: false);
        await SeedAsync("pasif", active: false);
        await using var db = NewDb(null);
        var shop = Shop(db);
        Assert.Equal(ok.Tenant, await shop.ResolveAsync(" ACIK "));
        Assert.Null(await shop.ResolveAsync("kapali"));
        Assert.Null(await shop.ResolveAsync("pasif"));
        Assert.Null(await shop.ResolveAsync("yok"));
        Assert.Null(await shop.ResolveAsync(null));
        Assert.Equal("Firma acik", (await shop.InfoAsync("acik"))!.Name);
        Assert.Null(await shop.InfoAsync("kapali"));
    }

    // --- liste ---

    [Fact]
    public async Task Liste_yalnizca_B2C_gorunur_ve_gizli_kategoride_olmayan_urunleri_verir_kampanya_fiyatini_yansitir()
    {
        var w = await SeedAsync("liste");
        await using var db = NewDb(null);
        var page = await Shop(db).ListAsync(w.Tenant, Q());
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(new[] { "Defter", "Defter", "Polo", "Tükenen", "Tişört" }.Order(), page.Items.Select(i => i.Name).Order());
        Assert.DoesNotContain(page.Items, i => i.Name is "Görünmez" or "Gizli Kategoride");

        var tee = page.Items.Single(i => i.Name == "Tişört");
        Assert.Equal((80m, 100m), (tee.Price, tee.ListPrice)); // kampanya: üstü çizili normal fiyat
        var polo = page.Items.Single(i => i.Name == "Polo");
        Assert.Equal((200m, (decimal?)null), (polo.Price, polo.ListPrice)); // normalden pahalı kampanya yok sayılır
    }

    [Fact]
    public async Task Stok_adedi_sizdirilmaz_yalnizca_durum_etiketi_gelir_guvenlik_stogu_dusulur()
    {
        var w = await SeedAsync("stok");
        await using var db = NewDb(null);
        var items = (await Shop(db).ListAsync(w.Tenant, Q())).Items;
        Assert.Equal(("Stokta var", true), (items.Single(i => i.Name == "Tişört").StockLabel, items.Single(i => i.Name == "Tişört").InStock));
        Assert.Equal("Az kaldı", items.Single(i => i.Name == "Polo").StockLabel); // 3 adet
        Assert.Equal(("Tükendi", false), (items.Single(i => i.Name == "Tükenen").StockLabel, items.Single(i => i.Name == "Tükenen").InStock));
        Assert.Equal("Az kaldı", items.Single(i => i.Code() == "N1").StockLabel); // 100 - 98 güvenlik = 2

        var onlyInStock = await Shop(db).ListAsync(w.Tenant, Q(inStock: true));
        Assert.DoesNotContain(onlyInStock.Items, i => i.Name == "Tükenen");
    }

    [Fact]
    public async Task Fiyat_siralamasi_suzgeci_ve_aralik_etkin_fiyata_gore_calisir()
    {
        var w = await SeedAsync("fiyat");
        await using var db = NewDb(null);
        var shop = Shop(db);
        var asc = (await shop.ListAsync(w.Tenant, Q(sort: "price"))).Items;
        Assert.Equal(["Tükenen", "Defter", "Defter", "Tişört", "Polo"], asc.Select(i => i.Name).ToArray()); // 30, 40, 45, 80 (kampanya), 200
        var desc = (await shop.ListAsync(w.Tenant, Q(sort: "price_desc"))).Items;
        Assert.Equal("Polo", desc[0].Name);

        var ranged = await shop.ListAsync(w.Tenant, Q(min: 70, max: 100));
        Assert.Equal(["Tişört"], ranged.Items.Select(i => i.Name).ToArray()); // normal fiyat 100 değil, etkin 80 sayılır
        Assert.Equal(1, ranged.Total);
        Assert.Equal((30m, 200m), (ranged.Facets.PriceMin, ranged.Facets.PriceMax)); // aralık, fiyat süzgecinden bağımsız tüm sonuç üzerindendir
    }

    [Fact]
    public async Task Arama_marka_ve_kategori_sayilari_calisir_gizli_kategori_listelenmez()
    {
        var w = await SeedAsync("arama");
        await using var db = NewDb(null);
        var shop = Shop(db);
        Assert.Equal(["Tişört"], (await shop.ListAsync(w.Tenant, Q(search: "tişör"))).Items.Select(i => i.Name).ToArray());
        var brands = (await shop.ListAsync(w.Tenant, Q())).Facets.Brands;
        Assert.Equal(2, brands.Single(b => b.Name == "Demo").Count);

        var categories = await shop.CategoriesAsync(w.Tenant);
        var giyim = Assert.Single(categories);
        Assert.Equal(("Giyim", 2), (giyim.Name, giyim.ProductCount));
    }

    // --- ayrıntı ve yalıtım ---

    [Fact]
    public async Task Ayrinti_gorunmeyen_urun_icin_null_varyantlar_listelenir_ve_baska_kiracinin_urunu_gorunmez()
    {
        var a = await SeedAsync("detaya");
        var b = await SeedAsync("detayb");
        await using var db = NewDb(null);
        var shop = Shop(db);

        var nb = (await shop.DetailAsync(a.Tenant, a.Notebook))!;
        Assert.Equal(2, nb.Variants.Count);
        Assert.Equal(new string?[] { "A4", "A5" }, nb.Variants.Select(v => v.Variant));
        Assert.Equal("TRY", nb.Currency);
        Assert.Equal(80m, (await shop.DetailAsync(a.Tenant, a.Tee))!.Price);

        Assert.Null(await shop.DetailAsync(a.Tenant, a.Hidden)); // B2C bayrağı kapalı
        Assert.Null(await shop.DetailAsync(b.Tenant, a.Tee)); // başka kiracının ürünü
        Assert.DoesNotContain((await shop.ListAsync(b.Tenant, Q())).Items, i => i.Id == a.Tee);
    }

    // --- yönetici ucu ---

    [Fact]
    public async Task Yonetici_B2C_gorunurlugunu_toplu_degistirir_kampanya_fiyatini_dogrular()
    {
        var w = await SeedAsync("yonetim");
        await using var db = NewDb(w.Tenant);
        var c = new B2CController(db);

        var ok = Assert.IsType<OkObjectResult>(Unwrap(await c.SetProductVisibility(new ProductVisibilityInput([w.Hidden], true), default)));
        Assert.NotNull(ok);
        Assert.True((await db.Products.AsNoTracking().SingleAsync(p => p.Id == w.Hidden)).B2CVisible);
        Assert.IsType<BadRequestObjectResult>((await c.SetProductVisibility(new ProductVisibilityInput([], true), default)).Result);

        Assert.IsType<BadRequestObjectResult>((await c.SetSalePrice(w.Tee, new SalePriceInput(100m), default)).Result); // normal fiyata eşit
        Assert.IsType<BadRequestObjectResult>((await c.SetSalePrice(w.Tee, new SalePriceInput(0m), default)).Result);
        Assert.IsType<NotFoundObjectResult>((await c.SetSalePrice(Guid.NewGuid(), new SalePriceInput(5m), default)).Result);
        await c.SetSalePrice(w.Tee, new SalePriceInput(60m), default);
        Assert.Equal(60m, (await db.Products.AsNoTracking().SingleAsync(p => p.Id == w.Tee)).B2CSalePrice);
        await c.SetSalePrice(w.Tee, new SalePriceInput(null), default); // kampanya kalkar
        Assert.Null((await db.Products.AsNoTracking().SingleAsync(p => p.Id == w.Tee)).B2CSalePrice);
    }

    [Fact]
    public async Task Toplu_kampanya_yuzde_ve_sabit_fiyat_uygular_kuralsizlari_atlar_ve_kaldirir()
    {
        var w = await SeedAsync("kampanya");
        await using var db = NewDb(w.Tenant);
        var c = new B2CController(db);

        var pct = (await c.SetSale(new BulkSaleInput([w.Tee, w.Notebook], "percent", 25), default)).Value!;
        Assert.Equal((2, 0, 0), (pct.Updated, pct.Cleared, pct.Skipped.Count));
        Assert.Equal((75m, 30m), (await SaleOf(db, w.Tee), await SaleOf(db, w.Notebook))); // 100 -> 75, 40 -> 30

        var fixedPrice = (await c.SetSale(new BulkSaleInput([w.Tee, w.Polo], "price", 150), default)).Value!;
        Assert.Equal(1, fixedPrice.Updated); // Polo normal 200, 150 geçerli; Tişört normal 100 < 150: atlanır
        Assert.Equal(["Tişört"], fixedPrice.Skipped.ToArray());
        Assert.Equal(75m, await SaleOf(db, w.Tee)); // atlanan ürünün eski kampanyası korunur
        Assert.Equal(150m, await SaleOf(db, w.Polo));

        var cleared = (await c.SetSale(new BulkSaleInput([w.Tee, w.Notebook, w.Out], "clear", 0), default)).Value!;
        Assert.Equal((0, 2), (cleared.Updated, cleared.Cleared)); // kampanyası olmayan sayılmaz
        Assert.Null(await SaleOf(db, w.Tee));
    }

    [Fact]
    public async Task Toplu_kampanya_girdisini_dogrular_ve_B2C_kategorileri_listelenir_gizlenir()
    {
        var w = await SeedAsync("kampanyadogrula");
        await using var db = NewDb(w.Tenant);
        var c = new B2CController(db);
        Assert.IsType<BadRequestObjectResult>((await c.SetSale(new BulkSaleInput([], "percent", 10), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.SetSale(new BulkSaleInput([w.Tee], "bilinmeyen", 10), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.SetSale(new BulkSaleInput([w.Tee], "percent", 0), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.SetSale(new BulkSaleInput([w.Tee], "percent", 95), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.SetSale(new BulkSaleInput([w.Tee], "price", 0), default)).Result);

        var categories = await c.Categories(default);
        Assert.Equal(("Giyim", false, 2), (categories.Single(x => x.Path == "Giyim").Path, categories.Single(x => x.Path == "Giyim").Hidden, categories.Single(x => x.Path == "Giyim").ProductCount));
        Assert.True(categories.Single(x => x.Path == "Gizli Kat").Hidden);
        await c.SetCategoryHidden(w.HiddenCat, new CategoryHiddenInput(false), default);
        Assert.False((await c.Categories(default)).Single(x => x.Path == "Gizli Kat").Hidden);
    }

    private static async Task<decimal?> SaleOf(AppDbContext db, Guid id) => (await db.Products.AsNoTracking().SingleAsync(p => p.Id == id)).B2CSalePrice;

    private static object? Unwrap(ActionResult<object> r) => r.Result ?? new OkObjectResult(r.Value);
}

internal static class ShopRowExtensions
{
    /// <summary>Testte barkoda göre satır seçmek için: ad aynı olan varyantlar ayırt edilsin.</summary>
    public static string Code(this ShopProductRow row) => row.Name == "Defter" && row.Price == 40m ? "N1" : row.Name;
}
