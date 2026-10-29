using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

public enum ChannelEnvironment
{
    Live,
    Staging,
}

/// <summary>
/// Kiracının bir kanal için, tek bir ortamdaki (canlı veya test) API bilgileri. Canlı ve test bilgileri ayrı satırlardır.
/// Gizli alanlar yalnızca şifreli saklanır; açık alanlar (satıcı kimliği gibi) düz tutulur.
/// </summary>
public class ChannelCredential : TenantEntity
{
    public string Channel { get; set; } = string.Empty;
    public ChannelEnvironment Environment { get; set; }
    /// <summary>Gizli olmayan alanlar: {"sellerId":"12345"}.</summary>
    public string PublicFieldsJson { get; set; } = "{}";
    /// <summary>Gizli alanların JSON'unun şifreli hali. Düz metin asla saklanmaz.</summary>
    public string EncryptedSecrets { get; set; } = string.Empty;
    /// <summary>Ekranda "••••1234" göstermek için her gizli alanın son 4 hanesi (yeterince uzunsa).</summary>
    public string SecretHintsJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
