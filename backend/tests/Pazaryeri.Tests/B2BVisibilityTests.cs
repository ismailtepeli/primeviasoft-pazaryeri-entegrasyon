using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Pricing;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Pricing;

namespace Pazaryeri.Tests;

public class B2BVisibilityTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_B2BVisibility;Trusted_Connection=True;TrustServerCertificate=True";

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

    private static async Task<List<string>> NamesAsync(AppDbContext db, Guid? group) =>
        (await (await new StoreCatalog(db).VisibleProductsAsync(group, default)).OrderBy(p => p.Name).Select(p => p.Name).ToListAsync());

    // --- stok gösterimi ---

    [Theory]
    [InlineData(StockDisplayMode.Exact, 20, 5, 15, "15", true)]
    [InlineData(StockDisplayMode.Exact, 3, 5, 0, "0", false)]       // güvenlik stoğu stoktan fazla → 0
    [InlineData(StockDisplayMode.Level, 100, null, null, "Stokta var", true)]
    [InlineData(StockDisplayMode.Level, 8, 5, null, "Az kaldı", true)]   // 3 satılabilir ≤ eşik 5
    [InlineData(StockDisplayMode.Level, 5, 5, null, "Tükendi", false)]
    [InlineData(StockDisplayMode.Hidden, 50, null, null, null, true)]
    public void Stok_gosterimi_guvenlik_stogu_dusulerek_hesaplanir(StockDisplayMode mode, int physical, int? safety, int? quantity, string? label, bool inStock)
    {
        var v = StockDisplay.Compute(physical, safety, mode, 5);
        Assert.Equal((quantity, label, inStock), (v.Quantity, v.Label, v.InStock));
    }

    // --- görünürlük kuralı ---

    [Fact]
    public async Task Varsayilan_olarak_hicbir_urun_gorunmez_bayrak_acilinca_gorunur()
    {
        var tenant = await TenantAsync("Varsayilan");
        await using var db = NewDb(tenant);
        db.Products.AddRange(new Product { Name = "A", Barcode = "A" }, new Product { Name = "B", Barcode = "B", B2BVisible = true });
        await db.SaveChangesAsync();
        Assert.Equal(["B"], await NamesAsync(db, null));
    }

    [Fact]
    public async Task Gizli_kategori_alt_kategorilerini_ve_urunlerini_gizler_kategorisiz_urun_gorunur()
    {
        var tenant = await TenantAsync("Kategori");
        await using var db = NewDb(tenant);
        var root = new Category { Name = "Giyim", B2BHidden = true };
        var child = new Category { Name = "Tişört" };
        var other = new Category { Name = "Kırtasiye" };
        db.Categories.AddRange(root, child, other);
        await db.SaveChangesAsync();
        child.ParentId = root.Id;
        db.Products.AddRange(
            new Product { Name = "Kök ürün", Barcode = "1", CategoryId = root.Id, B2BVisible = true },
            new Product { Name = "Alt ürün", Barcode = "2", CategoryId = child.Id, B2BVisible = true },
            new Product { Name = "Defter", Barcode = "3", CategoryId = other.Id, B2BVisible = true },
            new Product { Name = "Kategorisiz", Barcode = "4", B2BVisible = true });
        await db.SaveChangesAsync();
        Assert.Equal(["Defter", "Kategorisiz"], await NamesAsync(db, null));
    }

    [Fact]
    public async Task Grup_kisiti_yalnizca_o_grubu_etkiler_urun_ve_kategori_kisiti_birlikte_calisir()
    {
        var tenant = await TenantAsync("Grup");
        await using var db = NewDb(tenant);
        var gold = new DealerGroup { Name = "Altın" };
        var silver = new DealerGroup { Name = "Gümüş" };
        var cat = new Category { Name = "Özel" };
        db.AddRange(gold, silver, cat);
        await db.SaveChangesAsync();
        var a = new Product { Name = "A", Barcode = "A", B2BVisible = true };
        var b = new Product { Name = "B", Barcode = "B", B2BVisible = true, CategoryId = cat.Id };
        var c = new Product { Name = "C", Barcode = "C", B2BVisible = true };
        db.Products.AddRange(a, b, c);
        await db.SaveChangesAsync();
        db.GroupVisibilityRules.AddRange(
            new GroupVisibilityRule { GroupId = silver.Id, ProductId = a.Id },
            new GroupVisibilityRule { GroupId = silver.Id, CategoryId = cat.Id });
        await db.SaveChangesAsync();

        Assert.Equal(["A", "B", "C"], await NamesAsync(db, gold.Id));
        Assert.Equal(["C"], await NamesAsync(db, silver.Id));
        Assert.Equal(["A", "B", "C"], await NamesAsync(db, null)); // grupsuz bayi kısıtsız
    }

    [Fact]
    public void Alt_kategori_hesabi_zincirleme_iner()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid(); var d = Guid.NewGuid();
        var all = new List<(Guid, Guid?)> { (a, null), (b, a), (c, b), (d, null) };
        var result = StoreCatalog.Descendants(all, new HashSet<Guid> { a });
        Assert.Equal(new HashSet<Guid> { a, b, c }, result);
    }

    [Fact]
    public async Task Baska_kiracinin_urunu_ve_kisiti_gorunmez()
    {
        var t1 = await TenantAsync("Bir");
        var t2 = await TenantAsync("Iki");
        await using (var seed = NewDb(t1))
        {
            seed.Products.Add(new Product { Name = "Gizli", Barcode = "G", B2BVisible = true });
            await seed.SaveChangesAsync();
        }
        await using var db2 = NewDb(t2);
        Assert.Empty(await NamesAsync(db2, null));
        Assert.Empty(await new B2BController(db2).Restrictions(default));
    }

    // --- yönetici API ---

    [Fact]
    public async Task Toplu_gorunurluk_ayarlar_ve_dogrulamalar()
    {
        var tenant = await TenantAsync("Toplu");
        await using var db = NewDb(tenant);
        var p1 = new Product { Name = "A", Barcode = "A" };
        var p2 = new Product { Name = "B", Barcode = "B" };
        db.Products.AddRange(p1, p2);
        await db.SaveChangesAsync();
        var c = new B2BController(db);

        Assert.IsType<BadRequestObjectResult>((await c.SetProductVisibility(new ProductVisibilityInput([], true), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.SetProductVisibility(new ProductVisibilityInput(Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToList(), true), default)).Result);

        var ok = await c.SetProductVisibility(new ProductVisibilityInput([p1.Id, Guid.NewGuid()], true), default);
        Assert.Contains("updated = 1", ok.Value!.ToString());
        Assert.Equal(["A"], await NamesAsync(NewDb(tenant), null));

        await c.SetProductVisibility(new ProductVisibilityInput([p1.Id], false), default);
        Assert.Empty(await NamesAsync(NewDb(tenant), null));
    }

    [Fact]
    public async Task Kategori_gizleme_ayar_ve_kisit_uclari_calisir()
    {
        var tenant = await TenantAsync("Uclar");
        await using var db = NewDb(tenant);
        var group = new DealerGroup { Name = "G" };
        var cat = new Category { Name = "K" };
        var prod = new Product { Name = "P", Barcode = "P" };
        db.AddRange(group, cat, prod);
        await db.SaveChangesAsync();
        var c = new B2BController(db);

        Assert.IsType<NoContentResult>(await c.SetCategoryHidden(cat.Id, new CategoryHiddenInput(true), default));
        Assert.True((await c.Categories(default)).Single().Hidden);
        Assert.IsType<NotFoundObjectResult>(await c.SetCategoryHidden(Guid.NewGuid(), new CategoryHiddenInput(true), default));

        Assert.Equal(("Level", 5), ((await c.GetSettings(default)).StockDisplay, (await c.GetSettings(default)).LowStockThreshold));
        var saved = (await c.SaveSettings(new B2BSettingsDto("exact", 10), default)).Value!;
        Assert.Equal(("Exact", 10), (saved.StockDisplay, saved.LowStockThreshold));
        Assert.IsType<BadRequestObjectResult>((await c.SaveSettings(new B2BSettingsDto("x", 1), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.SaveSettings(new B2BSettingsDto("Level", -1), default)).Result);
        Assert.Equal("Exact", (await c.GetSettings(default)).StockDisplay);

        Assert.IsType<BadRequestObjectResult>((await c.AddRestriction(new RestrictionInput(group.Id, null, null), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.AddRestriction(new RestrictionInput(group.Id, prod.Id, cat.Id), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.AddRestriction(new RestrictionInput(null, prod.Id, null), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.AddRestriction(new RestrictionInput(Guid.NewGuid(), prod.Id, null), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.AddRestriction(new RestrictionInput(group.Id, Guid.NewGuid(), null), default)).Result);

        var first = (await c.AddRestriction(new RestrictionInput(group.Id, prod.Id, null), default)).Value!;
        var again = (await c.AddRestriction(new RestrictionInput(group.Id, prod.Id, null), default)).Value!;
        Assert.Equal(first.Id, again.Id); // tekrar eklenmez
        Assert.Equal(("G", "P"), (first.GroupName, first.ProductName));
        await c.AddRestriction(new RestrictionInput(group.Id, null, cat.Id), default);
        Assert.Equal(2, (await c.Restrictions(default)).Count);

        Assert.IsType<NoContentResult>(await c.RemoveRestriction(first.Id, default));
        Assert.IsType<NotFoundObjectResult>(await c.RemoveRestriction(first.Id, default));
    }
}
