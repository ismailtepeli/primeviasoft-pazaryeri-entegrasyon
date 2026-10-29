using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Credentials;
using Pazaryeri.Infrastructure.Marketplace;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Security;

namespace Pazaryeri.Tests;

public class ConnectionTesterTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_ConnTest;Trusted_Connection=True;TrustServerCertificate=True";
    private static readonly AesGcmCredentialProtector Protector = new(RandomNumberGenerator.GetBytes(32));

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    /// <summary>Test için kanalın vereceği sonucu (hata ya da başarı) belirleyen sahte adapter.</summary>
    private sealed class FakeClient(Exception? failure) : IMarketplaceClient
    {
        public string Channel => "Trendyol";
        public Task TestConnectionAsync(CancellationToken ct = default) => failure is null ? Task.CompletedTask : Task.FromException(failure);
        public Task<BatchResult> SendProductsAsync(IReadOnlyList<ProductItem> p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BatchResult> UpdateStockAndPriceAsync(IReadOnlyList<StockPriceItem> i, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BatchStatus> GetBatchResultAsync(string b, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrderPage> GetOrdersAsync(OrderQuery q, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateOrderStatusAsync(OrderStatusUpdate u, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeFactory(Exception? failure) : IMarketplaceClientFactory
    {
        public MarketplaceCredentials? Seen { get; private set; }
        public IMarketplaceClient Create(MarketplaceCredentials credentials)
        {
            Seen = credentials;
            return credentials.Channel == "Trendyol" ? new FakeClient(failure) : throw new NotSupportedException();
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

    private static async Task<Guid> TenantWithTrendyolAsync(string name, ChannelEnvironment env = ChannelEnvironment.Live, bool saveCredentials = true)
    {
        await using var db0 = NewDb(null);
        var tenant = new Tenant { Name = name };
        db0.Tenants.Add(tenant);
        await db0.SaveChangesAsync();
        if (saveCredentials)
        {
            await using var db = NewDb(tenant.Id);
            await new ChannelCredentialService(db, new FixedTenant(tenant.Id), Protector).SaveAsync("Trendyol", env,
                new Dictionary<string, string> { ["sellerId"] = "12345", ["apiKey"] = "KEY-1234567890", ["apiSecret"] = "SECRET-ABCDEFGH" });
        }
        return tenant.Id;
    }

    private static ChannelConnectionTester Tester(AppDbContext db, Guid tenant, FakeFactory factory) =>
        new(db, new ChannelCredentialService(db, new FixedTenant(tenant), Protector), factory);

    [Fact]
    public async Task Basarili_test_kanali_bagli_yapar_ve_cozulmus_bilgiyle_cagirir()
    {
        var tenant = await TenantWithTrendyolAsync("Test A");
        await using var db = NewDb(tenant);
        db.ChannelConnections.Single().Status = ConnectionStatus.AuthError; // önceki hatalı durum
        await db.SaveChangesAsync();
        var factory = new FakeFactory(null);

        var result = await Tester(db, tenant, factory).TestAsync("Trendyol");

        Assert.True(result.Success);
        Assert.Equal("KEY-1234567890", factory.Seen!.ApiKey); // adapter şifresi çözülmüş bilgiyi alır
        Assert.Equal(ConnectionStatus.Connected, (await NewDb(tenant).ChannelConnections.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(401, "API Key veya API Secret hatalı")]
    [InlineData(403, "Satıcı ID")]
    public async Task Kimlik_reddi_kanali_hata_durumuna_alir(int code, string expectedInMessage)
    {
        var tenant = await TenantWithTrendyolAsync($"Test B{code}");
        await using var db = NewDb(tenant);

        var result = await Tester(db, tenant, new FakeFactory(new MarketplaceApiException("Trendyol", code, "{}"))).TestAsync("Trendyol");

        Assert.False(result.Success);
        Assert.Equal(code, result.StatusCode);
        Assert.Contains(expectedInMessage, result.Message);
        Assert.Equal(ConnectionStatus.AuthError, (await NewDb(tenant).ChannelConnections.SingleAsync()).Status);
    }

    [Fact]
    public async Task Stage_ortaminda_503_IP_yetkisi_olarak_aciklanir_ve_baglı_sayilmaz()
    {
        var tenant = await TenantWithTrendyolAsync("Test C", ChannelEnvironment.Staging);
        await using var db = NewDb(tenant);

        var result = await Tester(db, tenant, new FakeFactory(new MarketplaceApiException("Trendyol", 503, ""))).TestAsync("Trendyol");

        Assert.False(result.Success);
        Assert.Contains("IP", result.Message);
        Assert.Equal(ConnectionStatus.Unverified, (await NewDb(tenant).ChannelConnections.SingleAsync()).Status); // doğrulanamadı: bağlı sayılmaz
    }

    [Fact]
    public async Task Daha_once_dogrulanmis_baglanti_gecici_503le_bozulmaz()
    {
        var tenant = await TenantWithTrendyolAsync("Test C2");
        await using var db = NewDb(tenant);
        db.ChannelConnections.Single().Status = ConnectionStatus.Connected;
        await db.SaveChangesAsync();

        await Tester(db, tenant, new FakeFactory(new MarketplaceApiException("Trendyol", 503, ""))).TestAsync("Trendyol");

        Assert.Equal(ConnectionStatus.Connected, (await NewDb(tenant).ChannelConnections.SingleAsync()).Status);
    }

    [Fact]
    public async Task Canlida_503_gecici_hizmet_hatasi_olarak_aciklanir()
    {
        var tenant = await TenantWithTrendyolAsync("Test D");
        await using var db = NewDb(tenant);

        var result = await Tester(db, tenant, new FakeFactory(new MarketplaceApiException("Trendyol", 503, ""))).TestAsync("Trendyol");

        Assert.DoesNotContain("IP", result.Message);
        Assert.Contains("hizmet vermiyor", result.Message);
    }

    [Fact]
    public async Task Ag_hatasi_ulasilamadi_mesajina_cevrilir()
    {
        var tenant = await TenantWithTrendyolAsync("Test E");
        await using var db = NewDb(tenant);

        var result = await Tester(db, tenant, new FakeFactory(new HttpRequestException("dns"))).TestAsync("Trendyol");

        Assert.False(result.Success);
        Assert.Null(result.StatusCode);
        Assert.Contains("ulaşılamadı", result.Message);
    }

    [Fact]
    public async Task Kayitli_bilgi_yoksa_once_kaydetmesi_istenir()
    {
        var tenant = await TenantWithTrendyolAsync("Test F", saveCredentials: false);
        await using var db = NewDb(tenant);

        var result = await Tester(db, tenant, new FakeFactory(null)).TestAsync("Trendyol");

        Assert.False(result.Success);
        Assert.Contains("kaydedin", result.Message);
    }

    [Fact]
    public async Task Adapteri_olmayan_kanal_desteklenmiyor_der()
    {
        var tenant = await TenantWithTrendyolAsync("Test G", saveCredentials: false);
        await using var db = NewDb(tenant);
        var service = new ChannelCredentialService(db, new FixedTenant(tenant), Protector);
        await service.SaveAsync("N11", ChannelEnvironment.Live, new Dictionary<string, string> { ["appKey"] = "n11-key-0001", ["appSecret"] = "n11-secret-0002" });

        var result = await Tester(db, tenant, new FakeFactory(null)).TestAsync("N11");

        Assert.False(result.Success);
        Assert.Contains("desteklenmiyor", result.Message);
    }
}
