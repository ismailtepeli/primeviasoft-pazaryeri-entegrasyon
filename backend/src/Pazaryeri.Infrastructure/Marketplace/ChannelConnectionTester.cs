using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Marketplace;

public class ChannelConnectionTester(AppDbContext db, IChannelCredentialService credentials, IMarketplaceClientFactory clients) : IChannelConnectionTester
{
    public async Task<ConnectionTestResult> TestAsync(string channel, CancellationToken ct = default)
    {
        var creds = await credentials.GetForClientAsync(channel, ct);
        if (creds is null) return new ConnectionTestResult(false, "Önce API bilgilerini kaydedin.");

        IMarketplaceClient client;
        try { client = clients.Create(creds); }
        catch (NotSupportedException) { return new ConnectionTestResult(false, $"{creds.Channel} için bağlantı testi henüz desteklenmiyor."); }

        try
        {
            await client.TestConnectionAsync(ct);
        }
        catch (MarketplaceApiException e)
        {
            // Yalnızca kimlik reddi durumu "Hata"ya çevirir; 503/429 gibi geçici sorunlar mevcut durumu bozmaz.
            if (e.StatusCode is 401 or 403) await SetStatusAsync(creds.Channel, ConnectionStatus.AuthError, ct);
            return new ConnectionTestResult(false, Explain(e.StatusCode, creds.UseStaging), e.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new ConnectionTestResult(false, "Kanala ulaşılamadı; ağ bağlantısını kontrol edip tekrar deneyin.");
        }

        await SetStatusAsync(creds.Channel, ConnectionStatus.Connected, ct);
        return new ConnectionTestResult(true, "Bağlantı başarılı.", 200);
    }

    /// <summary>Kanalın HTTP kodunu kullanıcının ne yapacağını söyleyen bir mesaja çevirir.</summary>
    public static string Explain(int statusCode, bool staging) => statusCode switch
    {
        401 => "Kimlik doğrulanamadı: API Key veya API Secret hatalı.",
        403 => "Erişim reddedildi (403): Satıcı ID'yi ve API yetkilerini kontrol edin.",
        503 when staging => "Test ortamına erişilemedi (503): IP adresiniz test ortamı için yetkilendirilmemiş olabilir. Kanaldan IP whitelist talebi oluşturun.",
        503 => "Kanal şu an hizmet vermiyor (503); biraz sonra tekrar deneyin.",
        429 => "Kanal çok fazla istek aldığını bildirdi (429); biraz bekleyip tekrar deneyin.",
        >= 500 => $"Kanal geçici bir hata döndürdü ({statusCode}); biraz sonra tekrar deneyin.",
        _ => $"Kanal beklenmeyen bir yanıt döndürdü ({statusCode}).",
    };

    private async Task SetStatusAsync(string channel, ConnectionStatus status, CancellationToken ct)
    {
        var connection = await db.ChannelConnections.FirstOrDefaultAsync(c => c.Channel == channel, ct);
        if (connection is null) return;
        connection.Status = status;
        await db.SaveChangesAsync(ct);
    }
}
