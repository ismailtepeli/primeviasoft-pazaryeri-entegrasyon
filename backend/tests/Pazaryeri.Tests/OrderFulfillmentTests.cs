using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Marketplace;
using Pazaryeri.Infrastructure.Modules;
using Pazaryeri.Infrastructure.Orders;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Stock;
using Pazaryeri.Sandbox;

namespace Pazaryeri.Tests;

/// <summary>
/// Webhook alımı, sipariş senkronu ve durumun kanala geri yazımı: gerçek TrendyolClient, yerel Trendyol sandbox'ı ve LocalDB ile uçtan uca.
/// </summary>
public class OrderFulfillmentTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Fulfillment;Trusted_Connection=True;TrustServerCertificate=True";
    private const string Channel = "Trendyol";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private sealed class AmbientProvider : ITenantProvider
    {
        public Guid? TenantId => AmbientTenant.TenantId;
    }

    private sealed class RecordingQueue(Guid tenant) : IJobQueue
    {
        public List<SyncJobType> Enqueued { get; } = [];
        public async Task<SyncJob> EnqueueAsync(SyncJobType type, string channel, string? batchId = null, CancellationToken ct = default, string? payload = null)
        {
            Enqueued.Add(type);
            await using var db = NewDb(tenant);
            var job = new SyncJob { Type = type, Channel = channel };
            db.SyncJobs.Add(job);
            await db.SaveChangesAsync(ct);
            return job;
        }
    }

    private sealed class SandboxCreds : IChannelCredentialService
    {
        public Task<CredentialView> SaveAsync(string channel, ChannelEnvironment environment, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CredentialView?> GetMaskedAsync(string channel, ChannelEnvironment environment, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MarketplaceCredentials?> GetForClientAsync(string channel, CancellationToken ct = default) =>
            Task.FromResult<MarketplaceCredentials?>(new(channel, "12345", "key", "secret", "Acme"));
    }

    private sealed class PlainHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private WebApplication _sandbox = null!;
    private WebApplication _hook = null!;
    private string _sandboxUrl = null!;
    private string _hookUrl = null!;
    private readonly HttpClient _http = new();
    private readonly List<(string? Auth, string Body)> _received = [];

    private static AppDbContext NewDb(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    /// <summary>Webhook alıcısı kiracıyı adresten alır (AmbientTenant); veritabanı bağlamı onu izler.</summary>
    private static AppDbContext NewAmbientDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new AmbientProvider());

    private static string UrlOf(WebApplication app) =>
        app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

    public async Task InitializeAsync()
    {
        await using (var db = NewDb(null))
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
        }

        _sandbox = SandboxApp.Build(["--urls", "http://127.0.0.1:0"]);
        await _sandbox.StartAsync();
        _sandboxUrl = UrlOf(_sandbox);

        // Webhook alıcısının yerine geçen uç: sandbox'ın gönderdiği isteği olduğu gibi tutar.
        var hookBuilder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0"]);
        _hook = hookBuilder.Build();
        _hook.MapPost("/hook", async (HttpRequest r) =>
        {
            using var reader = new StreamReader(r.Body, Encoding.UTF8);
            var text = await reader.ReadToEndAsync();
            lock (_received) _received.Add((r.Headers.Authorization.ToString(), text));
            return Results.Ok();
        });
        await _hook.StartAsync();
        _hookUrl = UrlOf(_hook) + "/hook";
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _sandbox.DisposeAsync();
        await _hook.DisposeAsync();
    }

    private IMarketplaceClientFactory Factory() => new MarketplaceClientFactory(new PlainHttpFactory(),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Marketplace:Sandbox:Trendyol"] = _sandboxUrl }).Build());

    private static async Task<Guid> SeedTenantAsync(string name)
    {
        await using var root = NewDb(null);
        var tenant = new Tenant { Name = name };
        root.Tenants.Add(tenant);
        await root.SaveChangesAsync();
        await using var db = NewDb(tenant.Id);
        db.Products.Add(new Product { Name = "P", Barcode = "BC-1", Price = 1, Stock = 100 });
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private Task AdminAsync(string path, object body) => _http.PostAsJsonAsync($"{_sandboxUrl}{path}", body);

    private async Task SyncAsync(Guid tenant, RecordingQueue queue)
    {
        await using var db = NewDb(tenant);
        var ingest = new OrderIngestService(db, new StockPoolService(db, queue));
        var handler = new OrderSyncHandler(db, new SandboxCreds(), Factory(), ingest, TimeProvider.System);
        await handler.HandleAsync(new SyncJob { Type = SyncJobType.OrderSync, Channel = Channel }, default);
    }

    private OrderFulfillmentService Fulfillment(AppDbContext db, RecordingQueue queue) => new(db, new SandboxCreds(), Factory(), queue);

    private static async Task<Order> LoadOrderAsync(Guid tenant)
    {
        await using var db = NewDb(tenant);
        return await db.Orders.AsNoTracking().Include(o => o.Packages).ThenInclude(p => p.Lines).SingleAsync();
    }

    private async Task<string> SandboxStatusAsync(long packageId)
    {
        var state = await _http.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>($"{_sandboxUrl}/__sandbox/state");
        return state!["orders"]!.AsArray().Single(o => o!["id"]!.GetValue<long>() == packageId)!["status"]!.ToString();
    }

    [Fact]
    public async Task Siparis_hazirlaniyor_ve_faturalandi_olarak_kanala_yazilir_kargo_hareketi_senkronla_gelir()
    {
        var tenant = await SeedTenantAsync("Akis");
        var queue = new RecordingQueue(tenant);
        await AdminAsync("/__sandbox/orders", new { count = 1, barcode = "BC-1", price = 50 });
        await SyncAsync(tenant, queue);
        var order = await LoadOrderAsync(tenant);
        Assert.Equal((OrderStatus.New, "Created"), (order.Status, order.Packages.Single().ChannelStatus));
        var packageId = long.Parse(order.Packages.Single().PackageId);

        FulfillmentResult picking;
        await using (var db = NewDb(tenant)) picking = await Fulfillment(db, queue).ActAsync(order.Id, FulfillmentAction.Picking, null, default);
        Assert.Equal((1, "Preparing", "Picking"), (picking.Updated, picking.Status, picking.ChannelStatus));
        Assert.Equal("Picking", await SandboxStatusAsync(packageId));
        Assert.Contains(SyncJobType.OrderSync, queue.Enqueued);

        await using (var db = NewDb(tenant)) await Fulfillment(db, queue).ActAsync(order.Id, FulfillmentAction.Invoice, " FTR2026000000001 ", default);
        Assert.Equal("Invoiced", await SandboxStatusAsync(packageId));
        Assert.Equal((OrderStatus.Preparing, "Invoiced"), ((await LoadOrderAsync(tenant)).Status, (await LoadOrderAsync(tenant)).Packages.Single().ChannelStatus));

        // Kargo firması paketi alır: bunu satıcı değil kanal bildirir; sipariş senkronu yerelde Shipped yapar.
        using (var r = await _http.PostAsJsonAsync($"{_sandboxUrl}/__sandbox/orders/{packageId}/carrier", new { status = "Shipped" })) r.EnsureSuccessStatusCode();
        await SyncAsync(tenant, queue);
        Assert.Equal((OrderStatus.Shipped, "Shipped"), ((await LoadOrderAsync(tenant)).Status, (await LoadOrderAsync(tenant)).Packages.Single().ChannelStatus));
    }

    [Fact]
    public async Task Kanalin_reddettigi_durum_yerelde_ilerlemez_ve_502_olur()
    {
        var tenant = await SeedTenantAsync("Red");
        var queue = new RecordingQueue(tenant);
        await AdminAsync("/__sandbox/orders", new { count = 1, barcode = "BC-1" });
        await SyncAsync(tenant, queue);
        var order = await LoadOrderAsync(tenant);
        await AdminAsync("/__sandbox/fail", new { count = 1, status = 400 });

        var ex = await Assert.ThrowsAsync<StoreException>(async () =>
        {
            await using var db = NewDb(tenant);
            await Fulfillment(db, queue).ActAsync(order.Id, FulfillmentAction.Picking, null, default);
        });

        Assert.Equal(502, ex.Status);
        var after = await LoadOrderAsync(tenant);
        Assert.Equal((OrderStatus.New, "Created"), (after.Status, after.Packages.Single().ChannelStatus));
    }

    [Fact]
    public async Task Yanlis_sirada_ya_da_numarasiz_istek_kanala_gitmeden_reddedilir()
    {
        var tenant = await SeedTenantAsync("Kural");
        var queue = new RecordingQueue(tenant);
        await AdminAsync("/__sandbox/orders", new { count = 1, barcode = "BC-1" });
        await SyncAsync(tenant, queue);
        var order = await LoadOrderAsync(tenant);
        var before = (await _http.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>($"{_sandboxUrl}/__sandbox/state"))!["requests"]!.AsArray().Count;

        await using (var db = NewDb(tenant))
        {
            var early = await Assert.ThrowsAsync<StoreException>(() => Fulfillment(db, queue).ActAsync(order.Id, FulfillmentAction.Invoice, "FTR1", default));
            Assert.Equal(409, early.Status);
            var noNumber = await Assert.ThrowsAsync<StoreException>(() => Fulfillment(db, queue).ActAsync(order.Id, FulfillmentAction.Invoice, "  ", default));
            Assert.Equal(400, noNumber.Status);
            var missing = await Assert.ThrowsAsync<StoreException>(() => Fulfillment(db, queue).ActAsync(Guid.NewGuid(), FulfillmentAction.Picking, null, default));
            Assert.Equal(404, missing.Status);
        }

        var requests = (await _http.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>($"{_sandboxUrl}/__sandbox/state"))!["requests"]!.AsArray();
        Assert.DoesNotContain(requests.Skip(before), r => r!.ToString().StartsWith("PUT"));
    }

    [Fact]
    public async Task Sandbox_webhook_bildirimi_alicidan_gecer_senkronu_tetikler_ve_tekrari_yinelenen_sayilir()
    {
        var tenant = await SeedTenantAsync("Hook");
        var secret = WebhookSecret.Generate();
        await using (var db = NewDb(tenant))
        {
            db.WebhookEndpoints.Add(new WebhookEndpoint { Channel = Channel, SecretHash = WebhookSecret.Hash(secret) });
            await db.SaveChangesAsync();
        }
        await AdminAsync("/__sandbox/orders", new { count = 1, barcode = "BC-1" });

        for (var i = 0; i < 2; i++) // Trendyol aynı bildirimi tekrar gönderebilir
        {
            using var sent = await _http.PostAsJsonAsync($"{_sandboxUrl}/__sandbox/webhooks/send", new { url = _hookUrl, username = WebhookSecret.Username, password = secret });
            sent.EnsureSuccessStatusCode();
        }
        Assert.Equal(2, _received.Count);

        var queue = new RecordingQueue(tenant);
        var outcomes = new List<WebhookOutcome>();
        foreach (var (auth, body) in _received)
        {
            await using var db = NewAmbientDb();
            var receiver = new WebhookReceiver(db, queue, new ModuleService(db, new FixedTenant(null), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System), TimeProvider.System);
            outcomes.Add(await receiver.ReceiveAsync(tenant, Channel, auth, null, body, default));
        }
        Assert.Equal([WebhookOutcome.Accepted, WebhookOutcome.Duplicate], outcomes);
        Assert.Equal([SyncJobType.OrderSync], queue.Enqueued);

        // Tetiklenen senkron siparişi getirir.
        await SyncAsync(tenant, queue);
        Assert.Equal(OrderStatus.New, (await LoadOrderAsync(tenant)).Status);

        // Yanlış anahtar reddedilir.
        await using var wrong = NewAmbientDb();
        var rec = new WebhookReceiver(wrong, queue, new ModuleService(wrong, new FixedTenant(null), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System), TimeProvider.System);
        Assert.Equal(WebhookOutcome.Unauthorized, await rec.ReceiveAsync(tenant, Channel, "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("pazaryeri:yanlis")), null, _received[0].Body, default));
    }
}
