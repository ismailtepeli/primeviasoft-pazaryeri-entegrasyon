using Pazaryeri.Infrastructure.Auth;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pazaryeri.Api;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Auth;
using Pazaryeri.Infrastructure;
using Pazaryeri.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key en az 32 karakter olmalı (user-secrets veya ortam değişkeni ile verin).");

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, CurrentTenantProvider>();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudiences = [jwt.Audience, jwt.DealerAudience],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization(AuthSetup.Configure);
builder.Services.AddRateLimiter(StoreRateLimiting.Configure);

// Ters vekil (nginx, yük dengeleyici) arkasında hız sınırı gerçek istemci IP'sine göre çalışsın diye X-Forwarded-For okunur.
// Yalnızca uygulamaya sadece vekil üzerinden ulaşılıyorsa açın; aksi hâlde istemci başlığı taklit edip sınırı aşabilir.
var trustForwarded = builder.Configuration.GetValue<bool>("Security:TrustForwardedHeaders");
if (trustForwarded)
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });

// Geliştirmede Vite sunucusundan gelen isteklere izin ver.
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173", "http://localhost:5174").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DevDataSeeder>().SeedAsync();
}

if (trustForwarded) app.UseForwardedHeaders();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
