using Microsoft.Extensions.Logging.Abstractions;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Application.RateLimiting;
using Pazaryeri.Infrastructure.Marketplace.Trendyol;
using Pazaryeri.Infrastructure.RateLimiting;
using StackExchange.Redis;

namespace Pazaryeri.Tests;

public class RateLimiterTests
{
    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Beklemeyi gerçekten uyumak yerine sahte saati ilerleterek yapar.</summary>
    private static (InMemoryRateLimiter Limiter, List<TimeSpan> Waits) Build()
    {
        var time = new FakeTime();
        var waits = new List<TimeSpan>();
        var limiter = new InMemoryRateLimiter(time, (t, _) => { waits.Add(t); time.Now += t; return Task.CompletedTask; });
        return (limiter, waits);
    }

    [Fact]
    public async Task Kapasite_dolana_kadar_beklemeden_gecer()
    {
        var (limiter, waits) = Build();

        for (var i = 0; i < 5; i++) await limiter.AcquireAsync("k", new RateLimit(5, TimeSpan.FromSeconds(10)));

        Assert.Empty(waits);
    }

    [Fact]
    public async Task Kapasite_dolunca_yenilenme_hizina_gore_bekler()
    {
        var (limiter, waits) = Build();
        var limit = new RateLimit(5, TimeSpan.FromSeconds(10)); // saniyede 0,5 hak

        for (var i = 0; i < 6; i++) await limiter.AcquireAsync("k", limit);

        Assert.Equal([TimeSpan.FromSeconds(2)], waits); // 6. istek 2 sn bekler
    }

    [Fact]
    public async Task Kovalar_birbirinden_bagimsizdir()
    {
        var (limiter, waits) = Build();
        var limit = new RateLimit(1, TimeSpan.FromMinutes(1));

        await limiter.AcquireAsync("satici-1:ProductWrite", limit);
        await limiter.AcquireAsync("satici-2:ProductWrite", limit);
        await limiter.AcquireAsync("satici-1:InventoryPriceWrite", limit);

        Assert.Empty(waits);
    }

    [Fact]
    public async Task Bekleme_suresi_gecince_kova_yeniden_dolar()
    {
        var time = new FakeTime();
        var limiter = new InMemoryRateLimiter(time, (_, _) => throw new InvalidOperationException("beklememeli"));
        var limit = new RateLimit(2, TimeSpan.FromSeconds(10));

        await limiter.AcquireAsync("k", limit);
        await limiter.AcquireAsync("k", limit);
        time.Now += TimeSpan.FromSeconds(10);

        await limiter.AcquireAsync("k", limit);
        await limiter.AcquireAsync("k", limit);
    }

    [Theory]
    [InlineData(ListingTier.Up50k, TrendyolServiceGroup.ProductRead, 1000)]
    [InlineData(ListingTier.Up50k, TrendyolServiceGroup.ProductWrite, 200)]
    [InlineData(ListingTier.Up50k, TrendyolServiceGroup.InventoryPriceWrite, 350)]
    [InlineData(ListingTier.Up150k, TrendyolServiceGroup.InventoryPriceWrite, 1000)]
    [InlineData(ListingTier.Up500k, TrendyolServiceGroup.ProductRead, 1750)]
    [InlineData(ListingTier.Unlimited, TrendyolServiceGroup.ProductWrite, 600)]
    [InlineData(ListingTier.Unlimited, TrendyolServiceGroup.InventoryPriceWrite, 2000)]
    public void Trendyol_limitleri_listeleme_seviyesine_gore_secilir(ListingTier tier, TrendyolServiceGroup group, int perMinute)
    {
        var limit = TrendyolRateLimits.ForGroup(tier, group);

        Assert.Equal(new RateLimit(perMinute, TimeSpan.FromMinutes(1)), limit);
    }

    [Fact]
    public void Grup_disi_servis_sadece_uc_nokta_kovasina_tabidir()
    {
        var rules = TrendyolRateLimits.RulesFor(ListingTier.Up50k, TrendyolServiceGroup.Other, HttpMethod.Get, "/integration/order/sellers/1/orders?page=0");

        var rule = Assert.Single(rules);
        Assert.Equal(new RateLimit(50, TimeSpan.FromSeconds(10)), rule.Limit);
        Assert.DoesNotContain("page=0", rule.Key); // sorgu dizesi kovayı bölmemeli
    }

    private sealed class RecordingLimiter : IRateLimiter
    {
        public List<(string Key, RateLimit Limit)> Calls { get; } = [];
        public Task AcquireAsync(string key, RateLimit limit, CancellationToken ct = default) { Calls.Add((key, limit)); return Task.CompletedTask; }
    }

    private sealed class OkServer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    [Fact]
    public async Task Handler_istek_gitmeden_once_satici_bazli_kovalardan_hak_alir()
    {
        var limiter = new RecordingLimiter();
        var handler = new Pazaryeri.Infrastructure.Marketplace.Http.RateLimitHandler(limiter) { InnerHandler = new OkServer() };
        var creds = new MarketplaceCredentials("Trendyol", "12345", "k", "s", "Acme", Tier: ListingTier.Up150k);
        var client = new TrendyolClient(new HttpClient(handler), creds);

        await client.UpdateStockAndPriceAsync([new StockPriceItem("bc1", 1, 10, 12)]);

        Assert.Equal(2, limiter.Calls.Count);
        Assert.Equal("Trendyol:12345:group:InventoryPriceWrite", limiter.Calls[0].Key);
        Assert.Equal(RateLimit.PerMinute(1000), limiter.Calls[0].Limit);
        Assert.StartsWith("Trendyol:12345:endpoint:POST /integration/inventory/", limiter.Calls[1].Key);
    }

    [Fact]
    public async Task Redis_kovasi_kapasiteyi_asan_istegi_bekletir()
    {
        ConnectionMultiplexer redis;
        try { redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379,connectTimeout=2000,abortConnect=false"); }
        catch (RedisConnectionException) { Assert.Fail("Redis (localhost:6379) çalışmıyor; `docker compose up -d` ile başlatın."); return; }
        if (!redis.IsConnected) Assert.Fail("Redis (localhost:6379) çalışmıyor; `docker compose up -d` ile başlatın.");

        using (redis)
        {
            var limiter = new RedisRateLimiter(redis, NullLogger<RedisRateLimiter>.Instance);
            var key = "test:" + Guid.NewGuid();
            var limit = new RateLimit(3, TimeSpan.FromSeconds(3)); // saniyede 1 hak

            await limiter.AcquireAsync("test:warmup:"+Guid.NewGuid(), limit); // ilk çağrı Lua betiğini yükler; ölçüme katılmasın
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 3; i++) await limiter.AcquireAsync(key, limit);
            var burst = watch.Elapsed;
            await limiter.AcquireAsync(key, limit); // 4. istek ~1 sn beklemeli
            var total = watch.Elapsed;

            // Gerçek bir bekleme jetonu başına ~1 sn; paralel testlerin yükü altında ilk bağlantı yavaşlayabildiği için sınır 1 sn.
            Assert.True(burst < TimeSpan.FromMilliseconds(1000), $"ilk 3 istek beklememeli ({burst})");
            Assert.True(total >= TimeSpan.FromMilliseconds(800), $"4. istek beklemeli ({total})");
            await redis.GetDatabase().KeyDeleteAsync("ratelimit:" + key);
        }
    }
}
