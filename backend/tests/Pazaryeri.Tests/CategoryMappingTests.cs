using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Tests;

public class CategoryMappingTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_CatMap;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private static AppDbContext NewDb(Guid? tenantId = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    public async Task InitializeAsync()
    {
        await using var db = NewDb();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        // Kanal kataloğu (kiracıdan bağımsız): Giyim > Tişört (yaprak), Giyim > Pantolon (yaprak), Çanta (yaprak), Kaldırılmış (yaprak, pasif), Giyim > Erkek > Polo
        db.ChannelCategories.AddRange(
            Cat(1, null, "Giyim", leaf: false), Cat(2, 1, "Tişört", leaf: true), Cat(3, 1, "Pantolon", leaf: true),
            Cat(4, null, "Çanta", leaf: true), Cat(5, null, "Eski Tişört", leaf: true, active: false),
            Cat(6, 1, "Erkek", leaf: false), Cat(7, 6, "Polo Tişört", leaf: true));
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static ChannelCategory Cat(long id, long? parent, string name, bool leaf, bool active = true) =>
        new() { Channel = "Trendyol", ExternalId = id, ParentExternalId = parent, Name = name, IsLeaf = leaf, IsActive = active };

    private static async Task<Guid> NewTenantAsync(string name)
    {
        await using var db = NewDb();
        var tenant = new Tenant { Name = name };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static async Task<Guid> OwnCategoryAsync(Guid tenant, string name, Guid? parent = null)
    {
        await using var db = NewDb(tenant);
        var c = new Category { Name = name, ParentId = parent };
        db.Categories.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    [Fact]
    public async Task Gezinme_koklerden_baslar_cocuklari_listeler_ve_pasifleri_gizler()
    {
        var tenant = await NewTenantAsync("Gezin");
        await using var db = NewDb(tenant);
        var c = new CategoryMappingController(db);

        var roots = (await c.Browse("Trendyol", null, null, default)).Value!;
        Assert.Equal(["Çanta", "Giyim"], roots.Select(r => r.Name)); // pasif "Eski Tişört" yok
        Assert.True(roots.Single(r => r.Name == "Giyim").HasChildren);
        Assert.False(roots.Single(r => r.Name == "Çanta").HasChildren);

        var children = (await c.Browse("Trendyol", null, 1, default)).Value!;
        Assert.Equal(["Erkek", "Pantolon", "Tişört"], children.Select(r => r.Name));
        Assert.Equal("Giyim > Tişört", children.Single(r => r.ExternalId == 2).Path);
    }

    [Fact]
    public async Task Arama_yapraklari_one_alir_tam_yol_verir_ve_kisa_aramayi_reddeder()
    {
        var tenant = await NewTenantAsync("Ara");
        await using var db = NewDb(tenant);
        var c = new CategoryMappingController(db);

        var found = (await c.Browse("Trendyol", "tişört", null, default)).Value!;

        Assert.Equal(["Giyim > Erkek > Polo Tişört", "Giyim > Tişört"], found.Select(r => r.Path)); // pasif olan yok, ikisi de yaprak
        Assert.All(found, r => Assert.True(r.IsLeaf));
        Assert.IsType<BadRequestObjectResult>((await c.Browse("Trendyol", "a", null, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Browse("N11", null, null, default)).Result);
    }

    [Fact]
    public async Task Eslemede_yalnizca_etkin_yaprak_kategori_secilebilir()
    {
        var tenant = await NewTenantAsync("Kaydet");
        var own = await OwnCategoryAsync(tenant, "Tişörtler");
        await using var db = NewDb(tenant);
        var c = new CategoryMappingController(db);

        var branch = await c.Save(new("Trendyol", own, 1), default);
        Assert.Contains("en alt seviye", System.Text.Json.JsonSerializer.Serialize(((BadRequestObjectResult)branch.Result!).Value));
        Assert.IsType<BadRequestObjectResult>((await c.Save(new("Trendyol", own, 5), default)).Result);   // pasif
        Assert.IsType<BadRequestObjectResult>((await c.Save(new("Trendyol", own, 999), default)).Result); // yok
        Assert.IsType<NotFoundObjectResult>((await c.Save(new("Trendyol", Guid.NewGuid(), 2), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Save(new("N11", own, 2), default)).Result);
        Assert.Empty(await NewDb(tenant).CategoryMappings.ToListAsync());

        var saved = (await c.Save(new("Trendyol", own, 2), default)).Value!;
        Assert.Equal("Giyim > Tişört", saved.Mapped!.Path);
    }

    [Fact]
    public async Task Eslemeyi_degistirmek_tek_kayit_tutar_ve_silmek_kaldirir()
    {
        var tenant = await NewTenantAsync("Degistir");
        var own = await OwnCategoryAsync(tenant, "Alt Giyim");
        await using var db = NewDb(tenant);
        var c = new CategoryMappingController(db);

        await c.Save(new("Trendyol", own, 2), default);
        await c.Save(new("Trendyol", own, 3), default);

        var stored = await NewDb(tenant).CategoryMappings.SingleAsync();
        Assert.Equal(3, stored.ExternalCategoryId);

        Assert.IsType<NoContentResult>(await c.Remove("Trendyol", own, default));
        Assert.IsType<NotFoundObjectResult>(await c.Remove("Trendyol", own, default));
    }

    [Fact]
    public async Task Liste_kendi_kategorilerini_eslemeleri_ve_kanaldan_kalkan_eslemeyi_gosterir_baska_kiracidan_ayrilir()
    {
        var a = await NewTenantAsync("Liste A");
        var b = await NewTenantAsync("Liste B");
        var parent = await OwnCategoryAsync(a, "Giyim");
        var child = await OwnCategoryAsync(a, "Tişört", parent);
        await OwnCategoryAsync(b, "Başkasının");
        await using (var seed = NewDb(a))
        {
            seed.CategoryMappings.Add(new CategoryMapping { Channel = "Trendyol", CategoryId = child, ExternalCategoryId = 2 });
            seed.CategoryMappings.Add(new CategoryMapping { Channel = "Trendyol", CategoryId = parent, ExternalCategoryId = 5 }); // sonradan kanaldan kalkmış
            seed.Products.Add(new Product { Name = "P", Barcode = "L1", Price = 1, CategoryId = child });
            await seed.SaveChangesAsync();
        }

        await using var db = NewDb(a);
        var list = (await new CategoryMappingController(db).List("Trendyol", default)).Value!;

        Assert.Equal(["Giyim", "Giyim > Tişört"], list.Rows.Select(r => r.Path));
        Assert.Equal((2, 1), (list.Total, list.MappedCount)); // kalkmış eşleme "eşlendi" sayılmaz
        var tisort = list.Rows.Single(r => r.CategoryId == child);
        Assert.Equal((1, "Giyim > Tişört"), (tisort.ProductCount, tisort.Mapped!.Path));
        Assert.False(list.Rows.Single(r => r.CategoryId == parent).Mapped!.IsActive);
    }
}
