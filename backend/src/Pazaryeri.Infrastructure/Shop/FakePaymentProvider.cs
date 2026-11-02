using Microsoft.Extensions.Options;
using Pazaryeri.Application.Shop;

namespace Pazaryeri.Infrastructure.Shop;

public class PaymentOptions
{
    public const string Section = "Payments";

    /// <summary>"fake" yalnızca geliştirme/deneme içindir (gerçek para geçmez); boşsa kart ödemesi kapalıdır.</summary>
    public string? Provider { get; set; }
}

/// <summary>
/// Geliştirme için sahte sağlayıcı: müşteriyi mağazadaki deneme ödeme sayfasına yollar, sonucu orada seçilir.
/// Yapılandırmada <c>Payments:Provider=fake</c> yoksa kapalıdır; üretimde açılmamalıdır.
/// </summary>
public class FakePaymentProvider(IOptions<PaymentOptions> options) : IPaymentProvider, IPaymentProviderInfo
{
    public bool IsFake => Available;
    public const string Name = "fake";
    public bool Available => string.Equals(options.Value.Provider, Name, StringComparison.OrdinalIgnoreCase);

    public Task<string> StartAsync(Guid orderId, string orderNumber, decimal amount, string currency, CancellationToken ct = default) =>
        Task.FromResult($"/odeme/test/{orderId}");
}
