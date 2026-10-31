using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Application.Modules;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Marketplace.Trendyol;
using Pazaryeri.Infrastructure.Modules;
using Pazaryeri.Infrastructure.Orders;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Tests;

public class WebhookTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Webhook;Trusted_Connection=True;TrustServerCertificate=True";
    private const string Channel = "Trendyol";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private sealed class AmbientProvider : ITenantProvider
    {
        public Guid? TenantId => AmbientTenant.TenantId;
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
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, tenantId is null ? new AmbientProvider() : new FixedTenant(tenantId));

    public async Task InitializeAsync()
    {
        await using var db = NewDb(null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Basic(string secret) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{WebhookSecret.Username}:{secret}"));

    private static WebhookReceiver Receiver(AppDbContext db) =>
        new(db, new RecordingQueue(db), new ModuleService(db, new FixedTenant(null), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System), TimeProvider.System);

    /// <summary>Kiracı + kayıtlı webhook (gizli anahtarı döner).</summary>
    private static async Task<(Guid Tenant, string Secret)> SeedAsync(string name, bool ordersEnabled = true)
    {
        await using var root = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(null));
        var tenant = new Tenant { Name = name };
        root.Tenants.Add(tenant);
        await root.SaveChangesAsync();
        var secret = WebhookSecret.Generate();
        await using var db = NewDb(tenant.Id);
        db.WebhookEndpoints.Add(new WebhookEndpoint { Channel = Channel, SecretHash = WebhookSecret.Hash(secret) });
        if (!ordersEnabled) db.TenantModules.Add(new TenantModule { ModuleId = ModuleIds.Orders, Enabled = false });
        await db.SaveChangesAsync();
        return (tenant.Id, secret);
    }

    private static async Task<List<SyncJob>> JobsAsync(Guid tenant)
    {
        await using var db = NewDb(tenant);
        return await db.SyncJobs.Where(j => j.Type == SyncJobType.OrderSync).ToListAsync();
    }

    private static string Body(string package, string status) => $"{{\"shipmentPackageId\": {package}, \"shipmentPackageStatus\": \"{status}\", \"orderNumber\": \"ON1\"}}";

    // --- gövde ve anahtar yardımcıları ---

    [Theory]
    [InlineData("{\"shipmentPackageId\": 123, \"shipmentPackageStatus\": \"Picking\"}", "123", "Picking")]
    [InlineData("{\"packageId\": \"P9\", \"status\": \"Shipped\"}", "P9", "Shipped")]
    [InlineData("{\"data\": {\"shipmentPackageId\": 7, \"status\": \"Created\"}}", "7", "Created")]
    [InlineData("[{\"shipmentPackageId\": 8, \"status\": \"Invoiced\"}]", "8", "Invoiced")]
    [InlineData("{\"id\": 5}", "5", null)]
    [InlineData("{}", null, null)]
    [InlineData("kırık json", null, null)]
    [InlineData("", null, null)]
    [InlineData("\"sadece metin\"", null, null)]
    public void Govde_hoshgoruyle_okunur(string body, string? package, string? status)
    {
        var p = WebhookPayload.Read(body);
        Assert.Equal((package, status), (p.PackageId, p.Status));
    }

    [Fact]
    public void Olay_anahtari_yalniz_paket_ve_durum_birlikte_bilinirse_vardir()
    {
        Assert.Equal("1:Created", new WebhookPayload("1", "Created").EventKey);
        Assert.Null(new WebhookPayload("1", null).EventKey);
        Assert.Null(new WebhookPayload(null, "Created").EventKey);
        Assert.Null(WebhookPayload.Read(new string('x', 10)).EventKey);
        Assert.Null(WebhookPayload.Read($"{{\"shipmentPackageId\": \"{new string('9', 101)}\", \"status\": \"Created\"}}").EventKey); // aşırı uzun kimlik yok sayılır
    }

    [Fact]
    public void Gizli_anahtar_uretilir_ozetlenir_dogrulanir_ve_basliklardan_cikarilir()
    {
        var a = WebhookSecret.Generate();
        Assert.NotEqual(a, WebhookSecret.Generate());
        Assert.True(a.Length >= 40 && a.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        var hash = WebhookSecret.Hash(a);
        Assert.Equal(64, hash.Length);
        Assert.NotEqual(a, hash);
        Assert.True(WebhookSecret.Verify(a, hash));
        Assert.False(WebhookSecret.Verify(a + "x", hash));
        Assert.False(WebhookSecret.Verify(null, hash));
        Assert.False(WebhookSecret.Verify("", hash));
        Assert.False(WebhookSecret.Verify(a, ""));

        Assert.Equal("gizli", WebhookSecret.Extract(Basic("gizli"), null));
        Assert.Equal("gizli:iki", WebhookSecret.Extract("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("u:gizli:iki")), null)); // parola iki nokta içerebilir
        Assert.Equal("tok", WebhookSecret.Extract("Bearer tok", null));
        Assert.Equal("anahtar", WebhookSecret.Extract(null, " anahtar "));
        Assert.Equal("anahtar", WebhookSecret.Extract(Basic("başka"), "anahtar"));   // x-api-key öncelikli
        Assert.Null(WebhookSecret.Extract(null, null));
        Assert.Null(WebhookSecret.Extract("Basic %%%", null));
        Assert.Null(WebhookSecret.Extract("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("iki-nokta-yok")), null));
        Assert.Null(WebhookSecret.Extract("Digest abc", null));
        Assert.Null(WebhookSecret.Extract("Basic", null));
    }

    // --- alıcı ---

    [Fact]
    public async Task Gecerli_bildirim_olayi_kaydeder_ve_siparis_senkronunu_kuyruga_alir()
    {
        var (tenant, secret) = await SeedAsync("Alici");
        await using var db = NewDb(null);
        var outcome = await Receiver(db).ReceiveAsync(tenant, Channel, Basic(secret), null, Body("100", "Created"), default);
        Assert.Equal(WebhookOutcome.Accepted, outcome);

        await using var check = NewDb(tenant);
        var ev = await check.WebhookEvents.SingleAsync();
        Assert.Equal(("Trendyol", "100:Created", "100", "Created"), (ev.Channel, ev.EventKey, ev.PackageId, ev.Status));
        var job = Assert.Single(await JobsAsync(tenant));
        Assert.Equal((Channel, tenant), (job.Channel, job.TenantId));
        Assert.NotNull((await check.WebhookEndpoints.SingleAsync()).LastEventAt);
    }

    [Fact]
    public async Task Ayni_paket_ve_durum_tekrar_gelirse_islem_yapilmaz_farkli_durum_yeni_olaydir()
    {
        var (tenant, secret) = await SeedAsync("Tekrar");
        await using var db = NewDb(null);
        var r = Receiver(db);
        Assert.Equal(WebhookOutcome.Accepted, await r.ReceiveAsync(tenant, Channel, Basic(secret), null, Body("100", "Created"), default));
        // iş çalışıp bitsin; tekrar denemesi yeni iş doğurmamalı
        await using (var admin = NewDb(tenant)) { foreach (var j in await admin.SyncJobs.ToListAsync()) j.Status = SyncJobStatus.Succeeded; await admin.SaveChangesAsync(); }

        Assert.Equal(WebhookOutcome.Duplicate, await r.ReceiveAsync(tenant, Channel, Basic(secret), null, Body("100", "Created"), default));
        Assert.Single(await JobsAsync(tenant));

        Assert.Equal(WebhookOutcome.Accepted, await r.ReceiveAsync(tenant, Channel, Basic(secret), null, Body("100", "Picking"), default));   // aynı paket, yeni durum
        Assert.Equal(WebhookOutcome.Accepted, await r.ReceiveAsync(tenant, Channel, Basic(secret), null, Body("101", "Created"), default));   // başka paket
        await using var check = NewDb(tenant);
        Assert.Equal(3, await check.WebhookEvents.CountAsync());
    }

    [Fact]
    public async Task Birden_cok_bildirim_tek_bekleyen_senkrona_birlesir_calisan_senkron_varsa_yenisi_alinir()
    {
        var (tenant, secret) = await SeedAsync("Birlesme");
        await using var db = NewDb(null);
        var r = Receiver(db);
        for (var i = 0; i < 5; i++) await r.ReceiveAsync(tenant, Channel, Basic(secret), null, Body($"{200 + i}", "Created"), default);
        Assert.Single(await JobsAsync(tenant));                        // 5 bildirim, bekleyen tek iş

        await using (var admin = NewDb(tenant)) { (await admin.SyncJobs.SingleAsync()).Status = SyncJobStatus.Running; await admin.SaveChangesAsync(); }
        await r.ReceiveAsync(tenant, Channel, Basic(secret), null, Body("300", "Created"), default);
        Assert.Equal(2, (await JobsAsync(tenant)).Count);              // çalışan senkron okuduktan sonra gelmiş olabilir: yeni iş
    }

    [Fact]
    public async Task Govdesi_okunamayan_bildirim_yine_de_senkronu_tetikler_ama_tekrar_engellenmez()
    {
        var (tenant, secret) = await SeedAsync("Bozuk");
        await using var db = NewDb(null);
        var r = Receiver(db);
        Assert.Equal(WebhookOutcome.Accepted, await r.ReceiveAsync(tenant, Channel, Basic(secret), null, "kırık", default));
        Assert.Equal(WebhookOutcome.Accepted, await r.ReceiveAsync(tenant, Channel, Basic(secret), null, null, default));
        await using var check = NewDb(tenant);
        Assert.Equal(0, await check.WebhookEvents.CountAsync());       // anahtarsız olay kaydedilmez
        Assert.Single(await JobsAsync(tenant));
    }

    [Fact]
    public async Task Yanlis_eksik_ya_da_baska_kiracinin_anahtari_ve_kayitsiz_kanal_ayni_sekilde_reddedilir()
    {
        var (t1, s1) = await SeedAsync("Bir");
        var (t2, s2) = await SeedAsync("Iki");
        await using var db = NewDb(null);
        var r = Receiver(db);
        Assert.Equal(WebhookOutcome.Unauthorized, await r.ReceiveAsync(t1, Channel, Basic("yanlis"), null, Body("1", "Created"), default));
        Assert.Equal(WebhookOutcome.Unauthorized, await r.ReceiveAsync(t1, Channel, null, null, Body("1", "Created"), default));
        Assert.Equal(WebhookOutcome.Unauthorized, await r.ReceiveAsync(t1, Channel, Basic(s2), null, Body("1", "Created"), default));       // diğer kiracının anahtarı
        Assert.Equal(WebhookOutcome.Unauthorized, await r.ReceiveAsync(Guid.NewGuid(), Channel, Basic(s1), null, Body("1", "Created"), default)); // bilinmeyen kiracı
        Assert.Equal(WebhookOutcome.Unauthorized, await r.ReceiveAsync(t1, "N11", Basic(s1), null, Body("1", "Created"), default));            // kayıtsız kanal
        Assert.Empty(await JobsAsync(t1));
        await using var check = NewDb(t1);
        Assert.Equal(0, await check.WebhookEvents.CountAsync());

        // doğru anahtar, x-api-key ile de kabul edilir
        Assert.Equal(WebhookOutcome.Accepted, await r.ReceiveAsync(t1, Channel, null, s1, Body("1", "Created"), default));
    }

    [Fact]
    public async Task Siparisler_modulu_kapaliysa_bildirim_yoksayilir_ve_is_acilmaz()
    {
        var (tenant, secret) = await SeedAsync("Kapali", ordersEnabled: false);
        await using var db = NewDb(null);
        Assert.Equal(WebhookOutcome.Ignored, await Receiver(db).ReceiveAsync(tenant, Channel, Basic(secret), null, Body("1", "Created"), default));
        Assert.Empty(await JobsAsync(tenant));
    }

    [Fact]
    public async Task Alici_ucu_beklenen_durum_kodlarini_doner()
    {
        var (tenant, secret) = await SeedAsync("Uc");
        await using var db = NewDb(null);
        var c = new WebhooksController(Receiver(db))
        {
            ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() },
        };
        c.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(Body("500", "Created")));
        c.Request.Headers.Authorization = Basic("yanlis");
        Assert.IsType<UnauthorizedResult>(await c.Receive(tenant, default));

        c.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(Body("500", "Created")));
        c.Request.Headers.Authorization = Basic(secret);
        Assert.IsType<OkObjectResult>(await c.Receive(tenant, default));
        c.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(Body("500", "Created")));
        var again = Assert.IsType<OkObjectResult>(await c.Receive(tenant, default));    // tekrar da 200: kanal yeniden denemez
        Assert.Contains("Duplicate", again.Value!.ToString());
    }

    // --- kayıt ---

    private sealed class FakeCreds(bool present = true) : IChannelCredentialService
    {
        public Task<CredentialView> SaveAsync(string channel, ChannelEnvironment environment, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CredentialView?> GetMaskedAsync(string channel, ChannelEnvironment environment, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MarketplaceCredentials?> GetForClientAsync(string channel, CancellationToken ct = default) =>
            Task.FromResult<MarketplaceCredentials?>(present ? new(channel, "1", "k", "s", "Acme") : null);
    }

    private sealed class FakeClient(bool fail = false) : IMarketplaceClient
    {
        public List<WebhookRegistration> Registrations { get; } = [];
        public string Channel => "Trendyol";
        public Task<BatchResult> SendProductsAsync(IReadOnlyList<ProductItem> products, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BatchResult> UpdateStockAndPriceAsync(IReadOnlyList<StockPriceItem> items, CancellationToken ct = default) => throw new NotSupportedException();
        public Task TestConnectionAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BatchStatus> GetBatchResultAsync(string batchId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrderPage> GetOrdersAsync(OrderQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateOrderStatusAsync(OrderStatusUpdate update, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WebhookRegistrationResult> CreateWebhookAsync(WebhookRegistration registration, CancellationToken ct = default)
        {
            if (fail) throw new MarketplaceApiException("Trendyol", 400, "geçersiz");
            Registrations.Add(registration);
            return Task.FromResult(new WebhookRegistrationResult("wh-" + Registrations.Count));
        }
    }

    private sealed class FakeFactory(FakeClient client) : IMarketplaceClientFactory
    {
        public IMarketplaceClient Create(MarketplaceCredentials credentials) => client;
    }

    private static async Task<Guid> TenantOnlyAsync(string name)
    {
        await using var root = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(null));
        var t = new Tenant { Name = name };
        root.Tenants.Add(t);
        await root.SaveChangesAsync();
        return t.Id;
    }

    private static WebhookRegistrationService Registration(AppDbContext db, Guid tenant, FakeClient client, string? baseUrl = "https://api.firma.test/", bool creds = true) =>
        new(db, new FixedTenant(tenant), new FakeCreds(creds), new FakeFactory(client), new WebhookOptions(baseUrl), TimeProvider.System);

    [Fact]
    public async Task Kayit_gizli_anahtar_uretir_kanala_gonderir_ozetini_saklar_ve_yeniden_kayit_anahtari_degistirir()
    {
        var tenant = await TenantOnlyAsync("Kayit");
        await using var db = NewDb(tenant);
        var client = new FakeClient();
        var svc = Registration(db, tenant, client);

        Assert.False((await svc.StatusAsync(Channel, default)).Registered);
        var status = await svc.RegisterAsync(Channel, default);
        Assert.True(status.Registered);
        Assert.Equal($"https://api.firma.test/webhooks/trendyol/{tenant}", status.Url);

        var sent = Assert.Single(client.Registrations);
        Assert.Equal(status.Url, sent.Url);
        Assert.Equal(WebhookSecret.Username, sent.Username);
        Assert.Contains("Created", sent.Statuses);
        await using (var check = NewDb(tenant))
        {
            var e = await check.WebhookEndpoints.SingleAsync();
            Assert.Equal("wh-1", e.ExternalId);
            Assert.NotEqual(sent.Password, e.SecretHash);                          // düz anahtar saklanmaz
            Assert.True(WebhookSecret.Verify(sent.Password, e.SecretHash));
        }

        // kanal gönderdiği anahtarla çağırır ve kabul edilir
        await using (var rcv = NewDb(null))
            Assert.Equal(WebhookOutcome.Accepted, await Receiver(rcv).ReceiveAsync(tenant, Channel, Basic(sent.Password), null, Body("1", "Created"), default));

        // yeniden kayıt: yeni anahtar, eskisi geçersiz
        await svc.RegisterAsync(Channel, default);
        Assert.Equal(2, client.Registrations.Count);
        await using var rcv2 = NewDb(null);
        Assert.Equal(WebhookOutcome.Unauthorized, await Receiver(rcv2).ReceiveAsync(tenant, Channel, Basic(sent.Password), null, Body("2", "Created"), default));
        Assert.Equal(WebhookOutcome.Accepted, await Receiver(rcv2).ReceiveAsync(tenant, Channel, Basic(client.Registrations[1].Password), null, Body("2", "Created"), default));
    }

    [Fact]
    public async Task Kayit_adres_kimlik_bilgisi_ya_da_kanal_hatasinda_mevcut_anahtari_bozmaz()
    {
        var tenant = await TenantOnlyAsync("KayitHata");
        await using var db = NewDb(tenant);
        var good = new FakeClient();
        await Registration(db, tenant, good).RegisterAsync(Channel, default);
        var firstPassword = good.Registrations.Single().Password;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Registration(db, tenant, good, baseUrl: null).RegisterAsync(Channel, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Registration(db, tenant, good, baseUrl: " ").RegisterAsync(Channel, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Registration(db, tenant, good, creds: false).RegisterAsync(Channel, default));
        await Assert.ThrowsAsync<MarketplaceApiException>(() => Registration(db, tenant, new FakeClient(fail: true)).RegisterAsync(Channel, default));

        await using var check = NewDb(tenant);
        Assert.True(WebhookSecret.Verify(firstPassword, (await check.WebhookEndpoints.SingleAsync()).SecretHash)); // kanal reddedince eski anahtar geçerli kalır
    }

    [Fact]
    public async Task Yonetici_ucu_kayit_ve_durum_doner_desteklenmeyen_kanal_404_hatalar_409_502()
    {
        var tenant = await TenantOnlyAsync("YoneticiUc");
        await using var db = NewDb(tenant);
        var client = new FakeClient();
        var c = new WebhooksAdminController(Registration(db, tenant, client));

        Assert.IsType<NotFoundObjectResult>((await c.Status("N11", default)).Result);
        Assert.IsType<NotFoundObjectResult>((await c.Register("N11", default)).Result);
        Assert.False((await c.Status("trendyol", default)).Value!.Registered);                // büyük/küçük harf önemsiz
        Assert.True((await c.Register("Trendyol", default)).Value!.Registered);

        var noUrl = new WebhooksAdminController(Registration(db, tenant, client, baseUrl: null));
        Assert.IsType<ConflictObjectResult>((await noUrl.Register("Trendyol", default)).Result);
        var rejected = new WebhooksAdminController(Registration(db, tenant, new FakeClient(fail: true)));
        Assert.Equal(502, Assert.IsType<ObjectResult>((await rejected.Register("Trendyol", default)).Result).StatusCode);
    }

    // --- Trendyol istemcisi ---

    private sealed class CapturingHandler(string response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Trendyol_istemcisi_webhook_kaydini_belgedeki_uc_nokta_ve_govdeyle_gonderir()
    {
        var handler = new CapturingHandler("{\"id\":\"abc-123\"}");
        var client = new TrendyolClient(new HttpClient(handler), new MarketplaceCredentials("Trendyol", "12345", "key", "secret", "Acme"));
        var result = await client.CreateWebhookAsync(new WebhookRegistration("https://x.test/webhooks/trendyol/t1", "pazaryeri", "pw", ["Created", "Shipped"]));

        Assert.Equal("abc-123", result.WebhookId);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.EndsWith("/integration/webhook/sellers/12345/webhooks", handler.Request.RequestUri!.AbsolutePath);
        Assert.StartsWith("Basic ", handler.Request.Headers.Authorization!.ToString());
        Assert.Contains("\"authenticationType\":\"BASIC_AUTHENTICATION\"", handler.Body);
        Assert.Contains("\"url\":\"https://x.test/webhooks/trendyol/t1\"", handler.Body);
        Assert.Contains("\"username\":\"pazaryeri\"", handler.Body);
        Assert.Contains("\"subscribedStatuses\":[\"Created\",\"Shipped\"]", handler.Body);

        var empty = new TrendyolClient(new HttpClient(new CapturingHandler("{}")), new MarketplaceCredentials("Trendyol", "1", "k", "s", "A"));
        Assert.Null((await empty.CreateWebhookAsync(new WebhookRegistration("u", "n", "p", []))).WebhookId);
    }

    [Fact]
    public async Task Webhook_desteklemeyen_istemci_varsayilan_olarak_hata_verir()
    {
        IMarketplaceClient plain = new FakeClientWithoutWebhook();
        await Assert.ThrowsAsync<NotSupportedException>(() => plain.CreateWebhookAsync(new WebhookRegistration("u", "n", "p", [])));
    }

    private sealed class FakeClientWithoutWebhook : IMarketplaceClient
    {
        public string Channel => "Amazon";
        public Task<BatchResult> SendProductsAsync(IReadOnlyList<ProductItem> products, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BatchResult> UpdateStockAndPriceAsync(IReadOnlyList<StockPriceItem> items, CancellationToken ct = default) => throw new NotSupportedException();
        public Task TestConnectionAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BatchStatus> GetBatchResultAsync(string batchId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrderPage> GetOrdersAsync(OrderQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateOrderStatusAsync(OrderStatusUpdate update, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
