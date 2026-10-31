using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Auth;
using Pazaryeri.Application.Dealers;
using Pazaryeri.Application.Modules;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Auth;
using Pazaryeri.Infrastructure.Dealers;
using Pazaryeri.Infrastructure.Modules;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Tests;

public class DealerPasswordTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_DealerPassword;Trusted_Connection=True;TrustServerCertificate=True";
    private const string Slug = "parola";
    private const string Pass = "Parola1234";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private sealed class MutableTime(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private sealed class RecordingEmail : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];
        public bool Fail { get; set; }
        public Task SendAsync(string to, string subject, string body, CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("smtp down");
            Sent.Add((to, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed class AmbientProvider : ITenantProvider
    {
        public Guid? TenantId => AmbientTenant.TenantId;
    }

    /// <summary>tenantId verilirse o kiracıya sabit; null ise oturumsuz istek gibi ortam kiracısına bakar.</summary>
    private static AppDbContext NewDb(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, tenantId is null ? new AmbientProvider() : new FixedTenant(tenantId));

    private Guid _tenant;
    private readonly MutableTime _time = new(DateTime.UtcNow);
    private readonly RecordingEmail _email = new();

    public async Task InitializeAsync()
    {
        await using var db = NewDb(null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        var t = new Tenant { Name = "Parola Firma", Slug = Slug };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        _tenant = t.Id;
        await using var seed = NewDb(_tenant);
        seed.TenantModules.Add(new TenantModule { ModuleId = ModuleIds.Dealers, Enabled = true });
        seed.TenantModules.Add(new TenantModule { ModuleId = ModuleIds.B2BStore, Enabled = true });
        await seed.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private DealerAuthService Service(AppDbContext db)
    {
        var modules = new ModuleService(db, new FixedTenant(null), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);
        return new DealerAuthService(db, new JwtTokenService(Options.Create(new JwtOptions { Key = new string('k', 40) })), modules, _time, _email,
            new StoreLinkOptions("https://x/{slug}/giris", "https://x/{slug}/sifre?token={token}"));
    }

    /// <summary>Aktif bayi + kullanıcı; kullanıcının kimliğini döner.</summary>
    private async Task<Guid> ActiveDealerAsync(string email = "ali@firma.com", string tax = "1234567890")
    {
        await using var anon = NewDb(null);
        var applied = await Service(anon).ApplyAsync(new DealerApplication(Slug, "Firma", tax, null, "Ali Veli", email, null, null, null, Pass), default);
        Assert.Equal(DealerAuthResult.Ok, applied.Result);
        await using var db = NewDb(_tenant);
        var dealer = await db.Dealers.FirstAsync(d => d.Id == applied.Value);
        dealer.Status = DealerStatus.Active;
        await db.SaveChangesAsync();
        return (await db.DealerUsers.FirstAsync(u => u.DealerId == dealer.Id)).Id;
    }

    private async Task<DealerAuthResult> LoginAsync(string password, string email = "ali@firma.com")
    {
        await using var db = NewDb(null);
        return (await Service(db).LoginAsync(new DealerLogin(Slug, email, password), default)).Result;
    }

    private static string TokenFrom(string body)
    {
        var i = body.IndexOf("token=", StringComparison.Ordinal);
        return Uri.UnescapeDataString(body[(i + 6)..].Split('\n', '\r')[0].Trim());
    }

    // --- parola değiştirme ---

    [Fact]
    public async Task Parola_degistirme_mevcut_parolayi_dogrular_ve_yenisiyle_giris_yapilir()
    {
        var userId = await ActiveDealerAsync();
        await using var db = NewDb(_tenant);
        var svc = Service(db);

        Assert.Equal(DealerAuthResult.InvalidCredentials, (await svc.ChangePasswordAsync(userId, new("Yanlis1234", "Yeni12345"), default)).Result);
        Assert.Equal(DealerAuthResult.InvalidInput, (await svc.ChangePasswordAsync(userId, new(Pass, "kisa"), default)).Result);
        Assert.Equal(DealerAuthResult.InvalidInput, (await svc.ChangePasswordAsync(userId, new(Pass, Pass), default)).Result);
        Assert.Equal(DealerAuthResult.InvalidInput, (await svc.ChangePasswordAsync(userId, new("", "Yeni12345"), default)).Result);
        Assert.Equal(DealerAuthResult.InvalidCredentials, (await svc.ChangePasswordAsync(Guid.NewGuid(), new(Pass, "Yeni12345"), default)).Result);

        Assert.Equal(DealerAuthResult.Ok, (await svc.ChangePasswordAsync(userId, new(Pass, "Yeni12345"), default)).Result);
        Assert.Equal(DealerAuthResult.InvalidCredentials, await LoginAsync(Pass));
        Assert.Equal(DealerAuthResult.Ok, await LoginAsync("Yeni12345"));
    }

    // --- sıfırlama isteği ---

    [Fact]
    public async Task Sifirlama_istegi_kayitli_ve_kayitsiz_icin_ayni_yaniti_verir_yalnizca_kayitliya_posta_gider()
    {
        await ActiveDealerAsync();
        await using var db = NewDb(null);
        var svc = Service(db);

        var known = await svc.RequestPasswordResetAsync(new(Slug, "ali@firma.com"), default);
        var unknown = await svc.RequestPasswordResetAsync(new(Slug, "yok@firma.com"), default);
        Assert.Equal((DealerAuthResult.Ok, true), (known.Result, known.Value));
        Assert.Equal((DealerAuthResult.Ok, true), (unknown.Result, unknown.Value));

        var mail = Assert.Single(_email.Sent);
        Assert.Equal("ali@firma.com", mail.To);
        Assert.Contains("https://x/parola/sifre?token=", mail.Body);

        Assert.Equal(DealerAuthResult.InvalidInput, (await svc.RequestPasswordResetAsync(new(Slug, "gecersiz"), default)).Result);
        Assert.Equal(DealerAuthResult.UnknownTenant, (await svc.RequestPasswordResetAsync(new("yok", "ali@firma.com"), default)).Result);
    }

    [Fact]
    public async Task Bekleyen_ya_da_pasif_bayiye_sifirlama_postasi_gitmez()
    {
        await using var anon = NewDb(null);
        await Service(anon).ApplyAsync(new DealerApplication(Slug, "Bekleyen", "9999999999", null, null, "bekle@firma.com", null, null, null, Pass), default);
        await Service(anon).RequestPasswordResetAsync(new(Slug, "bekle@firma.com"), default);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task Sik_tekrar_istek_bekleme_suresinde_yeni_posta_gondermez()
    {
        await ActiveDealerAsync();
        await using var db = NewDb(null);
        var svc = Service(db);
        await svc.RequestPasswordResetAsync(new(Slug, "ali@firma.com"), default);
        await svc.RequestPasswordResetAsync(new(Slug, "ali@firma.com"), default);
        Assert.Single(_email.Sent);

        _time.Now = DateTime.UtcNow.Add(DealerAuthService.ResetCooldown).AddSeconds(5); // kayıt zamanı gerçek saatten gelir
        await svc.RequestPasswordResetAsync(new(Slug, "ali@firma.com"), default);
        Assert.Equal(2, _email.Sent.Count);
    }

    // --- sıfırlama onayı ---

    [Fact]
    public async Task Sifirlama_token_ile_yeni_parola_belirler_ve_tek_kullanimliktir()
    {
        await ActiveDealerAsync();
        await using var db = NewDb(null);
        var svc = Service(db);
        await svc.RequestPasswordResetAsync(new(Slug, "ali@firma.com"), default);
        var token = TokenFrom(_email.Sent[0].Body);

        Assert.Equal(DealerAuthResult.Ok, (await svc.ResetPasswordAsync(new(Slug, token, "Sifirlandi99"), default)).Result);
        Assert.Equal(DealerAuthResult.Ok, await LoginAsync("Sifirlandi99"));
        Assert.Equal(DealerAuthResult.InvalidCredentials, await LoginAsync(Pass));

        // aynı bağlantı ikinci kez kullanılamaz
        Assert.Equal(DealerAuthResult.InvalidToken, (await svc.ResetPasswordAsync(new(Slug, token, "Baska12345"), default)).Result);
        Assert.Equal(DealerAuthResult.Ok, await LoginAsync("Sifirlandi99"));
    }

    [Fact]
    public async Task Sifirlama_gecersiz_sureli_ve_yanlis_kiraci_tokenini_reddeder()
    {
        await ActiveDealerAsync();
        await using var db = NewDb(null);
        var svc = Service(db);
        await svc.RequestPasswordResetAsync(new(Slug, "ali@firma.com"), default);
        var token = TokenFrom(_email.Sent[0].Body);

        Assert.Equal(DealerAuthResult.InvalidToken, (await svc.ResetPasswordAsync(new(Slug, "uydurma", "Yeni12345"), default)).Result);
        Assert.Equal(DealerAuthResult.InvalidToken, (await svc.ResetPasswordAsync(new(Slug, "", "Yeni12345"), default)).Result);
        Assert.Equal(DealerAuthResult.InvalidToken, (await svc.ResetPasswordAsync(new("baska", token, "Yeni12345"), default)).Result);
        Assert.Equal(DealerAuthResult.InvalidInput, (await svc.ResetPasswordAsync(new(Slug, token, "kisa"), default)).Result); // zayıf parola token'ı harcamaz

        _time.Now = DateTime.UtcNow.Add(DealerAuthService.ResetLifetime).AddSeconds(5);
        Assert.Equal(DealerAuthResult.InvalidToken, (await svc.ResetPasswordAsync(new(Slug, token, "Yeni12345"), default)).Result);
        Assert.Equal(DealerAuthResult.Ok, await LoginAsync(Pass)); // parola değişmedi
    }

    [Fact]
    public async Task Token_yalnizca_ozet_olarak_saklanir_ve_yeni_istek_eskisini_gecersiz_kilmaz_ama_degisiklik_hepsini_kapatir()
    {
        var userId = await ActiveDealerAsync();
        await using var db = NewDb(null);
        var svc = Service(db);
        await svc.RequestPasswordResetAsync(new(Slug, "ali@firma.com"), default);
        var token = TokenFrom(_email.Sent[0].Body);

        await using (var check = NewDb(_tenant))
        {
            var row = await check.DealerPasswordResets.SingleAsync();
            Assert.NotEqual(token, row.TokenHash);
            Assert.Equal(64, row.TokenHash.Length);
        }

        // parola değiştirilince açık bağlantı kullanılamaz
        await using var authed = NewDb(_tenant);
        Assert.Equal(DealerAuthResult.Ok, (await Service(authed).ChangePasswordAsync(userId, new(Pass, "Degisti1234"), default)).Result);
        Assert.Equal(DealerAuthResult.InvalidToken, (await svc.ResetPasswordAsync(new(Slug, token, "Yeni12345"), default)).Result);
    }

    [Fact]
    public async Task Es_zamanli_iki_sifirlama_istegi_tek_basarili_olur()
    {
        await ActiveDealerAsync();
        await using var seed = NewDb(null);
        await Service(seed).RequestPasswordResetAsync(new(Slug, "ali@firma.com"), default);
        var token = TokenFrom(_email.Sent[0].Body);

        async Task<DealerAuthResult> Try(string pw)
        {
            await using var d = NewDb(null);
            return (await Service(d).ResetPasswordAsync(new(Slug, token, pw), default)).Result;
        }
        var results = await Task.WhenAll(Try("Birinci1234"), Try("Ikinci12345"));
        Assert.Single(results, r => r == DealerAuthResult.Ok);
        Assert.Single(results, r => r == DealerAuthResult.InvalidToken);
    }

    // --- uçlar ---

    [Fact]
    public async Task Uc_noktalar_beklenen_durum_kodlarini_doner()
    {
        var userId = await ActiveDealerAsync();
        await using var db = NewDb(null);
        var c = new StoreAuthController(Service(db));

        Assert.IsType<OkObjectResult>(await c.ForgotPassword(new(Slug, "ali@firma.com"), default));
        Assert.IsType<OkObjectResult>(await c.ForgotPassword(new(Slug, "yok@firma.com"), default));
        Assert.IsType<BadRequestObjectResult>(await c.ForgotPassword(new(Slug, "x"), default));
        Assert.IsType<NotFoundObjectResult>(await c.ForgotPassword(new("yok", "ali@firma.com"), default));

        var token = TokenFrom(_email.Sent[0].Body);
        Assert.IsType<BadRequestObjectResult>(await c.ResetPassword(new(Slug, "uydurma", "Yeni12345"), default));
        Assert.IsType<BadRequestObjectResult>(await c.ResetPassword(new(Slug, token, "k"), default));
        Assert.IsType<OkObjectResult>(await c.ResetPassword(new(Slug, token, "Yeni12345"), default));
        Assert.IsType<BadRequestObjectResult>(await c.ResetPassword(new(Slug, token, "Yeni12345"), default));
        _ = userId;
    }

    // --- başvuru sonucu bildirimi ---

    [Fact]
    public async Task Basvuru_onay_ve_ret_firma_yetkilisine_eposta_gonderir_gonderim_hatasi_islemi_bozmaz()
    {
        await using var anon = NewDb(null);
        var a = await Service(anon).ApplyAsync(new DealerApplication(Slug, "Onay Firma", "1111111111", null, "Ayşe", "onay@firma.com", null, null, null, Pass), default);
        var b = await Service(anon).ApplyAsync(new DealerApplication(Slug, "Ret Firma", "2222222222", null, "Can", "ret@firma.com", null, null, null, Pass), default);
        var c = await Service(anon).ApplyAsync(new DealerApplication(Slug, "Hata Firma", "3333333333", null, "Eda", "hata@firma.com", null, null, null, Pass), default);

        await using var db = NewDb(_tenant);
        var ctl = new DealersController(db, _email, new StoreLinkOptions("https://x/{slug}/giris", "https://x/{slug}/sifre?token={token}"));

        Assert.NotNull((await ctl.Approve(a.Value, default)).Value);
        Assert.NotNull((await ctl.Deactivate(b.Value, default)).Value); // bekleyen başvuruyu pasife almak = ret
        var approval = _email.Sent[0];
        Assert.Equal(("onay@firma.com", "Bayi başvurunuz onaylandı"), (approval.To, approval.Subject));
        Assert.Contains("https://x/parola/giris", approval.Body);
        Assert.Equal("ret@firma.com", _email.Sent[1].To);
        Assert.DoesNotContain("giris", _email.Sent[1].Body);

        _email.Fail = true;
        var result = await ctl.Approve(c.Value, default);
        Assert.Equal("Active", result.Value!.Status);   // posta gitmese de onay kalıcı

        // yalnızca bekleyen başvuru bildirir; aktif bayiyi pasife almak ya da yeniden açmak posta göndermez
        _email.Fail = false;
        var before = _email.Sent.Count;
        await ctl.Deactivate(a.Value, default);
        await ctl.Activate(a.Value, default);
        Assert.Equal(before, _email.Sent.Count);
    }
}
