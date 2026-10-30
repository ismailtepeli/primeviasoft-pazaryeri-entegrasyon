using Pazaryeri.Domain;

namespace Pazaryeri.Application.Dealers;

/// <summary>Bayi sitesinden gelen başvuru. Kiracı şimdilik slug ile bulunur (alt alan adı çözümlemesi #68 ile gelecek).</summary>
public record DealerApplication(string? TenantSlug, string? CompanyName, string? TaxNumber, string? TaxOffice, string? ContactName,
    string? Email, string? Phone, string? City, string? Address, string? Password);

public record DealerLogin(string? TenantSlug, string? Email, string? Password);

public record DealerInfo(Guid UserId, Guid DealerId, Guid TenantId, string Email, string FullName, string DealerName, string Role);

public record DealerLoginResponse(string AccessToken, DateTime ExpiresAt, DealerInfo User);

public enum DealerAuthResult { Ok, InvalidInput, UnknownTenant, Duplicate, InvalidCredentials, Pending, Passive }

public record DealerAuthOutcome<T>(DealerAuthResult Result, T? Value = default, string? Error = null);

public interface IDealerAuthService
{
    /// <summary>Başvuru oluşturur: Beklemede bayi + firma yetkilisi kullanıcısı. Onaylanana kadar giriş yapılamaz.</summary>
    Task<DealerAuthOutcome<Guid>> ApplyAsync(DealerApplication application, CancellationToken ct);

    Task<DealerAuthOutcome<DealerLoginResponse>> LoginAsync(DealerLogin login, CancellationToken ct);
}

/// <summary>Alanlarla ilgili ortak doğrulamalar (yönetici ve bayi sitesi aynı kuralları kullanır).</summary>
public static class DealerRules
{
    public const int MinPasswordLength = 8;

    public static string? ValidateTaxNumber(string? value) =>
        string.IsNullOrWhiteSpace(value) || !(value.Length is 10 or 11) || !value.All(char.IsAsciiDigit)
            ? "Vergi numarası 10 (VKN) ya da 11 (TCKN) haneli rakam olmalı." : null;

    public static string? ValidateEmail(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 200 || !System.Net.Mail.MailAddress.TryCreate(value.Trim(), out _) || !value.Contains('.') || !value.Contains('@')
            ? "Geçerli bir e-posta girin." : null;

    public static string? ValidatePassword(string? value) =>
        string.IsNullOrEmpty(value) || value.Length < MinPasswordLength || value.Length > 100
            ? $"Parola en az {MinPasswordLength} karakter olmalı." : null;

    /// <summary>Boş bırakılabilen metin alanı için uzunluk denetimi.</summary>
    public static string? ValidateLength(string? value, int max, string label) =>
        value is not null && value.Trim().Length > max ? $"{label} en fazla {max} karakter olabilir." : null;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
