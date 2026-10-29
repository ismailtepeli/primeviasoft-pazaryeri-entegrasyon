using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pazaryeri.Application.Amazon;
using Pazaryeri.Application.Auth;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Marketplace;
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
        services.AddQuartz(q => q.ScheduleJob<StaleJobSweeper>(t => t
            .WithIdentity("stale-job-sweeper")
            .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(5)).RepeatForever())));
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
