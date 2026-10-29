using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Tests;

public class ProductListTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Products;Trusted_Connection=True;TrustServerCertificate=True";

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

    private static async Task<Guid> SeedTenantAsync(string name, params Product[] products)
    {
        await using var db = NewDb(null);
        var tenant = new Tenant { Name = name };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        await using var seed = NewDb(tenant.Id);
        seed.Products.AddRange(products);
        await seed.SaveChangesAsync();
        return tenant.Id;
    }

    private static Product P(string name, string barcode, params (string Channel, ListingStatus Status)[] listings) => new()
    {
        Name = name, Barcode = barcode, Stock = 5, Price = 99.9m,
        Listings = listings.Select(l => new ProductListing { Channel = l.Channel, Status = l.Status, ErrorMessage = l.Status == ListingStatus.Error ? "Barkod geçersiz" : null }).ToList(),
    };

    [Fact]
    public async Task Ad_ve_barkodla_arar()
    {
        var tenant = await SeedTenantAsync("Ara", P("Mavi Tişört", "869001"), P("Kırmızı Çanta", "869002"), P("Siyah Çanta", "111003"));
        await using var db = NewDb(tenant);
        var c = new ProductsController(db);

        Assert.Equal(["Kırmızı Çanta", "Siyah Çanta"], (await c.List("çanta", null)).Items.Select(p => p.Name));
        Assert.Equal(["Mavi Tişört", "Kırmızı Çanta"], (await c.List("8690", null)).Items.OrderBy(p => p.Barcode).Select(p => p.Name));
        Assert.Empty((await c.List("yok", null)).Items);
    }

    [Fact]
    public async Task Filtreler_hatali_ve_onay_bekleyenleri_ayirir()
    {
        var tenant = await SeedTenantAsync("Filtre",
            P("A", "1", ("Trendyol", ListingStatus.Live)),
            P("B", "2", ("Trendyol", ListingStatus.Live), ("N11", ListingStatus.Error)),
            P("C", "3", ("Trendyol", ListingStatus.Pending)),
            P("D", "4"));
        await using var db = NewDb(tenant);
        var c = new ProductsController(db);

        Assert.Equal(4, (await c.List(null, "all")).Total);
        Assert.Equal(["B"], (await c.List(null, "error")).Items.Select(p => p.Name));
        Assert.Equal(["C"], (await c.List(null, "pending")).Items.Select(p => p.Name));
    }

    [Fact]
    public async Task Sunucu_tarafli_sayfalama_toplam_sayiyi_korur()
    {
        var tenant = await SeedTenantAsync("Sayfa", Enumerable.Range(1, 25).Select(i => P($"Ürün {i:00}", $"B{i:00}")).ToArray());
        await using var db = NewDb(tenant);
        var c = new ProductsController(db);

        var p1 = await c.List(null, null, 1, 10);
        var p3 = await c.List(null, null, 3, 10);

        Assert.Equal(25, p1.Total);
        Assert.Equal(10, p1.Items.Count);
        Assert.Equal(5, p3.Items.Count);
        Assert.Equal("Ürün 21", p3.Items[0].Name);
    }

    [Fact]
    public async Task Kanal_durumlari_urunle_gelir_ve_baska_kiracinin_urunu_gorunmez()
    {
        var a = await SeedTenantAsync("Kiracı A", P("Gizli", "9", ("Trendyol", ListingStatus.Error)));
        var b = await SeedTenantAsync("Kiracı B", P("Benim", "8", ("Amazon", ListingStatus.Live), ("Trendyol", ListingStatus.Error)));
        await using var db = NewDb(b);

        var page = await new ProductsController(db).List(null, null);

        var item = Assert.Single(page.Items);
        Assert.Equal("Benim", item.Name);
        Assert.Equal(["Amazon", "Trendyol"], item.Channels.Select(x => x.Channel));
        Assert.Equal("Barkod geçersiz", item.Channels[1].ErrorMessage);
        Assert.NotEqual(a, b);
    }
}
