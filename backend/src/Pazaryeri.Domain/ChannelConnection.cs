using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

public enum ConnectionStatus
{
    Connected,
    /// <summary>Bilgiler kaydedildi ama henüz kanalda doğrulanmadı (ya da doğrulama geçici bir sorunla yapılamadı).</summary>
    Unverified,
    /// <summary>Kanal API bilgilerini reddetti (401/403); kullanıcının bilgileri yenilemesi gerekir.</summary>
    AuthError,
}

/// <summary>Kiracının bir pazaryeri kanalına bağlantısı. Kaydı olmayan kanal "bağlanmadı" sayılır.</summary>
public class ChannelConnection : TenantEntity
{
    public string Channel { get; set; } = string.Empty;
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Connected;
    /// <summary>Çağrıların hangi ortam bilgileriyle yapıldığı.</summary>
    public ChannelEnvironment Environment { get; set; } = ChannelEnvironment.Live;
}

public static class SupportedChannels
{
    public static readonly IReadOnlyList<string> All = ["Trendyol", "Hepsiburada", "N11", "Amazon"];
}
