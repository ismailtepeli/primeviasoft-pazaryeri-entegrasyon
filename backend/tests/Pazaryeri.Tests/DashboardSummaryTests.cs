using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Tests;

public class DashboardSummaryTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_DashSummary;Trusted_Connection=True;TrustServerCertificate=True";
    // 30 Ekim 2026 00:30 Türkiye saati = 29 Ekim 21:30 UTC; "bugün" Türkiye gününe göre hesaplanır.
    private static readonly DateTime Now = new(2026, 10, 29, 21, 30, 0, DateTimeKind.Utc);

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private sealed class FakeTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
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

    private static Order O(string number, OrderStatus status, DateTime date) => new() { Channel = "Trendyol", OrderNumber = number, Status = status, OrderDate = date, LastModifiedAt = date };

    private static Product P(string barcode, params ListingStatus[] listings) => new()
    {
        Name = barcode, Barcode = barcode, Price = 1, Stock = 1,
        Listings = listings.Select((s, i) => new ProductListing { Channel = "Kanal" + i, Status = s }).ToList(),
    };

    private static async Task<Guid> SeedAsync(string name, Order[] orders, Product[] products, int failedJobs)
    {
        await using var global = NewDb(null);
        var tenant = new Tenant { Name = name };
        global.Tenants.Add(tenant);
        await global.SaveChangesAsync();
        await using var seed = NewDb(tenant.Id);
        seed.Orders.AddRange(orders);
        seed.Products.AddRange(products);
        for (var i = 0; i < failedJobs; i++) seed.SyncJobs.Add(new SyncJob { Channel = "Trendyol", Type = SyncJobType.ProductSend, Status = SyncJobStatus.Failed, Error = "x" });
        seed.SyncJobs.Add(new SyncJob { Channel = "Trendyol", Type = SyncJobType.ProductSend, Status = SyncJobStatus.Succeeded });
        await seed.SaveChangesAsync();
        return tenant.Id;
    }

    [Fact]
    public async Task Ozet_bugunku_siparis_bekleyen_kargo_senkron_hatasi_ve_aktif_urunu_sayar()
    {
        var tenant = await SeedAsync("Ozet",
            [
                O("1", OrderStatus.New, Now),                                   // bugün (Türkiye saatiyle 00:30)
                O("2", OrderStatus.Preparing, Now.AddMinutes(-20)),             // bugün (00:10)
                O("3", OrderStatus.Preparing, Now.AddMinutes(-40)),             // dün 23:50 Türkiye saati
                O("4", OrderStatus.Shipped, Now.AddDays(-2)),
            ],
            [P("A", ListingStatus.Live, ListingStatus.Live), P("B", ListingStatus.Pending), P("C", ListingStatus.Live), P("D")],
            failedJobs: 2);
        await using var db = NewDb(tenant);

        var summary = await new DashboardController(db, new FakeTime()).Summary(default);

        Assert.Equal(new DashboardSummary(TodayOrders: 2, AwaitingShipment: 2, SyncErrors: 2, ActiveProducts: 2), summary); // A iki kanalda yayında ama bir ürün sayılır
    }

    [Fact]
    public async Task Ozet_bos_kiracida_sifir_doner_ve_kiracilar_birbirini_gormez()
    {
        var a = await SeedAsync("OzetA", [O("1", OrderStatus.New, Now)], [P("A", ListingStatus.Live)], 1);
        var b = await SeedAsync("OzetB", [], [], 0);

        await using var dbB = NewDb(b);
        Assert.Equal(new DashboardSummary(0, 0, 0, 0), await new DashboardController(dbB, new FakeTime()).Summary(default));
        await using var dbA = NewDb(a);
        Assert.Equal(new DashboardSummary(1, 0, 1, 1), await new DashboardController(dbA, new FakeTime()).Summary(default));
    }
}
