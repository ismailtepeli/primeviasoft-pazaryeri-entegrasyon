using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Auth;
using Pazaryeri.Application.Dealers;
using Pazaryeri.Application.Modules;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Dealers;

/// <summary>
/// Bayi sitesi kimlik doğrulaması: yönetici girişinden ayrıdır (ayrı kullanıcı tablosu, ayrı token hedef kitlesi ve "dealer" kimliği).
/// Giriş öncesi kiracı bilinmediği için kiracı slug'ı ile bulunur ve işlem boyunca ortam kiracısı olarak kullanılır.
/// </summary>
public class DealerAuthService(AppDbContext db, ITokenService tokens, IModuleService modules, TimeProvider time) : IDealerAuthService
{
    private readonly PasswordHasher<DealerUser> _hasher = new();

    public async Task<DealerAuthOutcome<Guid>> ApplyAsync(DealerApplication a, CancellationToken ct)
    {
        var error = Validate(a);
        if (error is not null) return new(DealerAuthResult.InvalidInput, Error: error);
        var tenantId = await ResolveTenantAsync(a.TenantSlug, ct);
        if (tenantId is null) return new(DealerAuthResult.UnknownTenant);

        using var _ = AmbientTenant.Use(tenantId.Value);
        var email = DealerRules.NormalizeEmail(a.Email!);
        var taxNumber = a.TaxNumber!.Trim();
        if (await db.DealerUsers.AnyAsync(u => u.Email == email, ct) || await db.Dealers.AnyAsync(d => d.TaxNumber == taxNumber, ct))
            return new(DealerAuthResult.Duplicate, Error: "Bu e-posta ya da vergi numarasıyla zaten bir kayıt var.");

        var dealer = new Dealer
        {
            TenantId = tenantId.Value, Name = a.CompanyName!.Trim(), TaxNumber = taxNumber, TaxOffice = DealerRules.Clean(a.TaxOffice),
            ContactName = DealerRules.Clean(a.ContactName), Email = email, Phone = DealerRules.Clean(a.Phone), City = DealerRules.Clean(a.City),
            Address = DealerRules.Clean(a.Address), Status = DealerStatus.Pending,
        };
        var user = new DealerUser
        {
            TenantId = tenantId.Value, DealerId = dealer.Id, Email = email, Role = DealerUserRole.Owner,
            FullName = DealerRules.Clean(a.ContactName) ?? dealer.Name,
        };
        user.PasswordHash = _hasher.HashPassword(user, a.Password!);
        db.Dealers.Add(dealer);
        db.DealerUsers.Add(user);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return new(DealerAuthResult.Duplicate, Error: "Bu e-posta ya da vergi numarasıyla zaten bir kayıt var."); } // eşzamanlı aynı başvuru
        return new(DealerAuthResult.Ok, dealer.Id);
    }

    public async Task<DealerAuthOutcome<DealerLoginResponse>> LoginAsync(DealerLogin login, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(login.Email) || string.IsNullOrEmpty(login.Password)) return new(DealerAuthResult.InvalidInput, Error: "E-posta ve parola zorunlu.");
        var tenantId = await ResolveTenantAsync(login.TenantSlug, ct);
        if (tenantId is null) return new(DealerAuthResult.InvalidCredentials);

        using var _ = AmbientTenant.Use(tenantId.Value);
        var email = DealerRules.NormalizeEmail(login.Email);
        var user = await db.DealerUsers.FirstOrDefaultAsync(u => u.Email == email && u.IsActive, ct);
        if (user is null || _hasher.VerifyHashedPassword(user, user.PasswordHash, login.Password) == PasswordVerificationResult.Failed)
            return new(DealerAuthResult.InvalidCredentials);

        var dealer = await db.Dealers.FirstAsync(d => d.Id == user.DealerId, ct);
        // Durum, parola doğrulandıktan sonra bildirilir; böylece yabancılar hangi e-postaların kayıtlı olduğunu öğrenemez.
        if (dealer.Status == DealerStatus.Pending) return new(DealerAuthResult.Pending, Error: "Başvurunuz henüz onaylanmadı.");
        if (dealer.Status == DealerStatus.Passive) return new(DealerAuthResult.Passive, Error: "Hesabınız pasif; lütfen firmayla iletişime geçin.");

        user.LastLoginAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        var (token, expires) = tokens.CreateForDealer(user, dealer);
        return new(DealerAuthResult.Ok, new DealerLoginResponse(token, expires,
            new DealerInfo(user.Id, dealer.Id, tenantId.Value, user.Email, user.FullName, dealer.Name, user.Role.ToString())));
    }

    private async Task<Guid?> ResolveTenantAsync(string? slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var key = slug.Trim().ToLowerInvariant();
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == key && t.IsActive, ct);
        if (tenant is null) return null;
        return await modules.IsEnabledAsync(ModuleIds.Dealers, tenant.Id, ct) ? tenant.Id : null; // modül kapalıysa site yokmuş gibi davranır
    }

    private static string? Validate(DealerApplication a) =>
        string.IsNullOrWhiteSpace(a.CompanyName) ? "Firma adı zorunlu."
        : DealerRules.ValidateLength(a.CompanyName, 200, "Firma adı")
        ?? DealerRules.ValidateTaxNumber(a.TaxNumber)
        ?? DealerRules.ValidateEmail(a.Email)
        ?? DealerRules.ValidatePassword(a.Password)
        ?? DealerRules.ValidateLength(a.TaxOffice, 100, "Vergi dairesi")
        ?? DealerRules.ValidateLength(a.ContactName, 150, "Yetkili adı")
        ?? DealerRules.ValidateLength(a.Phone, 40, "Telefon")
        ?? DealerRules.ValidateLength(a.City, 100, "Şehir")
        ?? DealerRules.ValidateLength(a.Address, 500, "Adres");
}
