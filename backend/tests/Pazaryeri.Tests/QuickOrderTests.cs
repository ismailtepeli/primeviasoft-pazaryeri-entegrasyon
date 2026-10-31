using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Orders;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Orders;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Pricing;
using Pazaryeri.Infrastructure.Stock;

namespace Pazaryeri.Tests;

public class QuickOrderTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_QuickOrder;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
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

    private static B2BOrderService Service(AppDbContext db) =>
        new(db, new StoreCatalog(db), new DealerPriceService(db), new StockPoolService(db, new RecordingQueue(db)), TimeProvider.System);

    public async Task InitializeAsync()
    {
        await using var db = NewDb(null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record World(Guid Tenant, Guid DealerId, Guid Pen, Guid Notebook);

    /// <summary>Kalem PEN (stok 100, min 10, koli 5), Defter NB (stok 8, güvenlik 3 → satılabilir 5), Hediye GF (B2B'de gizli).</summary>
    private static async Task<World> SeedAsync(string name)
    {
        await using var root = NewDb(null);
        var tenant = new Tenant { Name = name };
        root.Tenants.Add(tenant);
        await root.SaveChangesAsync();
        await using var db = NewDb(tenant.Id);
        var dealer = new Dealer { Name = "Mavi", TaxNumber = "1000000001", Email = "m@m.com", Status = DealerStatus.Active, Address = "A" };
        var pen = new Product { Name = "Kalem", Barcode = "PEN", Price = 12m, VatRate = 20, Stock = 100, B2BVisible = true, MinOrderQuantity = 10, PackSize = 5 };
        var notebook = new Product { Name = "Defter", Barcode = "NB", Price = 60m, VatRate = 20, Stock = 8, SafetyStock = 3, B2BVisible = true };
        var gift = new Product { Name = "Hediye", Barcode = "GF", Price = 5m, Stock = 50, B2BVisible = false };
        db.AddRange(dealer, pen, notebook, gift);
        await db.SaveChangesAsync();
        return new World(tenant.Id, dealer.Id, pen.Id, notebook.Id);
    }

    private static Task<QuickOrderResult> QuickAsync(AppDbContext db, World w, string text)
    {
        var parsed = QuickOrderParser.Parse(text);
        return Service(db).QuickAddAsync(w.DealerId, parsed.Rows, parsed.Errors, default);
    }

    // --- ayrıştırıcı ---

    [Theory]
    [InlineData("PEN;10", "PEN", 10)]
    [InlineData("PEN\t10", "PEN", 10)]            // Excel'den yapıştırma
    [InlineData("PEN,10", "PEN", 10)]
    [InlineData("PEN 10", "PEN", 10)]
    [InlineData("PEN x 10", "PEN", 10)]
    [InlineData("PEN X 10", "PEN", 10)]
    [InlineData("  PEN ; 10  ", "PEN", 10)]
    [InlineData("PEN", "PEN", 1)]                 // adet yoksa 1
    [InlineData("PEN;", "PEN", 1)]
    [InlineData("AX5;10", "AX5", 10)]             // kodun içindeki x'e dokunulmaz
    [InlineData("AB 12 CD 3", "AB 12 CD", 3)]     // boşluklu kod
    [InlineData("PEN;10,0", "PEN", 10)]           // ondalık gösterimli tam sayı
    public void Ayristirici_ayiricilari_ve_varsayilan_adedi_tanir(string text, string code, int qty)
    {
        var p = QuickOrderParser.Parse(text);
        Assert.Empty(p.Errors);
        var row = Assert.Single(p.Rows);
        Assert.Equal((code, qty, 1), (row.Code, row.Quantity, row.Row));
    }

    [Fact]
    public void Ayristirici_baslik_bos_satir_ve_satir_numaralarini_yonetir()
    {
        var p = QuickOrderParser.Parse("Kod;Adet\r\n\r\nPEN;10\r\nNB;abc\r\n;5\r\nGF;0\r\nXX;2,5\r\nYY;-3\r\nZZ;200000\r\nOK;7");
        Assert.Equal(new[] { (3, "PEN", 10), (10, "OK", 7) }, p.Rows.Select(r => (r.Row, r.Code, r.Quantity)));
        Assert.Equal(new[] { 4, 5, 6, 7, 8, 9 }, p.Errors.Select(e => e.Row));
        Assert.Contains("tam sayı", p.Errors.Single(e => e.Row == 4).Message);
        Assert.Equal("Ürün kodu boş.", p.Errors.Single(e => e.Row == 5).Message);
        Assert.Contains("0'dan büyük", p.Errors.Single(e => e.Row == 6).Message);
        Assert.Contains("tam sayı", p.Errors.Single(e => e.Row == 7).Message);      // 2,5
        Assert.Contains("0'dan büyük", p.Errors.Single(e => e.Row == 8).Message);   // -3
        Assert.Contains("çok büyük", p.Errors.Single(e => e.Row == 9).Message);
    }

    [Fact]
    public void Ayristirici_bos_girdide_sonuc_bos_ve_satir_sinirini_uygular()
    {
        Assert.Empty(QuickOrderParser.Parse(null).Rows);
        Assert.Empty(QuickOrderParser.Parse("  \n \n").Rows);
        var many = string.Join('\n', Enumerable.Range(1, QuickOrderParser.MaxRows + 50).Select(i => $"K{i};1"));
        var p = QuickOrderParser.Parse(many);
        Assert.Equal(QuickOrderParser.MaxRows, p.Rows.Count);
        Assert.Contains(p.Errors, e => e.Message.Contains("En fazla"));
    }

    // --- servis ---

    [Fact]
    public async Task Gecerli_satirlar_eklenir_gecersizler_satir_numarasiyla_raporlanir()
    {
        var w = await SeedAsync("Karisik");
        await using var db = NewDb(w.Tenant);

        var r = await QuickAsync(db, w, "PEN;15\nGF;1\nYOK;2\nNB;2\nPEN;abc");
        Assert.Equal(2, r.Added);                                                    // PEN 15 ve NB 2
        Assert.Equal(new[] { 2, 3, 5 }, r.Errors.Select(e => e.Row));
        Assert.Equal("Ürün bulunamadı.", r.Errors.Single(e => e.Row == 2).Message);  // gizli ürün sızmaz
        Assert.Equal("Ürün bulunamadı.", r.Errors.Single(e => e.Row == 3).Message);
        Assert.Contains("tam sayı", r.Errors.Single(e => e.Row == 5).Message);
        Assert.Equal((15, 2), (r.Cart.Lines.Single(l => l.Code == "PEN").Quantity, r.Cart.Lines.Single(l => l.Code == "NB").Quantity));
        Assert.False(r.Cart.HasIssues);
    }

    [Fact]
    public async Task Koli_minimum_ve_stok_kurallari_bildirilir_ve_satir_eklenmez()
    {
        var w = await SeedAsync("Kurallar");
        await using var db = NewDb(w.Tenant);

        var r = await QuickAsync(db, w, "PEN;5\nNB;6\nNB;0");
        Assert.Equal(0, r.Added);
        Assert.Empty(r.Cart.Lines);
        Assert.Contains("En az 10", r.Errors.Single(e => e.Row == 1).Message);       // minimum
        Assert.Contains("yalnızca 5", r.Errors.Single(e => e.Row == 2).Message);     // satılabilir 8-3
        Assert.Contains("0'dan büyük", r.Errors.Single(e => e.Row == 3).Message);

        // aynı kodun satırları toplanır: 5 + 12 = 17 koli katı değil, hata ilk satıra yazılır
        var sum = await QuickAsync(db, w, "PEN;5\nPEN;12");
        Assert.Equal(0, sum.Added);
        var err = Assert.Single(sum.Errors);
        Assert.Equal(1, err.Row);
        Assert.Contains("kolilerle", err.Message);
    }

    [Fact]
    public async Task Ayni_kod_birden_cok_satirda_adetleri_toplar_ve_kucuk_buyuk_harf_ayni_sayilir()
    {
        var w = await SeedAsync("Toplam");
        await using var db = NewDb(w.Tenant);

        var r = await QuickAsync(db, w, "PEN;5\npen;5\nPen;10");
        Assert.Equal((1, 0), (r.Added, r.Errors.Count));
        Assert.Equal(20, r.Cart.Lines.Single().Quantity);
    }

    [Fact]
    public async Task Mevcut_sepet_adedine_eklenir_ve_toplam_kuralla_denetlenir()
    {
        var w = await SeedAsync("Mevcut");
        await using var db = NewDb(w.Tenant);
        await Service(db).AddAsync(w.DealerId, w.Pen, 10, default);

        var r = await QuickAsync(db, w, "PEN;5");      // 10 + 5 = 15, koli katı
        Assert.Equal(15, r.Cart.Lines.Single().Quantity);

        var over = await QuickAsync(db, w, "PEN;90");  // 105 > stok 100
        Assert.Equal(0, over.Added);
        Assert.Contains("yalnızca 100", over.Errors.Single().Message);
        Assert.Equal(15, over.Cart.Lines.Single().Quantity);
    }

    [Fact]
    public async Task Bos_girdi_400_hata_satirlari_yalniz_hata_olarak_doner()
    {
        var w = await SeedAsync("Bos");
        await using var db = NewDb(w.Tenant);
        var s = Service(db);

        var e = await Assert.ThrowsAsync<StoreException>(() => s.QuickAddAsync(w.DealerId, [], [], default));
        Assert.Equal(400, e.Status);

        var onlyErrors = await QuickAsync(db, w, "PEN;abc");
        Assert.Equal((0, 1), (onlyErrors.Added, onlyErrors.Errors.Count));
    }

    [Fact]
    public async Task Baska_bayinin_sepeti_etkilenmez_ve_bayi_grubuna_gizli_urun_bulunamaz()
    {
        var w = await SeedAsync("Yalitim");
        await using var db = NewDb(w.Tenant);
        Guid other;
        await using (var seed = NewDb(w.Tenant))
        {
            var d = new Dealer { Name = "Diğer", TaxNumber = "1000000002", Email = "d@d.com", Status = DealerStatus.Active, Address = "B" };
            seed.Dealers.Add(d);
            await seed.SaveChangesAsync();
            other = d.Id;
        }
        await QuickAsync(db, w, "PEN;10");
        Assert.Empty((await Service(db).GetCartAsync(other, default)).Lines);
    }

    // --- Excel ---

    private static MemoryStream Xlsx(Action<IXLWorksheet> fill)
    {
        using var book = new XLWorkbook();
        fill(book.Worksheets.Add("Sipariş"));
        var ms = new MemoryStream();
        book.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Excel_basligi_atlar_sayisal_kodlari_okur_ve_satir_numarasi_dosyadaki_satirdir()
    {
        using var file = Xlsx(ws =>
        {
            ws.Cell(1, 1).Value = "Barkod"; ws.Cell(1, 2).Value = "Adet";
            ws.Cell(2, 1).Value = "PEN"; ws.Cell(2, 2).Value = 10;
            ws.Cell(3, 1).Value = 8690000000012; ws.Cell(3, 2).Value = 3;     // sayı olarak girilmiş barkod
            ws.Cell(4, 1).Value = "NB";                                          // adet boş → 1
            ws.Cell(5, 1).Value = "BAD"; ws.Cell(5, 2).Value = "x";
            ws.Cell(7, 1).Value = "OK"; ws.Cell(7, 2).Value = 2;
        });
        var p = QuickOrderFile.ReadXlsx(file);
        Assert.Equal(new[] { (2, "PEN", 10), (3, "8690000000012", 3), (4, "NB", 1), (7, "OK", 2) }, p.Rows.Select(r => (r.Row, r.Code, r.Quantity)));
        var err = Assert.Single(p.Errors);
        Assert.Equal((5, "BAD"), (err.Row, err.Code));
    }

    [Fact]
    public void Excel_gecersiz_dosya_ve_bos_dosya_reddedilir()
    {
        var bad = Assert.Throws<StoreException>(() => QuickOrderFile.ReadXlsx(new MemoryStream([1, 2, 3])));
        Assert.Equal(400, bad.Status);
        using var empty = Xlsx(_ => { });
        Assert.Equal(400, Assert.Throws<StoreException>(() => QuickOrderFile.ReadXlsx(empty)).Status);
    }

    [Fact]
    public async Task Excel_satirlari_servise_gider()
    {
        var w = await SeedAsync("Excel");
        await using var db = NewDb(w.Tenant);
        using var file = Xlsx(ws => { ws.Cell(1, 1).Value = "Kod"; ws.Cell(2, 1).Value = "PEN"; ws.Cell(2, 2).Value = 20; ws.Cell(3, 1).Value = "NOPE"; ws.Cell(3, 2).Value = 1; });
        var parsed = QuickOrderFile.ReadXlsx(file);
        var r = await Service(db).QuickAddAsync(w.DealerId, parsed.Rows, parsed.Errors, default);
        Assert.Equal(1, r.Added);
        Assert.Equal(3, Assert.Single(r.Errors).Row);
    }
}
