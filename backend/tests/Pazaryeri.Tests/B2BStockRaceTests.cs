using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Orders;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Pricing;
using Pazaryeri.Infrastructure.Stock;

namespace Pazaryeri.Tests;

/// <summary>Aynı ürüne aynı anda gelen çok sayıda bayi siparişi: fazla satış yok, haksız ret yok, kayıplar kalan adedi görür.</summary>
public class B2BStockRaceTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_B2BStockRace;Trusted_Connection=True;TrustServerCertificate=True";

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

    private sealed record World(Guid Tenant, List<Guid> Dealers, Guid A, Guid B);

    /// <summary>Beş bayi; A ürünü (barkod A) ve B ürünü (barkod B) <paramref name="stock"/> adet, minimum 1, koli 1.</summary>
    private static async Task<World> SeedAsync(string name, int stock = 4, int? safetyA = null)
    {
        await using var root = NewDb(null);
        var tenant = new Tenant { Name = name };
        root.Tenants.Add(tenant);
        await root.SaveChangesAsync();
        await using var db = NewDb(tenant.Id);
        var dealers = Enumerable.Range(1, 5).Select(i => new Dealer
        {
            Name = $"Bayi {i}", TaxNumber = $"100000000{i}", Email = $"b{i}@b.com", Status = DealerStatus.Active, Address = "İstanbul",
        }).ToList();
        var a = new Product { Name = "Ürün A", Barcode = "A", Price = 10m, VatRate = 20, Stock = stock, SafetyStock = safetyA, B2BVisible = true, MinOrderQuantity = 1, PackSize = 1 };
        var b = new Product { Name = "Ürün B", Barcode = "B", Price = 10m, VatRate = 20, Stock = stock, B2BVisible = true, MinOrderQuantity = 1, PackSize = 1 };
        db.AddRange(dealers);
        db.AddRange(a, b);
        await db.SaveChangesAsync();
        return new World(tenant.Id, dealers.Select(d => d.Id).ToList(), a.Id, b.Id);
    }

    private static async Task<int> StockOfAsync(Guid tenant, string barcode)
    {
        await using var db = NewDb(tenant);
        return await db.Products.Where(p => p.Barcode == barcode).Select(p => p.Stock).SingleAsync();
    }

    /// <summary>Her bayinin sepetine verilen kalemleri koyar, sonra hepsi aynı anda sipariş verir; (başarılı mı, hata metinleri) döner.</summary>
    private static async Task<List<(bool Ok, string[] Issues)>> RaceAsync(World w, IReadOnlyList<(Guid Product, int Qty)[]> carts)
    {
        for (var i = 0; i < carts.Count; i++)
            foreach (var (product, qty) in carts[i])
            {
                await using var db = NewDb(w.Tenant);
                await Service(db).AddAsync(w.Dealers[i], product, qty, default);
            }

        using var gate = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, carts.Count).Select(i => Task.Run(async () =>
        {
            gate.Wait();
            await using var db = NewDb(w.Tenant);
            try { await Service(db).CheckoutAsync(w.Dealers[i], new CheckoutInput(null, null, false), default); return (true, Array.Empty<string>()); }
            catch (StoreException ex) { return (false, ex.Details is null ? [ex.Message] : IssueTexts(ex.Details)); }
        })).ToList();
        gate.Set();
        return (await Task.WhenAll(tasks)).ToList();
    }

    private static string[] IssueTexts(object extra)
    {
        var issues = extra.GetType().GetProperty("issues")?.GetValue(extra) as IEnumerable<CartIssue>;
        return issues?.Select(i => i.Text).ToArray() ?? [];
    }

    [Fact]
    public async Task Bes_bayi_birer_adet_ister_stok_4_dort_siparis_olusur_biri_tukendi_gorur()
    {
        var w = await SeedAsync("BesBir");
        var results = await RaceAsync(w, Enumerable.Range(0, 5).Select(_ => new[] { (w.A, 1) }).ToList());

        Assert.Equal(4, results.Count(r => r.Ok));
        var loser = results.Single(r => !r.Ok);
        Assert.Contains("stok tükendi", string.Join(" ", loser.Issues));
        Assert.Equal(0, await StockOfAsync(w.Tenant, "A"));
        await using var check = NewDb(w.Tenant);
        Assert.Equal(4, await check.Orders.CountAsync());
    }

    [Fact]
    public async Task Bes_bayi_ikiser_adet_ister_yalnizca_iki_siparis_olusur()
    {
        var w = await SeedAsync("BesIki");
        var results = await RaceAsync(w, Enumerable.Range(0, 5).Select(_ => new[] { (w.A, 2) }).ToList());

        Assert.Equal(2, results.Count(r => r.Ok));
        Assert.Equal(0, await StockOfAsync(w.Tenant, "A"));
        await using var check = NewDb(w.Tenant);
        Assert.Equal(2, await check.Orders.CountAsync());
        // Kaybedenlerin sepeti korunur.
        foreach (var (ok, i) in results.Select((r, i) => (r.Ok, i)).Where(x => !x.Ok))
            Assert.Single((await Service(check).GetCartAsync(w.Dealers[i], default)).Lines);
    }

    [Fact]
    public async Task Karisik_adetlerde_stok_eksiye_dusmez_ve_hicbir_kaybeden_stok_varken_reddedilmez()
    {
        var w = await SeedAsync("Karisik");
        var wanted = new[] { 3, 3, 1, 1, 2 };
        var results = await RaceAsync(w, wanted.Select(q => new[] { (w.A, q) }).ToList());

        var sold = results.Select((r, i) => r.Ok ? wanted[i] : 0).Sum();
        var stock = await StockOfAsync(w.Tenant, "A");
        Assert.Equal(4 - sold, stock);
        Assert.InRange(stock, 0, 4);
        // Haksız ret yok: reddedilen her bayinin istediği, nihai kalan stoktan fazladır (yetseydi sipariş geçerdi).
        for (var i = 0; i < wanted.Length; i++)
            if (!results[i].Ok) Assert.True(wanted[i] > stock, $"Bayi {i + 1} {wanted[i]} adet istedi, {stock} adet kaldığı hâlde reddedildi.");
        // Defter tutarlı: düşen toplam = satılan toplam.
        await using var check = NewDb(w.Tenant);
        Assert.Equal(-sold, await check.StockMovements.Where(m => m.Type == StockMovementType.Sale).SumAsync(m => (int?)m.Quantity) ?? 0);
    }

    [Fact]
    public async Task Yetmeyen_siparis_stogu_gecici_tutmaz_ve_kalan_adedi_soyler()
    {
        var w = await SeedAsync("Kalan");
        await using (var db = NewDb(w.Tenant)) await Service(db).AddAsync(w.Dealers[0], w.A, 3, default);
        await using (var db = NewDb(w.Tenant)) await Service(db).AddAsync(w.Dealers[1], w.A, 3, default);
        await using (var db = NewDb(w.Tenant)) await Service(db).AddAsync(w.Dealers[2], w.A, 1, default);

        await using (var db = NewDb(w.Tenant)) await Service(db).CheckoutAsync(w.Dealers[0], new CheckoutInput(null, null, false), default);
        await using (var db = NewDb(w.Tenant))
        {
            var ex = await Assert.ThrowsAsync<StoreException>(() => Service(db).CheckoutAsync(w.Dealers[1], new CheckoutInput(null, null, false), default));
            Assert.Contains("yalnızca 1 adet", string.Join(" ", IssueTexts(ex.Details!)));
        }
        Assert.Equal(1, await StockOfAsync(w.Tenant, "A")); // başarısız sipariş stoğu tutmadı
        await using (var db = NewDb(w.Tenant)) await Service(db).CheckoutAsync(w.Dealers[2], new CheckoutInput(null, null, false), default); // son adet satılabildi
        Assert.Equal(0, await StockOfAsync(w.Tenant, "A"));
    }

    [Fact]
    public async Task Cok_satirli_siparislerde_ters_sirayla_eszamanli_istek_kilitlenmez_ve_ya_hep_ya_hic_olur()
    {
        var w = await SeedAsync("CiftSatir", stock: 3);
        var carts = new List<(Guid, int)[]>
        {
            new[] { (w.A, 2), (w.B, 2) },
            new[] { (w.B, 2), (w.A, 2) },
            new[] { (w.A, 1), (w.B, 1) },
        };
        var results = await RaceAsync(w, carts);

        var sold = results.Select((r, i) => r.Ok ? carts[i][0].Item2 : 0).Sum(); // iki üründe de aynı adet
        Assert.Equal(3 - sold, await StockOfAsync(w.Tenant, "A"));
        Assert.Equal(3 - sold, await StockOfAsync(w.Tenant, "B")); // kısmi düşüm yok: iki ürün birlikte düşer
        await using var check = NewDb(w.Tenant);
        Assert.Equal(results.Count(r => r.Ok), await check.Orders.CountAsync());
    }

    [Fact]
    public async Task Guvenlik_stogu_eszamanli_siparislerde_de_korunur()
    {
        var w = await SeedAsync("Guvenlik", stock: 4, safetyA: 2); // satılabilir 2
        var results = await RaceAsync(w, Enumerable.Range(0, 5).Select(_ => new[] { (w.A, 1) }).ToList());

        // Sepete ekleme satılabilir 2'yi aşan her istekte 409 verir; sipariş aşamasında en fazla 2 adet çıkabilir.
        Assert.True(results.Count(r => r.Ok) <= 2);
        Assert.True(await StockOfAsync(w.Tenant, "A") >= 2);
    }

    [Fact]
    public async Task Atomik_dusum_yetmeyen_satirda_hicbir_satiri_dusmez_ve_kalan_adedi_dondurur()
    {
        var w = await SeedAsync("Atomik", stock: 3);
        await using var db = NewDb(w.Tenant);
        var pool = new StockPoolService(db, new RecordingQueue(db));

        var fail = await pool.DeductAllOrNothingAsync("B2B", [new("A", 2, "S1:1"), new("B", 5, "S1:2")]);
        Assert.False(fail.Success);
        var shortage = Assert.Single(fail.Shortages);
        Assert.Equal(("B", 5, 3), (shortage.Barcode, shortage.Requested, shortage.Available));
        Assert.Equal((3, 3), (await StockOfAsync(w.Tenant, "A"), await StockOfAsync(w.Tenant, "B"))); // A da düşülmedi
        await using (var check = NewDb(w.Tenant)) Assert.Empty(await check.StockMovements.ToListAsync());

        var ok = await pool.DeductAllOrNothingAsync("B2B", [new("A", 2, "S2:1"), new("B", 3, "S2:2")]);
        Assert.True(ok.Success && ok.Applied);
        Assert.Equal((1, 0), (await StockOfAsync(w.Tenant, "A"), await StockOfAsync(w.Tenant, "B")));

        var again = await pool.DeductAllOrNothingAsync("B2B", [new("A", 2, "S2:1")]); // aynı olay tekrarı: stok bir kez düşer
        Assert.True(again.Success);
        Assert.Equal(1, await StockOfAsync(w.Tenant, "A"));
    }
}
