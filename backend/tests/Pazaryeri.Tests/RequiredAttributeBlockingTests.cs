using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Application.Products;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Products;

namespace Pazaryeri.Tests;

/// <summary>Zorunlu kanal özelliği eksik olan ürünler kuyruğa alınmaz; "Eksik" olarak nedenleriyle yazılır (#24).</summary>
public class RequiredAttributeBlockingTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_ReqAttr;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private sealed class FakeQueue(AppDbContext db) : IJobQueue
    {
        public List<SyncJob> Jobs { get; } = [];
        public async Task<SyncJob> EnqueueAsync(SyncJobType type, string channel, string? batchId = null, CancellationToken ct = default, string? payload = null)
        {
            var job = new SyncJob { Type = type, Channel = channel, Payload = payload };
            db.SyncJobs.Add(job);
            await db.SaveChangesAsync(ct);
            Jobs.Add(job);
            return job;
        }
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
        public List<IReadOnlyList<ProductItem>> Sent { get; } = [];
        public Task<BatchResult> SendProductsAsync(IReadOnlyList<ProductItem> products, CancellationToken ct = default) { Sent.Add(products); return Task.FromResult(new BatchResult("b")); }
        public Task<BatchStatus> GetBatchResultAsync(string batchId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BatchResult> UpdateStockAndPriceAsync(IReadOnlyList<StockPriceItem> items, CancellationToken ct = default) => throw new NotSupportedException();
        public Task TestConnectionAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrderPage> GetOrdersAsync(OrderQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateOrderStatusAsync(OrderStatusUpdate update, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeFactory(FakeClient client) : IMarketplaceClientFactory
    {
        public IMarketplaceClient Create(MarketplaceCredentials credentials) => client;
    }

    private static AppDbContext NewDb(Guid? tenantId = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    private static ProductChannelPreparer Preparer(AppDbContext db) => new(db, new ConfigurationBuilder().Build());

    public async Task InitializeAsync()
    {
        await using var db = NewDb();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Trendyol kategorisi 411'in iki zorunlu özelliği (Renk, Beden) olan, eşlenmiş bir kategori ve içinde bir ürün.</summary>
    private static async Task<(Guid Tenant, Guid Category, Product Product)> SetupAsync(string name, bool withCatalogAttributes = true, string? variant = null)
    {
        await using var global = NewDb();
        var tenant = new Tenant { Name = name };
        global.Tenants.Add(tenant);
        if (withCatalogAttributes && !await global.ChannelAttributes.AnyAsync())
        {
            global.ChannelAttributes.Add(new ChannelAttribute { Channel = "Trendyol", CategoryExternalId = 411, ExternalId = 47, Name = "Renk", Required = true });
            global.ChannelAttributes.Add(new ChannelAttribute { Channel = "Trendyol", CategoryExternalId = 411, ExternalId = 5, Name = "Beden", Required = true });
        }
        await global.SaveChangesAsync();

        await using var seed = NewDb(tenant.Id);
        seed.ChannelConnections.Add(new ChannelConnection { Channel = "Trendyol", Status = ConnectionStatus.Connected });
        var cat = new Category { Name = "Tişört" };
        seed.Categories.Add(cat);
        await seed.SaveChangesAsync();
        seed.CategoryMappings.Add(new CategoryMapping { Channel = "Trendyol", CategoryId = cat.Id, ExternalCategoryId = 411 });
        var product = NewProduct("REQ-" + name, cat.Id, variant);
        seed.Products.Add(product);
        seed.ProductChannelData.Add(new ProductChannelData { ProductId = product.Id, Channel = "Trendyol", BrandId = 9 });
        await seed.SaveChangesAsync();
        return (tenant.Id, cat.Id, product);
    }

    private static Product NewProduct(string barcode, Guid category, string? variant)
    {
        var p = new Product { Name = "Ürün " + barcode, Barcode = barcode, Description = "d", Price = 5, CategoryId = category, Variant = variant };
        p.Images.Add(new ProductImage { Kind = ImageKind.Url, ExternalUrl = "https://cdn.example.com/x.jpg" });
        return p;
    }

    private static AttributeMapping Source(Guid cat, long attribute, string source, string value, long valueId) => new()
    {
        Channel = "Trendyol", CategoryId = cat, ExternalAttributeId = attribute, SourceAttribute = source,
        Values = [new AttributeValueMapping { SourceValue = value, ExternalValueId = valueId }],
    };

    [Fact]
    public async Task Eksik_zorunlu_ozellikli_urun_kuyruga_alinmaz_ve_nedenleriyle_Eksik_yazilir()
    {
        var (tenant, cat, product) = await SetupAsync("Engel", variant: "Renk: Mavi");
        await using (var seed = NewDb(tenant)) // yalnızca Renk eşlendi; Beden eşlemesi yok
        {
            seed.AttributeMappings.Add(Source(cat, 47, "Renk", "Mavi", 2));
            await seed.SaveChangesAsync();
        }
        await using var db = NewDb(tenant);
        var queue = new FakeQueue(db);

        var result = await new ProductSendService(db, queue, Preparer(db)).EnqueueAsync("Trendyol", [product.Id]);

        Assert.Equal((0, 0, 1), (result.Jobs, result.Products, result.Blocked));
        Assert.Empty(queue.Jobs);
        var listing = await NewDb(tenant).ProductListings.SingleAsync();
        Assert.Equal((ListingStatus.Missing, "Zorunlu özellik eksik: Beden."), (listing.Status, listing.ErrorMessage));
    }

    [Fact]
    public async Task Karisik_secimde_yalnizca_eksik_olan_engellenir_hazir_olan_kuyruga_alinir()
    {
        var (tenant, cat, ready) = await SetupAsync("Karisik", variant: "Renk: Mavi, Beden: M");
        Product blocked = NewProduct("REQ-BEDENSIZ", cat, "Renk: Mavi"); // Beden bilgisi yok
        await using (var seed = NewDb(tenant))
        {
            seed.AttributeMappings.Add(Source(cat, 47, "Renk", "Mavi", 2));
            seed.AttributeMappings.Add(Source(cat, 5, "Beden", "M", 70));
            seed.Products.Add(blocked);
            seed.ProductChannelData.Add(new ProductChannelData { ProductId = blocked.Id, Channel = "Trendyol", BrandId = 9 });
            await seed.SaveChangesAsync();
        }
        await using var db = NewDb(tenant);
        var queue = new FakeQueue(db);

        var result = await new ProductSendService(db, queue, Preparer(db)).EnqueueAsync("Trendyol", [ready.Id, blocked.Id]);

        Assert.Equal((1, 1, 1), (result.Jobs, result.Products, result.Blocked));
        Assert.Equal([ready.Id], JsonSerializer.Deserialize<ProductSendPayload>(Assert.Single(queue.Jobs).Payload!)!.ProductIds);
        var only = await NewDb(tenant).ProductListings.SingleAsync(); // hazır ürünün kaydı işleyici çalışınca oluşur
        Assert.Equal((blocked.Id, ListingStatus.Missing), (only.ProductId, only.Status));
        Assert.Contains("Beden", only.ErrorMessage);
    }

    [Fact]
    public async Task Kanal_kategorisinin_ozellikleri_katalogda_yoksa_zorunluluk_bilinemez_ve_engellenmez()
    {
        var (tenant, _, product) = await SetupAsync("Bilinmez", withCatalogAttributes: false);
        await using var db = NewDb(tenant);
        var queue = new FakeQueue(db);

        var result = await new ProductSendService(db, queue, Preparer(db)).EnqueueAsync("Trendyol", [product.Id]);

        Assert.Equal((1, 1, 0), (result.Jobs, result.Products, result.Blocked));
    }

    [Fact]
    public async Task Isleyici_de_eksik_zorunlu_ozelligi_yakalar()
    {
        var (tenant, _, product) = await SetupAsync("Isleyici", variant: "Renk: Mavi"); // hiç özellik eşlemesi yok
        var client = new FakeClient();
        await using var db = NewDb(tenant);
        var job = new SyncJob { Type = SyncJobType.ProductSend, Channel = "Trendyol", Payload = JsonSerializer.Serialize(new ProductSendPayload([product.Id])) };
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync();

        await new ProductSendHandler(db, new FakeCreds(), new FakeFactory(client), Preparer(db), TimeProvider.System).HandleAsync(job, default);

        Assert.Empty(client.Sent);
        var listing = await NewDb(tenant).ProductListings.SingleAsync();
        Assert.Equal((ListingStatus.Missing, "Zorunlu özellik eksik: Beden, Renk."), (listing.Status, listing.ErrorMessage));
        Assert.Equal(1, job.AffectedCount);
    }
}
