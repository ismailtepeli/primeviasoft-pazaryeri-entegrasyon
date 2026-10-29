using Microsoft.Extensions.Logging;
using Pazaryeri.Application.RateLimiting;
using StackExchange.Redis;

namespace Pazaryeri.Infrastructure.RateLimiting;

/// <summary>Kova doluysa ne kadar bekleneceğini hesaplatan, bekleme döngüsünü ortak yürüten taban sınıf.</summary>
public abstract class RateLimiterBase(Func<TimeSpan, CancellationToken, Task>? delay = null) : IRateLimiter
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    /// <summary>Hak alındıysa sıfır, alınamadıysa hakkın doğacağı süreyi döner.</summary>
    protected abstract Task<TimeSpan> TryAcquireAsync(string key, RateLimit limit, CancellationToken ct);

    public async Task AcquireAsync(string key, RateLimit limit, CancellationToken ct = default)
    {
        while (true)
        {
            var wait = await TryAcquireAsync(key, limit, ct);
            if (wait <= TimeSpan.Zero) return;
            await _delay(wait, ct);
        }
    }
}

/// <summary>Tek süreçlik token bucket. Redis yokken (yerel geliştirme, testler) kullanılır.</summary>
public class InMemoryRateLimiter(TimeProvider? time = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    : RateLimiterBase(delay)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, (double Tokens, DateTimeOffset At)> _buckets = [];
    private readonly object _gate = new();

    protected override Task<TimeSpan> TryAcquireAsync(string key, RateLimit limit, CancellationToken ct)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var (tokens, at) = _buckets.TryGetValue(key, out var b) ? b : (limit.Requests, now);
            var perMs = limit.Requests / limit.Window.TotalMilliseconds;
            tokens = Math.Min(limit.Requests, tokens + (now - at).TotalMilliseconds * perMs);

            if (tokens >= 1)
            {
                _buckets[key] = (tokens - 1, now);
                return Task.FromResult(TimeSpan.Zero);
            }
            _buckets[key] = (tokens, now);
            return Task.FromResult(TimeSpan.FromMilliseconds(Math.Ceiling((1 - tokens) / perMs)));
        }
    }
}

/// <summary>
/// Redis üzerinde token bucket; birden fazla API/worker örneği aynı kovayı paylaşır.
/// Hesap tek bir Lua betiğinde yapıldığı için yarış olmaz; zamanı Redis'in saati verir.
/// </summary>
public class RedisRateLimiter(IConnectionMultiplexer redis, ILogger<RedisRateLimiter> logger) : RateLimiterBase
{
    private const string Script = """
        local cap = tonumber(ARGV[1])
        local rate = tonumber(ARGV[2])
        local t = redis.call('TIME')
        local now = t[1] * 1000 + math.floor(t[2] / 1000)
        local d = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
        local tokens = tonumber(d[1])
        local ts = tonumber(d[2])
        if tokens == nil then tokens = cap; ts = now end
        tokens = math.min(cap, tokens + (now - ts) * rate)
        local wait = 0
        if tokens >= 1 then tokens = tokens - 1 else wait = math.ceil((1 - tokens) / rate) end
        redis.call('HSET', KEYS[1], 'tokens', tostring(tokens), 'ts', now)
        redis.call('PEXPIRE', KEYS[1], math.ceil(cap / rate) + 1000)
        return wait
        """;

    protected override async Task<TimeSpan> TryAcquireAsync(string key, RateLimit limit, CancellationToken ct)
    {
        try
        {
            var perMs = limit.Requests / limit.Window.TotalMilliseconds;
            var result = await redis.GetDatabase().ScriptEvaluateAsync(Script,
                [(RedisKey)("ratelimit:" + key)],
                [limit.Requests, perMs.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            return TimeSpan.FromMilliseconds((long)result);
        }
        catch (RedisException ex)
        {
            // Redis erişilemezse istekleri durdurmak yerine geçir; kanalın 429'u retry katmanında yakalanır.
            logger.LogWarning(ex, "Rate limiter Redis'e ulaşamadı; limit uygulanmadan devam ediliyor.");
            return TimeSpan.Zero;
        }
    }
}
