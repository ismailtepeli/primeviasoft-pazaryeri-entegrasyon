using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Stock;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Products;

namespace Pazaryeri.Tests;

public class StockTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Stock;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private static AppDbContext NewDb(Guid? tenantId = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    public async Task InitializeAsync()
    {
        await using var db = NewDb();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // --- hesaplayıcı (aynı vektörler ön yüzdeki stockCalculator.test.ts'te de var) ---

    [Theory]
    [InlineData(100, 0, 100, 100)]    // kural yok: tüm stok
    [InlineData(100, 10, 100, 90)]    // güvenlik stoğu düşer
    [InlineData(100, 10, 50, 45)]     // satılabilirin yarısı
    [InlineData(7, 0, 50, 3)]         // aşağı yuvarlama
    [InlineData(5, 10, 100, 0)]       // güvenlik stoğu stoğu aşarsa negatif olmaz
    [InlineData(0, 0, 100, 0)]
    [InlineData(100, 0, 0, 0)]        // %0 pay: kanala hiç açılmaz
    [InlineData(1000, 100, 33.33, 299)] // 900 × 0,3333 = 299,97 → 299
    public void Kanala_acilan_miktar_guvenlik_stogu_ve_paya_gore_hesaplanir(int stock, int safety, double percent, int expected)
    {
        Assert.Equal(expected, StockCalculator.ChannelQuantity(stock, safety, (decimal)percent));
    }

    [Fact]
    public void Urune_ozel_guvenlik_stogu_varsa_kanal_varsayilanini_ezer_ve_gecersiz_degerler_reddedilir()
    {
        Assert.Equal(3, StockCalculator.EffectiveSafety(3, 10));
        Assert.Equal(0, StockCalculator.EffectiveSafety(0, 10)); // 0 açıkça verilmiş değerdir
        Assert.Equal(10, StockCalculator.EffectiveSafety(null, 10));

        Assert.Null(StockCalculator.Validate(0, 100));
        Assert.NotNull(StockCalculator.Validate(-1, 100));
        Assert.NotNull(StockCalculator.Validate(0, 100.5m));
        Assert.NotNull(StockCalculator.Validate(0, -1));
    }

    // --- API ---

    private static async Task<Guid> NewTenantAsync(string name)
    {
        await using var db = NewDb();
        var tenant = new Tenant { Name = name };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static StockController Controller(AppDbContext db) => new(db, TimeProvider.System);

    [Fact]
    public async Task Ayar_varsayilan_acik_gelir_ve_kiraciya_ozel_kaydedilir()
    {
        var a = await NewTenantAsync("Ayar A");
        var b = await NewTenantAsync("Ayar B");
        await using var dbA = NewDb(a);

        Assert.True((await Controller(dbA).GetSettings(default)).DeductAcrossChannels);
        await Controller(dbA).SaveSettings(new(false), default);
        await Controller(NewDb(a)).SaveSettings(new(false), default); // ikinci kayıt güncelleme olmalı

        Assert.False((await Controller(NewDb(a)).GetSettings(default)).DeductAcrossChannels);
        Assert.True((await Controller(NewDb(b)).GetSettings(default)).DeductAcrossChannels);
        Assert.Equal(1, await NewDb(a).StockSettings.CountAsync());
    }

    [Fact]
    public async Task Kurallar_varsayilanla_gelir_dogrulanir_ve_kaydedilir()
    {
        var tenant = await NewTenantAsync("Kural");
        await using var db = NewDb(tenant);
        var c = Controller(db);

        Assert.All(await c.Rules(default), r => Assert.Equal((0, 100m, false), (r.SafetyStock, r.AllocationPercent, r.Saved)));
        Assert.IsType<BadRequestObjectResult>((await c.SaveRule("Bilinmeyen", new(0, 100), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.SaveRule("Trendyol", new(null, 100), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.SaveRule("Trendyol", new(-1, 100), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.SaveRule("Trendyol", new(0, 101), default)).Result);
        Assert.Empty(await NewDb(tenant).StockRules.ToListAsync());

        await c.SaveRule("Trendyol", new(5, 80), default);
        var updated = (await Controller(NewDb(tenant)).SaveRule("Trendyol", new(8, 60.555m), default)).Value!;
        Assert.Equal((8, 60.56m, true), (updated.SafetyStock, updated.AllocationPercent, updated.Saved));
        Assert.Equal(1, await NewDb(tenant).StockRules.CountAsync());
    }

    [Fact]
    public async Task Urun_tablosu_fiziksel_guvenlik_ve_kanala_acilan_miktari_verir_sayfalar_ve_arar()
    {
        var tenant = await NewTenantAsync("Tablo");
        await using (var seed = NewDb(tenant))
        {
            seed.Products.AddRange(Enumerable.Range(1, 25).Select(i => new Product { Name = $"Ürün {i:00}", Barcode = $"T{i:00}", Price = 1, Stock = 100 }));
            seed.Products.Add(new Product { Name = "Özel", Barcode = "OZEL", Price = 1, Stock = 100, SafetyStock = 40 });
            await seed.SaveChangesAsync();
        }
        await using var db = NewDb(tenant);
        var c = Controller(db);
        await c.SaveRule("Trendyol", new(10, 50), default);

        var page = (await Controller(NewDb(tenant)).Products("Trendyol", null, 1, 10, default)).Value!;
        Assert.Equal((26, 10), (page.Total, page.Items.Count));

        var ozel = (await Controller(NewDb(tenant)).Products("Trendyol", "OZEL", 1, 10, default)).Value!.Items.Single();
        Assert.Equal((100, 40, 40, 30), (ozel.Stock, ozel.SafetyStock!.Value, ozel.EffectiveSafety, ozel.ChannelQuantity)); // (100−40)×%50

        var normal = (await Controller(NewDb(tenant)).Products("Trendyol", "T01", 1, 10, default)).Value!.Items.Single();
        Assert.Equal((null, 10, 45), (normal.SafetyStock, normal.EffectiveSafety, normal.ChannelQuantity)); // (100−10)×%50

        var other = (await Controller(NewDb(tenant)).Products("N11", "T01", 1, 10, default)).Value!.Items.Single();
        Assert.Equal(100, other.ChannelQuantity); // N11 için kural yok: tüm stok
        Assert.IsType<BadRequestObjectResult>((await Controller(NewDb(tenant)).Products("Yok", null, 1, 10, default)).Result);
    }

    [Fact]
    public async Task Urune_ozel_guvenlik_stogu_ayarlanir_temizlenir_ve_baska_kiracida_etkisizdir()
    {
        var a = await NewTenantAsync("Guv A");
        var b = await NewTenantAsync("Guv B");
        Guid product;
        await using (var seed = NewDb(a)) { var p = new Product { Name = "P", Barcode = "G1", Price = 1, Stock = 50 }; seed.Products.Add(p); await seed.SaveChangesAsync(); product = p.Id; }
        await using var db = NewDb(a);
        var c = Controller(db);

        Assert.IsType<NoContentResult>(await c.SetSafety(product, new(12), default));
        Assert.Equal(12, (await NewDb(a).Products.SingleAsync()).SafetyStock);
        Assert.IsType<BadRequestObjectResult>(await c.SetSafety(product, new(-1), default));
        Assert.IsType<NotFoundObjectResult>(await Controller(NewDb(b)).SetSafety(product, new(5), default)); // başka kiracı göremez

        await c.SetSafety(product, new(null), default); // varsayılana dön
        Assert.Null((await NewDb(a).Products.SingleAsync()).SafetyStock);
    }

    [Fact]
    public async Task Gonderimde_kanala_acilan_miktar_gider_kural_yoksa_tum_stok()
    {
        var tenant = await NewTenantAsync("Gonderim");
        Product product;
        await using (var seed = NewDb(tenant))
        {
            product = new Product { Name = "P", Barcode = "SQ1", Description = "d", Price = 5, Stock = 100 };
            product.Images.Add(new ProductImage { Kind = ImageKind.Url, ExternalUrl = "https://cdn.example.com/x.jpg" });
            seed.Products.Add(product);
            seed.ProductChannelData.Add(new ProductChannelData { ProductId = product.Id, Channel = "Trendyol", BrandId = 1, CategoryId = 2 });
            await seed.SaveChangesAsync();
        }
        Task<PreparedProduct> Prepare(AppDbContext db) => new ProductChannelPreparer(db, new ConfigurationBuilder().Build())
            .PrepareAsync("Trendyol", [product.Id], default).ContinueWith(t => Assert.Single(t.Result));

        Assert.Equal(100, (await Prepare(NewDb(tenant))).Item!.Quantity); // kural yok

        await Controller(NewDb(tenant)).SaveRule("Trendyol", new(10, 50), default);
        Assert.Equal(45, (await Prepare(NewDb(tenant))).Item!.Quantity);

        await Controller(NewDb(tenant)).SetSafety(product.Id, new(0), default); // ürüne özel 0: kanal varsayılanını ezer
        Assert.Equal(50, (await Prepare(NewDb(tenant))).Item!.Quantity);
    }
}
