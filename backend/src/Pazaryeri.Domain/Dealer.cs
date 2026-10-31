using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

public enum DealerStatus
{
    /// <summary>Başvurdu, yönetici onayı bekliyor; giriş yapamaz.</summary>
    Pending,
    Active,
    /// <summary>Reddedildi ya da pasife alındı; giriş yapamaz, verisi durur.</summary>
    Passive,
}

public enum DealerUserRole
{
    /// <summary>Firma yetkilisi; başvuruyu yapan kullanıcı.</summary>
    Owner,
    Buyer,
}

/// <summary>Bayi grubu (ör. Altın, Gümüş); fiyat listesi ve iskonto bu gruba bağlanır (#66). Ad kiracı içinde tekildir.</summary>
public class DealerGroup : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>B2B müşteri firma. Vergi numarası kiracı içinde tekildir.</summary>
public class Dealer : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    /// <summary>VKN (10 hane) ya da TCKN (11 hane).</summary>
    public string TaxNumber { get; set; } = string.Empty;
    public string? TaxOffice { get; set; }
    public string? ContactName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Notes { get; set; }
    public DealerStatus Status { get; set; } = DealerStatus.Pending;
    public Guid? GroupId { get; set; }
    /// <summary>Ödeme vadesi (gün); cari hesapta vadesi geçmiş tutarı bulmak için.</summary>
    public int PaymentTermDays { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public List<DealerUser> Users { get; set; } = [];
}

/// <summary>Bayinin ödeme kaydı (cari hesaptan düşer). Online ödeme sonraya; şimdilik yönetici elle girer.</summary>
public class DealerPayment : TenantEntity
{
    public Guid DealerId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
    public string? Note { get; set; }
}

/// <summary>Bayinin siteye giriş yapan kullanıcısı. E-posta kiracı içinde tekildir; yönetici kullanıcılarından (User) ayrıdır.</summary>
public class DealerUser : TenantEntity
{
    public Guid DealerId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DealerUserRole Role { get; set; } = DealerUserRole.Buyer;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>Bayi parola sıfırlama bağlantısı: tek kullanımlık, süreli. Token'ın kendisi saklanmaz, yalnızca özeti (SHA-256) tutulur.</summary>
public class DealerPasswordReset : TenantEntity
{
    public Guid DealerUserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
