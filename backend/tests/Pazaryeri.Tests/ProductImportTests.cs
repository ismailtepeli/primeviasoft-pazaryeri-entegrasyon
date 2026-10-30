using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Products;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Products;

namespace Pazaryeri.Tests;

public class ProductImportTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Import;Trusted_Connection=True;TrustServerCertificate=True";

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

    private static async Task<Guid> NewTenantAsync(string name)
    {
        await using var db = NewDb(null);
        var tenant = new Tenant { Name = name };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static readonly string[] Header = ["Ürün Ana Kodu", "Ürün Adı", "Barkod", "Varyant", "Stok", "Fiyat"];

    private static MemoryStream Sheet(string[] header, params object?[][] rows)
    {
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Ürünler");
        for (var c = 0; c < header.Length; c++) ws.Cell(1, c + 1).Value = header[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                if (rows[r][c] is { } v) ws.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(v);
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private static Task<ProductImportResult> Import(AppDbContext db, Stream s) => new ProductImportService(db).ImportAsync(s);

    [Fact]
    public async Task Sablon_kendi_basliklariyla_geri_yuklenebilir()
    {
        var tenant = await NewTenantAsync("Sablon");
        await using var db = NewDb(tenant);
        var service = new ProductImportService(db);

        await using var template = new MemoryStream(service.BuildTemplate());
        var result = await service.ImportAsync(template);

        Assert.Empty(result.Errors);
        Assert.Equal((2, 0, 1), (result.Created, result.Updated, result.Groups)); // iki varyant tek grup
    }

    [Fact]
    public async Task Varyantlari_ana_kodla_gruplar()
    {
        var tenant = await NewTenantAsync("Grup");
        await using var db = NewDb(tenant);

        var result = await Import(db, Sheet(Header,
            ["T1", "Tişört", "B1", "Beden: M", 5, 100],
            ["T1", "Tişört", "B2", "Beden: L", 3, 100],
            ["", "Çanta", "B3", null, 1, 50]));

        Assert.Equal((3, 2), (result.Created, result.Groups));
        var saved = await NewDb(tenant).Products.OrderBy(p => p.Barcode).ToListAsync();
        Assert.Equal(["T1", "T1", null], saved.Select(p => p.MainId));
        Assert.Equal("Beden: L", saved[1].Variant);
    }

    [Fact]
    public async Task Satir_hatalari_numarasiyla_raporlanir_gecerli_satirlar_kaydedilir()
    {
        var tenant = await NewTenantAsync("Hata");
        await using var db = NewDb(tenant);

        var result = await Import(db, Sheet(Header,
            ["", "Geçerli", "OK1", null, 2, 10],            // 2
            ["", "", "X1", null, 2, 10],                    // 3 ad boş
            ["", "Eksi stok", "X2", null, -1, 10],          // 4
            ["", "Kesirli stok", "X3", null, 1.5, 10],      // 5
            ["", "Sıfır fiyat", "X4", null, 1, 0],          // 6
            ["", "Barkodsuz", "", null, 1, 10],             // 7
            ["", "Tekrar", "OK1", null, 1, 10],             // 8 dosyada tekrar
            ["", "Metin sayı", "OK2", null, "12", "1.250,50"]));

        Assert.Equal(2, result.Created);
        Assert.Equal([3, 4, 5, 6, 7, 8], result.Errors.Select(e => e.Row));
        Assert.Contains("Ürün adı boş", result.Errors[0].Message);
        Assert.Contains("2. satırda", result.Errors[5].Message);
        var ok2 = await NewDb(tenant).Products.SingleAsync(p => p.Barcode == "OK2");
        Assert.Equal((12, 1250.50m), (ok2.Stock, ok2.Price)); // Türkçe biçimli metin sayılar okunur
    }

    [Fact]
    public async Task Ayni_ana_kodda_farkli_ad_hata_olur()
    {
        var tenant = await NewTenantAsync("Ad");
        await using var db = NewDb(tenant);

        var result = await Import(db, Sheet(Header,
            ["T1", "Tişört", "B1", null, 1, 10],
            ["T1", "Pantolon", "B2", null, 1, 10]));

        Assert.Equal(1, result.Created);
        var error = Assert.Single(result.Errors);
        Assert.Equal(3, error.Row);
        Assert.Contains("aynı olmalı", error.Message);
    }

    [Fact]
    public async Task Kayitli_barkod_guncellenir_ve_baska_kiracinin_urunune_dokunulmaz()
    {
        var a = await NewTenantAsync("Kiracı A");
        var b = await NewTenantAsync("Kiracı B");
        await using (var seed = NewDb(a)) { seed.Products.Add(new Product { Name = "Eski", Barcode = "B1", Stock = 1, Price = 1 }); await seed.SaveChangesAsync(); }
        await using (var seed = NewDb(b)) { seed.Products.Add(new Product { Name = "Başkası", Barcode = "B1", Stock = 9, Price = 9 }); await seed.SaveChangesAsync(); }

        await using var db = NewDb(a);
        var result = await Import(db, Sheet(Header, ["", "Yeni", "B1", null, 7, 70]));

        Assert.Equal((0, 1), (result.Created, result.Updated));
        var mine = await NewDb(a).Products.SingleAsync();
        Assert.Equal(("Yeni", 7), (mine.Name, mine.Stock));
        Assert.Equal("Başkası", (await NewDb(b).Products.SingleAsync()).Name);
    }

    [Fact]
    public async Task Eksik_baslik_ya_da_bozuk_dosya_tum_yuklemeyi_reddeder()
    {
        var tenant = await NewTenantAsync("Baslik");
        await using var db = NewDb(tenant);

        var missing = await Assert.ThrowsAsync<ProductImportException>(() => Import(db, Sheet(["Ürün Adı", "Barkod"], ["A", "1"])));
        Assert.Contains("Stok", missing.Message);
        Assert.Contains("Fiyat", missing.Message);

        await Assert.ThrowsAsync<ProductImportException>(() => Import(db, new MemoryStream([1, 2, 3])));
        Assert.Empty(await NewDb(tenant).Products.ToListAsync());
    }

    [Fact]
    public async Task Opsiyonel_sutunlar_olmadan_da_yuklenir_ve_satir_siniri_uygulanir()
    {
        var tenant = await NewTenantAsync("Opsiyonel");
        await using var db = NewDb(tenant);

        var ok = await Import(db, Sheet(["Ürün Adı", "Barkod", "Stok", "Fiyat"], ["A", "1", 1, 5]));
        Assert.Equal(1, ok.Created);

        var many = Enumerable.Range(1, ProductImportService.MaxRows + 1).Select(i => new object?[] { "P", $"B{i}", 1, 1 }).ToArray();
        var ex = await Assert.ThrowsAsync<ProductImportException>(() => Import(db, Sheet(["Ürün Adı", "Barkod", "Stok", "Fiyat"], many)));
        Assert.Contains("en fazla", ex.Message);
    }
}
