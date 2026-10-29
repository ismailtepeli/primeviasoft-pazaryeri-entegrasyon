using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Credentials;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Security;

namespace Pazaryeri.Tests;

public class AesGcmCredentialProtectorTests
{
    private static AesGcmCredentialProtector NewProtector() => new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Sifrele_cozer_ve_duz_metin_icermez()
    {
        var p = NewProtector();

        var cipher = p.Protect("super-gizli-anahtar", "t:Trendyol:Live");

        Assert.DoesNotContain("super-gizli-anahtar", cipher);
        Assert.Equal("super-gizli-anahtar", p.Unprotect(cipher, "t:Trendyol:Live"));
    }

    [Fact]
    public void Ayni_metin_her_seferinde_farkli_sifrelenir()
    {
        var p = NewProtector();

        Assert.NotEqual(p.Protect("x", "c"), p.Protect("x", "c"));
    }

    [Fact]
    public void Baska_baglamda_cozulemez()
    {
        var p = NewProtector();
        var cipher = p.Protect("x", "tenant-a:Trendyol:Live");

        Assert.Throws<AuthenticationTagMismatchException>(() => p.Unprotect(cipher, "tenant-b:Trendyol:Live"));
        Assert.Throws<AuthenticationTagMismatchException>(() => p.Unprotect(cipher, "tenant-a:Trendyol:Staging"));
    }

    [Fact]
    public void Baska_anahtarla_cozulemez_ve_kurcalanan_veri_reddedilir()
    {
        var cipher = NewProtector().Protect("x", "c");

        Assert.Throws<AuthenticationTagMismatchException>(() => NewProtector().Unprotect(cipher, "c"));

        var p = NewProtector();
        var good = p.Protect("x", "c");
        var tampered = good[..^2] + (good[^2] == 'A' ? "B=" : "A=");
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(tampered, "c"));
    }

    [Fact]
    public void Anahtar_32_bayt_degilse_reddedilir()
    {
        Assert.Throws<ArgumentException>(() => new AesGcmCredentialProtector(new byte[16]));
        Assert.Throws<InvalidOperationException>(() => AesGcmCredentialProtector.FromBase64(null));
        Assert.Throws<InvalidOperationException>(() => AesGcmCredentialProtector.FromBase64("bu-base64-degil!"));
    }
}

/// <summary>LocalDB'de ayrı bir veritabanı (Pazaryeri_Tests_Credentials) kullanır.</summary>
public class ChannelCredentialServiceTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Credentials;Trusted_Connection=True;TrustServerCertificate=True";
    private static readonly AesGcmCredentialProtector Protector = new(RandomNumberGenerator.GetBytes(32));

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private static AppDbContext NewDb(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    private static ChannelCredentialService Service(AppDbContext db, Guid tenantId) => new(db, new FixedTenant(tenantId), Protector);

    public async Task InitializeAsync()
    {
        await using var db = NewDb(null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> NewTenantAsync(string name)
    {
        await using var db = NewDb(null);
        var t = new Tenant { Name = name };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private static Dictionary<string, string> TrendyolFields(string key = "KEY-1234567890", string secret = "SECRET-ABCDEFGH") =>
        new() { ["sellerId"] = "12345", ["apiKey"] = key, ["apiSecret"] = secret };

    [Fact]
    public async Task Gizli_alanlar_veritabaninda_duz_metin_olarak_bulunmaz()
    {
        var tenant = await NewTenantAsync("Sifre A");
        await using var db = NewDb(tenant);

        await Service(db, tenant).SaveAsync("Trendyol", ChannelEnvironment.Live, TrendyolFields(), default);

        var row = await NewDb(tenant).ChannelCredentials.SingleAsync();
        var everything = row.PublicFieldsJson + row.EncryptedSecrets + row.SecretHintsJson;
        Assert.DoesNotContain("KEY-1234567890", everything);
        Assert.DoesNotContain("SECRET-ABCDEFGH", everything);
        Assert.Contains("12345", row.PublicFieldsJson); // satıcı kimliği gizli değil
    }

    [Fact]
    public async Task Kayittan_sonra_gizli_alanlar_yalnizca_son_4_haneyle_doner()
    {
        var tenant = await NewTenantAsync("Sifre B");
        await using var db = NewDb(tenant);
        var service = Service(db, tenant);

        var saved = await service.SaveAsync("Trendyol", ChannelEnvironment.Live, TrendyolFields(), default);
        var read = await service.GetMaskedAsync("Trendyol", ChannelEnvironment.Live, default);

        foreach (var view in new[] { saved, read! })
        {
            Assert.Equal("••••7890", view.Fields.Single(f => f.Name == "apiKey").Masked);
            Assert.Equal("••••EFGH", view.Fields.Single(f => f.Name == "apiSecret").Masked);
            Assert.All(view.Fields.Where(f => f.Secret), f => Assert.Null(f.Value));
            Assert.Equal("12345", view.Fields.Single(f => f.Name == "sellerId").Value);
        }
    }

    [Fact]
    public async Task Kisa_gizli_deger_son_haneleri_gostermez()
    {
        var tenant = await NewTenantAsync("Sifre C");
        await using var db = NewDb(tenant);

        var view = await Service(db, tenant).SaveAsync("Trendyol", ChannelEnvironment.Live, TrendyolFields(key: "abc123", secret: "xyz789"), default);

        Assert.All(view.Fields.Where(f => f.Secret), f => Assert.Equal("••••", f.Masked));
    }

    [Fact]
    public async Task Bos_birakilan_gizli_alan_eski_degeri_korur()
    {
        var tenant = await NewTenantAsync("Sifre D");
        await using var db = NewDb(tenant);
        var service = Service(db, tenant);
        await service.SaveAsync("Trendyol", ChannelEnvironment.Live, TrendyolFields(), default);

        await service.SaveAsync("Trendyol", ChannelEnvironment.Live,
            new Dictionary<string, string> { ["sellerId"] = "12345", ["apiKey"] = "", ["apiSecret"] = "YENI-SECRET-9999" }, default);

        var creds = await service.GetForClientAsync("Trendyol", default);
        Assert.Equal("KEY-1234567890", creds!.ApiKey); // değişmedi
        Assert.Equal("YENI-SECRET-9999", creds.ApiSecret); // güncellendi
    }

    [Fact]
    public async Task Ilk_kayitta_bos_gizli_alan_hata_verir()
    {
        var tenant = await NewTenantAsync("Sifre E");
        await using var db = NewDb(tenant);

        await Assert.ThrowsAsync<CredentialValidationException>(() => Service(db, tenant).SaveAsync("Trendyol", ChannelEnvironment.Live,
            new Dictionary<string, string> { ["sellerId"] = "12345", ["apiKey"] = "KEY-1234567890" }, default));
    }

    [Fact]
    public async Task Canli_ve_test_bilgileri_ayri_tutulur_ve_etkin_ortam_son_kaydedilendir()
    {
        var tenant = await NewTenantAsync("Sifre F");
        await using var db = NewDb(tenant);
        var service = Service(db, tenant);

        await service.SaveAsync("Trendyol", ChannelEnvironment.Live, TrendyolFields(key: "CANLI-KEY-0001"), default);
        await service.SaveAsync("Trendyol", ChannelEnvironment.Staging, TrendyolFields(key: "TEST-KEY-0002"), default);

        Assert.Equal(2, await db.ChannelCredentials.CountAsync());
        Assert.Equal("••••0001", (await service.GetMaskedAsync("Trendyol", ChannelEnvironment.Live, default))!.Fields.Single(f => f.Name == "apiKey").Masked);
        Assert.Equal("••••0002", (await service.GetMaskedAsync("Trendyol", ChannelEnvironment.Staging, default))!.Fields.Single(f => f.Name == "apiKey").Masked);

        var active = await service.GetForClientAsync("Trendyol", default);
        Assert.True(active!.UseStaging);
        Assert.Equal("TEST-KEY-0002", active.ApiKey);
    }

    [Fact]
    public async Task Adapter_icin_cozulmus_bilgiler_dogru_alanlara_eslenir()
    {
        var tenant = await NewTenantAsync("Sifre G");
        await using var db = NewDb(tenant);
        var service = Service(db, tenant);

        await service.SaveAsync("Trendyol", ChannelEnvironment.Live, TrendyolFields(), default);
        await service.SaveAsync("N11", ChannelEnvironment.Live, new Dictionary<string, string> { ["appKey"] = "n11-key-0001", ["appSecret"] = "n11-secret-0002" }, default);

        var trendyol = await service.GetForClientAsync("Trendyol", default);
        Assert.Equal(("Trendyol", "12345", "KEY-1234567890", "SECRET-ABCDEFGH", false), (trendyol!.Channel, trendyol.SellerId, trendyol.ApiKey, trendyol.ApiSecret, trendyol.UseStaging));
        var n11 = await service.GetForClientAsync("N11", default);
        Assert.Equal(("n11-key-0001", "n11-secret-0002"), (n11!.ApiKey, n11.ApiSecret));
        Assert.Null(await service.GetForClientAsync("Hepsiburada", default)); // kaydı yok
    }

    [Fact]
    public async Task Baska_kiracinin_bilgileri_gorunmez()
    {
        var a = await NewTenantAsync("Sifre H1");
        var b = await NewTenantAsync("Sifre H2");
        await using (var dbA = NewDb(a)) await Service(dbA, a).SaveAsync("Trendyol", ChannelEnvironment.Live, TrendyolFields(), default);

        await using var dbB = NewDb(b);
        Assert.Null(await Service(dbB, b).GetMaskedAsync("Trendyol", ChannelEnvironment.Live, default));
        Assert.Null(await Service(dbB, b).GetForClientAsync("Trendyol", default));
    }

    [Fact]
    public async Task Baska_kiracinin_sifreli_verisi_satira_kopyalansa_cozulemez()
    {
        var a = await NewTenantAsync("Sifre I1");
        var b = await NewTenantAsync("Sifre I2");
        await using (var dbA = NewDb(a)) await Service(dbA, a).SaveAsync("Trendyol", ChannelEnvironment.Live, TrendyolFields(), default);
        string stolen;
        await using (var dbA = NewDb(a)) stolen = (await dbA.ChannelCredentials.SingleAsync()).EncryptedSecrets;

        await using var dbB = NewDb(b);
        await Service(dbB, b).SaveAsync("Trendyol", ChannelEnvironment.Live, TrendyolFields(), default);
        var row = await dbB.ChannelCredentials.SingleAsync();
        row.EncryptedSecrets = stolen; // saldırgan A'nın şifreli verisini B'nin satırına yazar
        await dbB.SaveChangesAsync();

        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() => Service(dbB, b).GetForClientAsync("Trendyol", default));
    }

    [Fact]
    public async Task Bilinmeyen_alan_ve_desteklenmeyen_kanal_reddedilir()
    {
        var tenant = await NewTenantAsync("Sifre J");
        await using var db = NewDb(tenant);
        var service = Service(db, tenant);

        var fields = TrendyolFields();
        fields["evil"] = "x";
        await Assert.ThrowsAsync<CredentialValidationException>(() => service.SaveAsync("Trendyol", ChannelEnvironment.Live, fields, default));
        await Assert.ThrowsAsync<CredentialValidationException>(() => service.SaveAsync("Amazon", ChannelEnvironment.Live, TrendyolFields(), default));
    }

    [Fact]
    public async Task Kayit_kanali_bagli_yapar_ve_ortami_isler()
    {
        var tenant = await NewTenantAsync("Sifre K");
        await using var db = NewDb(tenant);

        await Service(db, tenant).SaveAsync("Trendyol", ChannelEnvironment.Staging, TrendyolFields(), default);

        var connection = await db.ChannelConnections.SingleAsync();
        Assert.Equal(ConnectionStatus.Connected, connection.Status);
        Assert.Equal(ChannelEnvironment.Staging, connection.Environment);
    }
}
