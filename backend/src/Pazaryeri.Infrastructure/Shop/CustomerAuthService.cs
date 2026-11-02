using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Auth;
using Pazaryeri.Application.Dealers;
using Pazaryeri.Application.Shop;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Shop;

/// <summary>
/// B2C mağaza müşteri girişi: e-posta + parola ve Google ile. Giriş öncesi kiracı bilinmediği için slug ile bulunur (B2C mağaza modülü açık olmalı)
/// ve işlem boyunca ortam kiracısı olarak kullanılır. Hatalı giriş hangi e-postaların kayıtlı olduğunu sızdırmaz.
/// </summary>
public class CustomerAuthService(AppDbContext db, ShopCatalogService shop, ITokenService tokens, IGoogleIdTokenVerifier google, TimeProvider time)
{
    private readonly PasswordHasher<Customer> _hasher = new();

    public static CustomerProfile ToProfile(Customer c) =>
        new(c.Id, c.Email, c.FullName, c.Phone, !string.IsNullOrEmpty(c.PasswordHash), c.GoogleSubject is not null, c.AvatarUrl, c.EmailVerified, c.MarketingConsent, c.CreatedAt);

    private CustomerSession Session(Customer c)
    {
        var (token, expires) = tokens.CreateForCustomer(c);
        return new CustomerSession(token, expires, ToProfile(c));
    }

    /// <summary>Girişte Google düğmesini kurmak için herkese açık yapılandırma.</summary>
    public async Task<string?> GoogleClientIdAsync(string? slug, CancellationToken ct) =>
        await shop.ResolveAsync(slug, ct) is null ? null : google.ClientId;

    public async Task<CustomerAuthOutcome<CustomerSession>> RegisterAsync(CustomerRegister r, CancellationToken ct)
    {
        var error = CustomerRules.ValidateRegistration(r);
        if (error is not null) return new(CustomerAuthResult.InvalidInput, Error: error);
        if (!r.AcceptTerms) return new(CustomerAuthResult.TermsRequired, Error: "Üyelik koşullarını ve aydınlatma metnini kabul etmelisiniz.");
        if (await shop.ResolveAsync(r.Slug, ct) is not { } tenantId) return new(CustomerAuthResult.UnknownShop);

        using var _ = AmbientTenant.Use(tenantId);
        var email = DealerRules.NormalizeEmail(r.Email!);
        if (await db.Customers.AnyAsync(c => c.Email == email, ct))
            return new(CustomerAuthResult.Duplicate, Error: "Bu e-posta ile zaten bir hesap var. Giriş yapın ya da (hesabınız Google ile açıldıysa) Google ile devam edin.");

        var now = time.GetUtcNow().UtcDateTime;
        var customer = new Customer
        {
            TenantId = tenantId, Email = email, FullName = r.FullName!.Trim(), Phone = DealerRules.Clean(r.Phone), MarketingConsent = r.MarketingConsent,
            TermsAcceptedAt = now, LastLoginAt = now,
        };
        customer.PasswordHash = _hasher.HashPassword(customer, r.Password!);
        db.Customers.Add(customer);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return new(CustomerAuthResult.Duplicate, Error: "Bu e-posta ile zaten bir hesap var."); } // eşzamanlı aynı kayıt
        return new(CustomerAuthResult.Ok, Session(customer));
    }

    public async Task<CustomerAuthOutcome<CustomerSession>> LoginAsync(CustomerLogin l, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(l.Email) || string.IsNullOrEmpty(l.Password)) return new(CustomerAuthResult.InvalidInput, Error: "E-posta ve parola zorunlu.");
        if (await shop.ResolveAsync(l.Slug, ct) is not { } tenantId) return new(CustomerAuthResult.InvalidCredentials);

        using var _ = AmbientTenant.Use(tenantId);
        var email = DealerRules.NormalizeEmail(l.Email);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Email == email, ct);
        // Parolası olmayan (yalnızca Google) hesap parolayla girilemez; neden söylenmez.
        if (customer is null || string.IsNullOrEmpty(customer.PasswordHash) || _hasher.VerifyHashedPassword(customer, customer.PasswordHash, l.Password) == PasswordVerificationResult.Failed)
            return new(CustomerAuthResult.InvalidCredentials);
        if (!customer.IsActive) return new(CustomerAuthResult.Inactive, Error: "Hesabınız kapatılmış; lütfen mağazayla iletişime geçin.");

        customer.LastLoginAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return new(CustomerAuthResult.Ok, Session(customer));
    }

    public async Task<CustomerAuthOutcome<CustomerSession>> GoogleLoginAsync(CustomerGoogleLogin g, CancellationToken ct)
    {
        if (!google.Configured) return new(CustomerAuthResult.GoogleNotConfigured, Error: "Google ile giriş bu mağazada henüz açık değil.");
        if (string.IsNullOrWhiteSpace(g.IdToken)) return new(CustomerAuthResult.InvalidInput, Error: "Google kimlik belirteci gerekli.");
        if (await shop.ResolveAsync(g.Slug, ct) is not { } tenantId) return new(CustomerAuthResult.UnknownShop);
        var identity = await google.VerifyAsync(g.IdToken, ct);
        if (identity is null) return new(CustomerAuthResult.InvalidGoogleToken, Error: "Google girişi doğrulanamadı; tekrar deneyin.");
        if (!identity.EmailVerified) return new(CustomerAuthResult.EmailNotVerified, Error: "Google hesabınızın e-postası doğrulanmamış.");

        using var _ = AmbientTenant.Use(tenantId);
        var now = time.GetUtcNow().UtcDateTime;
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.GoogleSubject == identity.Subject, ct);
        if (customer is null)
        {
            customer = await db.Customers.FirstOrDefaultAsync(c => c.Email == identity.Email, ct);
            if (customer is not null)
            {
                // Aynı e-postayla açılmış hesap: Google doğrulanmış e-posta verdiği için hesaba bağlanır (ikinci hesap açılmaz).
                customer.GoogleSubject = identity.Subject;
                customer.EmailVerified = true;
                customer.AvatarUrl ??= Trim(identity.Picture, 500);
            }
            else
            {
                if (!g.AcceptTerms) return new(CustomerAuthResult.TermsRequired, Error: "Üyelik koşullarını ve aydınlatma metnini kabul ederek devam edin.");
                customer = new Customer
                {
                    TenantId = tenantId, Email = identity.Email, FullName = Trim(identity.Name, 200) ?? identity.Email.Split('@')[0], GoogleSubject = identity.Subject,
                    AvatarUrl = Trim(identity.Picture, 500), EmailVerified = true, MarketingConsent = g.MarketingConsent, TermsAcceptedAt = now,
                };
                db.Customers.Add(customer);
            }
        }
        if (!customer.IsActive) return new(CustomerAuthResult.Inactive, Error: "Hesabınız kapatılmış; lütfen mağazayla iletişime geçin.");

        customer.LastLoginAt = now;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) // eşzamanlı ilk giriş: diğer istek hesabı oluşturdu
        {
            db.ChangeTracker.Clear();
            customer = await db.Customers.FirstOrDefaultAsync(c => c.GoogleSubject == identity.Subject || c.Email == identity.Email, ct);
            if (customer is null) return new(CustomerAuthResult.InvalidGoogleToken, Error: "Google girişi tamamlanamadı; tekrar deneyin.");
        }
        return new(CustomerAuthResult.Ok, Session(customer));
    }

    private static string? Trim(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];
}

/// <summary>Müşteri girdi kuralları (kayıt, profil, adres); sunucu ve testler aynı kuralı kullanır.</summary>
public static class CustomerRules
{
    public const int MaxName = 200;

    public static string? ValidateRegistration(CustomerRegister r) =>
        DealerRules.ValidateEmail(r.Email) ?? DealerRules.ValidatePassword(r.Password) ?? ValidateName(r.FullName) ?? ValidatePhone(r.Phone, required: false);

    public static string? ValidateName(string? name)
    {
        var t = name?.Trim() ?? "";
        return t.Length < 2 ? "Ad soyad en az 2 karakter olmalı." : t.Length > MaxName ? $"Ad soyad en fazla {MaxName} karakter olabilir." : null;
    }

    /// <summary>Telefon: rakam, boşluk, +, -, parantez; en az 10, en çok 15 rakam.</summary>
    public static string? ValidatePhone(string? phone, bool required)
    {
        var t = phone?.Trim() ?? "";
        if (t.Length == 0) return required ? "Telefon zorunlu." : null;
        if (t.Length > 30 || t.Any(ch => !(char.IsDigit(ch) || ch is ' ' or '+' or '-' or '(' or ')'))) return "Telefon numarası geçersiz.";
        var digits = t.Count(char.IsDigit);
        return digits is < 10 or > 15 ? "Telefon numarası 10 ile 15 rakam arasında olmalı." : null;
    }

    public static string? ValidateAddress(AddressInput a)
    {
        string Text(string? v) => v?.Trim() ?? "";
        if (Text(a.Title).Length is 0 or > 60) return "Adres başlığı zorunlu ve en fazla 60 karakter olmalı (ör. Ev, İş).";
        if (ValidateName(a.FullName) is { } nameError) return "Teslim alacak kişi: " + nameError;
        if (ValidatePhone(a.Phone, required: true) is { } phoneError) return phoneError;
        if (Text(a.City).Length is 0 or > 100) return "İl zorunlu.";
        if (Text(a.District).Length is 0 or > 100) return "İlçe zorunlu.";
        if (Text(a.AddressLine).Length is < 5 or > 500) return "Açık adres en az 5, en çok 500 karakter olmalı.";
        if (Text(a.PostalCode).Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(Text(a.PostalCode), @"^\d{5}$")) return "Posta kodu 5 haneli olmalı.";
        return null;
    }
}
