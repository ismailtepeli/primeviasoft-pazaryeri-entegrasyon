namespace Pazaryeri.Application.Shop;

public record CustomerRegister(string? Slug, string? Email, string? Password, string? FullName, string? Phone, bool AcceptTerms, bool MarketingConsent);

public record CustomerLogin(string? Slug, string? Email, string? Password);

/// <param name="IdToken">Google Identity Services'in verdiği kimlik belirteci (credential).</param>
/// <param name="AcceptTerms">İlk kez Google ile kayıt olurken üyelik koşulları kabulü gerekir.</param>
public record CustomerGoogleLogin(string? Slug, string? IdToken, bool AcceptTerms, bool MarketingConsent);

public record CustomerProfile(Guid Id, string Email, string FullName, string? Phone, bool HasPassword, bool GoogleLinked, string? AvatarUrl,
    bool EmailVerified, bool MarketingConsent, DateTime CreatedAt);

public record CustomerSession(string AccessToken, DateTime ExpiresAt, CustomerProfile Profile);

public record ProfileUpdate(string? FullName, string? Phone, bool MarketingConsent);

/// <param name="Current">Mevcut parola; hesapta parola yoksa (yalnızca Google) boş bırakılır ve ilk parola belirlenir.</param>
public record PasswordChange(string? Current, string? New);

public record AddressInput(string? Title, string? FullName, string? Phone, string? City, string? District, string? AddressLine, string? PostalCode, bool IsDefault);

public record AddressDto(Guid Id, string Title, string FullName, string Phone, string City, string District, string AddressLine, string? PostalCode, bool IsDefault);

public record CustomerOrderRow(Guid Id, string OrderNumber, DateTime OrderDate, string Status, decimal Total, string Currency, int ItemCount, string? FirstItemName, string? CoverUrl, string? PaymentStatus = null);

public record CustomerOrderLine(Guid? ProductId, string Name, int Quantity, decimal UnitPrice, string? CoverUrl);

public record CustomerOrderDetail(Guid Id, string OrderNumber, DateTime OrderDate, string Status, decimal Total, string Currency, string? ShippingAddress, string? Note,
    string? ShipmentCarrier, string? TrackingNumber, DateTime? ShippedAt, string? StatusNote, bool CanCancel, IReadOnlyList<CustomerOrderLine> Lines,
    string? PaymentMethod = null, string? PaymentStatus = null, decimal? ShippingFee = null, decimal? PaymentFee = null, string? PaymentInfo = null, DateTime? PaymentExpiresAt = null);

public record CustomerOrderPage(IReadOnlyList<CustomerOrderRow> Items, int Total, int PageSize);

public enum CustomerAuthResult { Ok, InvalidInput, UnknownShop, InvalidCredentials, Duplicate, GoogleNotConfigured, InvalidGoogleToken, EmailNotVerified, TermsRequired, Inactive }

public record CustomerAuthOutcome<T>(CustomerAuthResult Result, T? Value = default, string? Error = null);

/// <summary>Google kimlik belirteci doğrulamasının sonucu (imza, yayıncı, hedef kitle ve süre denetlenmiş).</summary>
public record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? Name, string? Picture);

/// <summary>Google kimlik belirtecini doğrular; yapılandırma (istemci kimliği) yoksa ya da belirteç geçersizse null.</summary>
public interface IGoogleIdTokenVerifier
{
    bool Configured { get; }

    /// <summary>Web istemcisinin Google düğmesini kurması için gereken herkese açık istemci kimliği.</summary>
    string? ClientId { get; }

    Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken ct = default);
}
