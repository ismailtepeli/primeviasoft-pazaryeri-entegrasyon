using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>
/// Kiracının bir modül için açık/kapalı durumu. Kayıt yoksa modülün varsayılanı geçerlidir.
/// Modül kapanınca verisi silinmez; yalnızca erişim ve arka plan işleri durur. (Kiracı, modül) tekildir.
/// </summary>
public class TenantModule : TenantEntity
{
    public string ModuleId { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    /// <summary>Modüle özgü ayarlar (JSON); modülün kendi tipli sınıfı yorumlar.</summary>
    public string? SettingsJson { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
