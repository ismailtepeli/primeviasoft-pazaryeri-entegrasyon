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

/// <summary>Yönetici sipariş ayrıntısında bayinin aynı ürünü önceki siparişinde hangi fiyata aldığı görünür.</summary>
public class B2BPreviousPriceTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_B2BPreviousPrice;Trusted_Connection=True;TrustServerCertificate=True";

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

    private static B2BOrderService Orders(AppDbContext db) =>
        new(db, new StoreCatalog(db), new DealerPriceService(db), new StockPoolService(db, new RecordingQueue(db)), TimeProvider.System);

    private static B2BAdminOrderService Admin(AppDbContext db) => new(db, new StockPoolService(db, new RecordingQueue(db)), TimeProvider.System);

    public async Task InitializeAsync()
    {
        await using var db = NewDb(null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record World(Guid Tenant, Guid Dealer, Guid Other, Guid Pen);

    private static async Task<World> SeedAsync(string name)
    {
        await using var root = NewDb(null);
        var tenant = new Tenant { Name = name };
        root.Tenants.Add(tenant);
        await root.SaveChangesAsync();
        await using var db = NewDb(tenant.Id);
        var dealer = new Dealer { Name = "Mavi", TaxNumber = "1000000001", Email = "m@m.com", Status = DealerStatus.Active, Address = "İstanbul" };
        var other = new Dealer { Name = "Diğer", TaxNumber = "1000000002", Email = "d@d.com", Status = DealerStatus.Active, Address = "Ankara" };
        var pen = new Product { Name = "Kalem", Barcode = "PEN", Price = 10m, VatRate = 20, Stock = 100, B2BVisible = true, MinOrderQuantity = 1, PackSize = 1 };
        db.AddRange(dealer, other, pen);
        await db.SaveChangesAsync();
        return new World(tenant.Id, dealer.Id, other.Id, pen.Id);
    }

    private static async Task SetPriceAsync(World w, decimal price)
    {
        await using var db = NewDb(w.Tenant);
        (await db.Products.SingleAsync()).Price = price;
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> OrderAsync(World w, Guid dealer)
    {
        await using var db = NewDb(w.Tenant);
        var s = Orders(db);
        await s.AddAsync(dealer, w.Pen, 2, default);
        var order = await s.CheckoutAsync(dealer, new CheckoutInput(null, null, false), default);
        await Task.Delay(20); // sipariş tarihleri sıralı olsun
        return order.Id;
    }

    private static async Task<B2BAdminOrderLine> LineAsync(World w, Guid orderId)
    {
        await using var db = NewDb(w.Tenant);
        return (await Admin(db).GetAsync(orderId, default)).Lines.Single();
    }

    [Fact]
    public async Task Onceki_siparis_yoksa_onceki_fiyat_bos_gelir()
    {
        var w = await SeedAsync("Ilk");
        var line = await LineAsync(w, await OrderAsync(w, w.Dealer));
        Assert.Null(line.PreviousUnitPrice);
        Assert.Null(line.PreviousChangePercent);
    }

    [Fact]
    public async Task Bayinin_son_alisi_ve_fiyat_degisimi_yuzdesi_gosterilir()
    {
        var w = await SeedAsync("Degisim");
        await OrderAsync(w, w.Dealer); // 10 ₺
        await SetPriceAsync(w, 12m);
        var second = await OrderAsync(w, w.Dealer);

        var line = await LineAsync(w, second);
        Assert.Equal((12m, 10m, 20m), (line.UnitPrice, line.PreviousUnitPrice, line.PreviousChangePercent));
        Assert.NotNull(line.PreviousDate);

        await SetPriceAsync(w, 9m);
        var third = await OrderAsync(w, w.Dealer);
        var last = await LineAsync(w, third);
        Assert.Equal((12m, -25m), (last.PreviousUnitPrice, last.PreviousChangePercent)); // en son alış 12 idi, şimdi 9
    }

    [Fact]
    public async Task Baska_bayinin_ve_iptal_edilmis_siparisler_onceki_fiyat_sayilmaz()
    {
        var w = await SeedAsync("Yalitim");
        await OrderAsync(w, w.Other); // başka bayi
        await SetPriceAsync(w, 11m);
        var cancelled = await OrderAsync(w, w.Dealer);
        await using (var db = NewDb(w.Tenant)) await Orders(db).CancelAsync(w.Dealer, cancelled, default);
        await SetPriceAsync(w, 13m);

        var line = await LineAsync(w, await OrderAsync(w, w.Dealer));
        Assert.Null(line.PreviousUnitPrice);
    }
}
