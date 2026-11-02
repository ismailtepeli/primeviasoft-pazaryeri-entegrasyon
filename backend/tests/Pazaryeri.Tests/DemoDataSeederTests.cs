using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Modules;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Tests;

public class DemoDataSeederTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_DemoSeed;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class AmbientProvider : ITenantProvider
    {
        public Guid? TenantId => AmbientTenant.TenantId;
    }

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private static AppDbContext NewDb(Guid? tenant = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, tenant is null ? new AmbientProvider() : new FixedTenant(tenant));

    private static IConfiguration Config(string? dealerPassword = "Demo12345") => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Seed:TenantName"] = "Demo Firma", ["Seed:DemoDealerEmail"] = "Bayi@Demo.local", ["Seed:DemoDealerPassword"] = dealerPassword,
    }).Build();

    public async Task InitializeAsync()
    {
        await using var db = NewDb(Guid.Empty);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        db.Tenants.Add(new Tenant { Name = "Demo Firma", Slug = "demo", SetupCompletedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<DemoSeedResult> RunAsync(IConfiguration config)
    {
        await using var db = NewDb();
        return await new DemoDataSeeder(db, config).SeedAsync();
    }

    private static async Task<Guid> TenantIdAsync()
    {
        await using var db = NewDb(Guid.Empty);
        return await db.Tenants.Where(t => t.Name == "Demo Firma").Select(t => t.Id).SingleAsync();
    }

    [Fact]
    public async Task Demo_veri_kategori_urun_bayi_ve_modulleri_olusturur()
    {
        var first = await RunAsync(Config());
        Assert.Equal((4, 10, 1), (first.Categories, first.Products, first.Dealers));

        var tenant = await TenantIdAsync();
        await using var db = NewDb(tenant);
        Assert.Equal(10, await db.Products.CountAsync(p => p.Barcode.StartsWith("DEMO-")));
        Assert.All(await db.Products.Where(p => p.Barcode.StartsWith("DEMO-")).ToListAsync(), p => Assert.True(p.CategoryId != null && p.B2BVisible && p.Price > 0));
        var dealer = await db.Dealers.SingleAsync();
        Assert.Equal(DealerStatus.Active, dealer.Status);
        var user = await db.DealerUsers.SingleAsync();
        Assert.Equal("bayi@demo.local", user.Email);
        Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<DealerUser>().VerifyHashedPassword(user, user.PasswordHash, "Demo12345"));
        var enabled = await db.TenantModules.Where(m => m.Enabled).Select(m => m.ModuleId).ToListAsync();
        Assert.Contains(ModuleIds.B2BStore, enabled);
        Assert.Contains(ModuleIds.Dealers, enabled);
    }

    [Fact]
    public async Task Komut_tekrar_calistirilinca_ayni_sonucu_verir_var_olana_dokunmaz()
    {
        await RunAsync(Config());
        var tenant = await TenantIdAsync();
        await using (var db = NewDb(tenant))
        {
            var changed = await db.Products.SingleAsync(p => p.Barcode == "DEMO-001");
            changed.Price = 999m; // elle değiştirilen demo ürün korunur
            await db.SaveChangesAsync();
        }

        var again = await RunAsync(Config());
        Assert.Equal((4, 10, 1), (again.Categories, again.Products, again.Dealers));
        await using var check = NewDb(tenant);
        Assert.Equal(999m, (await check.Products.SingleAsync(p => p.Barcode == "DEMO-001")).Price);
        Assert.Equal(1, await check.DealerUsers.CountAsync());
        Assert.Equal(4, await check.Categories.CountAsync());
    }

    [Fact]
    public async Task Demo_B2C_musterisi_adres_ve_uc_siparisle_olusur_tekrarda_cogalmaz()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:TenantName"] = "Demo Firma", ["Seed:DemoCustomerEmail"] = "Musteri@Demo.local", ["Seed:DemoCustomerPassword"] = "Demo12345",
        }).Build();
        Assert.Equal(1, (await RunAsync(config)).Customers);
        Assert.Equal(1, (await RunAsync(config)).Customers);

        await using var db = NewDb(await TenantIdAsync());
        var customer = await db.Customers.Include(c => c.Addresses).SingleAsync();
        Assert.Equal(("musteri@demo.local", 1, true), (customer.Email, customer.Addresses.Count, customer.Addresses[0].IsDefault));
        var orders = await db.Orders.Where(o => o.CustomerId == customer.Id && o.Channel == "B2C").Include(o => o.Packages).ThenInclude(p => p.Lines).ToListAsync();
        Assert.Equal(3, orders.Count);
        Assert.Equal(["B2C-DEMO-001", "B2C-DEMO-002", "B2C-DEMO-003"], orders.Select(o => o.OrderNumber).Order().ToArray());
        Assert.All(orders, o => Assert.Equal(o.TotalPrice, o.Packages.Single().Lines.Sum(l => l.Price * l.Quantity)));
    }

    [Fact]
    public async Task Bayi_parolasi_verilmezse_bayi_olusturulmaz_ve_kiracisiz_calismaz()
    {
        var r = await RunAsync(Config(dealerPassword: null));
        Assert.Equal(0, r.Dealers);

        await using var db = NewDb();
        var noTenant = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:TenantName"] = "Yok Firma" }).Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DemoDataSeeder(db, noTenant).SeedAsync());
    }

    [Fact]
    public void Uretim_ayari_tohum_bilgisi_icermez_yalnizca_gelistirme_ayarinda_bulunur()
    {
        var api = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Pazaryeri.Api"));
        using var production = JsonDocument.Parse(File.ReadAllText(Path.Combine(api, "appsettings.json")));
        Assert.False(production.RootElement.TryGetProperty("Seed", out _)); // admin@demo.local üretimde bulunmamalı
        using var development = JsonDocument.Parse(File.ReadAllText(Path.Combine(api, "appsettings.Development.json")));
        Assert.True(development.RootElement.TryGetProperty("Seed", out _));
    }
}
