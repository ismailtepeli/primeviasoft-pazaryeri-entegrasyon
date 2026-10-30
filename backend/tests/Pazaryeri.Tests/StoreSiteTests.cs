using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Pazaryeri.Api;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Auth;
using Pazaryeri.Application.Dealers;
using Pazaryeri.Application.Modules;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Auth;
using Pazaryeri.Infrastructure.Dealers;
using Pazaryeri.Infrastructure.Modules;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Tests;

public class StoreSiteTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_StoreSite;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private static AppDbContext NewDb(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    public async Task InitializeAsync()
    {
        await using var db = NewDb(null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> TenantAsync(string name, string slug, string[] modules, bool active = true)
    {
        await using var db = NewDb(null);
        var t = new Tenant { Name = name, Slug = slug, IsActive = active };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        await using var seed = NewDb(t.Id);
        foreach (var m in modules) seed.TenantModules.Add(new TenantModule { ModuleId = m, Enabled = true });
        await seed.SaveChangesAsync();
        return t.Id;
    }

    private static StoreAuthController Controller(AppDbContext db)
    {
        var modules = new ModuleService(db, new FixedTenant(null), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);
        var service = new DealerAuthService(db, new JwtTokenService(Options.Create(new JwtOptions { Key = new string('k', 40) })), modules, TimeProvider.System);
        return new StoreAuthController(service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    }

    [Fact]
    public async Task Bilgi_modul_ve_slug_durumuna_gore_doner()
    {
        await TenantAsync("Açık Firma", "acik", [ModuleIds.Dealers, ModuleIds.B2BStore]);
        await TenantAsync("Yarım Firma", "yarim", [ModuleIds.Dealers]);        // bayi sitesi modülü kapalı
        await TenantAsync("Kapalı Firma", "kapali", [], active: true);
        await TenantAsync("Pasif Firma", "pasif", [ModuleIds.Dealers, ModuleIds.B2BStore], active: false);
        await using var db = NewDb(null);
        var c = Controller(db);

        var open = Assert.IsType<StoreInfo>(Assert.IsType<OkObjectResult>(await c.Info("ACIK ", default)).Value); // büyük/küçük harf ve boşluk önemsiz
        Assert.Equal(("Açık Firma", true), (open.Name, open.Available));
        Assert.Equal("public, max-age=30", c.Response.Headers.CacheControl.ToString());

        Assert.False(Assert.IsType<StoreInfo>(Assert.IsType<OkObjectResult>(await c.Info("yarim", default)).Value).Available);
        Assert.False(Assert.IsType<StoreInfo>(Assert.IsType<OkObjectResult>(await c.Info("kapali", default)).Value).Available);

        Assert.IsType<NotFoundObjectResult>(await c.Info("pasif", default));
        Assert.IsType<NotFoundObjectResult>(await c.Info("yok", default));
        Assert.IsType<NotFoundObjectResult>(await c.Info(null, default));
        Assert.IsType<NotFoundObjectResult>(await c.Info("  ", default));
    }

    [Fact]
    public void Hiz_siniri_istemci_basina_uygulanir()
    {
        using var limiter = PartitionedRateLimiter.Create<string, string>(ip => StoreRateLimiting.Partition(ip, StoreRateLimiting.AuthPermits));
        for (var i = 0; i < StoreRateLimiting.AuthPermits; i++) Assert.True(limiter.AttemptAcquire("1.1.1.1").IsAcquired);
        Assert.False(limiter.AttemptAcquire("1.1.1.1").IsAcquired);   // sınır aşıldı
        Assert.True(limiter.AttemptAcquire("2.2.2.2").IsAcquired);    // başka istemci etkilenmez
    }

    [Fact]
    public void Anonim_uclarin_hiz_siniri_ve_bilgi_ucu_isaretlidir()
    {
        var t = typeof(StoreAuthController);
        foreach (var name in new[] { nameof(StoreAuthController.Apply), nameof(StoreAuthController.Login), nameof(StoreAuthController.Info) })
            Assert.NotEmpty(t.GetMethod(name)!.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), true));
    }
}
