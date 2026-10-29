using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

public enum ConnectionStatus
{
    Connected,
    /// <summary>Kanal API bilgilerini reddetti (401/403); kullanıcının bilgileri yenilemesi gerekir.</summary>
    AuthError,
}

/// <summary>Kiracının bir pazaryeri kanalına bağlantısı. Kaydı olmayan kanal "bağlanmadı" sayılır.</summary>
public class ChannelConnection : TenantEntity
{
    public string Channel { get; set; } = string.Empty;
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Connected;
}

public static class SupportedChannels
{
    public static readonly IReadOnlyList<string> All = ["Trendyol", "Hepsiburada", "N11", "Amazon"];
}
