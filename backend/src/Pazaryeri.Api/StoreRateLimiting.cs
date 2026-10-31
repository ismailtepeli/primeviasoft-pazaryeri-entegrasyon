using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Pazaryeri.Api;

/// <summary>
/// Bayi sitesinin anonim uçları için istemci (IP) başına hız sınırı: giriş denemesi ve bot başvurusuna karşı.
/// Ters vekil arkasında çalışırken gerçek istemci IP'si için ForwardedHeaders yapılandırılmalıdır.
/// </summary>
public static class StoreRateLimiting
{
    public const string AuthPolicy = "store-auth";
    public const string InfoPolicy = "store-info";
    public const string WebhookPolicy = "webhook";
    public const int WebhookPermits = 600;
    public const int AuthPermits = 10;
    public const int InfoPermits = 60;

    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(AuthPolicy, http => Window(http, AuthPermits));
        options.AddPolicy(InfoPolicy, http => Window(http, InfoPermits));
        options.AddPolicy(WebhookPolicy, http => Window(http, WebhookPermits));
        options.OnRejected = async (ctx, ct) =>
        {
            ctx.HttpContext.Response.Headers.RetryAfter = "60";
            await ctx.HttpContext.Response.WriteAsJsonAsync(new { error = "Çok fazla istek; bir dakika sonra tekrar deneyin." }, ct);
        };
    }

    public static RateLimitPartition<string> Window(HttpContext http, int permits) => Partition(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", permits);

    public static RateLimitPartition<string> Partition(string client, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
        });
}
