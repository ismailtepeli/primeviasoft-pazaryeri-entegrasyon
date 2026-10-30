using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Catalog;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Catalog;
using Pazaryeri.Infrastructure.Marketplace.Trendyol;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Tests;

public class CatalogSyncTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Catalog_Sync;Trusted_Connection=True;TrustServerCertificate=True";

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

    // --- Trendyol istemcisi ---

    private sealed class PathServer(Dictionary<string, string> routes) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.PathAndQuery;
            Requests.Add(path);
            var body = routes.FirstOrDefault(kv => path.StartsWith(kv.Key)).Value;
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static TrendyolClient Trendyol(PathServer server) =>
        new(new HttpClient(server), new MarketplaceCredentials("Trendyol", "1", "k", "s", "Acme"));

    [Fact]
    public async Task Kategori_agaci_duz_listeye_cevrilir_ve_ust_kategori_korunur()
    {
        var server = new PathServer(new()
        {
            ["/integration/product/product-categories"] = """
                {"categories":[{"id":1,"name":"Giyim","parentId":null,"subCategories":[
                    {"id":2,"name":"Tişört","parentId":1,"subCategories":[{"id":3,"name":"Polo","parentId":2,"subCategories":[]}]},
                    {"id":4,"name":"Pantolon","subCategories":[]}]}]}
                """,
        });

        var all = await Trendyol(server).GetCategoriesAsync();

        Assert.Equal([(1L, (long?)null), (2L, 1L), (3L, 2L), (4L, 1L)], all.Select(c => (c.Id, c.ParentId)));
        Assert.Equal("Polo", all[2].Name);
    }

    [Fact]
    public async Task Markalar_sayfa_sayfa_okunur_ve_kisa_sayfada_durur()
    {
        var full = string.Join(',', Enumerable.Range(1, TrendyolClient.BrandPageSize).Select(i => $$"""{"id":{{i}},"name":"B{{i}}"}"""));
        var server = new PathServer(new()
        {
            ["/integration/product/brands?page=0"] = $$"""{"brands":[{{full}}]}""",
            ["/integration/product/brands?page=1"] = """{"brands":[{"id":5001,"name":"Son"}]}""",
        });

        var pages = new List<int>();
        await foreach (var page in Trendyol(server).GetBrandPagesAsync()) pages.Add(page.Count);

        Assert.Equal([TrendyolClient.BrandPageSize, 1], pages);
        Assert.Equal(2, server.Requests.Count); // kısa sayfadan sonra yeni istek atılmaz
    }

    [Fact]
    public async Task Kategori_ozellikleri_zorunluluk_ve_degerleriyle_okunur()
    {
        var server = new PathServer(new()
        {
            ["/integration/product/categories/411/attributes/47/values"] = """{"attributeValues":[{"id":1,"name":"Kırmızı"},{"id":2,"name":"Mavi"}]}""",
            ["/integration/product/categories/411/attributes"] = """
                {"categoryAttributes":[
                  {"allowCustom":false,"required":true,"varianter":true,"slicer":false,"attribute":{"id":47,"name":"Renk"},"attributeValues":[]},
                  {"allowCustom":true,"required":false,"varianter":false,"slicer":true,"attribute":{"id":9,"name":"Not"},"attributeValues":[{"id":7,"name":"X"}]}]}
                """,
        });
        var client = Trendyol(server);

        var attributes = await client.GetCategoryAttributesAsync(411);
        var values = await client.GetAttributeValuesAsync(411, 47);

        Assert.Equal([(47L, true, false, true), (9L, false, true, false)], attributes.Select(a => (a.Id, a.Required, a.AllowCustom, a.Varianter)));
        Assert.Equal(["X"], attributes[1].Values.Select(v => v.Name));
        Assert.Equal(["Kırmızı", "Mavi"], values.Select(v => v.Name));
    }

    // --- senkron servisi ---

    private sealed class FakeCatalog : IChannelCatalogClient
    {
        public List<CatalogCategory> Categories { get; set; } = [];
        public List<List<CatalogBrand>> BrandPages { get; set; } = [];
        public Dictionary<long, List<CatalogAttribute>> Attributes { get; set; } = [];
        public Dictionary<(long, long), List<CatalogValue>> Values { get; set; } = [];
        public Exception? BrandError { get; set; }
        public int ValueCalls { get; private set; }

        public Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CatalogCategory>>(Categories);

        public async IAsyncEnumerable<IReadOnlyList<CatalogBrand>> GetBrandPagesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var page in BrandPages)
            {
                yield return page;
                if (BrandError is not null) throw BrandError;
                await Task.Yield();
            }
        }

        public Task<IReadOnlyList<CatalogAttribute>> GetCategoryAttributesAsync(long categoryId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogAttribute>>(Attributes.GetValueOrDefault(categoryId) ?? []);

        public Task<IReadOnlyList<CatalogValue>> GetAttributeValuesAsync(long categoryId, long attributeId, CancellationToken ct = default)
        {
            ValueCalls++;
            return Task.FromResult<IReadOnlyList<CatalogValue>>(Values.GetValueOrDefault((categoryId, attributeId)) ?? []);
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

    private static (ChannelCatalogService Service, AppDbContext Db, FakeTime Time) Service()
    {
        var time = new FakeTime();
        var db = NewDb();
        return (new ChannelCatalogService(db, time), db, time);
    }

    [Fact]
    public async Task Kategori_senkronu_ekler_degisikligi_izler_kaldirileni_pasif_yapar_ve_geri_geleni_etkinlestirir()
    {
        var (service, db, time) = Service();
        var client = new FakeCatalog { Categories = [new(1, null, "Giyim"), new(2, 1, "Tişört"), new(3, 1, "Pantolon")] };

        var first = await service.SyncCategoriesAsync("Trendyol", client);
        Assert.Equal((3, 0, 0), (first.Added, first.Changed, first.Removed));
        Assert.Equal([false, true, true], (await db.ChannelCategories.OrderBy(c => c.ExternalId).ToListAsync()).Select(c => c.IsLeaf));

        time.Now = time.Now.AddDays(7);
        client.Categories = [new(1, null, "Giyim"), new(2, 1, "T-Shirt"), new(4, 2, "Polo")]; // 2 yeniden adlandı, 3 kalktı, 4 yeni (2 artık yaprak değil)
        var second = await service.SyncCategoriesAsync("Trendyol", client);

        Assert.Equal((1, 1, 1), (second.Added, second.Changed, second.Removed)); // 2 tek kayıt olarak değişti (ad + yaprak), 1 aynı
        await using var check = NewDb();
        var rows = await check.ChannelCategories.ToDictionaryAsync(c => c.ExternalId);
        Assert.Equal(("T-Shirt", false, true), (rows[2].Name, rows[2].IsLeaf, rows[2].IsActive));
        Assert.False(rows[3].IsActive);
        Assert.Equal(time.Now.UtcDateTime, rows[3].ChangedAt);
        Assert.True(rows[4].IsLeaf);

        client.Categories = [new(1, null, "Giyim"), new(2, 1, "T-Shirt"), new(3, 1, "Pantolon"), new(4, 2, "Polo")];
        var third = await service.SyncCategoriesAsync("Trendyol", client);
        Assert.Equal(1, third.Added); // geri gelen kategori yeniden etkin
        Assert.True((await NewDb().ChannelCategories.SingleAsync(c => c.ExternalId == 3)).IsActive);

        var runs = await NewDb().CatalogSyncRuns.OrderBy(r => r.StartedAt).ToListAsync();
        Assert.Equal(3, runs.Count);
        Assert.All(runs, r => Assert.NotNull(r.FinishedAt));
    }

    [Fact]
    public async Task Bos_kategori_agaci_mevcut_veriyi_silmez_ve_hata_kaydi_olusur()
    {
        var (service, db, _) = Service();
        await service.SyncCategoriesAsync("Trendyol", new FakeCatalog { Categories = [new(1, null, "A")] });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SyncCategoriesAsync("Trendyol", new FakeCatalog()));

        await using var check = NewDb();
        Assert.True((await check.ChannelCategories.SingleAsync()).IsActive);
        Assert.Contains("boş döndü", (await check.CatalogSyncRuns.SingleAsync(r => r.Error != null)).Error);
    }

    [Fact]
    public async Task Marka_senkronu_sayfalari_isler_ve_yalnizca_tamamlaninca_kaldirilanlari_pasif_yapar()
    {
        var (service, _, time) = Service();
        var client = new FakeCatalog { BrandPages = [[new(1, "Nike"), new(2, "Adidas")], [new(3, "Puma")]] };
        var first = await service.SyncBrandsAsync("Trendyol", client);
        Assert.Equal((3, 0, 0), (first.Added, first.Changed, first.Removed));

        time.Now = time.Now.AddDays(7);
        client.BrandPages = [[new(1, "Nike Inc"), new(2, "Adidas")]]; // 3 kalktı, 1 yeniden adlandı
        var second = await service.SyncBrandsAsync("Trendyol", client);
        Assert.Equal((0, 1, 1), (second.Added, second.Changed, second.Removed));

        // Yarıda kesilen senkron kimseyi pasife almamalı.
        time.Now = time.Now.AddDays(7);
        client.BrandPages = [[new(1, "Nike Inc")], [new(2, "Adidas")]];
        client.BrandError = new HttpRequestException("kesildi");
        await Assert.ThrowsAsync<HttpRequestException>(() => service.SyncBrandsAsync("Trendyol", client));

        await using var check = NewDb();
        var brands = await check.ChannelBrands.ToDictionaryAsync(b => b.ExternalId);
        Assert.Equal(("Nike Inc", true), (brands[1].Name, brands[1].IsActive));
        Assert.True(brands[2].IsActive);
        Assert.False(brands[3].IsActive);
        Assert.Contains("kesildi", (await check.CatalogSyncRuns.Where(r => r.Error != null).SingleAsync()).Error);
    }

    [Fact]
    public async Task Ozellik_senkronu_zorunlulugu_ve_degerleri_yazar_degeri_gelmeyen_icin_ayri_sorgular()
    {
        var (service, db, _) = Service();
        var client = new FakeCatalog
        {
            Attributes = new()
            {
                [411] = [
                    new(47, "Renk", true, false, true, false, []),                          // değer listesi gelmedi → ayrıca sorgulanır
                    new(9, "Not", false, true, false, false, []),                            // serbest metin → sorgulanmaz
                    new(5, "Beden", true, false, true, false, [new(70, "M"), new(71, "L")]),   // listede geldi
                ],
            },
            Values = new() { [(411, 47)] = [new(1, "Kırmızı"), new(2, "Mavi")] },
        };

        var summary = await service.SyncAttributesAsync("Trendyol", client, [411, 411]);

        Assert.Equal(1, client.ValueCalls);
        Assert.Equal(3 + 4, summary.Added);
        await using var check = NewDb();
        var renk = await check.ChannelAttributes.SingleAsync(a => a.ExternalId == 47);
        Assert.Equal((true, false, true), (renk.Required, renk.AllowCustom, renk.Varianter));
        Assert.Equal(["Kırmızı", "Mavi"], (await check.ChannelAttributeValues.Where(v => v.AttributeExternalId == 47).OrderBy(v => v.ExternalId).ToListAsync()).Select(v => v.Name));
        Assert.Equal(2, await check.ChannelAttributeValues.CountAsync(v => v.AttributeExternalId == 5));

        // İkinci turda Renk'in zorunluluğu kalktı, Mavi değeri silindi.
        client.Attributes[411] = [new(47, "Renk", false, false, true, false, []), new(5, "Beden", true, false, true, false, [new(70, "M"), new(71, "L")])];
        client.Values[(411, 47)] = [new(1, "Kırmızı")];
        var second = await service.SyncAttributesAsync("Trendyol", client, [411]);
        Assert.Equal((0, 1, 2), (second.Added, second.Changed, second.Removed)); // Not özelliği ve Mavi kaldırıldı
    }

    [Fact]
    public async Task Bir_haftadan_eski_ozellikli_kategoriler_yenilenecekler_arasindadir()
    {
        var (service, _, time) = Service();
        var client = new FakeCatalog { Attributes = new() { [1] = [new(1, "A", false, true, false, false, [])], [2] = [new(2, "B", false, true, false, false, [])] } };
        await service.SyncAttributesAsync("Trendyol", client, [1]);
        time.Now = time.Now.AddDays(6);
        await service.SyncAttributesAsync("Trendyol", client, [2]);

        time.Now = time.Now.AddDays(2); // kategori 1: 8 gün, kategori 2: 2 gün
        Assert.Equal([1L], await service.StaleAttributeCategoriesAsync("Trendyol"));
    }

    // --- işleyici ve haftalık iş ---

    private sealed class FakeCreds : IChannelCredentialService
    {
        public Task<CredentialView> SaveAsync(string channel, ChannelEnvironment environment, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CredentialView?> GetMaskedAsync(string channel, ChannelEnvironment environment, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MarketplaceCredentials?> GetForClientAsync(string channel, CancellationToken ct = default) => Task.FromResult<MarketplaceCredentials?>(new(channel, "1", "k", "s", "Acme"));
    }

    private sealed class CatalogClientAdapter(FakeCatalog catalog) : IMarketplaceClient, IChannelCatalogClient
    {
        public string Channel => "Trendyol";
        public Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken ct = default) => catalog.GetCategoriesAsync(ct);
        public IAsyncEnumerable<IReadOnlyList<CatalogBrand>> GetBrandPagesAsync(CancellationToken ct = default) => catalog.GetBrandPagesAsync(ct);
        public Task<IReadOnlyList<CatalogAttribute>> GetCategoryAttributesAsync(long categoryId, CancellationToken ct = default) => catalog.GetCategoryAttributesAsync(categoryId, ct);
        public Task<IReadOnlyList<CatalogValue>> GetAttributeValuesAsync(long categoryId, long attributeId, CancellationToken ct = default) => catalog.GetAttributeValuesAsync(categoryId, attributeId, ct);
        public Task<BatchResult> SendProductsAsync(IReadOnlyList<ProductItem> products, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BatchResult> UpdateStockAndPriceAsync(IReadOnlyList<StockPriceItem> items, CancellationToken ct = default) => throw new NotSupportedException();
        public Task TestConnectionAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BatchStatus> GetBatchResultAsync(string batchId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrderPage> GetOrdersAsync(OrderQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateOrderStatusAsync(OrderStatusUpdate update, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeFactory(IMarketplaceClient client) : IMarketplaceClientFactory
    {
        public IMarketplaceClient Create(MarketplaceCredentials credentials) => client;
    }

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

    [Fact]
    public async Task Isleyici_kategori_ve_markalari_yeniler_eslenmis_kategorilerin_ozelliklerini_ceker()
    {
        var tenant = await NewTenantAsync("Katalog A");
        await using (var seed = NewDb(tenant))
        {
            var p = new Product { Name = "P", Barcode = "K1", Price = 1 };
            seed.Products.Add(p);
            seed.ProductChannelData.Add(new ProductChannelData { ProductId = p.Id, Channel = "Trendyol", CategoryId = 411 });
            await seed.SaveChangesAsync();
        }
        var catalog = new FakeCatalog
        {
            Categories = [new(411, null, "Tişört")],
            BrandPages = [[new(1, "Nike")]],
            Attributes = new() { [411] = [new(47, "Renk", true, false, true, false, [new(1, "Mavi")])] },
        };
        await using var db = NewDb(tenant);
        var handler = new CatalogSyncHandler(db, new FakeCreds(), new FakeFactory(new CatalogClientAdapter(catalog)), new ChannelCatalogService(db, TimeProvider.System));

        await handler.HandleAsync(new SyncJob { Type = SyncJobType.CatalogSync, Channel = "Trendyol" }, default);

        await using var check = NewDb();
        Assert.Equal(1, await check.ChannelCategories.CountAsync());
        Assert.Equal(1, await check.ChannelBrands.CountAsync());
        Assert.Equal(47, (await check.ChannelAttributes.SingleAsync()).ExternalId);
        Assert.Equal([CatalogKind.Categories, CatalogKind.Brands, CatalogKind.Attributes], (await check.CatalogSyncRuns.OrderBy(r => r.StartedAt).ToListAsync()).Select(r => r.Kind));
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

    [Fact]
    public async Task Haftalik_is_bagli_kiracinin_adina_senkronu_kuyruga_alir_ve_tekrar_etmez()
    {
        var tenant = await NewTenantAsync("Katalog B");
        await NewTenantAsync("Katalog C", connected: false);
        var services = new ServiceCollection();
        services.AddSingleton<ITenantProvider, AmbientProvider>();
        services.AddMemoryCache(); services.AddSingleton(TimeProvider.System); services.AddScoped<Pazaryeri.Application.Modules.IModuleService, Pazaryeri.Infrastructure.Modules.ModuleService>();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(Conn));
        services.AddScoped<IJobQueue, RecordingQueue>();
        await using var sp = services.BuildServiceProvider();
        var job = new CatalogWeeklySync(sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<CatalogWeeklySync>.Instance);

        await job.Execute(null!, default);
        await job.Execute(null!, default); // ilki bekliyor: ikincisi yeni iş eklemez

        var jobs = await NewDb(tenant).SyncJobs.ToListAsync();
        var queued = Assert.Single(jobs);
        Assert.Equal((SyncJobType.CatalogSync, "Trendyol"), (queued.Type, queued.Channel));
    }

    [Fact]
    public async Task Katalog_durumu_sayilari_ve_son_calismalari_verir_ve_senkron_bagli_kanal_ister()
    {
        var tenant = await NewTenantAsync("Katalog D");
        var notConnected = await NewTenantAsync("Katalog E", connected: false);
        var (service, _, _) = Service();
        await service.SyncCategoriesAsync("Trendyol", new FakeCatalog { Categories = [new(1, null, "A"), new(2, 1, "B")] });

        await using var db = NewDb(tenant);
        var controller = new Pazaryeri.Api.Controllers.CatalogController(db, new RecordingQueue(db));
        var status = Assert.Single(await controller.Status(default));
        Assert.Equal((2, 1, false), (status.Categories, status.LeafCategories, status.SyncInFlight));
        Assert.Equal("Categories", Assert.Single(status.LastRuns).Kind);

        var started = (await controller.Sync("Trendyol", default)).Value!;
        Assert.True(started.Enqueued);
        Assert.False((await controller.Sync("Trendyol", default)).Value!.Enqueued); // zaten sırada
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>((await controller.Sync("N11", default)).Result);

        await using var db2 = NewDb(notConnected);
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>((await new Pazaryeri.Api.Controllers.CatalogController(db2, new RecordingQueue(db2)).Sync("Trendyol", default)).Result);
    }
}
