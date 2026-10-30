using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pazaryeri.Application.Amazon;
using Pazaryeri.Application.Auth;
using Pazaryeri.Application.Catalog;
using Pazaryeri.Infrastructure.Catalog;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Application.Products;
using Pazaryeri.Application.Stock;
using Pazaryeri.Application.Modules;
using Pazaryeri.Application.Dealers;
using Pazaryeri.Infrastructure.Dealers;
using Pazaryeri.Infrastructure.Modules;
using Pazaryeri.Infrastructure.Orders;
using Pazaryeri.Infrastructure.Products;
using Pazaryeri.Infrastructure.Stock;
using Pazaryeri.Application.Products;
using Pazaryeri.Infrastructure.Products;
using Pazaryeri.Infrastructure.Amazon;
using Pazaryeri.Infrastructure.Auth;
using Pazaryeri.Infrastructure.Credentials;
using Pazaryeri.Infrastructure.Security;
using Pazaryeri.Infrastructure.Jobs;
using Pazaryeri.Infrastructure.Marketplace;
using Pazaryeri.Infrastructure.Marketplace.Http;
using Pazaryeri.Application.RateLimiting;
using Pazaryeri.Infrastructure.Messaging;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.RateLimiting;
using StackExchange.Redis;
using Quartz;

namespace Pazaryeri.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(o =>
            o.UseSqlServer(config.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default tanımlı değil.")));

        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IDealerAuthService, DealerAuthService>();
        services.AddScoped<Pricing.DealerPriceService>();
        services.AddScoped<Pricing.StoreCatalog>();
        services.AddScoped<Pricing.StoreCatalogService>();
        services.AddScoped<Pricing.PriceListImportService>();
        services.AddScoped<IProductImportService, ProductImportService>();
        services.AddScoped<IProductImageService, ProductImageService>();
        services.AddScoped<ProductChannelPreparer>();
        services.AddScoped<StockPriceDeltaPlanner>();
        services.AddScoped<IStockPoolService, StockPoolService>();
        services.AddScoped<ISyncJobHandler, StockPriceSyncHandler>();
        services.AddMemoryCache();
        services.AddScoped<IModuleService, ModuleService>();
        services.AddScoped<ISyncJobHandler, OrderSyncHandler>();
        services.AddScoped<OrderIngestService>();
        services.AddScoped<IProductSendService, ProductSendService>();
        services.AddScoped<IChannelCatalogService, ChannelCatalogService>();
        services.AddScoped<ISyncJobHandler, CatalogSyncHandler>();
        services.AddScoped<ISyncJobHandler, ProductSendHandler>();
        // Yüklenen görseller; üretimde Storage:ImagesPath kalıcı bir diske işaret etmeli.
        var imagesPath = config["Storage:ImagesPath"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "images");
        services.AddSingleton<IImageStore>(new LocalImageStore(imagesPath));
        services.AddScoped<DevDataSeeder>();

        // API bilgileri AES-256-GCM ile şifrelenir; anahtar yapılandırmadan (üretimde Key Vault/ortam değişkeni) gelir.
        services.AddSingleton<ICredentialProtector>(AesGcmCredentialProtector.FromBase64(config[$"{CredentialEncryptionOptions.Section}:Key"]));
        services.AddScoped<IChannelCredentialService, ChannelCredentialService>();

        // Kuyruk: bağlantı tembel açılır; işçi yalnızca RabbitMq:Enabled ise çalışır.
        services.Configure<RabbitMqOptions>(config.GetSection(RabbitMqOptions.Section));
        services.AddSingleton<RabbitMqConnection>();
        services.AddScoped<IJobQueue, JobQueue>();
        if (config.GetValue<bool>($"{RabbitMqOptions.Section}:Enabled"))
            services.AddHostedService<SyncJobWorker>();

        // Zamanlanmış işler (Quartz). Yeni periyodik işler buraya eklenir.
        services.AddQuartz(q =>
        {
            q.ScheduleJob<StaleJobSweeper>(t => t
                .WithIdentity("stale-job-sweeper")
                .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(5)).RepeatForever()));
            // Kanal kataloğu (kategori, marka, özellik) her pazar 03:00'te (UTC) yenilenir.
            q.ScheduleJob<CatalogWeeklySync>(t => t
                .WithIdentity("catalog-weekly-sync")
                .WithCronSchedule("0 0 3 ? * SUN", c => c.InTimeZone(TimeZoneInfo.Utc)));
            // Değişen stok-fiyatı kanala gönderir (5 dk) ve gönderilen toplu işlemlerin sonucunu okur (1 dk).
            q.ScheduleJob<StockPriceSyncScheduler>(t => t
                .WithIdentity("stock-price-sync-scheduler")
                .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(5)).RepeatForever()));
            // Kanaldan değişen siparişleri çeker (3 dk); webhook (#30) anlık bildirim ekler.
            q.ScheduleJob<OrderSyncScheduler>(t => t
                .WithIdentity("order-sync-scheduler")
                .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(3)).RepeatForever()));
            q.ScheduleJob<StockPriceBatchPoller>(t => t
                .WithIdentity("stock-price-batch-poller")
                .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever()));
            // Kanalın toplu ürün işlemi sonuçlarını sorgular (4 saat dolmadan).
            q.ScheduleJob<ProductBatchPoller>(t => t
                .WithIdentity("product-batch-poller")
                .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever()));
        });
        services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

        // Rate limiter: Redis:ConnectionString varsa Redis (örnekler arası paylaşım), yoksa süreç içi.
        var redisConn = config["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConn))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(ConfigurationOptions.Parse(redisConn + ",abortConnect=false")));
            services.AddSingleton<IRateLimiter, RedisRateLimiter>();
        }
        else
        {
            services.AddSingleton<IRateLimiter, InMemoryRateLimiter>();
        }

        // Pazaryeri adapter'ları: 429'da üstel bekleme (dış), rate limit (orta), her çağrının logu (iç).
        services.AddSingleton<IApiCallLogger, ApiCallLogger>();
        services.AddTransient<RetryOn429Handler>();
        services.AddTransient<RateLimitHandler>();
        services.AddTransient<ApiLogHandler>();
        services.AddHttpClient(MarketplaceClientFactory.HttpClientName)
            .AddHttpMessageHandler<RetryOn429Handler>()
            .AddHttpMessageHandler<RateLimitHandler>()
            .AddHttpMessageHandler<ApiLogHandler>();
        services.AddSingleton<IMarketplaceClientFactory, MarketplaceClientFactory>();
        services.AddScoped<IChannelConnectionTester, ChannelConnectionTester>();

        // Amazon LWA (OAuth) yetkilendirmesi.
        services.Configure<AmazonLwaOptions>(config.GetSection(AmazonLwaOptions.Section));
        services.AddHttpClient<AmazonLwaClient>(AmazonLwaClient.HttpClientName);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAmazonOAuthService, AmazonOAuthService>();

        return services;
    }
}
