using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Application.RateLimiting;

namespace Pazaryeri.Infrastructure.Marketplace.Http;

/// <summary>İstekle birlikte handler'lara kanal ve mağaza bilgisini taşır (loglama için).</summary>
public static class MarketplaceRequestOptions
{
    public static readonly HttpRequestOptionsKey<string> Channel = new("pz.channel");
    public static readonly HttpRequestOptionsKey<string> SellerId = new("pz.seller");
    /// <summary>Bu isteğin hangi kovalardan hak alacağı (servis grubu, uç nokta...).</summary>
    public static readonly HttpRequestOptionsKey<IReadOnlyList<RateLimitRule>> RateLimits = new("pz.ratelimits");
}

/// <summary>
/// 429 (çok fazla istek) yanıtında üstel bekleme ile yeniden dener: 1 sn, 2 sn, 4 sn...
/// Kanal Retry-After gönderirse o süre kullanılır.
/// </summary>
public class RetryOn429Handler : DelegatingHandler
{
    public int MaxRetries { get; set; } = 5;
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(60);
    /// <summary>Testlerde beklemeyi atlamak için değiştirilebilir.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // Aynı HttpRequestMessage iki kez gönderilemez; içeriği bir kez okuyup her denemede kopya kurarız.
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct);

        for (var attempt = 0; ; attempt++)
        {
            var response = await base.SendAsync(Clone(request, body), ct);
            if ((int)response.StatusCode != 429 || attempt >= MaxRetries) return response;

            var wait = response.Headers.RetryAfter?.Delta
                ?? TimeSpan.FromTicks(BaseDelay.Ticks * (1L << attempt));
            if (wait > MaxDelay) wait = MaxDelay;
            response.Dispose();
            await Delay(wait, ct);
        }
    }

    private static HttpRequestMessage Clone(HttpRequestMessage source, byte[]? body)
    {
        var copy = new HttpRequestMessage(source.Method, source.RequestUri) { Version = source.Version };
        foreach (var h in source.Headers) copy.Headers.TryAddWithoutValidation(h.Key, h.Value);
        foreach (var o in source.Options) copy.Options.Set(new HttpRequestOptionsKey<object?>(o.Key), o.Value);
        if (body is not null)
        {
            copy.Content = new ByteArrayContent(body);
            foreach (var h in source.Content!.Headers) copy.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        return copy;
    }
}

/// <summary>
/// İstek gitmeden önce adapter'ın bildirdiği kovalardan hak alır (kanal × satıcı × kural anahtarı).
/// Retry katmanının içinde durur; böylece 429 sonrası her yeniden deneme de limite takılır.
/// </summary>
public class RateLimitHandler(IRateLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Options.TryGetValue(MarketplaceRequestOptions.Channel, out var channel)
            && request.Options.TryGetValue(MarketplaceRequestOptions.SellerId, out var seller)
            && request.Options.TryGetValue(MarketplaceRequestOptions.RateLimits, out var rules))
        {
            foreach (var rule in rules)
                await limiter.AcquireAsync($"{channel}:{seller}:{rule.Key}", rule.Limit, ct);
        }
        return await base.SendAsync(request, ct);
    }
}

/// <summary>Her HTTP çağrısının isteğini ve yanıtını mağaza bazında loglar (Authorization asla yazılmaz).</summary>
public class ApiLogHandler(IApiCallLogger apiLog, ILogger<ApiLogHandler> logger) : DelegatingHandler
{
    private const int MaxBody = 4000;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Options.TryGetValue(MarketplaceRequestOptions.Channel, out var channel);
        request.Options.TryGetValue(MarketplaceRequestOptions.SellerId, out var seller);
        if (channel is null || seller is null) return await base.SendAsync(request, ct);

        var requestBody = request.Content is null ? null : Truncate(await request.Content.ReadAsStringAsync(ct));
        var url = request.RequestUri?.ToString() ?? string.Empty;
        var clock = Stopwatch.StartNew();

        using var scope = logger.BeginScope(new Dictionary<string, object> { ["Channel"] = channel, ["SellerId"] = seller });
        try
        {
            var response = await base.SendAsync(request, ct);
            await response.Content.LoadIntoBufferAsync(ct); // gövde loglandıktan sonra çağıran da okuyabilsin
            var responseBody = Truncate(await response.Content.ReadAsStringAsync(ct));

            logger.LogInformation("{Method} {Url} -> {Status} ({Ms} ms)", request.Method, url, (int)response.StatusCode, clock.ElapsedMilliseconds);
            await apiLog.LogAsync(new ApiCallEntry(channel, seller, request.Method.Method, url, requestBody,
                (int)response.StatusCode, responseBody, clock.ElapsedMilliseconds, null));
            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Method} {Url} başarısız ({Ms} ms)", request.Method, url, clock.ElapsedMilliseconds);
            await apiLog.LogAsync(new ApiCallEntry(channel, seller, request.Method.Method, url, requestBody,
                null, null, clock.ElapsedMilliseconds, ex.Message));
            throw;
        }
    }

    private static string Truncate(string s) => s.Length > MaxBody ? s[..MaxBody] + "…" : s;
}
