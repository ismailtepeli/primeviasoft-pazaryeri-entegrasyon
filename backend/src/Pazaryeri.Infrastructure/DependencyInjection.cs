using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pazaryeri.Application.Auth;
using Pazaryeri.Infrastructure.Auth;
using Pazaryeri.Infrastructure.Persistence;

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
        return services;
    }
}
