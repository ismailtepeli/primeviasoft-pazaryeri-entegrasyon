using Pazaryeri.Application.Marketplace;
using Pazaryeri.Domain;

namespace Pazaryeri.Application.Credentials;

/// <summary>Gizli bilgileri şifreler. Üretimde anahtar KMS/Key Vault'tan gelir; arayüz aynı kalır.</summary>
public interface ICredentialProtector
{
    /// <param name="context">Şifreli verinin bağlandığı bağlam (kiracı:kanal:ortam); başka satıra taşınırsa çözülemez.</param>
    string Protect(string plaintext, string context);
    string Unprotect(string protectedText, string context);
}

/// <summary>Bir kanalın hangi alanları aldığı ve hangilerinin gizli olduğu.</summary>
/// <param name="SellerField">MarketplaceCredentials.SellerId'ye giden açık alan.</param>
/// <param name="KeyField">ApiKey'e giden alan (açık ya da gizli olabilir).</param>
/// <param name="SecretField">ApiSecret'e giden gizli alan.</param>
public record ChannelCredentialSpec(
    string Channel,
    IReadOnlyList<string> PublicFields,
    IReadOnlyList<string> SecretFields,
    string? SellerField,
    string? KeyField,
    string SecretField)
{
    public static readonly IReadOnlyList<ChannelCredentialSpec> All =
    [
        new("Trendyol", ["sellerId"], ["apiKey", "apiSecret"], "sellerId", "apiKey", "apiSecret"),
        new("Hepsiburada", ["merchantId"], ["serviceKey"], "merchantId", "merchantId", "serviceKey"),
        new("N11", [], ["appKey", "appSecret"], null, "appKey", "appSecret"),
        // Amazon anahtarla değil OAuth ile bağlanır: refresh token yetki akışının sonunda sunucu tarafından yazılır.
        new("Amazon", ["sellingPartnerId"], ["refreshToken"], "sellingPartnerId", null, "refreshToken"),
    ];

    public static ChannelCredentialSpec? For(string channel) =>
        All.FirstOrDefault(s => s.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<string> AllFields => PublicFields.Concat(SecretFields);
}

/// <param name="Value">Açık alanlarda değer; gizli alanlarda null.</param>
/// <param name="Masked">Gizli alanlarda "••••1234" gibi maskeli gösterim; kayıtlı değilse null.</param>
public record CredentialFieldView(string Name, bool Secret, string? Value, string? Masked);

public record CredentialView(string Channel, string Environment, IReadOnlyList<CredentialFieldView> Fields);

/// <summary>Kullanıcı girdisi geçersiz olduğunda fırlatılır (eksik/bilinmeyen alan, desteklenmeyen kanal).</summary>
public class CredentialValidationException(string message) : Exception(message);

public interface IChannelCredentialService
{
    /// <summary>Bilgileri şifreleyip kaydeder. Boş bırakılan gizli alanlar, daha önce kayıtlıysa korunur.</summary>
    Task<CredentialView> SaveAsync(string channel, ChannelEnvironment environment, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default);

    /// <summary>Maskeli görünüm; kayıt yoksa null. Gizli değerlerin tamamı asla dönmez.</summary>
    Task<CredentialView?> GetMaskedAsync(string channel, ChannelEnvironment environment, CancellationToken ct = default);

    /// <summary>Kanalın etkin ortamındaki bilgileri çözüp adapter için hazırlar (yalnızca sunucu içi kullanım).</summary>
    Task<MarketplaceCredentials?> GetForClientAsync(string channel, CancellationToken ct = default);
}

public class CredentialEncryptionOptions
{
    public const string Section = "Credentials";
    /// <summary>Base64, tam 32 bayt (AES-256). Üretimde ortam değişkeni veya Key Vault'tan gelmeli.</summary>
    public string Key { get; set; } = string.Empty;
}
