using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Pricing;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Products;

namespace Pazaryeri.Tests;

public class PricingTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Pricing;Trusted_Connection=True;TrustServerCertificate=True";

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

    // --- hesaplayıcı (aynı vektörler ön yüzdeki priceCalculator.test.ts'te de var) ---

    [Theory]
    [InlineData(100, 15, 10, 20, PriceRounding.None, 155.29)]        // (100+10)*1.2/0.85 = 155.2941…
    [InlineData(100, 15, 10, 20, PriceRounding.NearestWhole, 155)]
    [InlineData(100, 15, 10, 20, PriceRounding.UpWhole, 156)]
    [InlineData(100, 15, 10, 20, PriceRounding.Ending90, 155.90)]
    [InlineData(100, 15, 10, 20, PriceRounding.Ending99, 155.99)]
    [InlineData(200, 10, 0, 0, PriceRounding.None, 222.22)]          // komisyon dışında ekleme yok
    [InlineData(200, 10, 0, 0, PriceRounding.Ending90, 222.90)]
    [InlineData(50, 0, 0, 0, PriceRounding.None, 50)]                // kural yoksa maliyetin kendisi
    [InlineData(80, 0, 0, 25, PriceRounding.UpWhole, 100)]           // tam sayıya denk gelen tutar aynı kalır
    public void Fiyat_formule_ve_yuvarlamaya_gore_hesaplanir(double cost, double commission, double shipping, double profit, PriceRounding rounding, double expected)
    {
        var result = PriceCalculator.Calculate((decimal)cost, (decimal)commission, (decimal)shipping, (decimal)profit, rounding);

        Assert.Equal((decimal)expected, result.Price);
    }

    [Theory]
    [InlineData("222.95", PriceRounding.Ending90, "223.90")]
    [InlineData("222.90", PriceRounding.Ending90, "222.90")]   // zaten ,90 ise aynı kalır
    [InlineData("222.91", PriceRounding.Ending90, "223.90")]
    [InlineData("222.99", PriceRounding.Ending99, "222.99")]
    [InlineData("222.00", PriceRounding.Ending99, "222.99")]
    [InlineData("222.50", PriceRounding.NearestWhole, "223")]  // yarıda yukarı
    [InlineData("222.001", PriceRounding.UpWhole, "223")]
    [InlineData("222.901", PriceRounding.Ending90, "223.90")]  // kuruş altı fark bile yukarı yuvarlanır
    public void Yuvarlama_kenar_durumlari(string value, PriceRounding rounding, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            PriceCalculator.Round(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), rounding));
    }

    [Fact]
    public void Dokum_komisyon_ve_net_kari_verir()
    {
        var b = PriceCalculator.Calculate(100, 15, 10, 20, PriceRounding.None);

        Assert.Equal((110m, 132m, 155.29m, 155.29m), (b.Base, b.WithProfit, b.BeforeRounding, b.Price));
        Assert.Equal(23.29m, b.CommissionAmount);
        Assert.Equal(22.00m, b.NetProfit); // 155.29 − 23.29 − 110
    }

    [Fact]
    public void Gecersiz_degerler_reddedilir()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PriceCalculator.Calculate(0, 10, 0, 0, PriceRounding.None));
        Assert.Throws<ArgumentException>(() => PriceCalculator.Calculate(10, 100, 0, 0, PriceRounding.None)); // %100 komisyonla fiyat sonsuz
        Assert.NotNull(PriceCalculator.Validate(-1, 0, 0));
        Assert.NotNull(PriceCalculator.Validate(10, -5, 0));
        Assert.NotNull(PriceCalculator.Validate(10, 0, 5000));
        Assert.Null(PriceCalculator.Validate(99.99m, 0, 1000));
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

    private static string Body(object? value) =>
        System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    private static PricingController Controller(AppDbContext db) => new(db, TimeProvider.System);

    [Fact]
    public async Task Kurallar_tum_kanallar_icin_varsayilanla_gelir_ve_kayit_degistirir()
    {
        var tenant = await NewTenantAsync("Kural");
        await using var db = NewDb(tenant);
        var c = Controller(db);

        var initial = await c.Rules(default);
        Assert.Equal(["Trendyol", "Hepsiburada", "N11", "Amazon"], initial.Select(r => r.Channel));
        Assert.All(initial, r => Assert.False(r.Saved));

        await c.Save("Trendyol", new(15, 10, 20, "Ending90"), default);
        await using var db2 = NewDb(tenant);
        var saved = (await Controller(db2).Save("Trendyol", new(12.5m, 8, 15, "UpWhole"), default)).Value!; // güncelleme
        Assert.Equal((12.5m, 8m, 15m, "UpWhole", true), (saved.CommissionPercent, saved.ShippingCost, saved.ProfitPercent, saved.Rounding, saved.Saved));
        Assert.Equal(1, await NewDb(tenant).PricingRules.CountAsync());
        Assert.True((await Controller(NewDb(tenant)).Rules(default)).Single(r => r.Channel == "Trendyol").Saved);
    }

    [Fact]
    public async Task Gecersiz_kural_kaydedilmez()
    {
        var tenant = await NewTenantAsync("Gecersiz");
        await using var db = NewDb(tenant);
        var c = Controller(db);

        Assert.IsType<BadRequestObjectResult>((await c.Save("Bilinmeyen", new(10, 0, 0, "None"), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Save("Trendyol", new(null, 0, 0, "None"), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Save("Trendyol", new(10, 0, 0, "Yok"), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Save("Trendyol", new(100, 0, 0, "None"), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Save("Trendyol", new(10, -1, 0, "None"), default)).Result);
        Assert.Contains("Komisyon", Body(((BadRequestObjectResult)(await c.Save("Trendyol", new(120, 0, 0, "None"), default)).Result!).Value));
        Assert.Empty(await NewDb(tenant).PricingRules.ToListAsync());
    }

    [Fact]
    public async Task Kurallar_uygulaninca_maliyeti_olan_urunlerin_kanal_fiyati_yazilir_degismeyenler_atlanir()
    {
        var tenant = await NewTenantAsync("Uygula");
        await using (var seed = NewDb(tenant))
        {
            seed.Products.AddRange(
                new Product { Name = "A", Barcode = "A", Price = 1, Cost = 100 },
                new Product { Name = "B", Barcode = "B", Price = 1, Cost = 200 },
                new Product { Name = "C", Barcode = "C", Price = 1 }); // maliyeti yok
            await seed.SaveChangesAsync();
        }
        await using var db = NewDb(tenant);
        var c = Controller(db);

        Assert.IsType<BadRequestObjectResult>((await c.Apply("Trendyol", default)).Result); // kural henüz yok
        await c.Save("Trendyol", new(15, 10, 20, "Ending90"), default);

        var first = (await c.Apply("Trendyol", default)).Value!;
        Assert.Equal((2, 0, 1), (first.Updated, first.Unchanged, first.NoCost));
        var prices = (await NewDb(tenant).ChannelPrices.Select(x => x.Price).ToListAsync()).OrderBy(v => v).ToList();
        Assert.Equal([155.90m, 296.90m], prices); // 100 → 155,29 → 155,90; 200 → (210×1,2)÷0,85 = 296,47 → 296,90

        var second = (await Controller(NewDb(tenant)).Apply("Trendyol", default)).Value!;
        Assert.Equal((0, 2, 1), (second.Updated, second.Unchanged, second.NoCost));

        // Kural değişince yalnızca fiyatı değişenler güncellenir.
        await using var db3 = NewDb(tenant);
        await Controller(db3).Save("Trendyol", new(15, 10, 25, "Ending90"), default);
        var third = (await Controller(NewDb(tenant)).Apply("Trendyol", default)).Value!;
        Assert.Equal((2, 0, 1), (third.Updated, third.Unchanged, third.NoCost));
    }

    [Fact]
    public async Task Kanal_fiyatlari_kiracilar_arasinda_ayridir()
    {
        var a = await NewTenantAsync("Fiyat A");
        var b = await NewTenantAsync("Fiyat B");
        foreach (var t in new[] { a, b })
        {
            await using var seed = NewDb(t);
            seed.Products.Add(new Product { Name = "P", Barcode = "P-" + t, Price = 1, Cost = 100 });
            await seed.SaveChangesAsync();
        }
        await using var dbA = NewDb(a);
        await Controller(dbA).Save("Trendyol", new(10, 0, 0, "None"), default);
        await Controller(dbA).Apply("Trendyol", default);

        Assert.Equal(1, await NewDb(a).ChannelPrices.CountAsync());
        Assert.Equal(0, await NewDb(b).ChannelPrices.CountAsync()); // B'nin kuralı yok, fiyatı da yazılmadı
        Assert.IsType<BadRequestObjectResult>((await Controller(NewDb(b)).Apply("Trendyol", default)).Result);
    }

    [Fact]
    public async Task Gonderim_katalog_fiyati_yerine_hesaplanan_kanal_fiyatini_kullanir()
    {
        var tenant = await NewTenantAsync("Fiyat C");
        Product product;
        await using (var seed = NewDb(tenant))
        {
            product = new Product { Name = "P", Barcode = "FC1", Description = "d", Price = 50, Cost = 100 };
            product.Images.Add(new ProductImage { Kind = ImageKind.Url, ExternalUrl = "https://cdn.example.com/x.jpg" });
            seed.Products.Add(product);
            seed.ProductChannelData.Add(new ProductChannelData { ProductId = product.Id, Channel = "Trendyol", BrandId = 1, CategoryId = 2 });
            await seed.SaveChangesAsync();
        }
        await using var db = NewDb(tenant);
        var preparer = new ProductChannelPreparer(db, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        var before = Assert.Single(await preparer.PrepareAsync("Trendyol", [product.Id], default));
        Assert.Equal(50m, before.Item!.SalePrice); // kural uygulanmadan katalog fiyatı

        await Controller(db).Save("Trendyol", new(15, 10, 20, "Ending90"), default);
        await Controller(db).Apply("Trendyol", default);
        await using var db2 = NewDb(tenant);
        var after = Assert.Single(await new ProductChannelPreparer(db2, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()).PrepareAsync("Trendyol", [product.Id], default));

        Assert.Equal((155.90m, 155.90m), (after.Item!.SalePrice, after.Item.ListPrice));
    }
}
