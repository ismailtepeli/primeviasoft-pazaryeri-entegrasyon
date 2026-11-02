using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>
/// B2C mağazanın son tüketici müşterisi. Bayi kullanıcılarından (<see cref="DealerUser"/>) ve yöneticilerden ayrıdır: ayrı token hedef kitlesi.
/// E-posta kiracı içinde tekildir. Parola yoksa (<see cref="PasswordHash"/> boş) hesap yalnızca Google ile girilir.
/// </summary>
public class Customer : TenantEntity
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? PasswordHash { get; set; }
    /// <summary>Google hesap kimliği (sub); kiracı içinde tekildir. Doluysa hesap Google ile bağlıdır.</summary>
    public string? GoogleSubject { get; set; }
    public string? AvatarUrl { get; set; }
    /// <summary>E-posta doğrulandı mı (Google doğrulanmış e-posta verir; parola ile kayıtta sonradan doğrulanır).</summary>
    public bool EmailVerified { get; set; }
    /// <summary>Kampanya/bilgilendirme iletisi izni (KVKK: açık rıza, varsayılan kapalı).</summary>
    public bool MarketingConsent { get; set; }
    /// <summary>Üyelik koşulları ve aydınlatma metni kabul zamanı.</summary>
    public DateTime? TermsAcceptedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public List<CustomerAddress> Addresses { get; set; } = [];
}

/// <summary>Müşterinin kayıtlı teslimat adresi (adres defteri). Müşteri başına en fazla <see cref="MaxPerCustomer"/> adres.</summary>
public class CustomerAddress : TenantEntity
{
    public const int MaxPerCustomer = 10;

    public Guid CustomerId { get; set; }
    /// <summary>Kısa ad: "Ev", "İş".</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Teslim alacak kişi.</summary>
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
    public bool IsDefault { get; set; }
}
