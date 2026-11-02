using Pazaryeri.Domain;

namespace Pazaryeri.Application.Shop;

public record CartLineInput(Guid ProductId, int Quantity);

/// <summary>Sepet fiyatlama isteği: yalnızca ürün ve adet gelir; fiyat, stok ve kargo sunucuda hesaplanır.</summary>
public record CartQuoteRequest(IReadOnlyList<CartLineInput>? Lines);

/// <param name="Kind">unavailable (satışta değil), stock (stok yetmiyor). Boşsa satır siparişe uygundur.</param>
public record CartQuoteIssue(string Kind, string Text, int? Available = null);

public record CartQuoteLine(Guid ProductId, string Name, string? CoverUrl, int Quantity, decimal UnitPrice, decimal? ListPrice, decimal LineTotal, CartQuoteIssue? Issue);

public record PaymentOption(string Method, string Label, decimal Fee, string? Info);

/// <param name="FreeShippingRemaining">Ücretsiz kargoya kalan tutar; ücretsiz kargo yoksa ya da kazanıldıysa boş.</param>
public record CartQuote(IReadOnlyList<CartQuoteLine> Lines, decimal Subtotal, decimal ShippingFee, decimal? FreeShippingRemaining, decimal Total, string Currency,
    bool HasIssues, IReadOnlyList<PaymentOption> PaymentOptions);

public record CheckoutRequest(IReadOnlyList<CartLineInput>? Lines, Guid? AddressId, string? PaymentMethod, string? Note);

/// <param name="PaymentUrl">Kart ödemesinde ödeme sayfasının mağaza içi yolu; diğer yöntemlerde boş.</param>
public record CheckoutResult(Guid OrderId, string OrderNumber, string PaymentMethod, string PaymentStatus, decimal Total, string? PaymentInfo, string? PaymentUrl);

/// <summary>Yönetici ekranındaki B2C kargo ve ödeme ayarları.</summary>
public record B2CSettingsDto(decimal ShippingFee, decimal? FreeShippingThreshold, bool BankTransferEnabled, string? BankTransferInfo,
    bool CashOnDeliveryEnabled, decimal CashOnDeliveryFee, bool CardEnabled, bool CardProviderAvailable);

public record B2CSettingsInput(decimal ShippingFee, decimal? FreeShippingThreshold, bool BankTransferEnabled, string? BankTransferInfo,
    bool CashOnDeliveryEnabled, decimal CashOnDeliveryFee, bool CardEnabled);

/// <summary>Sağlayıcının deneme (sahte) sağlayıcı olup olmadığı; yalnızca deneme uçlarını açmak için.</summary>
public interface IPaymentProviderInfo
{
    bool IsFake { get; }
}

/// <summary>Kart ödeme sağlayıcısı soyutlaması; gerçek sağlayıcı (iyzico/PayTR/Stripe) bu arayüzü uygular.</summary>
public interface IPaymentProvider
{
    /// <summary>Sağlayıcı yapılandırılmış mı; değilse kart ödemesi hiç sunulmaz.</summary>
    bool Available { get; }

    /// <summary>Ödemeyi başlatır ve müşterinin gideceği ödeme sayfası yolunu döner.</summary>
    Task<string> StartAsync(Guid orderId, string orderNumber, decimal amount, string currency, CancellationToken ct = default);
}
