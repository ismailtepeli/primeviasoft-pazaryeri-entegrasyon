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

/// <summary>
/// B2C kargo firması. Kural yoksa sabit ücret (<see cref="FlatFee"/>) uygulanır; kural varsa sepetin toplam desisine uyan
/// en küçük aralığın ücreti uygulanır, en büyük aralığı aşan sepet için firma sunulmaz.
/// </summary>
public class ShippingCarrier : TenantEntity
{
    public const int MaxNameLength = 60;
    public const int MaxUrlLength = 300;
    public const int MaxRates = 20;
    public const string TrackingPlaceholder = "{takipNo}";

    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    public decimal FlatFee { get; set; }
    /// <summary>Takip bağlantısı şablonu; "{takipNo}" yer tutucusu takip numarasıyla değişir. https olmalı. Boşsa bağlantı gösterilmez.</summary>
    public string? TrackingUrlTemplate { get; set; }
    public List<ShippingRate> Rates { get; set; } = [];
}

/// <summary>Desi aralığı ücreti: sepet desisi <see cref="UpToDesi"/> değerine kadar ise <see cref="Fee"/> uygulanır.</summary>
public class ShippingRate : TenantEntity
{
    public Guid CarrierId { get; set; }
    public decimal UpToDesi { get; set; }
    public decimal Fee { get; set; }
}

/// <summary>Kiracının B2C mağaza kargo ve ödeme ayarları (kiracı başına tek kayıt).</summary>
public class B2CSettings : TenantEntity
{
    public const int MaxInfoLength = 1000;

    public decimal ShippingFee { get; set; } = 49.90m;
    /// <summary>Ara toplam bu tutara ulaşınca kargo ücretsizdir; boşsa her sipariş kargo ücretlidir.</summary>
    public decimal? FreeShippingThreshold { get; set; }
    /// <summary>Ayar satırı yokken havale açık sayılmaz: banka bilgisi girilmeden müşteriye havale sunulamaz.</summary>
    public bool BankTransferEnabled { get; set; }
    public const int MinHoldHours = 24, MaxHoldHours = 72, DefaultHoldHours = 48;
    /// <summary>Havale siparişinin ödeme bekleme süresi (saat, 24–72); dolunca sipariş iptal olur ve stok geri eklenir.</summary>
    public int BankTransferHoldHours { get; set; } = DefaultHoldHours;
    /// <summary>Müşteriye gösterilen havale bilgisi (banka, IBAN, alıcı, açıklama).</summary>
    public string? BankTransferInfo { get; set; }
    public bool CashOnDeliveryEnabled { get; set; }
    public decimal CashOnDeliveryFee { get; set; }
    public bool CardEnabled { get; set; }
}
