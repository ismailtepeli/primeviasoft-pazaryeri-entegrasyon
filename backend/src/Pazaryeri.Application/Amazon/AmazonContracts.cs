namespace Pazaryeri.Application.Amazon;

/// <summary>
/// Amazon SP-API için Login with Amazon (LWA) uygulama ayarları. Değerler Seller Central → Uygulamalar ve Hizmetler →
/// Uygulama Geliştir bölümünde kayıtlı uygulamadan gelir; üretimde ortam değişkeni/Key Vault ile verilmeli.
/// </summary>
public class AmazonLwaOptions
{
    public const string Section = "Amazon:Lwa";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    /// <summary>Seller Central'daki uygulama kimliği (amzn1.sp.solution...).</summary>
    public string ApplicationId { get; set; } = string.Empty;
    /// <summary>Amazon'un yetkiden sonra yönlendireceği adres: bu API'nin /api/oauth/amazon/callback adresi.</summary>
    public string RedirectUri { get; set; } = string.Empty;
    /// <summary>Yetkilendirme sonrası kullanıcının döneceği ön yüz adresi.</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
    public string SellerCentralUrl { get; set; } = "https://sellercentral.amazon.com.tr";
    public string TokenUrl { get; set; } = "https://api.amazon.com/auth/o2/token";
    /// <summary>Uygulamanın client secret'ının oluşturulduğu/yenilendiği gün. Secret 180 gün geçerli.</summary>
    public DateTime? ClientSecretIssuedAt { get; set; }
    /// <summary>İzinsiz taslak (draft) uygulama ile test için "beta".</summary>
    public bool Draft { get; set; }

    public const int SecretLifetimeDays = 180;
    /// <summary>Bitişe bu kadar gün kala hatırlatma gösterilir.</summary>
    public const int ReminderDays = 30;

    public bool Configured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(ApplicationId) && !string.IsNullOrWhiteSpace(RedirectUri);
}

/// <param name="Configured">Sunucuda LWA uygulama ayarları tanımlı mı.</param>
/// <param name="Authorized">Satıcı yetki verdi ve refresh token kayıtlı mı.</param>
/// <param name="DaysUntilSecretExpiry">Client secret bitişine kalan gün (negatifse süresi dolmuş); tarih tanımsızsa null.</param>
/// <param name="SecretReminder">Bitişe ReminderDays veya daha az kaldıysa (ya da dolduysa) true.</param>
public record AmazonOAuthStatus(
    bool Configured,
    bool Authorized,
    string? SellingPartnerId,
    DateTime? ClientSecretExpiresAt,
    int? DaysUntilSecretExpiry,
    bool SecretReminder);

/// <summary>Yetki akışında kullanıcıya gösterilebilecek bir hata.</summary>
public class AmazonOAuthException(string message) : Exception(message);

public interface IAmazonOAuthService
{
    /// <summary>Satıcıyı Seller Central onay sayfasına götüren adres; state kiracıya bağlı ve kısa ömürlüdür.</summary>
    string BuildAuthorizationUrl();

    /// <summary>Amazon geri dönüşünü tamamlar: state'i doğrular, kodu refresh token ile takas eder, şifreli saklar.</summary>
    Task CompleteAsync(string? code, string? state, string? sellingPartnerId, CancellationToken ct = default);

    Task<AmazonOAuthStatus> GetStatusAsync(CancellationToken ct = default);
}
