using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Amazon;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Amazon;

public class AmazonOAuthService(
    IOptions<AmazonLwaOptions> options,
    ICredentialProtector protector,
    IChannelCredentialService credentials,
    AmazonLwaClient lwa,
    AppDbContext db,
    ITenantProvider tenant,
    TimeProvider time) : IAmazonOAuthService
{
    private const string Channel = "Amazon";
    private const string StateContext = "amazon-oauth-state";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private record StatePayload(Guid TenantId, string Nonce, long ExpiresAt);

    public string BuildAuthorizationUrl()
    {
        var o = options.Value;
        if (!o.Configured) throw new AmazonOAuthException("Amazon uygulama ayarları (LWA) sunucuda tanımlı değil.");
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Kiracı bilinmeden yetki başlatılamaz.");

        // State, kiracıyı taşıyan şifreli ve kısa ömürlü bir değerdir: callback oturumsuz geldiği için kiracıyı bundan öğreniriz,
        // değiştirilirse (GCM etiketi) çözülemez.
        var payload = new StatePayload(tenantId, Guid.NewGuid().ToString("N"), time.GetUtcNow().Add(StateLifetime).ToUnixTimeSeconds());
        var state = protector.Protect(JsonSerializer.Serialize(payload), StateContext);

        var url = $"{o.SellerCentralUrl.TrimEnd('/')}/apps/authorize/consent?application_id={Uri.EscapeDataString(o.ApplicationId)}&state={Uri.EscapeDataString(state)}";
        return o.Draft ? url + "&version=beta" : url;
    }

    public async Task CompleteAsync(string? code, string? state, string? sellingPartnerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(sellingPartnerId))
            throw new AmazonOAuthException("Amazon'dan eksik yanıt geldi; işlemi baştan başlatın.");

        var payload = ReadState(state);
        using var _ = AmbientTenant.Use(payload.TenantId); // callback oturumsuz; kiracı state'ten gelir

        var refreshToken = await lwa.ExchangeCodeAsync(code, ct);
        await credentials.SaveAsync(Channel, ChannelEnvironment.Live,
            new Dictionary<string, string> { ["sellingPartnerId"] = sellingPartnerId, ["refreshToken"] = refreshToken }, ct);

        // Amazon kodu kabul edip token verdiyse yetki doğrulanmıştır.
        var connection = await db.ChannelConnections.SingleAsync(c => c.Channel == Channel, ct);
        connection.Status = ConnectionStatus.Connected;
        await db.SaveChangesAsync(ct);
    }

    public async Task<AmazonOAuthStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var o = options.Value;
        var view = await credentials.GetMaskedAsync(Channel, ChannelEnvironment.Live, ct);
        var partner = view?.Fields.FirstOrDefault(f => f.Name == "sellingPartnerId")?.Value;

        DateTime? expires = o.ClientSecretIssuedAt?.AddDays(AmazonLwaOptions.SecretLifetimeDays);
        int? daysLeft = expires is null ? null : (int)Math.Floor((expires.Value - time.GetUtcNow().UtcDateTime).TotalDays);
        return new AmazonOAuthStatus(o.Configured, view is not null, partner, expires, daysLeft,
            SecretReminder: daysLeft is not null && daysLeft <= AmazonLwaOptions.ReminderDays);
    }

    private StatePayload ReadState(string state)
    {
        StatePayload? payload;
        try { payload = JsonSerializer.Deserialize<StatePayload>(protector.Unprotect(state, StateContext)); }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or FormatException or JsonException)
        {
            throw new AmazonOAuthException("Yetkilendirme doğrulanamadı (geçersiz state); işlemi baştan başlatın.");
        }
        if (payload is null || payload.ExpiresAt < time.GetUtcNow().ToUnixTimeSeconds())
            throw new AmazonOAuthException("Yetkilendirme süresi doldu; işlemi baştan başlatın.");
        return payload;
    }
}
