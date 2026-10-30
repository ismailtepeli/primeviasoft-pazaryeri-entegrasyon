using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Orders;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Stock;

namespace Pazaryeri.Tests;

public class OrderSyncTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_OrderSync;Trusted_Connection=True;TrustServerCertificate=True";
    private static readonly DateTime T0 = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private sealed class FakeTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(T0, TimeSpan.Zero);
    }

    private sealed class NullQueue : IJobQueue
    {
        public Task<SyncJob> EnqueueAsync(SyncJobType type, string channel, string? batchId = null, CancellationToken ct = default, string? payload = null) =>
            Task.FromResult(new SyncJob { Type = type, Channel = channel });
    }

    private sealed class FakeCreds : IChannelCredentialService
    {
        public Task<CredentialView> SaveAsync(string channel, ChannelEnvironment environment, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CredentialView?> GetMaskedAsync(string channel, ChannelEnvironment environment, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MarketplaceCredentials?> GetForClientAsync(string channel, CancellationToken ct = default) => Task.FromResult<MarketplaceCredentials?>(new(channel, "1", "k", "s", "Acme"));
    }

    private sealed class FakeClient(IReadOnlyList<IReadOnlyList<MarketplaceOrder>> pages) : IMarketplaceClient
    {
        public string Channel => "Trendyol";
        public List<OrderQuery> Queries { get; } = [];
        public Task<OrderPage> GetOrdersAsync(OrderQuery query, CancellationToken ct = default)
        {
            Queries.Add(query);
            return Task.FromResult(new OrderPage(pages[query.Page], query.Page, pages.Count));
        }
        public Task<BatchResult> SendProductsAsync(IReadOnlyList<ProductItem> products, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BatchResult> UpdateStockAndPriceAsync(IReadOnlyList<StockPriceItem> items, CancellationToken ct = default) => throw new NotSupportedException();
        public Task TestConnectionAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BatchStatus> GetBatchResultAsync(string batchId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateOrderStatusAsync(OrderStatusUpdate update, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeFactory(FakeClient client) : IMarketplaceClientFactory
    {
        public IMarketplaceClient Create(MarketplaceCredentials credentials) => client;
    }

    private static AppDbContext NewDb(Guid? tenantId = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    private static OrderIngestService Ingest(AppDbContext db) => new(db, new StockPoolService(db, new NullQueue()));

    public async Task InitializeAsync()
    {
        await using var db = NewDb();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> SetupAsync(string name, int stock = 10)
    {
        await using var global = NewDb();
        var tenant = new Tenant { Name = name };
        global.Tenants.Add(tenant);
        await global.SaveChangesAsync();
        await using var seed = NewDb(tenant.Id);
        seed.Products.Add(new Product { Name = "P", Barcode = "BC-" + name, Price = 1, Stock = stock });
        await seed.SaveChangesAsync();
        return tenant.Id;
    }

    private static MarketplaceOrder Pkg(string name, string order = "ORD-1", string package = "PK-1", string status = "Created", int minutes = 0, string line = "L1", int qty = 2) =>
        new(order, package, status, T0, 100, "Ayşe Yılmaz", [new(line, "BC-" + name, "Ürün", qty, 50)], T0.AddMinutes(minutes));

    [Theory]
    [InlineData("Created", OrderStatus.New)]
    [InlineData("Picking", OrderStatus.Preparing)]
    [InlineData("Invoiced", OrderStatus.Preparing)]
    [InlineData("Shipped", OrderStatus.Shipped)]
    [InlineData("AtCollectionPoint", OrderStatus.Shipped)]
    [InlineData("Delivered", OrderStatus.Delivered)]
    [InlineData("Cancelled", OrderStatus.Cancelled)]
    [InlineData("UnSupplied", OrderStatus.Cancelled)]
    [InlineData("Returned", OrderStatus.Returned)]
    [InlineData("Bilinmeyen", OrderStatus.New)]
    public void Kanal_durumu_ortak_duruma_cevrilir(string channelStatus, OrderStatus expected) =>
        Assert.Equal(expected, OrderStatusMapper.Map("Trendyol", channelStatus));

    [Fact]
    public async Task Yeni_paket_siparis_paket_ve_satir_olarak_kaydedilir_stok_bir_kez_duser_tekrar_gelirse_degismez()
    {
        var tenant = await SetupAsync("Yeni");

        await using (var db = NewDb(tenant)) Assert.Equal(1, await Ingest(db).IngestAsync("Trendyol", [Pkg("Yeni")], default));
        await using (var db = NewDb(tenant)) await Ingest(db).IngestAsync("Trendyol", [Pkg("Yeni")], default);

        await using var check = NewDb(tenant);
        var order = await check.Orders.Include(o => o.Packages).ThenInclude(p => p.Lines).SingleAsync();
        Assert.Equal(("ORD-1", OrderStatus.New, 100m, "Ayşe Yılmaz"), (order.OrderNumber, order.Status, order.TotalPrice, order.CustomerName));
        var package = Assert.Single(order.Packages);
        Assert.Equal(("PK-1", "Created"), (package.PackageId, package.ChannelStatus));
        Assert.Equal((2, true), (package.Lines.Single().Quantity, package.Lines.Single().StockDeducted));
        Assert.Equal(8, (await check.Products.SingleAsync()).Stock);
        Assert.Single(await check.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task Durum_ilerler_eski_bildirim_yeni_durumu_ezmez()
    {
        var tenant = await SetupAsync("Ilerle");
        foreach (var (status, minutes) in new[] { ("Created", 0), ("Picking", 5), ("Shipped", 10), ("Picking", 6) }) // son bildirim eski
        {
            await using var db = NewDb(tenant);
            await Ingest(db).IngestAsync("Trendyol", [Pkg("Ilerle", status: status, minutes: minutes)], default);
        }

        await using var check = NewDb(tenant);
        var order = await check.Orders.Include(o => o.Packages).SingleAsync();
        Assert.Equal((OrderStatus.Shipped, "Shipped", T0.AddMinutes(10)), (order.Status, order.Packages.Single().ChannelStatus, order.LastModifiedAt));
        Assert.Equal(8, (await check.Products.SingleAsync()).Stock);
    }

    [Fact]
    public async Task Iptal_dusulen_stogu_bir_kez_geri_ekler()
    {
        var tenant = await SetupAsync("Iptal");
        await using (var db = NewDb(tenant)) await Ingest(db).IngestAsync("Trendyol", [Pkg("Iptal")], default);
        foreach (var _ in new[] { 1, 2 }) // iptal iki kez bildirilse de tek geri ekleme
        {
            await using var db = NewDb(tenant);
            await Ingest(db).IngestAsync("Trendyol", [Pkg("Iptal", status: "Cancelled", minutes: 5)], default);
        }

        await using var check = NewDb(tenant);
        Assert.Equal(10, (await check.Products.SingleAsync()).Stock);
        Assert.Equal(OrderStatus.Cancelled, (await check.Orders.SingleAsync()).Status);
        Assert.Equal([StockMovementType.Sale, StockMovementType.Restore], (await check.StockMovements.OrderBy(m => m.CreatedAt).ToListAsync()).Select(m => m.Type));
    }

    [Fact]
    public async Task Daha_once_hic_gorulmeyen_iptal_edilmis_siparis_stoga_dokunmaz()
    {
        var tenant = await SetupAsync("Hayalet");

        await using (var db = NewDb(tenant)) await Ingest(db).IngestAsync("Trendyol", [Pkg("Hayalet", status: "Cancelled")], default);

        await using var check = NewDb(tenant);
        Assert.Equal(10, (await check.Products.SingleAsync()).Stock);
        Assert.Empty(await check.StockMovements.ToListAsync());
        Assert.Equal(OrderStatus.Cancelled, (await check.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Paket_bolununce_eski_paket_gecmiste_kalir_ve_ayni_satir_iki_kez_dusulmez_geri_eklenmez()
    {
        var tenant = await SetupAsync("Bol");
        await using (var db = NewDb(tenant)) await Ingest(db).IngestAsync("Trendyol", [Pkg("Bol", package: "PK-1")], default);
        // Bölünme: eski paket iptal, aynı satır yeni pakette aktif
        await using (var db = NewDb(tenant)) await Ingest(db).IngestAsync("Trendyol", [Pkg("Bol", package: "PK-1", status: "Cancelled", minutes: 5), Pkg("Bol", package: "PK-2", status: "Created", minutes: 5)], default);

        await using var check = NewDb(tenant);
        var order = await check.Orders.Include(o => o.Packages).SingleAsync();
        Assert.Equal(2, order.Packages.Count);
        Assert.Equal([OrderStatus.Cancelled, OrderStatus.New], order.Packages.OrderBy(p => p.PackageId).Select(p => p.Status));
        Assert.Equal(OrderStatus.New, order.Status); // sipariş aktif pakete göre
        Assert.Equal(8, (await check.Products.SingleAsync()).Stock);
        Assert.Single(await check.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task Birden_cok_paketli_siparis_en_geri_aktif_paketin_durumunu_alir_ve_tutari_toplar()
    {
        var tenant = await SetupAsync("Coklu");
        await using var db = NewDb(tenant);
        await Ingest(db).IngestAsync("Trendyol", [Pkg("Coklu", package: "A", status: "Shipped", line: "L1", qty: 1), Pkg("Coklu", package: "B", status: "Picking", line: "L2", qty: 1)], default);

        await using var check = NewDb(tenant);
        var order = await check.Orders.SingleAsync();
        Assert.Equal((OrderStatus.Preparing, 200m), (order.Status, order.TotalPrice));
    }

    [Fact]
    public async Task Ayni_siparis_numarasi_farkli_kiracilarda_ayri_kayit_olur()
    {
        var a = await SetupAsync("KiracA");
        var b = await SetupAsync("KiracB");
        await using (var db = NewDb(a)) await Ingest(db).IngestAsync("Trendyol", [Pkg("KiracA")], default);
        await using (var db = NewDb(b)) await Ingest(db).IngestAsync("Trendyol", [Pkg("KiracB")], default);

        Assert.Single(await NewDb(a).Orders.ToListAsync());
        Assert.Single(await NewDb(b).Orders.ToListAsync());
        Assert.Equal(8, (await NewDb(b).Products.SingleAsync()).Stock);
    }

    [Fact]
    public async Task Is_tum_sayfalari_isler_imleci_ilerletir_ve_sonraki_calismada_imlecten_ortusmeyle_ister()
    {
        var tenant = await SetupAsync("Is", 20);
        var client = new FakeClient([[Pkg("Is", order: "O1", package: "P1", minutes: 1)], [Pkg("Is", order: "O2", package: "P2", minutes: 7)]]);
        await using (var db = NewDb(tenant))
        {
            var handler = new OrderSyncHandler(db, new FakeCreds(), new FakeFactory(client), Ingest(db), new FakeTime());
            var job = new SyncJob { Type = SyncJobType.OrderSync, Channel = "Trendyol" };
            await handler.HandleAsync(job, default);
            Assert.Equal(2, job.AffectedCount);
        }

        Assert.Equal([0, 1], client.Queries.Select(q => q.Page));
        Assert.Equal(T0 - OrderSyncHandler.InitialBackfill - OrderSyncHandler.Overlap, client.Queries[0].ModifiedFrom);
        await using (var db = NewDb(tenant))
        {
            var state = await db.OrderSyncStates.SingleAsync();
            Assert.Equal((T0.AddMinutes(7), T0), (state.Cursor, state.LastSyncAt));
            Assert.Equal(2, await db.Orders.CountAsync());
            Assert.Equal(16, (await db.Products.SingleAsync()).Stock);
        }

        var second = new FakeClient([[]]);
        await using (var db = NewDb(tenant))
            await new OrderSyncHandler(db, new FakeCreds(), new FakeFactory(second), Ingest(db), new FakeTime()).HandleAsync(new SyncJob { Channel = "Trendyol" }, default);
        Assert.Equal(T0.AddMinutes(7) - OrderSyncHandler.Overlap, second.Queries.Single().ModifiedFrom);
    }

    [Fact]
    public async Task Sayfa_hatasinda_imlec_ilerlemez_ve_islenenler_tekrarda_cift_dusmez()
    {
        var tenant = await SetupAsync("Hata", 20);
        var bad = new FailingSecondPage(Pkg("Hata", order: "O1", package: "P1", minutes: 3));
        await using (var db = NewDb(tenant))
        {
            var handler = new OrderSyncHandler(db, new FakeCreds(), new BadFactory(bad), Ingest(db), new FakeTime());
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(new SyncJob { Channel = "Trendyol" }, default));
        }
        Assert.Empty(await NewDb(tenant).OrderSyncStates.ToListAsync());

        var good = new FakeClient([[Pkg("Hata", order: "O1", package: "P1", minutes: 3)]]);
        await using (var db = NewDb(tenant))
            await new OrderSyncHandler(db, new FakeCreds(), new FakeFactory(good), Ingest(db), new FakeTime()).HandleAsync(new SyncJob { Channel = "Trendyol" }, default);

        Assert.Equal(18, (await NewDb(tenant).Products.SingleAsync()).Stock);
        Assert.Equal(T0.AddMinutes(3), (await NewDb(tenant).OrderSyncStates.SingleAsync()).Cursor);
    }

    private sealed class FailingSecondPage(MarketplaceOrder first) : IMarketplaceClient
    {
        public string Channel => "Trendyol";
        public Task<OrderPage> GetOrdersAsync(OrderQuery query, CancellationToken ct = default) =>
            query.Page == 0 ? Task.FromResult(new OrderPage([first], 0, 2)) : throw new InvalidOperationException("ağ hatası");
        public Task<BatchResult> SendProductsAsync(IReadOnlyList<ProductItem> products, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BatchResult> UpdateStockAndPriceAsync(IReadOnlyList<StockPriceItem> items, CancellationToken ct = default) => throw new NotSupportedException();
        public Task TestConnectionAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BatchStatus> GetBatchResultAsync(string batchId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateOrderStatusAsync(OrderStatusUpdate update, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class BadFactory(IMarketplaceClient client) : IMarketplaceClientFactory
    {
        public IMarketplaceClient Create(MarketplaceCredentials credentials) => client;
    }
}
