using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Orders;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Tests;

public class OrderListTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_OrderList;Trusted_Connection=True;TrustServerCertificate=True";
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

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

    private static OrdersController Controller(AppDbContext db) => new(db, new FakeTime());

    public async Task InitializeAsync()
    {
        await using var db = NewDb(null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Order O(string number, OrderStatus status, TimeSpan age, string customer = "Ayşe Yılmaz", decimal total = 100, int qty = 1, string channelStatus = "Created") => new()
    {
        Channel = "Trendyol", OrderNumber = number, Status = status, OrderDate = Now - age, CustomerName = customer, TotalPrice = total, LastModifiedAt = Now - age,
        Packages = [new OrderPackage
        {
            Channel = "Trendyol", PackageId = "PK-" + number, ChannelStatus = channelStatus, Status = status, LastModifiedAt = Now - age,
            Lines = [new OrderLine { LineId = "L1", Barcode = "bc", Name = "Ürün", Quantity = qty, Price = 10 }],
        }],
    };

    private static async Task<Guid> SeedAsync(string name, params Order[] orders)
    {
        await using var global = NewDb(null);
        var tenant = new Tenant { Name = name };
        global.Tenants.Add(tenant);
        await global.SaveChangesAsync();
        await using var seed = NewDb(tenant.Id);
        seed.Orders.AddRange(orders);
        await seed.SaveChangesAsync();
        return tenant.Id;
    }

    [Fact]
    public void Gecikme_kurali_yalnizca_yeni_ve_hazirlanan_siparisler_icin_sureye_bakar()
    {
        Assert.False(OrderRules.IsLate(OrderStatus.New, Now - TimeSpan.FromHours(24), Now));
        Assert.True(OrderRules.IsLate(OrderStatus.New, Now - TimeSpan.FromHours(25), Now));
        Assert.False(OrderRules.IsLate(OrderStatus.Preparing, Now - TimeSpan.FromHours(30), Now));
        Assert.True(OrderRules.IsLate(OrderStatus.Preparing, Now - TimeSpan.FromHours(49), Now));
        Assert.False(OrderRules.IsLate(OrderStatus.Shipped, Now - TimeSpan.FromDays(9), Now));
        Assert.False(OrderRules.IsLate(OrderStatus.Cancelled, Now - TimeSpan.FromDays(9), Now));
    }

    [Fact]
    public async Task Sekmeler_duruma_gore_suzer_iptal_sekmesi_iade_de_icerir_ve_sayaclar_tum_siparisleri_sayar()
    {
        var tenant = await SeedAsync("Sekme",
            O("1", OrderStatus.New, TimeSpan.FromHours(1)), O("2", OrderStatus.New, TimeSpan.FromHours(2)),
            O("3", OrderStatus.Preparing, TimeSpan.FromHours(3)), O("4", OrderStatus.Shipped, TimeSpan.FromHours(4)),
            O("5", OrderStatus.Delivered, TimeSpan.FromHours(5)), O("6", OrderStatus.Cancelled, TimeSpan.FromHours(6)), O("7", OrderStatus.Returned, TimeSpan.FromHours(7)));
        await using var db = NewDb(tenant);
        var c = Controller(db);

        var page = (await c.List("new", null)).Value!;

        Assert.Equal(["1", "2"], page.Items.Select(i => i.OrderNumber)); // en yeni önce
        Assert.Equal((2, 1, 1, 1, 2), (page.Counts.New, page.Counts.Preparing, page.Counts.Shipped, page.Counts.Delivered, page.Counts.Cancelled));
        Assert.Equal(["6", "7"], (await c.List("cancelled", null)).Value!.Items.Select(i => i.OrderNumber));
        Assert.Equal(7, (await c.List(null, null)).Value!.Total);
        Assert.IsType<BadRequestObjectResult>((await c.List("yok", null)).Result);
    }

    [Fact]
    public async Task Arama_siparis_no_ve_musteriye_bakar_sayaclar_aramadan_etkilenmez()
    {
        var tenant = await SeedAsync("Ara", O("TY-100", OrderStatus.New, TimeSpan.FromHours(1), "Mehmet Kaya"), O("TY-200", OrderStatus.New, TimeSpan.FromHours(2), "Ayşe Demir"));
        await using var db = NewDb(tenant);
        var c = Controller(db);

        Assert.Equal(["TY-200"], (await c.List("new", "demir")).Value!.Items.Select(i => i.OrderNumber));
        var byNumber = (await c.List("new", "TY-100")).Value!;
        Assert.Equal(["TY-100"], byNumber.Items.Select(i => i.OrderNumber));
        Assert.Equal(2, byNumber.Counts.New);
    }

    [Fact]
    public async Task Satir_alanlari_tutar_adet_kanal_durumu_ve_gecikme_dogru_gelir()
    {
        var tenant = await SeedAsync("Alan",
            O("LATE", OrderStatus.New, TimeSpan.FromHours(30), total: 250.5m, qty: 3, channelStatus: "Created"),
            O("OK", OrderStatus.Preparing, TimeSpan.FromHours(2), channelStatus: "Picking"));
        await using var db = NewDb(tenant);

        var items = (await Controller(db).List(null, null)).Value!.Items;

        var late = items.Single(i => i.OrderNumber == "LATE");
        Assert.Equal(("Trendyol", "New", "Created", 3, 250.5m, true), (late.Channel, late.Status, late.ChannelStatus, late.ItemCount, late.TotalPrice, late.IsLate));
        var ok = items.Single(i => i.OrderNumber == "OK");
        Assert.Equal(("Preparing", "Picking", false), (ok.Status, ok.ChannelStatus, ok.IsLate));
    }

    [Fact]
    public async Task Sayfalama_calisir_ve_kiracilar_birbirinin_siparisini_gormez()
    {
        var orders = Enumerable.Range(1, 25).Select(i => O("S" + i.ToString("00"), OrderStatus.New, TimeSpan.FromMinutes(i))).ToArray();
        var tenant = await SeedAsync("Sayfa", orders);
        var other = await SeedAsync("Baska", O("X1", OrderStatus.New, TimeSpan.FromHours(1)));

        await using var db = NewDb(tenant);
        var page2 = (await Controller(db).List("new", null, page: 2, pageSize: 20)).Value!;
        Assert.Equal((5, 25), (page2.Items.Count, page2.Total));
        Assert.DoesNotContain(page2.Items, i => i.OrderNumber == "X1");
        await using var db2 = NewDb(other);
        Assert.Equal(["X1"], (await Controller(db2).List(null, null)).Value!.Items.Select(i => i.OrderNumber));
    }
}
