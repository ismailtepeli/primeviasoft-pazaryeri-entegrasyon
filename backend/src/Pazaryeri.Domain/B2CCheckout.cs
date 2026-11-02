using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

public enum PaymentMethod
{
    /// <summary>Havale/EFT: müşteri tutarı banka hesabına gönderir, yönetici ödemeyi onaylar.</summary>
    BankTransfer,
    /// <summary>Kapıda ödeme: tutar teslimatta tahsil edilir.</summary>
    CashOnDelivery,
    /// <summary>Kredi/banka kartı: ödeme sağlayıcısı üzerinden.</summary>
    Card,
}

public enum PaymentStatus
{
    /// <summary>Ödeme bekleniyor (havale gelmedi ya da kart ödemesi sürüyor).</summary>
    Pending,
    Paid,
    /// <summary>Kapıda ödeme: tutar teslimatta alınır.</summary>
    OnDelivery,
    /// <summary>Ödeme başarısız oldu; sipariş iptal edildi.</summary>
    Failed,
    /// <summary>Kart ödemesi süresinde tamamlanmadı; sipariş iptal edildi.</summary>
    Expired,
}

/// <summary>Kiracının B2C mağaza kargo ve ödeme ayarları (kiracı başına tek kayıt).</summary>
public class B2CSettings : TenantEntity
{
    public const int MaxInfoLength = 1000;

    public decimal ShippingFee { get; set; } = 49.90m;
    /// <summary>Ara toplam bu tutara ulaşınca kargo ücretsizdir; boşsa her sipariş kargo ücretlidir.</summary>
    public decimal? FreeShippingThreshold { get; set; }
    public bool BankTransferEnabled { get; set; } = true;
    /// <summary>Müşteriye gösterilen havale bilgisi (banka, IBAN, alıcı, açıklama).</summary>
    public string? BankTransferInfo { get; set; }
    public bool CashOnDeliveryEnabled { get; set; }
    public decimal CashOnDeliveryFee { get; set; }
    public bool CardEnabled { get; set; }
}
