using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pazaryeri.Api;
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

public class DealerTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Dealers;Trusted_Connection=True;TrustServerCertificate=True";
    private static readonly JwtOptions Jwt = new() { Key = new string('k', 40) };

    private sealed class AmbientProvider : ITenantProvider
    {
        public Guid? TenantId => AmbientTenant.TenantId;
    }

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private static AppDbContext NewDb(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    /// <summary>Bayi sitesi isteği gibi: kiracı oturumdan değil, işlem sırasında belirlenen ortam kiracısından gelir.</summary>
    private static AppDbContext NewAmbientDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new AmbientProvider());

    private static DealerAuthService Auth(AppDbContext db, Guid? tenant = null)
    {
        var provider = new FixedTenant(tenant);
        var modules = new ModuleService(db, provider, new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);
        return new DealerAuthService(db, new JwtTokenService(Options.Create(Jwt)), modules, TimeProvider.System);
    }

    public async Task InitializeAsync()
    {
        await using var db = NewDb(null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> TenantAsync(string name, string? slug = null, bool dealersModule = true)
    {
        await using var db = NewDb(null);
        var t = new Tenant { Name = name, Slug = slug };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        if (dealersModule)
        {
            await using var seed = NewDb(t.Id);
            seed.TenantModules.Add(new TenantModule { ModuleId = ModuleIds.Dealers, Enabled = true });
            await seed.SaveChangesAsync();
        }
        return t.Id;
    }

    private static DealerInput Input(string name = "Mavi Ticaret", string tax = "1234567890", string email = "info@mavi.com", Guid? group = null) =>
        new(name, tax, "Kadıköy", "Ayşe Yılmaz", email, "0555 000 00 00", "Adres", "İstanbul", null, group);

    private static DealerApplication Application(string slug, string email = "ayse@mavi.com", string tax = "1234567890", string password = "Parola123") =>
        new(slug, "Mavi Ticaret", tax, "Kadıköy", "Ayşe Yılmaz", email, "0555", "İstanbul", "Adres", password);

    // --- yönetici API ---

    [Fact]
    public async Task Bayi_eklenir_aktif_olur_dogrulama_ve_tekrar_eden_vergi_numarasi_reddedilir()
    {
        var tenant = await TenantAsync("Ekle");
        await using var db = NewDb(tenant);
        var c = new DealersController(db);

        var created = Assert.IsType<CreatedAtActionResult>((await c.Create(Input(), default)).Result);
        var dealer = Assert.IsType<DealerDetailDto>(created.Value);
        Assert.Equal(("Active", "info@mavi.com"), (dealer.Status, dealer.Email));
        Assert.NotNull(dealer.ApprovedAt);

        Assert.IsType<ConflictObjectResult>((await c.Create(Input(name: "Başka", email: "x@y.com"), default)).Result); // aynı vergi no
        Assert.IsType<BadRequestObjectResult>((await c.Create(Input(name: " "), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Create(Input(tax: "123"), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Create(Input(tax: "12345678ab"), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Create(Input(tax: "2234567890", email: "gecersiz"), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Create(Input(tax: "2234567890", group: Guid.NewGuid()), default)).Result); // olmayan grup
    }

    [Fact]
    public async Task Liste_durum_aramaya_gore_suzer_sayaclar_aramadan_bagimsizdir()
    {
        var tenant = await TenantAsync("Liste");
        await using var db = NewDb(tenant);
        db.Dealers.AddRange(
            new Dealer { Name = "Alfa Ltd", TaxNumber = "1000000001", Email = "a@a.com", Status = DealerStatus.Pending },
            new Dealer { Name = "Beta AŞ", TaxNumber = "1000000002", Email = "b@b.com", Status = DealerStatus.Active, ContactName = "Mehmet" },
            new Dealer { Name = "Gama AŞ", TaxNumber = "1000000003", Email = "g@g.com", Status = DealerStatus.Active },
            new Dealer { Name = "Delta", TaxNumber = "1000000004", Email = "d@d.com", Status = DealerStatus.Passive });
        await db.SaveChangesAsync();
        var c = new DealersController(db);

        var active = (await c.List("active", null)).Value!;
        Assert.Equal(["Beta AŞ", "Gama AŞ"], active.Items.Select(i => i.Name).Order());
        Assert.Equal((1, 2, 1), (active.Counts.Pending, active.Counts.Active, active.Counts.Passive));
        Assert.Equal(["Beta AŞ"], (await c.List(null, "mehmet")).Value!.Items.Select(i => i.Name));
        Assert.Equal(["Delta"], (await c.List(null, "1000000004")).Value!.Items.Select(i => i.Name));
        Assert.Equal(2, (await c.List("active", "AŞ")).Value!.Counts.Active);
        Assert.IsType<BadRequestObjectResult>((await c.List("yok", null)).Result);
    }

    [Fact]
    public async Task Durum_gecisleri_kurallara_uyar()
    {
        var tenant = await TenantAsync("Gecis");
        await using var db = NewDb(tenant);
        var pending = new Dealer { Name = "P", TaxNumber = "1000000001", Email = "p@p.com", Status = DealerStatus.Pending };
        db.Dealers.Add(pending);
        await db.SaveChangesAsync();
        var c = new DealersController(db);

        Assert.IsType<ConflictObjectResult>((await c.Activate(pending.Id, default)).Result); // bekleyen "activate" ile açılmaz
        var approved = (await c.Approve(pending.Id, default)).Value!;
        Assert.Equal("Active", approved.Status);
        Assert.NotNull(approved.ApprovedAt);
        Assert.IsType<ConflictObjectResult>((await c.Approve(pending.Id, default)).Result);
        Assert.Equal("Passive", (await c.Deactivate(pending.Id, default)).Value!.Status);
        Assert.IsType<ConflictObjectResult>((await c.Deactivate(pending.Id, default)).Result);
        Assert.Equal("Active", (await c.Activate(pending.Id, default)).Value!.Status);
        Assert.IsType<NotFoundObjectResult>((await c.Approve(Guid.NewGuid(), default)).Result);
    }

    [Fact]
    public async Task Gruplar_eklenir_ayni_ad_reddedilir_bayisi_olan_grup_silinmez()
    {
        var tenant = await TenantAsync("Grup");
        await using var db = NewDb(tenant);
        var groups = new DealerGroupsController(db);
        var dealers = new DealersController(db);

        var gold = Assert.IsType<CreatedResult>((await groups.Create(new("Altın", "En iyi fiyat"), default)).Result);
        var goldId = ((DealerGroupDto)gold.Value!).Id;
        Assert.IsType<ConflictObjectResult>((await groups.Create(new("Altın", null), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await groups.Create(new(" ", null), default)).Result);

        await dealers.Create(Input(group: goldId), default);
        Assert.Equal(1, (await groups.List(default)).Single().DealerCount);
        Assert.IsType<ConflictObjectResult>(await groups.Delete(goldId, default));

        var all = (await dealers.List(null, null)).Value!;
        Assert.Equal("Altın", all.Items.Single().GroupName);

        await dealers.Update(all.Items.Single().Id, Input(), default); // gruptan çıkar
        Assert.IsType<NoContentResult>(await groups.Delete(goldId, default));
    }

    [Fact]
    public async Task Kullanici_eklenir_parola_ve_eposta_kurallari_ayni_eposta_reddedilir_pasife_alinir()
    {
        var tenant = await TenantAsync("Kullanici");
        await using var db = NewDb(tenant);
        var c = new DealersController(db);
        var dealerId = ((DealerDetailDto)((CreatedAtActionResult)(await c.Create(Input(), default)).Result!).Value!).Id;

        Assert.IsType<BadRequestObjectResult>((await c.AddUser(dealerId, new("a@b.com", "Ad", "kisa", null), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.AddUser(dealerId, new("gecersiz", "Ad", "Parola123", null), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.AddUser(dealerId, new("a@b.com", "Ad", "Parola123", "Patron"), default)).Result);
        var created = Assert.IsType<CreatedResult>((await c.AddUser(dealerId, new("Alici@Mavi.com", "Alıcı Kişi", "Parola123", "Buyer"), default)).Result);
        var user = (DealerUserDto)created.Value!;
        Assert.Equal(("alici@mavi.com", "Buyer", true), (user.Email, user.Role, user.IsActive)); // e-posta küçük harfe çevrilir
        Assert.IsType<ConflictObjectResult>((await c.AddUser(dealerId, new("alici@mavi.com", "Başka", "Parola123", null), default)).Result);

        var updated = (await c.UpdateUser(dealerId, user.Id, new(null, false, "YeniParola1"), default)).Value!;
        Assert.False(updated.IsActive);
        Assert.IsType<BadRequestObjectResult>((await c.UpdateUser(dealerId, user.Id, new(null, null, "x"), default)).Result);
        Assert.IsType<NotFoundObjectResult>((await c.UpdateUser(Guid.NewGuid(), user.Id, new(null, null, null), default)).Result);
        Assert.Single((await c.Get(dealerId, default)).Value!.Users);
    }

    [Fact]
    public async Task Kiracilar_birbirinin_bayisini_gormez_ayni_vergi_no_farkli_kiracida_olabilir()
    {
        var a = await TenantAsync("IzolA");
        var b = await TenantAsync("IzolB");
        await using var dbA = NewDb(a);
        await using var dbB = NewDb(b);
        await new DealersController(dbA).Create(Input(), default);

        Assert.Empty((await new DealersController(dbB).List(null, null)).Value!.Items);
        Assert.IsType<CreatedAtActionResult>((await new DealersController(dbB).Create(Input(), default)).Result);
        var idA = (await new DealersController(dbA).List(null, null)).Value!.Items.Single().Id;
        Assert.IsType<NotFoundObjectResult>((await new DealersController(dbB).Get(idA, default)).Result);
    }

    // --- bayi sitesi kimlik doğrulaması ---

    [Fact]
    public async Task Basvuru_bekleyen_bayi_olusturur_onaylanana_kadar_giris_yapilamaz_onayla_girilir()
    {
        var tenant = await TenantAsync("Basvuru", "basvuru");
        await using var db = NewAmbientDb();
        var auth = Auth(db);

        var apply = await auth.ApplyAsync(Application("Basvuru"), default); // slug büyük/küçük harf duyarsız
        Assert.Equal(DealerAuthResult.Ok, apply.Result);

        var pending = await auth.LoginAsync(new("basvuru", "AYSE@mavi.com", "Parola123"), default);
        Assert.Equal((DealerAuthResult.Pending, "Başvurunuz henüz onaylanmadı."), (pending.Result, pending.Error));
        Assert.Equal(DealerAuthResult.InvalidCredentials, (await auth.LoginAsync(new("basvuru", "ayse@mavi.com", "yanlis-parola"), default)).Result); // durum, parola doğrulanmadan açıklanmaz

        await using var admin = NewDb(tenant);
        await new DealersController(admin).Approve(apply.Value, default);

        await using var next = NewAmbientDb(); // her istek yeni bağlam kullanır; izlenen eski kayıt durumu göstermesin
        var ok = await Auth(next).LoginAsync(new("basvuru", "ayse@mavi.com", "Parola123"), default);
        Assert.Equal(DealerAuthResult.Ok, ok.Result);
        Assert.Equal(("Mavi Ticaret", "Owner", tenant), (ok.Value!.User.DealerName, ok.Value.User.Role, ok.Value.User.TenantId));
        Assert.NotNull((await NewDb(tenant).DealerUsers.SingleAsync()).LastLoginAt);

        await new DealersController(admin).Deactivate(apply.Value, default);
        await using var last = NewAmbientDb();
        Assert.Equal(DealerAuthResult.Passive, (await Auth(last).LoginAsync(new("basvuru", "ayse@mavi.com", "Parola123"), default)).Result);
    }

    [Fact]
    public async Task Basvuru_dogrulamalari_tekrar_ve_bilinmeyen_kiraci_ve_kapali_modul()
    {
        await TenantAsync("Dogrula", "dogrula");
        await TenantAsync("Kapali", "kapali", dealersModule: false);
        await using var db = NewAmbientDb();
        var auth = Auth(db);

        Assert.Equal(DealerAuthResult.InvalidInput, (await auth.ApplyAsync(Application("dogrula", password: "kisa"), default)).Result);
        Assert.Equal(DealerAuthResult.InvalidInput, (await auth.ApplyAsync(Application("dogrula", tax: "abc"), default)).Result);
        Assert.Equal(DealerAuthResult.UnknownTenant, (await auth.ApplyAsync(Application("yok"), default)).Result);
        Assert.Equal(DealerAuthResult.UnknownTenant, (await auth.ApplyAsync(Application(""), default)).Result);
        Assert.Equal(DealerAuthResult.UnknownTenant, (await auth.ApplyAsync(Application("kapali"), default)).Result); // modül kapalı: site yok
        Assert.Equal(DealerAuthResult.Ok, (await auth.ApplyAsync(Application("dogrula"), default)).Result);
        Assert.Equal(DealerAuthResult.Duplicate, (await auth.ApplyAsync(Application("dogrula", tax: "2234567890"), default)).Result); // aynı e-posta
        Assert.Equal(DealerAuthResult.Duplicate, (await auth.ApplyAsync(Application("dogrula", email: "baska@mavi.com"), default)).Result); // aynı vergi no
        Assert.Equal(DealerAuthResult.InvalidCredentials, (await auth.LoginAsync(new("yok", "ayse@mavi.com", "Parola123"), default)).Result);
    }

    [Fact]
    public async Task Bayi_kullanicisi_baska_kiracinin_slug_ile_giris_yapamaz()
    {
        await TenantAsync("KiracX", "kiracx");
        await TenantAsync("KiracY", "kiracy");
        await using var db = NewAmbientDb();
        var auth = Auth(db);
        await auth.ApplyAsync(Application("kiracx"), default);

        Assert.Equal(DealerAuthResult.InvalidCredentials, (await auth.LoginAsync(new("kiracy", "ayse@mavi.com", "Parola123"), default)).Result);
    }

    // --- token ayrımı ---

    private static ClaimsPrincipal Principal(string? principal)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()) };
        if (principal is not null) claims.Add(new Claim(AppClaims.Principal, principal));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static async Task<(bool Admin, bool Dealer)> Evaluate(ClaimsPrincipal user)
    {
        var sp = new ServiceCollection().AddLogging().AddAuthorization(AuthSetup.Configure).BuildServiceProvider();
        var authz = sp.GetRequiredService<IAuthorizationService>();
        var provider = sp.GetRequiredService<IAuthorizationPolicyProvider>();
        var fallback = (await provider.GetFallbackPolicyAsync())!;
        var dealer = (await provider.GetPolicyAsync(AuthPolicies.Dealer))!;
        return ((await authz.AuthorizeAsync(user, null, fallback)).Succeeded, (await authz.AuthorizeAsync(user, null, dealer)).Succeeded);
    }

    [Fact]
    public async Task Bayi_token_yonetim_API_sine_giremez_yonetici_ve_eski_token_bayi_API_sine_giremez()
    {
        Assert.Equal((false, true), await Evaluate(Principal("dealer")));
        Assert.Equal((true, false), await Evaluate(Principal("admin")));
        Assert.Equal((true, false), await Evaluate(Principal(null))); // principal alanı olmayan eski yönetici token'ı
        Assert.Equal((false, false), await Evaluate(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public void Bayi_token_ayri_hedef_kitleyle_uretilir_ve_dealer_kimligi_tasir()
    {
        var tenant = Guid.NewGuid();
        var dealer = new Dealer { TenantId = tenant, Name = "D" };
        var user = new DealerUser { TenantId = tenant, DealerId = dealer.Id, Email = "a@b.com", FullName = "Ad" };

        var (token, _) = new JwtTokenService(Options.Create(Jwt)).CreateForDealer(user, dealer);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal([Jwt.DealerAudience], jwt.Audiences);
        Assert.NotEqual(Jwt.Audience, Jwt.DealerAudience);
        Assert.Equal("dealer", jwt.Claims.Single(c => c.Type == AppClaims.Principal).Value);
        Assert.Equal(dealer.Id.ToString(), jwt.Claims.Single(c => c.Type == AppClaims.DealerId).Value);
        Assert.Equal(tenant.ToString(), jwt.Claims.Single(c => c.Type == AppClaims.TenantId).Value);

        var (adminToken, _) = new JwtTokenService(Options.Create(Jwt)).Create(new User { TenantId = tenant, Email = "x@y.com", FullName = "Y" });
        var adminJwt = new JwtSecurityTokenHandler().ReadJwtToken(adminToken);
        Assert.Equal([Jwt.Audience], adminJwt.Audiences);
        Assert.Equal("admin", adminJwt.Claims.Single(c => c.Type == AppClaims.Principal).Value);
    }
}
