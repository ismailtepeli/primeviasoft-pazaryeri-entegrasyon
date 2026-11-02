using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Products;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Products;

namespace Pazaryeri.Tests;

public class ProductPriceTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_ProductPrices;Trusted_Connection=True;TrustServerCertificate=True";

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

    private sealed record World(Guid Tenant, Guid A, Guid B, Guid C);

    private static async Task<Guid> TenantAsync(string name)
    {
        await using var root = NewDb(null);
        var t = new Tenant { Name = name };
        root.Tenants.Add(t);
        await root.SaveChangesAsync();
        return t.Id;
    }

    /// <summary>Kalem 10 ₺, Defter 100 ₺, Silgi 0,50 ₺.</summary>
    private static async Task<World> SeedAsync(string name)
    {
        var tenant = await TenantAsync(name);
        await using var db = NewDb(tenant);
        var a = new Product { Name = "Kalem", Barcode = "0001", Price = 10m, Stock = 5 };
        var b = new Product { Name = "Defter", Barcode = "0002", Price = 100m, Stock = 5 };
        var c = new Product { Name = "Silgi", Barcode = "0003", Price = 0.50m, Stock = 5 };
        db.AddRange(a, b, c);
        await db.SaveChangesAsync();
        return new World(tenant, a.Id, b.Id, c.Id);
    }

    private static ProductPriceService Service(AppDbContext db) => new(db);

    private static async Task<decimal> PriceAsync(Guid tenant, string barcode)
    {
        await using var db = NewDb(tenant);
        return await db.Products.Where(p => p.Barcode == barcode).Select(p => p.Price).SingleAsync();
    }

    private static async Task<ProductPriceException> FailAsync(Func<Task> action) => await Assert.ThrowsAsync<ProductPriceException>(action);

    // --- tek ürün ---

    [Fact]
    public async Task Tek_urun_fiyati_yuvarlanarak_guncellenir_gecersiz_fiyat_ve_baska_kiraci_reddedilir()
    {
        var w = await SeedAsync("Tek");
        await using var db = NewDb(w.Tenant);
        var s = Service(db);
        Assert.Equal(12.35m, (await s.SetPriceAsync(w.A, 12.345m)).Price);
        Assert.Equal(12.35m, await PriceAsync(w.Tenant, "0001"));

        Assert.Equal(400, (await FailAsync(() => s.SetPriceAsync(w.A, 0))).Status);
        Assert.Equal(400, (await FailAsync(() => s.SetPriceAsync(w.A, -5))).Status);
        Assert.Equal(400, (await FailAsync(() => s.SetPriceAsync(w.A, 0.004m))).Status); // yuvarlayınca sıfır
        Assert.Equal(400, (await FailAsync(() => s.SetPriceAsync(w.A, 10_000_000_000m))).Status);
        Assert.Equal(404, (await FailAsync(() => s.SetPriceAsync(Guid.NewGuid(), 5))).Status);

        var other = await SeedAsync("TekDiger");
        await using var otherDb = NewDb(other.Tenant);
        Assert.Equal(404, (await FailAsync(() => Service(otherDb).SetPriceAsync(w.A, 99))).Status); // başka kiracının ürünü
        Assert.Equal(12.35m, await PriceAsync(w.Tenant, "0001"));
    }

    // --- toplu ---

    [Fact]
    public async Task Toplu_yuzde_tutar_ve_sabit_fiyat_uygulanir()
    {
        var w = await SeedAsync("Toplu");
        await using (var db = NewDb(w.Tenant))
        {
            var r = await Service(db).BulkChangeAsync(new BulkPriceInput([w.A, w.B], PriceChangeModes.Percent, 10));
            Assert.Equal((2, 0, 0), (r.Updated, r.Unchanged, r.Skipped.Count));
        }
        Assert.Equal((11m, 110m, 0.50m), (await PriceAsync(w.Tenant, "0001"), await PriceAsync(w.Tenant, "0002"), await PriceAsync(w.Tenant, "0003"))); // seçilmeyen Silgi değişmez

        await using (var db = NewDb(w.Tenant)) await Service(db).BulkChangeAsync(new BulkPriceInput([w.A], PriceChangeModes.Amount, -1.5m));
        Assert.Equal(9.5m, await PriceAsync(w.Tenant, "0001"));

        await using (var db = NewDb(w.Tenant))
        {
            var r = await Service(db).BulkChangeAsync(new BulkPriceInput([w.A, w.B], PriceChangeModes.Set, 110));
            Assert.Equal((1, 1, 0), (r.Updated, r.Unchanged, r.Skipped.Count)); // Defter zaten 110
        }
        Assert.Equal(110m, await PriceAsync(w.Tenant, "0001"));
    }

    [Fact]
    public async Task Toplu_degisiklikte_sifira_dusen_urun_atlanir_digerleri_guncellenir()
    {
        var w = await SeedAsync("Atla");
        await using var db = NewDb(w.Tenant);
        var r = await Service(db).BulkChangeAsync(new BulkPriceInput([w.A, w.C], PriceChangeModes.Amount, -0.5m)); // Silgi 0,50 → 0
        Assert.Equal((1, 1), (r.Updated, r.Skipped.Count));
        Assert.Equal("Silgi", r.Skipped.Single().Name);
        Assert.Equal(9.5m, await PriceAsync(w.Tenant, "0001"));
        Assert.Equal(0.50m, await PriceAsync(w.Tenant, "0003"));
    }

    [Fact]
    public async Task Toplu_degisiklik_girdisini_dogrular_ve_baska_kiracinin_urunune_dokunmaz()
    {
        var w = await SeedAsync("Dogrula");
        var other = await SeedAsync("DogrulaDiger");
        await using var db = NewDb(w.Tenant);
        var s = Service(db);
        Assert.Equal(400, (await FailAsync(() => s.BulkChangeAsync(new BulkPriceInput([], PriceChangeModes.Percent, 5)))).Status);
        Assert.Equal(400, (await FailAsync(() => s.BulkChangeAsync(new BulkPriceInput([w.A], "bilinmeyen", 5)))).Status);
        Assert.Equal(400, (await FailAsync(() => s.BulkChangeAsync(new BulkPriceInput([w.A], PriceChangeModes.Percent, -95)))).Status);
        Assert.Equal(400, (await FailAsync(() => s.BulkChangeAsync(new BulkPriceInput([w.A], PriceChangeModes.Set, 0)))).Status);
        Assert.Equal(400, (await FailAsync(() => s.BulkChangeAsync(new BulkPriceInput(Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToList(), PriceChangeModes.Percent, 5)))).Status);

        var r = await s.BulkChangeAsync(new BulkPriceInput([other.A], PriceChangeModes.Percent, 50));
        Assert.Equal(0, r.Updated);
        Assert.Equal(10m, await PriceAsync(other.Tenant, "0001"));
    }

    // --- Excel ---

    private static MemoryStream Workbook(params (string Barcode, object? Price)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Fiyatlar");
        ws.Cell(1, 1).Value = "Barkod";
        ws.Cell(1, 2).Value = "Fiyat";
        for (var i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 2, 1).SetValue(rows[i].Barcode);
            switch (rows[i].Price) { case decimal d: ws.Cell(i + 2, 2).Value = d; break; case string s: ws.Cell(i + 2, 2).SetValue(s); break; }
        }
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Fiyat_sayfasi_mevcut_fiyatlarla_iner_ve_doldurulup_geri_yuklenebilir()
    {
        var w = await SeedAsync("Sayfa");
        byte[] bytes;
        await using (var db = NewDb(w.Tenant)) bytes = await Service(db).BuildSheetAsync();

        using (var wb = new XLWorkbook(new MemoryStream(bytes)))
        {
            var ws = wb.Worksheet(1);
            Assert.Equal(["Barkod", "Ürün Adı", "Fiyat"], [ws.Cell(1, 1).GetString(), ws.Cell(1, 2).GetString(), ws.Cell(1, 3).GetString()]);
            Assert.Equal(4, ws.RangeUsed()!.LastRow().RowNumber()); // başlık + 3 ürün
            Assert.Equal("0002", ws.Cell(2, 1).GetString()); // ad sırası: Defter önce; barkodun başındaki sıfırlar korunur
        }

        using var edited = new XLWorkbook(new MemoryStream(bytes));
        var sheet = edited.Worksheet(1);
        for (var r = 2; r <= 4; r++) if (sheet.Cell(r, 1).GetString() == "0002") sheet.Cell(r, 3).Value = 120m;
        var ms = new MemoryStream();
        edited.SaveAs(ms);
        ms.Position = 0;
        await using (var db = NewDb(w.Tenant))
        {
            var result = await Service(db).ImportAsync(ms);
            Assert.Equal((1, 2, 0), (result.Updated, result.Unchanged, result.Errors.Count));
        }
        Assert.Equal(120m, await PriceAsync(w.Tenant, "0002"));
    }

    [Fact]
    public async Task Excel_yukleme_virgullu_fiyati_okur_hatali_satirlari_raporlar_gecerlileri_kaydeder()
    {
        var w = await SeedAsync("Yukle");
        using var file = Workbook(("0001", "12,50"), ("0002", "abc"), ("YOK", 5m), ("0003", -1m), ("0001", 7m), ("", 3m));
        await using var db = NewDb(w.Tenant);
        var r = await Service(db).ImportAsync(file);

        Assert.Equal((1, 0), (r.Updated, r.Unchanged));
        Assert.Equal(12.50m, await PriceAsync(w.Tenant, "0001"));
        Assert.Equal(100m, await PriceAsync(w.Tenant, "0002")); // hatalı satır dokunmadı
        Assert.Equal([3, 4, 5, 6, 7], r.Errors.Select(e => e.Row).ToArray());
        Assert.Contains(r.Errors, e => e.Barcode == "YOK" && e.Message.Contains("kayıtlı ürün yok"));
        Assert.Contains(r.Errors, e => e.Row == 6 && e.Message.Contains("2. satırda"));
    }

    [Fact]
    public async Task Excel_yukleme_baslik_eksikse_dosya_hatasi_verir_ve_baska_kiracinin_barkodunu_bulamaz()
    {
        var w = await SeedAsync("Baslik");
        var other = await SeedAsync("BaslikDiger");
        using (var wb = new XLWorkbook())
        {
            wb.AddWorksheet("x").Cell(1, 1).Value = "Başka";
            var ms = new MemoryStream();
            wb.SaveAs(ms);
            ms.Position = 0;
            await using var db = NewDb(w.Tenant);
            Assert.Equal(400, (await FailAsync(() => Service(db).ImportAsync(ms))).Status);
        }
        await using (var db = NewDb(w.Tenant))
            Assert.Equal(400, (await FailAsync(() => Service(db).ImportAsync(new MemoryStream([1, 2, 3])))).Status);

        // Başka kiracının barkodu bu kiracıda "kayıtlı değil" sayılır; fiyat değişmez.
        var nameless = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Fiyatlar");
            ws.Cell(1, 1).Value = "Barkod"; ws.Cell(1, 2).Value = "Fiyat";
            ws.Cell(2, 1).SetValue("ZZZ"); ws.Cell(2, 2).Value = 5m;
            wb.SaveAs(nameless);
        }
        nameless.Position = 0;
        await using var other2 = NewDb(other.Tenant);
        var r = await Service(other2).ImportAsync(nameless);
        Assert.Equal(0, r.Updated);
        Assert.Single(r.Errors);
    }
}
