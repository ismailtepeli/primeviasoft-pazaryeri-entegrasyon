using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Application.Modules;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Orders;

public record WebhookStatus(bool Registered, DateTime? RegisteredAt, DateTime? LastEventAt, string? Url);

/// <summary>
/// Kanalın webhook çağrısını karşılar: gizli anahtarı doğrular, olayı (paket + durum) bir kez kaydeder ve sipariş senkronunu kuyruğa alır.
/// Asıl sipariş verisi webhook gövdesinden değil, zaten idempotent olan sipariş senkronundan okunur; böylece eksik, sırası bozuk ya da
/// tekrarlanan bildirimler veri bozmaz. İşlem kısa tutulur: kayıt + kuyruğa alma, kanal beklemez.
/// </summary>
public class WebhookReceiver(AppDbContext db, IJobQueue queue, IModuleService modules, TimeProvider time)
{
    private static readonly SyncJobStatus[] NotStarted = [SyncJobStatus.Pending, SyncJobStatus.Retrying];

    public async Task<WebhookOutcome> ReceiveAsync(Guid tenantId, string channel, string? authorization, string? apiKey, string? body, CancellationToken ct)
    {
        using var _ = AmbientTenant.Use(tenantId);
        var endpoint = await db.WebhookEndpoints.FirstOrDefaultAsync(w => w.Channel == channel, ct);
        // Bilinmeyen kiracı, kayıtsız kanal ve yanlış anahtar aynı yanıtı alır; hangisinin yanlış olduğu sızmaz.
        if (endpoint is null || !WebhookSecret.Verify(WebhookSecret.Extract(authorization, apiKey), endpoint.SecretHash)) return WebhookOutcome.Unauthorized;
        if (!await modules.IsEnabledAsync(ModuleIds.Orders, tenantId, ct)) return WebhookOutcome.Ignored;

        var now = time.GetUtcNow().UtcDateTime;
        var payload = WebhookPayload.Read(body);
        if (payload.EventKey is { } key)
        {
            db.WebhookEvents.Add(new WebhookEvent { TenantId = tenantId, Channel = channel, EventKey = key, PackageId = payload.PackageId, Status = payload.Status, ReceivedAt = now });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); return WebhookOutcome.Duplicate; } // kanalın tekrar denemesi: zaten işlendi
        }

        db.ChangeTracker.Clear();
        await db.WebhookEndpoints.Where(w => w.Id == endpoint.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.LastEventAt, now), ct);
        // Başlamamış bir senkron varsa o bu olayı da görür; çalışıyorsa okuduktan sonra gelmiş olabilir, yenisi kuyruğa alınır.
        if (!await db.SyncJobs.AnyAsync(j => j.Type == SyncJobType.OrderSync && j.Channel == channel && NotStarted.Contains(j.Status), ct))
            await queue.EnqueueAsync(SyncJobType.OrderSync, channel, ct: ct);
        return WebhookOutcome.Accepted;
    }
}

/// <summary>Kanalda webhook kaydı oluşturur: yeni gizli anahtar üretir, kanala gönderir, başarılıysa özetini saklar.</summary>
public class WebhookRegistrationService(AppDbContext db, ITenantProvider tenant, IChannelCredentialService credentials, IMarketplaceClientFactory clients, WebhookOptions options, TimeProvider time)
{
    /// <summary>Kanalın dinlemesi gereken sipariş durumları.</summary>
    public static readonly string[] Statuses = ["Created", "Picking", "Invoiced", "Shipped", "Cancelled", "Delivered", "UnDelivered", "Returned", "UnSupplied", "Awaiting", "UnPacked", "AtCollectionPoint", "Verified"];

    public static string UrlFor(string baseUrl, string channel, Guid tenantId) => $"{baseUrl.TrimEnd('/')}/webhooks/{channel.ToLowerInvariant()}/{tenantId}";

    public async Task<WebhookStatus> StatusAsync(string channel, CancellationToken ct)
    {
        var tenantId = tenant.TenantId;
        var e = await db.WebhookEndpoints.AsNoTracking().Where(w => w.Channel == channel).FirstOrDefaultAsync(ct);
        var url = tenantId is { } id && !string.IsNullOrWhiteSpace(options.PublicBaseUrl) ? UrlFor(options.PublicBaseUrl, channel, id) : null;
        return new WebhookStatus(e is not null, e?.CreatedAt, e?.LastEventAt, url);
    }

    public async Task<WebhookStatus> RegisterAsync(string channel, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Kiracı bulunamadı.");
        if (string.IsNullOrWhiteSpace(options.PublicBaseUrl))
            throw new InvalidOperationException("Webhook için herkese açık sunucu adresi ayarlanmamış (Webhooks:PublicBaseUrl).");
        var creds = await credentials.GetForClientAsync(channel, ct) ?? throw new InvalidOperationException($"{channel} için kayıtlı API bilgisi yok.");
        var secret = WebhookSecret.Generate();
        var result = await clients.Create(creds).CreateWebhookAsync(new WebhookRegistration(UrlFor(options.PublicBaseUrl, channel, tenantId), WebhookSecret.Username, secret, Statuses), ct);

        var e = await db.WebhookEndpoints.FirstOrDefaultAsync(w => w.Channel == channel, ct);
        if (e is null) db.WebhookEndpoints.Add(e = new WebhookEndpoint { TenantId = tenantId, Channel = channel });
        e.SecretHash = WebhookSecret.Hash(secret);
        e.ExternalId = result.WebhookId;
        e.CreatedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return await StatusAsync(channel, ct);
    }
}
