using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pazaryeri.Api;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Auth;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Shop;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Auth;
using Pazaryeri.Infrastructure.Modules;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Shop;

namespace Pazaryeri.Tests;

public class CustomerTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Customers;Trusted_Connection=True;TrustServerCertificate=True";
    private const string ClientId = "test-client.apps.googleusercontent.com";
    private static readonly JwtOptions Jwt = new() { Key = new string('k', 40) };

    private sealed class AmbientProvider : ITenantProvider
    {
        public Guid? TenantId => AmbientTenant.TenantId;
    }

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private sealed class FakeGoogle(GoogleIdentity? identity, bool configured = true) : IGoogleIdTokenVerifier
    {
        public bool Configured => configured;
        public string? ClientId => configured ? CustomerTests.ClientId : null;
        public Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken ct = default) => Task.FromResult(idToken == "bad" ? null : identity);
    }

    private static AppDbContext NewDb(Guid? tenant = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, tenant is null ? new AmbientProvider() : new FixedTenant(tenant));

    private static CustomerAuthService Auth(AppDbContext db, IGoogleIdTokenVerifier? google = null)
    {
        var shop = new ShopCatalogService(db, new ModuleService(db, new AmbientProvider(), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System));
        return new CustomerAuthService(db, shop, new JwtTokenService(Options.Create(Jwt)), google ?? new FakeGoogle(null), TimeProvider.System);
    }

    public async Task InitializeAsync()
    {
        await using var db = NewDb(Guid.Empty);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> ShopAsync(string slug, bool shopOn = true)
    {
        await using var root = NewDb(Guid.Empty);
        var tenant = new Tenant { Name = "Firma " + slug, Slug = slug };
        root.Tenants.Add(tenant);
        await root.SaveChangesAsync();
        if (shopOn)
        {
            await using var db = NewDb(tenant.Id);
            db.TenantModules.Add(new TenantModule { ModuleId = ModuleIds.B2CStore, Enabled = true });
            await db.SaveChangesAsync();
        }
        return tenant.Id;
    }

    private static CustomerRegister Register(string slug, string email = "ayse@mail.com", string password = "Parola123", bool terms = true) =>
        new(slug, email, password, "Ayşe Yılmaz", "0555 123 45 67", terms, false);

    // --- kayıt ve parola ile giriş ---

    [Fact]
    public async Task Kayit_hesap_acar_token_verir_parola_hashli_saklanir_ve_ayni_eposta_ikinci_kez_acilamaz()
    {
        await ShopAsync("kayit");
        await using var db = NewDb();
        var auth = Auth(db);

        var ok = await auth.RegisterAsync(Register("kayit", "Ayse@Mail.com"), default);
        Assert.Equal(CustomerAuthResult.Ok, ok.Result);
        Assert.Equal(("ayse@mail.com", true, false), (ok.Value!.Profile.Email, ok.Value.Profile.HasPassword, ok.Value.Profile.GoogleLinked));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(ok.Value.AccessToken);
        Assert.Equal([Jwt.CustomerAudience], jwt.Audiences);
        Assert.Equal("customer", jwt.Claims.Single(c => c.Type == AppClaims.Principal).Value);

        await using var check = NewDb();
        using var _ = AmbientTenant.Use(await TenantIdAsync("kayit"));
        var stored = await check.Customers.SingleAsync();
        Assert.NotEqual("Parola123", stored.PasswordHash);
        Assert.NotNull(stored.TermsAcceptedAt);

        Assert.Equal(CustomerAuthResult.Duplicate, (await auth.RegisterAsync(Register("kayit", "ayse@mail.com"), default)).Result);
    }

    private static async Task<Guid> TenantIdAsync(string slug)
    {
        await using var db = NewDb(Guid.Empty);
        return await db.Tenants.Where(t => t.Slug == slug).Select(t => t.Id).SingleAsync();
    }

    [Fact]
    public async Task Kayit_girdisini_dogrular_kosullar_kabul_edilmeden_ve_kapali_magazada_acilmaz()
    {
        await ShopAsync("dogrula");
        await ShopAsync("kapali", shopOn: false);
        await using var db = NewDb();
        var auth = Auth(db);
        Assert.Equal(CustomerAuthResult.InvalidInput, (await auth.RegisterAsync(Register("dogrula", email: "gecersiz"), default)).Result);
        Assert.Equal(CustomerAuthResult.InvalidInput, (await auth.RegisterAsync(Register("dogrula", password: "kisa"), default)).Result);
        Assert.Equal(CustomerAuthResult.InvalidInput, (await auth.RegisterAsync(Register("dogrula") with { Phone = "abc" }, default)).Result);
        Assert.Equal(CustomerAuthResult.InvalidInput, (await auth.RegisterAsync(Register("dogrula") with { FullName = "A" }, default)).Result);
        Assert.Equal(CustomerAuthResult.TermsRequired, (await auth.RegisterAsync(Register("dogrula", terms: false), default)).Result);
        Assert.Equal(CustomerAuthResult.UnknownShop, (await auth.RegisterAsync(Register("kapali"), default)).Result);
        Assert.Equal(CustomerAuthResult.UnknownShop, (await auth.RegisterAsync(Register("yok"), default)).Result);
    }

    [Fact]
    public async Task Giris_dogru_parolayla_calisir_hatali_parola_ve_bilinmeyen_eposta_ayni_sonucu_verir()
    {
        await ShopAsync("giris");
        await using var db = NewDb();
        var auth = Auth(db);
        await auth.RegisterAsync(Register("giris"), default);

        Assert.Equal(CustomerAuthResult.Ok, (await auth.LoginAsync(new("giris", " AYSE@mail.com ", "Parola123"), default)).Result);
        Assert.Equal(CustomerAuthResult.InvalidCredentials, (await auth.LoginAsync(new("giris", "ayse@mail.com", "yanlis-parola"), default)).Result);
        Assert.Equal(CustomerAuthResult.InvalidCredentials, (await auth.LoginAsync(new("giris", "yok@mail.com", "Parola123"), default)).Result);
        Assert.Equal(CustomerAuthResult.InvalidCredentials, (await auth.LoginAsync(new("yok", "ayse@mail.com", "Parola123"), default)).Result);
        Assert.Equal(CustomerAuthResult.InvalidInput, (await auth.LoginAsync(new("giris", "", ""), default)).Result);
    }

    [Fact]
    public async Task Ayni_eposta_iki_magazada_ayri_hesaplardir()
    {
        await ShopAsync("magazaa");
        await ShopAsync("magazab");
        await using var db = NewDb();
        var auth = Auth(db);
        Assert.Equal(CustomerAuthResult.Ok, (await auth.RegisterAsync(Register("magazaa", "ortak@mail.com", "ParolaA123"), default)).Result);
        Assert.Equal(CustomerAuthResult.Ok, (await auth.RegisterAsync(Register("magazab", "ortak@mail.com", "ParolaB123"), default)).Result);
        Assert.Equal(CustomerAuthResult.InvalidCredentials, (await auth.LoginAsync(new("magazab", "ortak@mail.com", "ParolaA123"), default)).Result);
    }

    // --- Google ---

    private static GoogleIdentity Person(string sub = "g-1", string email = "ayse@mail.com", bool verified = true) => new(sub, email, verified, "Ayşe Google", "https://img/p.png");

    [Fact]
    public async Task Google_ile_ilk_giris_hesap_acar_sonra_ayni_hesaba_girer_koşullar_ilk_kayitta_istenir()
    {
        await ShopAsync("google");
        await using var db = NewDb();
        var auth = Auth(db, new FakeGoogle(Person()));

        Assert.Equal(CustomerAuthResult.TermsRequired, (await auth.GoogleLoginAsync(new("google", "ok", false, false), default)).Result);
        var first = await auth.GoogleLoginAsync(new("google", "ok", true, true), default);
        Assert.Equal(CustomerAuthResult.Ok, first.Result);
        Assert.Equal((false, true, true, "Ayşe Google"), (first.Value!.Profile.HasPassword, first.Value.Profile.GoogleLinked, first.Value.Profile.EmailVerified, first.Value.Profile.FullName));

        var again = await auth.GoogleLoginAsync(new("google", "ok", false, false), default); // ikinci girişte koşul sorulmaz
        Assert.Equal(CustomerAuthResult.Ok, again.Result);
        Assert.Equal(first.Value.Profile.Id, again.Value!.Profile.Id);

        // Yalnızca Google hesabı parolayla girilemez.
        Assert.Equal(CustomerAuthResult.InvalidCredentials, (await auth.LoginAsync(new("google", "ayse@mail.com", "Parola123"), default)).Result);
    }

    [Fact]
    public async Task Google_ayni_epostali_mevcut_hesaba_baglanir_ikinci_hesap_acmaz()
    {
        await ShopAsync("baglan");
        await using var db = NewDb();
        var registered = (await Auth(db).RegisterAsync(Register("baglan"), default)).Value!;

        var google = await Auth(db, new FakeGoogle(Person("g-9"))).GoogleLoginAsync(new("baglan", "ok", false, false), default);
        Assert.Equal(registered.Profile.Id, google.Value!.Profile.Id);
        Assert.True(google.Value.Profile.GoogleLinked && google.Value.Profile.HasPassword && google.Value.Profile.EmailVerified);
        Assert.Equal(CustomerAuthResult.Ok, (await Auth(db).LoginAsync(new("baglan", "ayse@mail.com", "Parola123"), default)).Result); // parola da çalışmaya devam eder
    }

    [Fact]
    public async Task Google_dogrulanmamis_eposta_gecersiz_belirtec_ve_yapilandirma_yoksa_reddedilir()
    {
        await ShopAsync("red");
        await using var db = NewDb();
        Assert.Equal(CustomerAuthResult.EmailNotVerified, (await Auth(db, new FakeGoogle(Person(verified: false))).GoogleLoginAsync(new("red", "ok", true, false), default)).Result);
        Assert.Equal(CustomerAuthResult.InvalidGoogleToken, (await Auth(db, new FakeGoogle(Person())).GoogleLoginAsync(new("red", "bad", true, false), default)).Result);
        Assert.Equal(CustomerAuthResult.GoogleNotConfigured, (await Auth(db, new FakeGoogle(Person(), configured: false)).GoogleLoginAsync(new("red", "ok", true, false), default)).Result);
        Assert.Equal(CustomerAuthResult.UnknownShop, (await Auth(db, new FakeGoogle(Person())).GoogleLoginAsync(new("yok", "ok", true, false), default)).Result);
        Assert.Equal(CustomerAuthResult.InvalidInput, (await Auth(db, new FakeGoogle(Person())).GoogleLoginAsync(new("red", "", true, false), default)).Result);
        Assert.Equal(ClientId, await Auth(db, new FakeGoogle(Person())).GoogleClientIdAsync("red", default));
        Assert.Null(await Auth(db, new FakeGoogle(Person(), configured: false)).GoogleClientIdAsync("red", default));
        Assert.Null(await Auth(db, new FakeGoogle(Person())).GoogleClientIdAsync("yok", default));
    }

    // --- Google belirteç doğrulayıcı (gerçek imza, sahte Google sertifika uç noktası) ---

    private sealed class CertHandler(Func<string> json) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json()) });
        }
    }

    private static string Jwks(RSA rsa, string kid)
    {
        var p = rsa.ExportParameters(false);
        return $$"""{"keys":[{"kty":"RSA","kid":"{{kid}}","use":"sig","alg":"RS256","n":"{{Base64UrlEncoder.Encode(p.Modulus!)}}","e":"{{Base64UrlEncoder.Encode(p.Exponent!)}}"}]}""";
    }

    private static string Token(RSA rsa, string kid, string audience = ClientId, string issuer = "https://accounts.google.com", DateTime? expires = null, string email = "ayse@mail.com", string verified = "true")
    {
        var key = new RsaSecurityKey(rsa) { KeyId = kid };
        var claims = new[] { new Claim("sub", "g-123"), new Claim("email", email), new Claim("email_verified", verified), new Claim("name", "Ayşe"), new Claim("picture", "https://img/p.png") };
        var jwt = new JwtSecurityToken(issuer, audience, claims, (expires ?? DateTime.UtcNow.AddMinutes(30)).AddHours(-1), expires ?? DateTime.UtcNow.AddMinutes(30), new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static GoogleIdTokenVerifier Verifier(CertHandler handler, string? clientId = ClientId) =>
        new(new HttpClient(handler), Options.Create(new GoogleOptions { ClientId = clientId }), new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task Google_belirteci_imza_yayinci_hedef_kitle_ve_sure_denetlenerek_dogrulanir()
    {
        using var rsa = RSA.Create(2048);
        using var attacker = RSA.Create(2048);
        var verifier = Verifier(new CertHandler(() => Jwks(rsa, "k1")));

        var ok = await verifier.VerifyAsync(Token(rsa, "k1"));
        Assert.Equal(("g-123", "ayse@mail.com", true), (ok!.Subject, ok.Email, ok.EmailVerified));
        Assert.True((await verifier.VerifyAsync(Token(rsa, "k1", issuer: "accounts.google.com")))!.EmailVerified); // ikinci geçerli yayıncı biçimi

        Assert.Null(await verifier.VerifyAsync(Token(rsa, "k1", audience: "baska-istemci"))); // başka uygulama için üretilmiş belirteç
        Assert.Null(await verifier.VerifyAsync(Token(rsa, "k1", issuer: "https://evil.example")));
        Assert.Null(await verifier.VerifyAsync(Token(rsa, "k1", expires: DateTime.UtcNow.AddMinutes(-10)))); // süresi dolmuş
        Assert.Null(await verifier.VerifyAsync(Token(attacker, "k1"))); // başkasının anahtarıyla imzalanmış
        Assert.Null(await verifier.VerifyAsync("not-a-jwt"));
        Assert.Null(await verifier.VerifyAsync(""));
        Assert.False((await verifier.VerifyAsync(Token(rsa, "k1", verified: "false")))!.EmailVerified);
    }

    [Fact]
    public async Task Google_dogrulayici_yapilandirma_yoksa_kapali_anahtar_donunce_bir_kez_yeniler_sertifikalari_onbellekler()
    {
        using var oldKey = RSA.Create(2048);
        using var newKey = RSA.Create(2048);
        Assert.Null(await Verifier(new CertHandler(() => Jwks(oldKey, "k1")), clientId: null).VerifyAsync(Token(oldKey, "k1"))); // istemci kimliği yok

        var current = Jwks(oldKey, "k1");
        var handler = new CertHandler(() => current);
        var verifier = Verifier(handler);
        Assert.NotNull(await verifier.VerifyAsync(Token(oldKey, "k1")));
        Assert.NotNull(await verifier.VerifyAsync(Token(oldKey, "k1")));
        Assert.Equal(1, handler.Calls); // anahtarlar önbellekte

        current = Jwks(newKey, "k2"); // Google anahtarı döndürdü
        Assert.NotNull(await verifier.VerifyAsync(Token(newKey, "k2")));
        Assert.Equal(2, handler.Calls); // bilinmeyen anahtarda bir kez yenilendi
    }

    // --- hesap: profil, parola, adresler ---

    private static async Task<(Guid Tenant, Guid A, Guid B)> TwoCustomersAsync(string slug)
    {
        var tenant = await ShopAsync(slug);
        await using var db = NewDb();
        var auth = Auth(db);
        var a = (await auth.RegisterAsync(Register(slug, "a@mail.com"), default)).Value!.Profile.Id;
        var b = (await auth.RegisterAsync(Register(slug, "b@mail.com"), default)).Value!.Profile.Id;
        return (tenant, a, b);
    }

    private static AddressInput Addr(string title = "Ev", bool isDefault = false) =>
        new(title, "Ayşe Yılmaz", "0555 123 45 67", "İstanbul", "Kadıköy", "Moda Cad. No:5 D:3", "34710", isDefault);

    [Fact]
    public async Task Profil_guncellenir_dogrulanir_ve_parola_degisimi_mevcut_parolayi_ister()
    {
        var (tenant, a, _) = await TwoCustomersAsync("profil");
        await using var db = NewDb(tenant);
        var account = new CustomerAccountService(db);

        var updated = await account.UpdateProfileAsync(a, new("Ayşe Demir", "0532 000 00 00", true), default);
        Assert.Equal(("Ayşe Demir", true), (updated.FullName, updated.MarketingConsent));
        await Assert.ThrowsAsync<CustomerException>(() => account.UpdateProfileAsync(a, new("A", null, false), default));
        await Assert.ThrowsAsync<CustomerException>(() => account.UpdateProfileAsync(a, new("Ayşe", "abc", false), default));

        await Assert.ThrowsAsync<CustomerException>(() => account.ChangePasswordAsync(a, new("yanlis", "YeniParola123"), default));
        await Assert.ThrowsAsync<CustomerException>(() => account.ChangePasswordAsync(a, new(null, "YeniParola123"), default));
        await Assert.ThrowsAsync<CustomerException>(() => account.ChangePasswordAsync(a, new("Parola123", "kisa"), default));
        await account.ChangePasswordAsync(a, new("Parola123", "YeniParola123"), default);
        await using var authDb = NewDb();
        Assert.Equal(CustomerAuthResult.Ok, (await Auth(authDb).LoginAsync(new("profil", "a@mail.com", "YeniParola123"), default)).Result);
        Assert.Equal(CustomerAuthResult.InvalidCredentials, (await Auth(authDb).LoginAsync(new("profil", "a@mail.com", "Parola123"), default)).Result);
    }

    [Fact]
    public async Task Yalnizca_Google_hesabi_mevcut_parola_istemeden_ilk_parolasini_belirler()
    {
        await ShopAsync("ilkparola");
        await using var authDb = NewDb();
        var id = (await Auth(authDb, new FakeGoogle(Person())).GoogleLoginAsync(new("ilkparola", "ok", true, false), default)).Value!.Profile.Id;
        await using var db = NewDb(await TenantIdAsync("ilkparola"));
        await new CustomerAccountService(db).ChangePasswordAsync(id, new(null, "IlkParola123"), default);
        await using var freshDb = NewDb(); // yeni bağlam: önceki bağlamın izlenen kaydı eski kalır
        Assert.Equal(CustomerAuthResult.Ok, (await Auth(freshDb).LoginAsync(new("ilkparola", "ayse@mail.com", "IlkParola123"), default)).Result);
    }

    [Fact]
    public async Task Adres_defteri_ilk_adresi_varsayilan_yapar_varsayilan_degisir_silinince_devredilir_ve_sinirlanir()
    {
        var (tenant, a, _) = await TwoCustomersAsync("adres");
        await using var db = NewDb(tenant);
        var account = new CustomerAccountService(db);

        var home = await account.AddAddressAsync(a, Addr("Ev"), default);
        Assert.True(home.IsDefault); // ilk adres otomatik varsayılan
        var work = await account.AddAddressAsync(a, Addr("İş"), default);
        Assert.False(work.IsDefault);

        await account.UpdateAddressAsync(a, work.Id, Addr("İş", isDefault: true), default);
        var list = await account.AddressesAsync(a, default);
        Assert.Equal(["İş", "Ev"], list.Select(x => x.Title).ToArray()); // varsayılan başta
        Assert.Single(list, x => x.IsDefault);

        await account.DeleteAddressAsync(a, work.Id, default); // varsayılan silinince kalan adres devralır
        Assert.True((await account.AddressesAsync(a, default)).Single().IsDefault);

        for (var i = 0; i < CustomerAddress.MaxPerCustomer - 1; i++) await account.AddAddressAsync(a, Addr($"Adres {i}"), default);
        Assert.Equal(409, (await Assert.ThrowsAsync<CustomerException>(() => account.AddAddressAsync(a, Addr("Fazla"), default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<CustomerException>(() => account.AddAddressAsync(a, Addr() with { City = "" }, default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<CustomerException>(() => account.UpdateAddressAsync(a, home.Id, Addr() with { PostalCode = "12" }, default))).Status);
    }

    [Fact]
    public async Task Musteri_baskasinin_adresini_goremez_degistiremez_silemez()
    {
        var (tenant, a, b) = await TwoCustomersAsync("yalitim");
        await using var db = NewDb(tenant);
        var account = new CustomerAccountService(db);
        var mine = await account.AddAddressAsync(a, Addr(), default);

        Assert.Empty(await account.AddressesAsync(b, default));
        Assert.Equal(404, (await Assert.ThrowsAsync<CustomerException>(() => account.UpdateAddressAsync(b, mine.Id, Addr("Ele geçirildi"), default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<CustomerException>(() => account.DeleteAddressAsync(b, mine.Id, default))).Status);
        Assert.Equal("Ev", (await account.AddressesAsync(a, default)).Single().Title);
    }

    // --- siparişler ---

    private static Order MakeOrder(Guid tenant, Guid? customer, string number, OrderStatus status, string channel = "B2C", DateTime? date = null) => new()
    {
        TenantId = tenant, Channel = channel, OrderNumber = number, Status = status, OrderDate = date ?? DateTime.UtcNow, CustomerId = customer, CustomerName = "Ayşe",
        TotalPrice = 250m, Currency = "TRY", ShippingAddress = "Moda Cad. No:5 Kadıköy / İstanbul",
        Packages = [new OrderPackage { TenantId = tenant, Channel = channel, PackageId = number, Status = status, ChannelStatus = status.ToString(), TotalPrice = 250m,
            Lines = [new OrderLine { TenantId = tenant, LineId = "1", Barcode = "X1", Name = "Tişört", Quantity = 2, Price = 100m, VatRate = 20 }, new OrderLine { TenantId = tenant, LineId = "2", Barcode = "X2", Name = "Kalem", Quantity = 1, Price = 50m, VatRate = 20 }] }],
    };

    [Fact]
    public async Task Siparisler_yalnizca_kendi_B2C_siparisleri_yeniden_eskiye_ve_sayfali_gelir_baskasinin_ayrintisi_404()
    {
        var (tenant, a, b) = await TwoCustomersAsync("siparis");
        await using (var seed = NewDb(tenant))
        {
            for (var i = 1; i <= 12; i++) seed.Orders.Add(MakeOrder(tenant, a, $"B2C-{i:00}", i == 1 ? OrderStatus.Delivered : OrderStatus.New, date: DateTime.UtcNow.AddHours(-20 + i)));
            seed.Orders.Add(MakeOrder(tenant, b, "B2C-B", OrderStatus.New));
            seed.Orders.Add(MakeOrder(tenant, a, "TY-1", OrderStatus.New, channel: "Trendyol")); // başka kanalın siparişi sayılmaz
            await seed.SaveChangesAsync();
        }

        await using var db = NewDb(tenant);
        var account = new CustomerAccountService(db);
        var first = await account.OrdersAsync(a, 1, default);
        Assert.Equal((12, 10), (first.Total, first.Items.Count));
        Assert.Equal("B2C-12", first.Items[0].OrderNumber); // en yeni önce
        Assert.Equal((3, "Tişört"), (first.Items[0].ItemCount, first.Items[0].FirstItemName));
        Assert.Equal(2, (await account.OrdersAsync(a, 2, default)).Items.Count);
        Assert.Equal(1, (await account.OrdersAsync(b, 1, default)).Total);

        var detail = await account.OrderAsync(a, first.Items[0].Id, default);
        Assert.Equal((2, "Tişört", 100m), (detail.Lines.Count, detail.Lines[0].Name, detail.Lines[0].UnitPrice));
        Assert.Equal("Moda Cad. No:5 Kadıköy / İstanbul", detail.ShippingAddress);
        Assert.Equal(404, (await Assert.ThrowsAsync<CustomerException>(() => account.OrderAsync(b, first.Items[0].Id, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<CustomerException>(() => account.OrderAsync(a, Guid.NewGuid(), default))).Status);
    }

    [Fact]
    public async Task Baska_kiracinin_musterisi_ve_siparisi_gorunmez()
    {
        var one = await TwoCustomersAsync("kiraci1");
        var two = await TwoCustomersAsync("kiraci2");
        await using (var seed = NewDb(one.Tenant)) { seed.Orders.Add(MakeOrder(one.Tenant, one.A, "B2C-1", OrderStatus.New)); await seed.SaveChangesAsync(); }

        await using var db = NewDb(two.Tenant);
        var account = new CustomerAccountService(db);
        Assert.Equal(0, (await account.OrdersAsync(one.A, 1, default)).Total); // başka kiracının müşteri kimliğiyle de sipariş çıkmaz
        Assert.Equal(404, (await Assert.ThrowsAsync<CustomerException>(() => account.GetProfileAsync(one.A, default))).Status);
    }

    // --- token ayrımı ---

    private static ClaimsPrincipal Principal(string? principal)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()) };
        if (principal is not null) claims.Add(new Claim(AppClaims.Principal, principal));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public async Task Musteri_token_yonetim_ve_bayi_API_sine_giremez_yalnizca_musteri_API_sine_girer()
    {
        var sp = new ServiceCollection().AddLogging().AddAuthorization(AuthSetup.Configure).BuildServiceProvider();
        var authz = sp.GetRequiredService<IAuthorizationService>();
        var provider = sp.GetRequiredService<IAuthorizationPolicyProvider>();
        var fallback = (await provider.GetFallbackPolicyAsync())!;
        var dealer = (await provider.GetPolicyAsync(AuthPolicies.Dealer))!;
        var customer = (await provider.GetPolicyAsync(AuthPolicies.Customer))!;

        async Task<(bool, bool, bool)> Eval(ClaimsPrincipal u) =>
            ((await authz.AuthorizeAsync(u, null, fallback)).Succeeded, (await authz.AuthorizeAsync(u, null, dealer)).Succeeded, (await authz.AuthorizeAsync(u, null, customer)).Succeeded);

        Assert.Equal((false, false, true), await Eval(Principal("customer")));
        Assert.Equal((false, true, false), await Eval(Principal("dealer")));
        Assert.Equal((true, false, false), await Eval(Principal("admin")));
        Assert.Equal((true, false, false), await Eval(Principal(null))); // eski yönetici token'ı
    }

    [Fact]
    public void Musteri_sifresi_hashlidir_ve_dogrulanabilir()
    {
        var hasher = new PasswordHasher<Customer>();
        var c = new Customer { Email = "a@b.com" };
        c.PasswordHash = hasher.HashPassword(c, "Parola123");
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(c, c.PasswordHash, "Parola123"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(c, c.PasswordHash, "Parola124"));
    }
}
