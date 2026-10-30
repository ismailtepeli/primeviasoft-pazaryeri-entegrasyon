using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Application.Products;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Products;

namespace Pazaryeri.Tests;

public class ProductSendTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Send;Trusted_Connection=True;TrustServerCertificate=True";

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
        public bool HasCreds { get; set; } = true;
        public Task<CredentialView> SaveAsync(string channel, ChannelEnvironment environment, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CredentialView?> GetMaskedAsync(string channel, ChannelEnvironment environment, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MarketplaceCredentials?> GetForClientAsync(string channel, CancellationToken ct = default) =>
            Task.FromResult<MarketplaceCredentials?>(HasCreds ? new MarketplaceCredentials(channel, "1", "k", "s", "Firma") : null);
    }

    private sealed class FakeClient : IMarketplaceClient
    {
        public string Channel => "Trendyol";
        public List<IReadOnlyList<ProductItem>> Sent { get; } = [];
        public BatchStatus? Result { get; set; }
        public Exception? ResultError { get; set; }
        public Task<BatchResult> SendProductsAsync(IReadOnlyList<ProductItem> products, CancellationToken ct = default) { Sent.Add(products); return Task.FromResult(new BatchResult("batch-1")); }
        public Task<BatchStatus> GetBatchResultAsync(string batchId, CancellationToken ct = default) => ResultError is not null ? throw ResultError : Task.FromResult(Result!);
        public Task<BatchResult> UpdateStockAndPriceAsync(IReadOnlyList<StockPriceItem> items, CancellationToken ct = default) => throw new NotSupportedException();
        public Task TestConnectionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<OrderPage> GetOrdersAsync(OrderQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateOrderStatusAsync(OrderStatusUpdate update, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeFactory(FakeClient client) : IMarketplaceClientFactory
    {
        public IMarketplaceClient Create(MarketplaceCredentials credentials) => client;
    }

    private sealed class FakeQueue(AppDbContext db) : IJobQueue
    {
        public List<SyncJob> Jobs { get; } = [];
        public async Task<SyncJob> EnqueueAsync(SyncJobType type, string channel, string? batchId = null, CancellationToken ct = default, string? payload = null)
        {
            var job = new SyncJob { Type = type, Channel = channel, BatchId = batchId, Payload = payload };
            db.SyncJobs.Add(job);
            await db.SaveChangesAsync(ct);
            Jobs.Add(job);
            return job;
        }
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

    private static async Task<Guid> NewTenantAsync(string name, ConnectionStatus? connection = ConnectionStatus.Connected)
    {
        await using var db = NewDb(null);
        var tenant = new Tenant { Name = name };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        if (connection is { } status)
        {
            await using var seed = NewDb(tenant.Id);
            seed.ChannelConnections.Add(new ChannelConnection { Channel = "Trendyol", Status = status });
            await seed.SaveChangesAsync();
        }
        return tenant.Id;
    }

    private static async Task<Product> NewProductAsync(Guid tenant, string barcode, bool mapped = true, bool withImage = true)
    {
        await using var db = NewDb(tenant);
        var p = new Product { Name = "Ürün " + barcode, Barcode = barcode, Description = "Açıklama", Price = 100, Stock = 5, VatRate = 20 };
        if (withImage) p.Images.Add(new ProductImage { Kind = ImageKind.Url, ExternalUrl = $"https://cdn.example.com/{barcode}.jpg", Position = 0 });
        db.Products.Add(p);
        if (mapped) db.ProductChannelData.Add(new ProductChannelData { ProductId = p.Id, Channel = "Trendyol", BrandId = 11, CategoryId = 22, AttributesJson = """[{"attributeId":1,"valueId":2}]""" });
        await db.SaveChangesAsync();
        return p;
    }

    private static string Payload(params Guid[] ids) => JsonSerializer.Serialize(new ProductSendPayload([.. ids]));

    private static ProductSendHandler Handler(AppDbContext db, FakeClient client, FakeTime? time = null, string? baseUrl = null, bool creds = true) =>
        new(db, new FakeCreds { HasCreds = creds }, new FakeFactory(client),
            new ConfigurationBuilder().AddInMemoryCollection(baseUrl is null ? [] : new Dictionary<string, string?> { ["Storage:PublicBaseUrl"] = baseUrl }).Build(),
            time ?? new FakeTime());

    // --- eşleyici ---

    [Fact]
    public void Eslenmemis_urun_nedenleriyle_eksik_sayilir()
    {
        var p = new Product { Name = new string('x', 101), Barcode = "B", Price = 1 };

        var (item, missing) = TrendyolProductMapper.Map(p, null, null);

        Assert.Null(item);
        Assert.Contains(missing, m => m.Contains("marka"));
        Assert.Contains(missing, m => m.Contains("kategori"));
        Assert.Contains(missing, m => m.Contains("Başlık"));
        Assert.Contains(missing, m => m.Contains("Açıklama boş"));
        Assert.Contains(missing, m => m.Contains("görsel"));
    }

    [Fact]
    public void Yuklenen_gorsel_icin_herkese_acik_adres_gerekir_adres_varsa_tam_url_olusur()
    {
        var image = new ProductImage { Kind = ImageKind.Upload, Extension = ".png" };
        var p = new Product { Name = "A", Barcode = "B", Description = "d", Price = 5, Images = [image] };
        var data = new ProductChannelData { BrandId = 1, CategoryId = 2 };

        Assert.Contains(TrendyolProductMapper.Map(p, data, null).Missing, m => m.Contains("PublicBaseUrl"));

        var (item, missing) = TrendyolProductMapper.Map(p, data, "https://app.example.com/");
        Assert.Empty(missing);
        Assert.Equal($"https://app.example.com/api/images/{image.Id}", item!.ImageUrls[0]);
        Assert.Equal((1L, 2L), (item.BrandId, item.CategoryId));
    }

    // --- gönderim servisi ---

    [Fact]
    public async Task Gonderim_bin_urunluk_islere_bolunur_ve_baglanti_denetlenir()
    {
        var tenant = await NewTenantAsync("Gonder A");
        await using (var seed = NewDb(tenant))
        {
            seed.Products.AddRange(Enumerable.Range(0, 1001).Select(i => new Product { Name = "P" + i, Barcode = "S" + i, Price = 1 }));
            await seed.SaveChangesAsync();
        }
        await using var db = NewDb(tenant);
        var queue = new FakeQueue(db);
        var ids = await db.Products.Select(p => p.Id).ToListAsync();

        var result = await new ProductSendService(db, queue).EnqueueAsync("Trendyol", [.. ids, Guid.NewGuid()]);

        Assert.Equal((2, 1001, 1), (result.Jobs, result.Products, result.Unknown));
        Assert.Equal([1000, 1], queue.Jobs.Select(j => JsonSerializer.Deserialize<ProductSendPayload>(j.Payload!)!.ProductIds.Count));
        Assert.All(queue.Jobs, j => Assert.Equal(SyncJobType.ProductSend, j.Type));
    }

    [Fact]
    public async Task Bagli_olmayan_dogrulanmamis_ya_da_desteklenmeyen_kanala_gonderilmez()
    {
        var notConnected = await NewTenantAsync("Gonder B", null);
        var unverified = await NewTenantAsync("Gonder C", ConnectionStatus.Unverified);
        var product = await NewProductAsync(notConnected, "G1");
        var product2 = await NewProductAsync(unverified, "G2");

        await using var db1 = NewDb(notConnected);
        var e1 = await Assert.ThrowsAsync<CatalogException>(() => new ProductSendService(db1, new FakeQueue(db1)).EnqueueAsync("Trendyol", [product.Id]));
        Assert.Contains("bağlı değil", e1.Message);

        await using var db2 = NewDb(unverified);
        var e2 = await Assert.ThrowsAsync<CatalogException>(() => new ProductSendService(db2, new FakeQueue(db2)).EnqueueAsync("Trendyol", [product2.Id]));
        Assert.Contains("doğrulanmadı", e2.Message);

        var e3 = await Assert.ThrowsAsync<CatalogException>(() => new ProductSendService(db2, new FakeQueue(db2)).EnqueueAsync("N11", [product2.Id]));
        Assert.Contains("henüz desteklenmiyor", e3.Message);
        await Assert.ThrowsAsync<CatalogException>(() => new ProductSendService(db2, new FakeQueue(db2)).EnqueueAsync("Trendyol", []));
    }

    // --- işleyici ---

    [Fact]
    public async Task Isleyici_eksikleri_isaretler_gonderilebilenleri_yollar_ve_batch_kimligini_kaydeder()
    {
        var tenant = await NewTenantAsync("Isleyici");
        var ok = await NewProductAsync(tenant, "OK1");
        var unmapped = await NewProductAsync(tenant, "NO1", mapped: false);
        var time = new FakeTime();
        var client = new FakeClient();
        await using var db = NewDb(tenant);
        var job = new SyncJob { Type = SyncJobType.ProductSend, Channel = "Trendyol", Payload = Payload(ok.Id, unmapped.Id) };
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync();

        await Handler(db, client, time).HandleAsync(job, default);

        var sent = Assert.Single(client.Sent);
        var item = Assert.Single(sent);
        Assert.Equal(("OK1", 11L, 22L, 20), (item.Barcode, item.BrandId, item.CategoryId, item.VatRate));
        Assert.Equal(["https://cdn.example.com/OK1.jpg"], item.ImageUrls);
        Assert.Equal(2L, item.Attributes.Single().ValueId);

        await using var check = NewDb(tenant);
        var saved = await check.SyncJobs.SingleAsync();
        Assert.Equal(("batch-1", true, 1), (saved.BatchId, saved.BatchPending, saved.AffectedCount));
        Assert.Equal(time.Now.UtcDateTime, saved.BatchSentAt);
        var listings = await check.ProductListings.ToListAsync();
        Assert.Equal(ListingStatus.Pending, listings.Single(l => l.ProductId == ok.Id).Status);
        var missing = listings.Single(l => l.ProductId == unmapped.Id);
        Assert.Equal(ListingStatus.Missing, missing.Status);
        Assert.Contains("kategori eşlemesi yok", missing.ErrorMessage);
    }

    [Fact]
    public async Task Hic_gonderilebilir_urun_yoksa_kanala_istek_atilmaz_ve_kimlik_yoksa_hata_verir()
    {
        var tenant = await NewTenantAsync("Isleyici2");
        var unmapped = await NewProductAsync(tenant, "NO2", mapped: false);
        var client = new FakeClient();
        await using var db = NewDb(tenant);
        var job = new SyncJob { Type = SyncJobType.ProductSend, Channel = "Trendyol", Payload = Payload(unmapped.Id) };
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync();

        await Handler(db, client).HandleAsync(job, default);
        Assert.Empty(client.Sent);
        Assert.False(job.BatchPending);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(db, client, creds: false).HandleAsync(job, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(db, client).HandleAsync(new SyncJob { Channel = "Trendyol" }, default));
    }

    [Fact]
    public async Task Isleyici_kategori_eslemesini_ve_marka_adini_kullanir_eslenmemis_kategori_eksik_olur()
    {
        var tenant = await NewTenantAsync("Isleyici3");
        await using (var seed = NewDb(null))
        {
            seed.ChannelBrands.Add(new ChannelBrand { Channel = "Trendyol", ExternalId = 77, Name = "Nike" });
            await seed.SaveChangesAsync();
        }
        Guid mappedCat, unmappedCat;
        await using (var seed = NewDb(tenant))
        {
            var a = new Category { Name = "Tişört" };
            var b = new Category { Name = "Şapka" };
            seed.Categories.AddRange(a, b);
            await seed.SaveChangesAsync();
            seed.CategoryMappings.Add(new CategoryMapping { Channel = "Trendyol", CategoryId = a.Id, ExternalCategoryId = 411 });
            await seed.SaveChangesAsync();
            (mappedCat, unmappedCat) = (a.Id, b.Id);
        }
        Product Make(string barcode, Guid cat)
        {
            var p = new Product { Name = "Ürün " + barcode, Barcode = barcode, Description = "d", Price = 5, Brand = "nike", CategoryId = cat };
            p.Images.Add(new ProductImage { Kind = ImageKind.Url, ExternalUrl = "https://cdn.example.com/x.jpg" });
            return p;
        }
        var ok = Make("MAP1", mappedCat);
        var bad = Make("MAP2", unmappedCat);
        await using (var seed = NewDb(tenant)) { seed.Products.AddRange(ok, bad); await seed.SaveChangesAsync(); }

        var client = new FakeClient();
        await using var db = NewDb(tenant);
        var job = new SyncJob { Type = SyncJobType.ProductSend, Channel = "Trendyol", Payload = Payload(ok.Id, bad.Id) };
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync();

        await Handler(db, client).HandleAsync(job, default);

        var item = Assert.Single(Assert.Single(client.Sent));
        Assert.Equal(("MAP1", 411L, 77L), (item.Barcode, item.CategoryId, item.BrandId)); // eşleme + marka adı (büyük/küçük harf duyarsız)
        var listing = await NewDb(tenant).ProductListings.SingleAsync(l => l.ProductId == bad.Id);
        Assert.Equal(ListingStatus.Missing, listing.Status);
        Assert.Contains("kategori eşlemesi yok", listing.ErrorMessage);
        Assert.DoesNotContain("marka", listing.ErrorMessage);
    }

    [Fact]
    public async Task Isleyici_ozellik_eslemelerinden_kanal_ozelliklerini_uretir()
    {
        var tenant = await NewTenantAsync("Isleyici4");
        await using (var seed = NewDb(null))
        {
            seed.ChannelAttributes.AddRange(
                new ChannelAttribute { Channel = "Trendyol", CategoryExternalId = 411, ExternalId = 47, Name = "Renk", Required = true },
                new ChannelAttribute { Channel = "Trendyol", CategoryExternalId = 411, ExternalId = 5, Name = "Beden", Required = true });
            await seed.SaveChangesAsync();
        }
        Product product;
        await using (var seed = NewDb(tenant))
        {
            var cat = new Category { Name = "Tişört" };
            seed.Categories.Add(cat);
            await seed.SaveChangesAsync();
            seed.CategoryMappings.Add(new CategoryMapping { Channel = "Trendyol", CategoryId = cat.Id, ExternalCategoryId = 411 });
            seed.AttributeMappings.Add(new AttributeMapping
            {
                Channel = "Trendyol", CategoryId = cat.Id, ExternalAttributeId = 47, SourceAttribute = "Renk",
                Values = [new AttributeValueMapping { SourceValue = "Mavi", ExternalValueId = 2 }],
            });
            seed.AttributeMappings.Add(new AttributeMapping { Channel = "Trendyol", CategoryId = cat.Id, ExternalAttributeId = 5, FixedValueId = 70 });
            product = new Product { Name = "P", Barcode = "ATTR1", Description = "d", Price = 5, Brand = "x", CategoryId = cat.Id, Variant = "Renk: mavi" };
            product.Images.Add(new ProductImage { Kind = ImageKind.Url, ExternalUrl = "https://cdn.example.com/a.jpg" });
            seed.Products.Add(product);
            seed.ProductChannelData.Add(new ProductChannelData { ProductId = product.Id, Channel = "Trendyol", BrandId = 9 });
            await seed.SaveChangesAsync();
        }
        var client = new FakeClient();
        await using var db = NewDb(tenant);
        var job = new SyncJob { Type = SyncJobType.ProductSend, Channel = "Trendyol", Payload = Payload(product.Id) };
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync();

        await Handler(db, client).HandleAsync(job, default);

        var item = Assert.Single(Assert.Single(client.Sent));
        Assert.Equal([(47L, (long?)2), (5L, 70)], item.Attributes.Select(a => (a.AttributeId, a.ValueId)).OrderByDescending(x => x.Item1));
    }

    // --- sonuç uygulayıcı ---

    private static (SyncJob Job, Dictionary<Guid, string> Barcodes, List<ProductListing> Listings) Scenario(DateTime sentAt, params string[] barcodes)
    {
        var job = new SyncJob { Type = SyncJobType.ProductSend, BatchPending = true, BatchSentAt = sentAt, BatchId = "b" };
        var ids = barcodes.ToDictionary(b => Guid.NewGuid(), b => b);
        var listings = ids.Select(kv => new ProductListing { ProductId = kv.Key, Channel = "Trendyol", Status = ListingStatus.Pending }).ToList();
        return (job, ids, listings);
    }

    private static readonly DateTime T0 = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Item_sonuclari_urunlerin_kanal_durumuna_yazilir_ve_hata_nedenleri_saklanir()
    {
        var (job, barcodes, listings) = Scenario(T0, "A", "B", "C");
        var status = new BatchStatus("b", "COMPLETED",
        [
            new BatchItemResult("A", "SUCCESS", []),
            new BatchItemResult("B", "FAILED", ["Barkod geçersiz", "Kategori hatalı", "Barkod geçersiz"]),
        ]);

        var outcome = ProductBatchApplier.Apply(job, barcodes, listings, status, T0.AddMinutes(3));

        Assert.Equal(BatchOutcome.Completed, outcome);
        ProductListing Of(string b) => listings.Single(l => barcodes[l.ProductId] == b);
        Assert.Equal(ListingStatus.Pending, Of("A").Status); // kabul edildi, onay sürüyor
        Assert.Equal((ListingStatus.Error, "Barkod geçersiz\nKategori hatalı"), (Of("B").Status, Of("B").ErrorMessage));
        Assert.Equal(ProductBatchApplier.MissingFromResultMessage, Of("C").ErrorMessage); // sonuçta yok
        Assert.Equal((false, SyncJobStatus.Failed, 2), (job.BatchPending, job.Status, job.AffectedCount));
    }

    [Fact]
    public void Hepsi_basariliysa_is_hatali_sayilmaz()
    {
        var (job, barcodes, listings) = Scenario(T0, "A");
        job.Status = SyncJobStatus.Succeeded;

        ProductBatchApplier.Apply(job, barcodes, listings, new BatchStatus("b", "", [new BatchItemResult("A", "SUCCESS", [])]), T0.AddMinutes(1));

        Assert.Equal((false, SyncJobStatus.Succeeded, 0), (job.BatchPending, job.Status, job.AffectedCount));
    }

    [Fact]
    public void Sonuc_gelmediyse_4_saat_dolana_kadar_beklenir_dolunca_hata_olur()
    {
        var (job, barcodes, listings) = Scenario(T0, "A");
        var inProgress = new BatchStatus("b", "IN_PROGRESS", []);

        Assert.Equal(BatchOutcome.StillPending, ProductBatchApplier.Apply(job, barcodes, listings, inProgress, T0.AddHours(3).AddMinutes(59)));
        Assert.True(job.BatchPending);
        Assert.Equal(ListingStatus.Pending, listings[0].Status);

        Assert.Equal(BatchOutcome.TimedOut, ProductBatchApplier.Apply(job, barcodes, listings, null, T0.AddHours(4).AddMinutes(1))); // sorgulanamasa da süre dolar
        Assert.Equal((ListingStatus.Error, ProductBatchApplier.TimeoutMessage), (listings[0].Status, listings[0].ErrorMessage));
        Assert.Equal((false, SyncJobStatus.Failed), (job.BatchPending, job.Status));
    }

    [Fact]
    public void Kismi_sonuclu_toplu_islem_tamamlanmamis_sayilir()
    {
        var partial = new BatchStatus("b", "IN_PROGRESS", [new BatchItemResult("A", "SUCCESS", []), new BatchItemResult("B", "PROCESSING", [])]);
        Assert.False(ProductBatchApplier.IsDone(partial));
        Assert.True(ProductBatchApplier.IsDone(new BatchStatus("b", "", [new BatchItemResult("A", "SUCCESS", []), new BatchItemResult("B", "FAILED", ["x"])])));
        Assert.False(ProductBatchApplier.IsDone(new BatchStatus("b", "", [])));
    }

    // --- takipçi (poller) ---

    private ServiceProvider Services(FakeClient client, FakeTime time)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantProvider, AmbientProvider>();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(Conn));
        services.AddSingleton<IChannelCredentialService>(new FakeCreds());
        services.AddSingleton<IMarketplaceClientFactory>(new FakeFactory(client));
        services.AddSingleton<TimeProvider>(time);
        return services.BuildServiceProvider();
    }

    private static async Task<(Guid Tenant, Guid ProductId, Guid JobId)> PendingBatchAsync(string name, DateTime sentAt)
    {
        var tenant = await NewTenantAsync(name);
        var product = await NewProductAsync(tenant, "PL-" + name);
        await using var db = NewDb(tenant);
        db.ProductListings.Add(new ProductListing { ProductId = product.Id, Channel = "Trendyol", Status = ListingStatus.Pending });
        var job = new SyncJob { Type = SyncJobType.ProductSend, Channel = "Trendyol", Status = SyncJobStatus.Succeeded, BatchId = "b-" + name,
            BatchPending = true, BatchSentAt = sentAt, Payload = Payload(product.Id) };
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync();
        return (tenant, product.Id, job.Id);
    }

    [Fact]
    public async Task Takipci_tum_kiracilarin_bekleyen_batchlerini_kendi_baglaminda_isler()
    {
        var time = new FakeTime();
        var a = await PendingBatchAsync("PollA", time.Now.UtcDateTime);
        var b = await PendingBatchAsync("PollB", time.Now.UtcDateTime);
        var client = new FakeClient { Result = new BatchStatus("x", "COMPLETED", [new BatchItemResult("PL-PollA", "FAILED", ["Marka hatalı"]), new BatchItemResult("PL-PollB", "SUCCESS", [])]) };
        using var sp = Services(client, time);

        await new ProductBatchPoller(sp.GetRequiredService<IServiceScopeFactory>(), time, NullLogger<ProductBatchPoller>.Instance).Execute(null!, default);

        var listingA = await NewDb(a.Tenant).ProductListings.SingleAsync();
        Assert.Equal((ListingStatus.Error, "Marka hatalı"), (listingA.Status, listingA.ErrorMessage));
        Assert.Equal(ListingStatus.Pending, (await NewDb(b.Tenant).ProductListings.SingleAsync()).Status);
        Assert.False((await NewDb(b.Tenant).SyncJobs.SingleAsync()).BatchPending);
        Assert.Equal(SyncJobStatus.Failed, (await NewDb(a.Tenant).SyncJobs.SingleAsync()).Status);
    }

    [Fact]
    public async Task Kanal_hata_verirse_batch_beklemeye_devam_eder_sure_dolunca_hatali_olur()
    {
        var time = new FakeTime();
        var p = await PendingBatchAsync("PollC", time.Now.UtcDateTime);
        var client = new FakeClient { ResultError = new MarketplaceApiException("Trendyol", 503, "") };
        using var sp = Services(client, time);
        var poller = new ProductBatchPoller(sp.GetRequiredService<IServiceScopeFactory>(), time, NullLogger<ProductBatchPoller>.Instance);

        await poller.Execute(null!, default);
        Assert.True((await NewDb(p.Tenant).SyncJobs.SingleAsync()).BatchPending);

        time.Now = time.Now.AddHours(4).AddMinutes(5);
        await poller.Execute(null!, default);
        var listing = await NewDb(p.Tenant).ProductListings.SingleAsync();
        Assert.Equal((ListingStatus.Error, ProductBatchApplier.TimeoutMessage), (listing.Status, listing.ErrorMessage));
    }
}
