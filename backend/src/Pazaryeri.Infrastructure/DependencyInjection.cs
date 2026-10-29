using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pazaryeri.Application.Auth;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Infrastructure.Auth;
using Pazaryeri.Infrastructure.Jobs;
using Pazaryeri.Infrastructure.Messaging;
using Pazaryeri.Infrastructure.Persistence;
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

        return services;
    }
}
