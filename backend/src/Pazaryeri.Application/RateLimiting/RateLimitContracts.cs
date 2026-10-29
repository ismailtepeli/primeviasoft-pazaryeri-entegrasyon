namespace Pazaryeri.Application.RateLimiting;

/// <summary>Bir pencerede izin verilen en fazla istek sayısı (token bucket kapasitesi = Requests).</summary>
public record RateLimit(int Requests, TimeSpan Window)
{
    public static RateLimit PerMinute(int requests) => new(requests, TimeSpan.FromMinutes(1));
}

/// <summary>Bir isteğin hangi kovadan düşeceği: anahtar son eki + o kovanın limiti.</summary>
public record RateLimitRule(string Key, RateLimit Limit);

public interface IRateLimiter
{
    /// <summary>Kovadan bir hak alınana kadar bekler. Anahtar, kiracı/satıcı/kanal ön ekini çağıran taraf verir.</summary>
    Task AcquireAsync(string key, RateLimit limit, CancellationToken ct = default);
}
