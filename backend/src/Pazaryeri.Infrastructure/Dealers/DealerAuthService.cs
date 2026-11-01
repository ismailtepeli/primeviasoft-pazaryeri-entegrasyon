using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Auth;
using Pazaryeri.Application.Dealers;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Notifications;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Dealers;

/// <summary>
/// Bayi sitesi kimlik doğrulaması: yönetici girişinden ayrıdır (ayrı kullanıcı tablosu, ayrı token hedef kitlesi ve "dealer" kimliği).
/// Giriş öncesi kiracı bilinmediği için kiracı slug'ı ile bulunur ve işlem boyunca ortam kiracısı olarak kullanılır.
/// </summary>
public class DealerAuthService(AppDbContext db, ITokenService tokens, IModuleService modules, TimeProvider time,
    IEmailSender? email = null, StoreLinkOptions? links = null, INotifier? notifier = null) : IDealerAuthService
{
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan ResetCooldown = TimeSpan.FromMinutes(1);
    private const string InvalidLink = "Bağlantı geçersiz ya da süresi dolmuş.";

    private readonly PasswordHasher<DealerUser> _hasher = new();
    private readonly StoreLinkOptions _links = links ?? StoreLinkOptions.Default;

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
        if (notifier is not null)
            await notifier.NotifyAdminAsync(NotificationKinds.DealerApplication, "Yeni bayi başvurusu", $"{dealer.Name} · VN {dealer.TaxNumber}", "/bayiler", $"dealer-apply:{dealer.Id}", ct);
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

    public async Task<DealerAuthOutcome<bool>> ChangePasswordAsync(Guid userId, DealerPasswordChange change, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(change.CurrentPassword)) return new(DealerAuthResult.InvalidInput, Error: "Mevcut parola zorunlu.");
        var error = DealerRules.ValidatePassword(change.NewPassword);
        if (error is not null) return new(DealerAuthResult.InvalidInput, Error: error);
        if (change.NewPassword == change.CurrentPassword) return new(DealerAuthResult.InvalidInput, Error: "Yeni parola mevcut parolayla aynı olamaz.");

        var user = await db.DealerUsers.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        if (user is null || _hasher.VerifyHashedPassword(user, user.PasswordHash, change.CurrentPassword) == PasswordVerificationResult.Failed)
            return new(DealerAuthResult.InvalidCredentials, Error: "Mevcut parola hatalı.");
        user.PasswordHash = _hasher.HashPassword(user, change.NewPassword!);
        await RevokeResetsAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
        return new(DealerAuthResult.Ok, true);
    }

    public async Task<DealerAuthOutcome<bool>> RequestPasswordResetAsync(DealerPasswordResetRequest request, CancellationToken ct)
    {
        var invalid = DealerRules.ValidateEmail(request.Email);
        if (invalid is not null) return new(DealerAuthResult.InvalidInput, Error: invalid);
        var tenantId = await ResolveTenantAsync(request.TenantSlug, ct);
        if (tenantId is null) return new(DealerAuthResult.UnknownTenant);

        using var _ = AmbientTenant.Use(tenantId.Value);
        var address = DealerRules.NormalizeEmail(request.Email!);
        var user = await db.DealerUsers.FirstOrDefaultAsync(u => u.Email == address && u.IsActive, ct);
        var now = time.GetUtcNow().UtcDateTime;
        // Kayıtsız e-posta, aktif olmayan bayi ve sık tekrar aynı sonucu verir; yalnızca uygun durumda e-posta gider.
        if (user is null || email is null) return new(DealerAuthResult.Ok, true);
        var dealer = await db.Dealers.FirstAsync(d => d.Id == user.DealerId, ct);
        if (dealer.Status != DealerStatus.Active) return new(DealerAuthResult.Ok, true);
        if (await db.DealerPasswordResets.AnyAsync(r => r.DealerUserId == user.Id && r.CreatedAt > now - ResetCooldown, ct)) return new(DealerAuthResult.Ok, true);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        db.DealerPasswordResets.Add(new DealerPasswordReset
        {
            TenantId = tenantId.Value, DealerUserId = user.Id, TokenHash = Hash(token), ExpiresAt = now + ResetLifetime,
        });
        await db.SaveChangesAsync(ct);
        var slug = request.TenantSlug!.Trim().ToLowerInvariant();
        await email.SendAsync(user.Email, "Parola sıfırlama",
            $"Merhaba {user.FullName},\n\nParolanızı sıfırlamak için bağlantıya tıklayın (60 dakika geçerli, tek kullanımlık):\n{_links.Reset(slug, token)}\n\nBu isteği siz yapmadıysanız bu iletiyi yok sayabilirsiniz.", ct);
        return new(DealerAuthResult.Ok, true);
    }

    public async Task<DealerAuthOutcome<bool>> ResetPasswordAsync(DealerPasswordResetConfirm confirm, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(confirm.Token)) return new(DealerAuthResult.InvalidToken, Error: InvalidLink);
        var error = DealerRules.ValidatePassword(confirm.NewPassword);
        if (error is not null) return new(DealerAuthResult.InvalidInput, Error: error);
        var tenantId = await ResolveTenantAsync(confirm.TenantSlug, ct);
        if (tenantId is null) return new(DealerAuthResult.InvalidToken, Error: InvalidLink);

        using var _ = AmbientTenant.Use(tenantId.Value);
        var now = time.GetUtcNow().UtcDateTime;
        var hash = Hash(confirm.Token.Trim());
        // Tek kullanım koşullu güncellemeyle sahiplenilir; eşzamanlı ikinci istek reddedilir.
        var claimed = await db.DealerPasswordResets
            .Where(r => r.TokenHash == hash && r.UsedAt == null && r.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsedAt, now), ct);
        if (claimed == 0) return new(DealerAuthResult.InvalidToken, Error: InvalidLink);

        var reset = await db.DealerPasswordResets.AsNoTracking().FirstAsync(r => r.TokenHash == hash, ct);
        var user = await db.DealerUsers.FirstOrDefaultAsync(u => u.Id == reset.DealerUserId && u.IsActive, ct);
        if (user is null) return new(DealerAuthResult.InvalidToken, Error: InvalidLink);
        user.PasswordHash = _hasher.HashPassword(user, confirm.NewPassword!);
        await RevokeResetsAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
        return new(DealerAuthResult.Ok, true);
    }

    /// <summary>Kullanılmamış sıfırlama bağlantılarını geçersiz kılar.</summary>
    private async Task RevokeResetsAsync(Guid userId, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await db.DealerPasswordResets.Where(r => r.DealerUserId == userId && r.UsedAt == null).ExecuteUpdateAsync(s => s.SetProperty(r => r.UsedAt, now), ct);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public async Task<StoreInfo?> InfoAsync(string? slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var key = slug.Trim().ToLowerInvariant();
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == key && t.IsActive, ct);
        if (tenant is null) return null;
        var available = await modules.IsEnabledAsync(ModuleIds.Dealers, tenant.Id, ct) && await modules.IsEnabledAsync(ModuleIds.B2BStore, tenant.Id, ct);
        return new StoreInfo(tenant.Name, available);
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
