using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Amazon;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Amazon;
using Pazaryeri.Infrastructure.Credentials;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Security;

namespace Pazaryeri.Tests;

public class AmazonOAuthTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Amazon;Trusted_Connection=True;TrustServerCertificate=True";
    private static readonly AesGcmCredentialProtector Protector = new(RandomNumberGenerator.GetBytes(32));

    private sealed class AmbientProvider(Guid? fixedId = null) : ITenantProvider
    {
        public Guid? TenantId => fixedId ?? AmbientTenant.TenantId;
    }

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeLwa(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public List<(string Url, string Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls.Add((r.RequestUri!.ToString(), await r.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(code) { Content = new StringContent(body) };
        }
    }

    private static AmazonLwaOptions Options(DateTime? issued = null) => new()
    {
        ClientId = "amzn1.application-oa2-client.abc", ClientSecret = "client-secret-xyz", ApplicationId = "amzn1.sp.solution.123",
        RedirectUri = "http://localhost:5284/api/oauth/amazon/callback", Draft = true, ClientSecretIssuedAt = issued,
    };

    private static AppDbContext NewDb(Guid? tenantId = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new AmbientProvider(tenantId));

    private static AmazonOAuthService Service(AppDbContext db, AmazonLwaOptions options, FakeLwa lwaHandler, FakeTime? time = null)
    {
        var tenant = new AmbientProvider();
        var lwa = new AmazonLwaClient(new HttpClient(lwaHandler), Microsoft.Extensions.Options.Options.Create(options));
        return new AmazonOAuthService(Microsoft.Extensions.Options.Options.Create(options), Protector,
            new ChannelCredentialService(db, tenant, Protector), lwa, db, tenant, time ?? new FakeTime());
    }

    private static FakeLwa OkLwa() => new(HttpStatusCode.OK, """{"access_token":"at","refresh_token":"Atzr|REFRESH-TOKEN-1234","token_type":"bearer"}""");

    public async Task InitializeAsync()
    {
        await using var db = NewDb();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> NewTenantAsync(string name)
    {
        await using var db = NewDb();
        var t = new Tenant { Name = name };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private static string StateOf(string url) => Uri.UnescapeDataString(url.Split("state=")[1].Split('&')[0]);

    [Fact]
    public async Task Yetki_adresi_Seller_Central_onay_sayfasina_gider_ve_beta_isaretlenir()
    {
        var tenant = await NewTenantAsync("Amz A");
        using var _ = AmbientTenant.Use(tenant);
        await using var db = NewDb();

        var url = Service(db, Options(), OkLwa()).BuildAuthorizationUrl();

        Assert.StartsWith("https://sellercentral.amazon.com.tr/apps/authorize/consent?application_id=amzn1.sp.solution.123&state=", url);
        Assert.EndsWith("&version=beta", url);
        Assert.DoesNotContain("client-secret-xyz", url);
    }

    [Fact]
    public async Task Ayarlar_tanimsizsa_yetki_baslatilmaz()
    {
        var tenant = await NewTenantAsync("Amz B");
        using var _ = AmbientTenant.Use(tenant);
        await using var db = NewDb();

        Assert.Throws<AmazonOAuthException>(() => Service(db, new AmazonLwaOptions(), OkLwa()).BuildAuthorizationUrl());
        Assert.False((await Service(db, new AmazonLwaOptions(), OkLwa()).GetStatusAsync()).Configured);
    }

    [Fact]
    public async Task Callback_kodu_takas_eder_refresh_tokeni_sifreli_saklar_ve_kanali_baglar()
    {
        var tenant = await NewTenantAsync("Amz C");
        string state;
        using (AmbientTenant.Use(tenant))
        {
            await using var db = NewDb();
            state = StateOf(Service(db, Options(), OkLwa()).BuildAuthorizationUrl());
        }

        // Callback oturumsuz gelir: kiracı yalnızca state'ten bilinir (ambient yok).
        var lwa = OkLwa();
        await using (var db = NewDb())
            await Service(db, Options(), lwa).CompleteAsync("ANxxxx-code", state, "A1SELLER");

        var call = Assert.Single(lwa.Calls);
        Assert.Equal("https://api.amazon.com/auth/o2/token", call.Url);
        Assert.Contains("grant_type=authorization_code", call.Body);
        Assert.Contains("code=ANxxxx-code", call.Body);
        Assert.Contains("client_id=amzn1.application-oa2-client.abc", call.Body);

        using var __ = AmbientTenant.Use(tenant);
        await using var check = NewDb();
        var row = await check.ChannelCredentials.SingleAsync();
        Assert.DoesNotContain("REFRESH-TOKEN-1234", row.EncryptedSecrets + row.PublicFieldsJson + row.SecretHintsJson); // düz metin yok
        Assert.Equal(ConnectionStatus.Connected, (await check.ChannelConnections.SingleAsync()).Status);

        var creds = await new ChannelCredentialService(check, new AmbientProvider(), Protector).GetForClientAsync("Amazon");
        Assert.Equal(("A1SELLER", "Atzr|REFRESH-TOKEN-1234"), (creds!.SellerId, creds.ApiSecret));

        var status = await Service(check, Options(), OkLwa()).GetStatusAsync();
        Assert.True(status.Authorized);
        Assert.Equal("A1SELLER", status.SellingPartnerId);
    }

    [Fact]
    public async Task Kurcalanmis_veya_sahte_state_reddedilir_ve_Amazona_gidilmez()
    {
        var lwa = OkLwa();
        await using var db = NewDb();
        var service = Service(db, Options(), lwa);

        await Assert.ThrowsAsync<AmazonOAuthException>(() => service.CompleteAsync("code", "v1:sahte", "A1"));
        await Assert.ThrowsAsync<AmazonOAuthException>(() => service.CompleteAsync("code", Protector.Protect("""{"TenantId":"00000000-0000-0000-0000-000000000001"}""", "baska-baglam"), "A1"));
        Assert.Empty(lwa.Calls);
    }

    [Fact]
    public async Task Suresi_dolan_state_reddedilir()
    {
        var tenant = await NewTenantAsync("Amz D");
        var time = new FakeTime();
        string state;
        using (AmbientTenant.Use(tenant))
        {
            await using var db = NewDb();
            state = StateOf(Service(db, Options(), OkLwa(), time).BuildAuthorizationUrl());
        }
        time.Now += TimeSpan.FromMinutes(11);

        await using var db2 = NewDb();
        var ex = await Assert.ThrowsAsync<AmazonOAuthException>(() => Service(db2, Options(), OkLwa(), time).CompleteAsync("code", state, "A1"));
        Assert.Contains("süresi doldu", ex.Message);
    }

    [Fact]
    public async Task Eksik_parametreli_donus_reddedilir()
    {
        await using var db = NewDb();
        await Assert.ThrowsAsync<AmazonOAuthException>(() => Service(db, Options(), OkLwa()).CompleteAsync(null, "state", "A1"));
        await Assert.ThrowsAsync<AmazonOAuthException>(() => Service(db, Options(), OkLwa()).CompleteAsync("code", "state", null));
    }

    [Theory]
    [InlineData("""{"error":"invalid_grant","error_description":"secret-leak"}""", "süresi dolmuş")]
    [InlineData("""{"error":"invalid_client"}""", "client secret")]
    [InlineData("<html>502</html>", "tamamlayamadı")]
    public async Task LWA_hatalari_anlasilir_mesaja_cevrilir_ve_ham_govde_sizmaz(string body, string expected)
    {
        var lwa = new AmazonLwaClient(new HttpClient(new FakeLwa(HttpStatusCode.BadRequest, body)), Microsoft.Extensions.Options.Options.Create(Options()));

        var ex = await Assert.ThrowsAsync<AmazonOAuthException>(() => lwa.ExchangeCodeAsync("code"));

        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain("secret-leak", ex.Message);
    }

    [Theory]
    [InlineData(10, 170, false)]   // 170 gün kaldı
    [InlineData(150, 30, true)]    // tam 30 gün kaldı: hatırlat
    [InlineData(179, 1, true)]
    [InlineData(200, -20, true)]   // süresi dolmuş
    public async Task Client_secret_bitisine_30_gun_kala_hatirlatir(int issuedDaysAgo, int daysLeft, bool reminder)
    {
        var time = new FakeTime();
        await using var db = NewDb();

        var status = await Service(db, Options(time.Now.UtcDateTime.AddDays(-issuedDaysAgo)), OkLwa(), time).GetStatusAsync();

        Assert.Equal(daysLeft, status.DaysUntilSecretExpiry);
        Assert.Equal(reminder, status.SecretReminder);
    }

    [Fact]
    public async Task Secret_tarihi_tanimsizsa_hatirlatici_yok()
    {
        await using var db = NewDb();

        var status = await Service(db, Options(null), OkLwa()).GetStatusAsync();

        Assert.Null(status.DaysUntilSecretExpiry);
        Assert.False(status.SecretReminder);
    }
}
