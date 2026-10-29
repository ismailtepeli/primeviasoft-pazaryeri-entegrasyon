namespace Pazaryeri.Application.Marketplace;

/// <param name="Success">Kanal kimlik bilgilerini kabul etti mi.</param>
/// <param name="Message">Kullanıcıya gösterilecek anlaşılır sonuç.</param>
/// <param name="StatusCode">Kanalın döndürdüğü HTTP kodu; ağ hatasında null.</param>
public record ConnectionTestResult(bool Success, string Message, int? StatusCode = null);

public interface IChannelConnectionTester
{
    /// <summary>Kayıtlı bilgilerle kanala bağlanmayı dener ve sonucu kanalın bağlantı durumuna yazar.</summary>
    Task<ConnectionTestResult> TestAsync(string channel, CancellationToken ct = default);
}
