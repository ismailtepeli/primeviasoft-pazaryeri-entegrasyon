using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Application.Stock;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Stock;

namespace Pazaryeri.Tests;

public class StockPriceSyncTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_StockSync;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private sealed class AmbientProvider : ITenantProvider
    {
        public Guid? TenantId => AmbientTenant.TenantId;
    }

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeCreds : IChannelCredentialService
    {
        public Task<CredentialView> SaveAsync(string channel, ChannelEnvironment environment, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CredentialView?> GetMaskedAsync(string channel, ChannelEnvironment environment, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MarketplaceCredentials?> GetForClientAsync(string channel, CancellationToken ct = default) => Task.FromResult<MarketplaceCredentials?>(new(channel, "1", "k", "s", "Acme"));
    }

    private sealed class FakeClient : IMarketplaceClient
    {
        public string Channel => "Trendyol";
        public List<IReadOnlyList<StockPriceItem>> Calls { get; } = [];
        public BatchStatus? Result { get; set; }
        public Exception? ResultError { get; set; }
        private int _n;
        public Task<BatchResult> UpdateStockAndPriceAsync(IReadOnlyList<StockPriceItem> items, CancellationToken ct = default)
        {
            Calls.Add(items);
            return Task.FromResult(new BatchResult("sp-" + ++_n));
        }
        public Task<BatchStatus> GetBatchResultAsync(string batchId, CancellationToken ct = default) => ResultError is not null ? throw ResultError : Task.FromResult(Result!);
        public Task<BatchResult> SendProductsAsync(IReadOnlyList<ProductItem> products, CancellationToken ct = default) => throw new NotSupportedException();
        public Task TestConnectionAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrderPage> GetOrdersAsync(OrderQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateOrderStatusAsync(OrderStatusUpdate update, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeFactory(FakeClient client) : IMarketplaceClientFactory
    {
        public IMarketplaceClient Create(MarketplaceCredentials credentials) => client;
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

    private static AppDbContext NewDb(Guid? tenantId = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    public async Task InitializeAsync()
    {
        await using var db = NewDb();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // --- saf mantık ---

    private static ChannelStockPriceState State(int qty = 5, decimal price = 100, DateTime? windowStart = null, int count = 0) =>
        new() { Quantity = qty, Price = price, PriceWindowStart = windowStart, PriceUpdatesInWindow = count };

    [Fact]
    public void Degisiklik_yalnizca_stok_ya_da_fiyat_farkliysa_vardir()
    {
        Assert.True(StockPriceDelta.IsChanged(null, 5, 100));
        Assert.False(StockPriceDelta.IsChanged(State(), 5, 100));
        Assert.True(StockPriceDelta.IsChanged(State(), 6, 100));
        Assert.True(StockPriceDelta.IsChanged(State(), 5, 100.01m));
        Assert.True(StockPriceDelta.PriceChanged(null, 100));
        Assert.False(StockPriceDelta.PriceChanged(State(), 100));
    }

    [Fact]
    public void Barkod_basina_dakikada_30_fiyat_guncellemesi_sinirlanir_pencere_dolunca_sifirlanir()
    {
        var now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal((true, now, 1), StockPriceDelta.PriceGuard(null, now)); // ilk güncelleme
        Assert.Equal((true, now, 30), StockPriceDelta.PriceGuard(State(windowStart: now, count: 29), now.AddSeconds(30))); // 30. izinli
        Assert.False(StockPriceDelta.PriceGuard(State(windowStart: now, count: 30), now.AddSeconds(30)).Allowed);       // 31. engellenir
        var reset = StockPriceDelta.PriceGuard(State(windowStart: now, count: 30), now.AddSeconds(61));                 // pencere geçti
        Assert.Equal((true, now.AddSeconds(61), 1), reset);
    }

    [Fact]
    public void Govde_ozeti_siradan_bagimsiz_ve_icerige_duyarlidir()
    {
        var a = new StockPriceItem("B1", 5, 10.5m, 10.5m);
        var b = new StockPriceItem("B2", 7, 20m, 20m);

        Assert.Equal(StockPriceDelta.BodyHash([a, b]), StockPriceDelta.BodyHash([b, a]));
        Assert.NotEqual(StockPriceDelta.BodyHash([a, b]), StockPriceDelta.BodyHash([a, b with { Quantity = 8 }]));
        Assert.NotEqual(StockPriceDelta.BodyHash([a]), StockPriceDelta.BodyHash([a, b]));
    }

    // --- planlayıcı ve işleyici ---

    private static async Task<Guid> NewTenantAsync(string name, bool connected = true)
    {
        await using var db = NewDb();
        var tenant = new Tenant { Name = name };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        if (connected)
        {
            await using var seed = NewDb(tenant.Id);
            seed.ChannelConnections.Add(new ChannelConnection { Channel = "Trendyol", Status = ConnectionStatus.Connected });
            await seed.SaveChangesAsync();
        }
        return tenant.Id;
    }

    private static async Task<List<Product>> ProductsAsync(Guid tenant, int count, ListingStatus status = ListingStatus.Live, string prefix = "S")
    {
        await using var seed = NewDb(tenant);
        var products = Enumerable.Range(1, count).Select(i => new Product { Name = $"P{i:0000}", Barcode = $"{prefix}{i:0000}", Price = 100, Stock = 10 }).ToList();
        foreach (var p in products) p.Listings.Add(new ProductListing { Channel = "Trendyol", Status = status });
        seed.Products.AddRange(products);
        await seed.SaveChangesAsync();
        return products;
    }

    private static StockPriceSyncHandler Handler(AppDbContext db, FakeClient client, FakeTime time) =>
        new(db, new FakeCreds(), new FakeFactory(client), new StockPriceDeltaPlanner(db, time), time);

    private static async Task RunAsync(Guid tenant, FakeClient client, FakeTime time)
    {
        await using var db = NewDb(tenant);
        var job = new SyncJob { Type = SyncJobType.StockPriceSync, Channel = "Trendyol" };
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync();
        await Handler(db, client, time).HandleAsync(job, default);
    }

    [Fact]
    public async Task Ilk_calistirma_hepsini_gonderir_ikincisi_degisiklik_olmadigi_icin_istek_atmaz()
    {
        var tenant = await NewTenantAsync("Delta A");
        await ProductsAsync(tenant, 3);
        var client = new FakeClient();
        var time = new FakeTime();

        await RunAsync(tenant, client, time);
        var call = Assert.Single(client.Calls);
        Assert.Equal(["S0001", "S0002", "S0003"], call.Select(i => i.Barcode).OrderBy(x => x));
        Assert.All(call, i => Assert.Equal((10, 100m, 100m), (i.Quantity, i.SalePrice, i.ListPrice)));

        await RunAsync(tenant, client, time);
        Assert.Single(client.Calls); // değişiklik yok → kanala istek yok
        Assert.Single(await NewDb(tenant).StockPriceBatches.ToListAsync());
    }

    [Fact]
    public async Task Yalnizca_degisen_barkodlar_gonderilir_ve_stok_kurali_uygulanir()
    {
        var tenant = await NewTenantAsync("Delta B");
        var products = await ProductsAsync(tenant, 3);
        var client = new FakeClient();
        var time = new FakeTime();
        await RunAsync(tenant, client, time);

        await using (var edit = NewDb(tenant))
        {
            (await edit.Products.SingleAsync(p => p.Barcode == "S0002")).Stock = 4;
            edit.StockRules.Add(new StockRule { Channel = "Trendyol", SafetyStock = 0, AllocationPercent = 100 });
            await edit.SaveChangesAsync();
        }
        time.Now = time.Now.AddMinutes(20);
        await RunAsync(tenant, client, time);

        var second = client.Calls[1];
        Assert.Equal(["S0002"], second.Select(i => i.Barcode));
        Assert.Equal(4, second[0].Quantity);

        // Kanal fiyatı hesaplanınca yalnızca o ürün için fiyat gider.
        await using (var edit = NewDb(tenant)) { edit.ChannelPrices.Add(new ChannelPrice { ProductId = products[0].Id, Channel = "Trendyol", Price = 129.9m }); await edit.SaveChangesAsync(); }
        time.Now = time.Now.AddMinutes(20);
        await RunAsync(tenant, client, time);
        var third = Assert.Single(client.Calls[2]);
        Assert.Equal(("S0001", 129.9m), (third.Barcode, third.SalePrice));
    }

    [Fact]
    public async Task Yalnizca_kanalda_var_olan_Yayinda_ve_Onayda_urunler_senkronlanir()
    {
        var tenant = await NewTenantAsync("Delta C");
        await ProductsAsync(tenant, 1, ListingStatus.Live, "L");
        await ProductsAsync(tenant, 1, ListingStatus.Pending, "P");
        await ProductsAsync(tenant, 1, ListingStatus.Missing, "M");
        await ProductsAsync(tenant, 1, ListingStatus.Error, "E");
        await ProductsAsync(tenant, 1, ListingStatus.Passive, "X");
        await using (var seed = NewDb(tenant)) { seed.Products.Add(new Product { Name = "Kanalsız", Barcode = "NOCH", Price = 1 }); await seed.SaveChangesAsync(); }
        var client = new FakeClient();

        await RunAsync(tenant, client, new FakeTime());

        Assert.Equal(["L0001", "P0001"], Assert.Single(client.Calls).Select(i => i.Barcode).OrderBy(x => x));
    }

    [Fact]
    public async Task Bin_barkoddan_fazlasi_1000lik_paketlere_bolunur()
    {
        var tenant = await NewTenantAsync("Delta D");
        await ProductsAsync(tenant, 1001);
        var client = new FakeClient();

        await RunAsync(tenant, client, new FakeTime());

        Assert.Equal([1000, 1], client.Calls.Select(c => c.Count));
        var batches = await NewDb(tenant).StockPriceBatches.OrderBy(b => b.ItemCount).ToListAsync();
        Assert.Equal([1, 1000], batches.Select(b => b.ItemCount));
        Assert.All(batches, b => Assert.True(b.Pending));
    }

    [Fact]
    public async Task Ayni_govde_15_dakika_icinde_yeniden_gonderilmez_sonra_gonderilir()
    {
        var tenant = await NewTenantAsync("Delta E");
        await ProductsAsync(tenant, 2);
        var client = new FakeClient();
        var time = new FakeTime();
        await RunAsync(tenant, client, time);

        // Durum kaybolmuş gibi (ör. reddedilen batch sonrası) aynı içerik yeniden planlanır.
        await using (var db = NewDb(tenant)) { db.ChannelStockPriceStates.RemoveRange(db.ChannelStockPriceStates); await db.SaveChangesAsync(); }
        time.Now = time.Now.AddMinutes(10);
        await RunAsync(tenant, client, time);
        Assert.Single(client.Calls); // 15 dk dolmadı: aynı gövde gönderilmedi

        time.Now = time.Now.AddMinutes(6);
        await RunAsync(tenant, client, time);
        Assert.Equal(2, client.Calls.Count); // 16. dakikada gönderildi
    }

    [Fact]
    public async Task Dakikada_30_fiyat_siniri_dolan_barkod_ertelenir_ve_pencere_gecince_gonderilir()
    {
        var tenant = await NewTenantAsync("Delta F");
        var products = await ProductsAsync(tenant, 2);
        var client = new FakeClient();
        var time = new FakeTime();
        await RunAsync(tenant, client, time);

        await using (var edit = NewDb(tenant))
        {
            // S0001: dakikalık pencere dolu; S0002 serbest. İkisinin de fiyatı değişti.
            var s1 = await edit.ChannelStockPriceStates.SingleAsync(s => s.ProductId == products[0].Id);
            s1.PriceWindowStart = time.Now.UtcDateTime; s1.PriceUpdatesInWindow = StockPriceDelta.MaxPriceUpdatesPerBarcodePerMinute;
            edit.ChannelPrices.AddRange(new ChannelPrice { ProductId = products[0].Id, Channel = "Trendyol", Price = 150 }, new ChannelPrice { ProductId = products[1].Id, Channel = "Trendyol", Price = 160 });
            await edit.SaveChangesAsync();
        }
        time.Now = time.Now.AddSeconds(20);
        await RunAsync(tenant, client, time);

        Assert.Equal(["S0002"], client.Calls[1].Select(i => i.Barcode)); // S0001 ertelendi
        time.Now = time.Now.AddSeconds(50); // pencere (1 dk) geçti
        await RunAsync(tenant, client, time);
        Assert.Equal(["S0001"], client.Calls[2].Select(i => i.Barcode));
    }

    [Fact]
    public async Task Kural_kaydedilmemisse_planlayici_tum_stogu_ve_katalog_fiyatini_kullanir()
    {
        var tenant = await NewTenantAsync("Delta G");
        await ProductsAsync(tenant, 1);
        await using var db = NewDb(tenant);
        var plan = await new StockPriceDeltaPlanner(db, new FakeTime()).PlanAsync("Trendyol", default);

        var item = Assert.Single(plan.Items);
        Assert.Equal((10, 100m, 0), (item.Quantity, item.Price, plan.Deferred));
    }

    // --- sonuç takipçisi ---

    private ServiceProvider Services(FakeClient client, FakeTime time)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantProvider, AmbientProvider>();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(Conn));
        services.AddSingleton<IChannelCredentialService>(new FakeCreds());
        services.AddSingleton<IMarketplaceClientFactory>(new FakeFactory(client));
        services.AddSingleton<TimeProvider>(time);
        services.AddScoped<StockPriceDeltaPlanner>();
        services.AddScoped<IJobQueue, RecordingQueue>();
        return services.BuildServiceProvider();
    }

    private static StockPriceBatchPoller Poller(ServiceProvider sp, FakeTime time) =>
        new(sp.GetRequiredService<IServiceScopeFactory>(), time, NullLogger<StockPriceBatchPoller>.Instance);

    [Fact]
    public async Task Reddedilen_barkodlarin_durumu_silinir_yeniden_gonderilir_ve_nedenler_batche_yazilir()
    {
        var tenant = await NewTenantAsync("Poll A");
        await ProductsAsync(tenant, 3);
        var client = new FakeClient();
        var time = new FakeTime();
        await RunAsync(tenant, client, time);

        client.Result = new BatchStatus("sp-1", "", [new("S0001", "SUCCESS", []), new("S0002", "FAILED", ["Barkod bulunamadı"]), new("S0003", "SUCCESS", [])]);
        using var sp = Services(client, time);
        await Poller(sp, time).Execute(null!, default);

        await using var check = NewDb(tenant);
        var batch = await check.StockPriceBatches.SingleAsync();
        Assert.Equal((false, 1), (batch.Pending, batch.FailedCount));
        Assert.Contains("S0002: Barkod bulunamadı", batch.ErrorSummary);
        Assert.Equal(2, await check.ChannelStockPriceStates.CountAsync()); // reddedilenin durumu silindi

        time.Now = time.Now.AddMinutes(20);
        await RunAsync(tenant, client, time);
        Assert.Equal(["S0002"], client.Calls[1].Select(i => i.Barcode)); // yalnızca reddedilen yeniden gider
    }

    [Fact]
    public async Task Tamamlanmamis_batch_beklemeye_devam_eder_4_saat_dolunca_barkodlar_yeniden_gonderilir()
    {
        var tenant = await NewTenantAsync("Poll B");
        await ProductsAsync(tenant, 2);
        var client = new FakeClient { ResultError = new MarketplaceApiException("Trendyol", 503, "") };
        var time = new FakeTime();
        await RunAsync(tenant, client, time);
        using var sp = Services(client, time);

        await Poller(sp, time).Execute(null!, default);
        Assert.True((await NewDb(tenant).StockPriceBatches.SingleAsync()).Pending); // 503: sonuç bilinmiyor, beklemeye devam

        time.Now = time.Now.AddHours(4).AddMinutes(1);
        await Poller(sp, time).Execute(null!, default);
        await using var check = NewDb(tenant);
        var batch = await check.StockPriceBatches.SingleAsync();
        Assert.Equal((false, StockPriceBatchPoller.TimeoutMessage), (batch.Pending, batch.ErrorSummary));
        Assert.Equal(0, await check.ChannelStockPriceStates.CountAsync());
    }

    // --- zamanlayıcı ---

    [Fact]
    public async Task Zamanlayici_yalnizca_bekleyen_degisiklik_varsa_is_acar_ve_tekrar_etmez()
    {
        var withChanges = await NewTenantAsync("Sched A");
        await NewTenantAsync("Sched B"); // bağlı ama ürünü yok
        await NewTenantAsync("Sched C", connected: false);
        await ProductsAsync(withChanges, 1);
        using var sp = Services(new FakeClient(), new FakeTime());
        var scheduler = new StockPriceSyncScheduler(sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<StockPriceSyncScheduler>.Instance);

        await scheduler.Execute(null!, default);
        await scheduler.Execute(null!, default); // ilk iş sırada: ikincisi eklemez

        var jobs = await NewDb(withChanges).SyncJobs.ToListAsync();
        var job = Assert.Single(jobs);
        Assert.Equal((SyncJobType.StockPriceSync, "Trendyol"), (job.Type, job.Channel));
        await using var all = NewDb();
        Assert.Equal(1, await all.SyncJobs.IgnoreQueryFilters().CountAsync()); // diğer kiracılar için iş yok
    }

    // --- durum uç noktası ---

    [Fact]
    public async Task Durum_bekleyen_degisiklik_batch_ve_son_hatayi_verir()
    {
        var tenant = await NewTenantAsync("Durum");
        await ProductsAsync(tenant, 2);
        await using var db = NewDb(tenant);
        var controller = new Pazaryeri.Api.Controllers.StockSyncController(db, new StockPriceDeltaPlanner(db, new FakeTime()));

        var before = (await controller.Status("Trendyol", default)).Value!;
        Assert.Equal((true, 2, 0, 0), (before.Supported, before.PendingChanges, before.PendingBatches, before.LastSentItems));

        var client = new FakeClient();
        var time = new FakeTime();
        await RunAsync(tenant, client, time);
        client.Result = new BatchStatus("sp-1", "", [new("S0001", "FAILED", ["Hatalı"]), new("S0002", "SUCCESS", [])]);
        using var sp = Services(client, time);
        await Poller(sp, time).Execute(null!, default);

        await using var db2 = NewDb(tenant);
        var after = (await new Pazaryeri.Api.Controllers.StockSyncController(db2, new StockPriceDeltaPlanner(db2, time)).Status("Trendyol", default)).Value!;
        Assert.Equal((1, 2, 1), (after.PendingChanges, after.LastSentItems, after.LastFailed)); // reddedilen yeniden bekliyor
        Assert.Contains("S0001: Hatalı", after.LastError);
        Assert.False((await new Pazaryeri.Api.Controllers.StockSyncController(db2, new StockPriceDeltaPlanner(db2, time)).Status("N11", default)).Value!.Supported);
    }
}
