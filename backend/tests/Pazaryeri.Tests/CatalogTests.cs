using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Products;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Products;

namespace Pazaryeri.Tests;

public class CatalogTests : IAsyncLifetime, IDisposable
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Catalog;Trusted_Connection=True;TrustServerCertificate=True";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pz-img-" + Guid.NewGuid().ToString("N"));

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

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static async Task<Guid> NewTenantAsync(string name)
    {
        await using var db = NewDb(null);
        var tenant = new Tenant { Name = name };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static async Task<Guid> NewProductAsync(Guid tenant, string barcode = "B1")
    {
        await using var db = NewDb(tenant);
        var p = new Product { Name = "Ürün", Barcode = barcode, Price = 10 };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        return p.Id;
    }

    private (ProductImageService Service, LocalImageStore Store) Images(AppDbContext db, Guid tenant)
    {
        var store = new LocalImageStore(_dir);
        return (new ProductImageService(db, store, new FixedTenant(tenant)), store);
    }

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private static (string, Stream) File_(string name, byte[] bytes) => (name, new MemoryStream(bytes));

    [Fact]
    public void Gorsel_turu_dosya_basligindan_anlasilir()
    {
        Assert.Equal(".png", ImageSniffer.Detect(Png)!.Value.Extension);
        Assert.Equal(".jpg", ImageSniffer.Detect(Jpg)!.Value.Extension);
        Assert.Equal(".webp", ImageSniffer.Detect("RIFF\0\0\0\0WEBPVP8 "u8)!.Value.Extension);
        Assert.Null(ImageSniffer.Detect("<svg onload=alert(1)>"u8));
        Assert.Null(ImageSniffer.Detect("MZ\x90\0"u8));
    }

    [Fact]
    public async Task Yuklenen_gorseller_sirayla_saklanir_ve_dosyalar_diske_yazilir()
    {
        var tenant = await NewTenantAsync("Gorsel A");
        var product = await NewProductAsync(tenant);
        await using var db = NewDb(tenant);
        var (service, store) = Images(db, tenant);

        var result = await service.AddUploadsAsync(product, [File_("a.png", Png), File_("b.jpg", Jpg), File_("c.png", Png)]);

        Assert.Equal([0, 1, 2], result.Select(i => i.Position));
        Assert.All(result, i => Assert.StartsWith("/api/images/", i.Url));
        using var read = store.Open(tenant, result[1].Id, ".jpg");
        Assert.NotNull(read);
    }

    [Fact]
    public async Task Gecersiz_dosya_tum_yuklemeyi_reddeder_ve_hicbir_sey_kaydedilmez()
    {
        var tenant = await NewTenantAsync("Gorsel B");
        var product = await NewProductAsync(tenant);
        await using var db = NewDb(tenant);
        var (service, _) = Images(db, tenant);

        var ex = await Assert.ThrowsAsync<CatalogException>(() =>
            service.AddUploadsAsync(product, [File_("ok.png", Png), File_("virus.png", "<script>alert(1)</script>"u8.ToArray())]));

        Assert.Contains("virus.png", ex.Message);
        Assert.Empty(await NewDb(tenant).ProductImages.ToListAsync());
        Assert.False(Directory.Exists(_dir) && Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Buyuk_dosya_ve_gorsel_siniri_reddedilir()
    {
        var tenant = await NewTenantAsync("Gorsel C");
        var product = await NewProductAsync(tenant);
        await using var db = NewDb(tenant);
        var (service, _) = Images(db, tenant);

        var big = new byte[ImageSniffer.MaxBytes + 1];
        Png.CopyTo(big, 0);
        var tooBig = await Assert.ThrowsAsync<CatalogException>(() => service.AddUploadsAsync(product, [File_("big.png", big)]));
        Assert.Contains("5 MB", tooBig.Message);

        for (var i = 0; i < ProductImage.MaxPerProduct; i++) await service.AddUrlAsync(product, $"https://cdn.example.com/{i}.jpg");
        var over = await Assert.ThrowsAsync<CatalogException>(() => service.AddUrlAsync(product, "https://cdn.example.com/9.jpg"));
        Assert.Contains("en fazla 8", over.Message);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://x.com/a.jpg")]
    [InlineData("https://user:pass@x.com/a.jpg")]
    [InlineData("/relative/a.jpg")]
    [InlineData("")]
    public async Task Gecersiz_gorsel_adresi_reddedilir(string url)
    {
        var tenant = await NewTenantAsync("Gorsel D" + url.GetHashCode());
        var product = await NewProductAsync(tenant);
        await using var db = NewDb(tenant);
        var (service, _) = Images(db, tenant);

        await Assert.ThrowsAsync<CatalogException>(() => service.AddUrlAsync(product, url));
    }

    [Fact]
    public async Task Siralama_degisir_kapak_ilk_gorsel_olur_ve_silinince_bosluk_kalmaz()
    {
        var tenant = await NewTenantAsync("Gorsel E");
        var product = await NewProductAsync(tenant);
        await using var db = NewDb(tenant);
        var (service, store) = Images(db, tenant);
        await service.AddUploadsAsync(product, [File_("a.png", Png)]);
        await service.AddUrlAsync(product, "https://cdn.example.com/b.jpg");
        var all = await service.AddUrlAsync(product, "https://cdn.example.com/c.jpg");

        var reordered = await service.ReorderAsync(product, [all[2].Id, all[0].Id, all[1].Id]);
        Assert.Equal("https://cdn.example.com/c.jpg", reordered[0].Url); // kapak değişti
        Assert.Equal([0, 1, 2], reordered.Select(i => i.Position));

        var upload = reordered.Single(i => i.Url.StartsWith("/api/images/"));
        var after = await service.DeleteAsync(product, upload.Id);
        Assert.Equal([0, 1], after.Select(i => i.Position));
        Assert.Null(store.Open(tenant, upload.Id, ".png")); // dosya da silindi

        await Assert.ThrowsAsync<CatalogException>(() => service.ReorderAsync(product, [after[0].Id])); // eksik liste
        await Assert.ThrowsAsync<CatalogException>(() => service.ReorderAsync(product, [after[0].Id, after[0].Id])); // tekrar
    }

    [Fact]
    public async Task Baska_kiracinin_urunune_gorsel_eklenemez()
    {
        var a = await NewTenantAsync("Gorsel F1");
        var b = await NewTenantAsync("Gorsel F2");
        var productOfA = await NewProductAsync(a);
        await using var db = NewDb(b);
        var (service, _) = Images(db, b);

        var ex = await Assert.ThrowsAsync<CatalogException>(() => service.AddUrlAsync(productOfA, "https://cdn.example.com/x.jpg"));
        Assert.Equal(404, ex.Status);
    }

    [Fact]
    public async Task Gorseller_listede_kapak_ve_sayiyla_gelir()
    {
        var tenant = await NewTenantAsync("Liste");
        var product = await NewProductAsync(tenant);
        await using var db = NewDb(tenant);
        var (service, _) = Images(db, tenant);
        await service.AddUrlAsync(product, "https://cdn.example.com/cover.jpg");
        await service.AddUrlAsync(product, "https://cdn.example.com/2.jpg");
        await NewProductAsync(tenant, "B2");

        var page = await new ProductsController(NewDb(tenant)).List(null, null);

        var withImages = page.Items.Single(p => p.ImageCount == 2);
        Assert.Equal("https://cdn.example.com/cover.jpg", withImages.CoverUrl);
        Assert.Null(page.Items.Single(p => p.ImageCount == 0).CoverUrl);
    }

    [Fact]
    public async Task Kategori_agaci_yolu_olusur_ve_kurallar_uygulanir()
    {
        var tenant = await NewTenantAsync("Kategori");
        await using var db = NewDb(tenant);
        var c = new CategoriesController(db);

        var root = (await c.Create(new CategoryInput("Giyim", null), default)).Value!;
        var child = (await c.Create(new CategoryInput("Tişört", root.Id), default)).Value!;

        Assert.Equal("Giyim > Tişört", child.Path);
        Assert.Equal(["Giyim", "Giyim > Tişört"], (await c.List(default)).Select(x => x.Path));
        Assert.IsType<BadRequestObjectResult>((await c.Create(new CategoryInput("giyim", null), default)).Result); // aynı ad (büyük/küçük harf duyarsız)
        Assert.IsType<BadRequestObjectResult>((await c.Create(new CategoryInput("A > B", null), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Create(new CategoryInput(" ", null), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Create(new CategoryInput("X", Guid.NewGuid()), default)).Result);

        Assert.IsType<BadRequestObjectResult>(await c.Delete(root.Id, default)); // alt kategorisi var
        var renamed = (await c.Rename(child.Id, new CategoryInput("Polo", null), default)).Value!;
        Assert.Equal("Giyim > Polo", renamed.Path);
        Assert.IsType<NoContentResult>(await c.Delete(child.Id, default));
    }

    [Fact]
    public async Task Urun_olusturma_ve_guncelleme_dogrulanir_ve_kategoriyle_gelir()
    {
        var tenant = await NewTenantAsync("Urun CRUD");
        await using var db = NewDb(tenant);
        var cat = (await new CategoriesController(db).Create(new CategoryInput("Çanta", null), default)).Value!;
        var c = new ProductsController(db);

        var created = (await c.Create(new ProductInput(" Deri Çanta ", " BC1 ", "M1", "Renk: Siyah", "Marka X", "Açıklama", cat.Id, 4, 99.999m), default)).Value!;

        Assert.Equal(("Deri Çanta", "BC1", "Çanta", 100.00m), (created.Name, created.Barcode, created.CategoryPath, created.Price));

        Assert.IsType<BadRequestObjectResult>((await c.Create(new ProductInput("A", "BC1", null, null, null, null, null, 1, 1), default)).Result); // barkod çakışması
        Assert.IsType<BadRequestObjectResult>((await c.Create(new ProductInput("", "Z", null, null, null, null, null, 1, 1), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Create(new ProductInput("A", "Z", null, null, null, null, null, -1, 1), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Create(new ProductInput("A", "Z", null, null, null, null, null, 1, 0), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Create(new ProductInput("A", "Z", null, null, null, null, Guid.NewGuid(), 1, 1), default)).Result);

        var updated = (await c.Update(created.Id, new ProductInput("Yeni Ad", "BC1", null, null, null, null, null, 9, 5), default)).Value!;
        Assert.Equal(("Yeni Ad", 9, null), (updated.Name, updated.Stock, updated.CategoryPath)); // barkod aynı kalınca çakışma sayılmaz
        Assert.IsType<NotFoundObjectResult>((await c.Update(Guid.NewGuid(), new ProductInput("A", "Q", null, null, null, null, null, 1, 1), default)).Result);
        Assert.IsType<NoContentResult>(await new CategoriesController(db).Delete(cat.Id, default)); // ürün kategoriden çıktığı için silinebilir
    }
}
