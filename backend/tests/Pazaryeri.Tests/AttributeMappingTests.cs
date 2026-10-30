using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Products;

namespace Pazaryeri.Tests;

public class AttributeMappingTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_AttrMap;Trusted_Connection=True;TrustServerCertificate=True";
    private const long ExtCategory = 411;
    private const long Renk = 47, Beden = 5, Not = 9;

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
        db.ChannelCategories.Add(new ChannelCategory { Channel = "Trendyol", ExternalId = ExtCategory, Name = "Tişört", IsLeaf = true });
        db.ChannelAttributes.AddRange(
            Attr(Renk, "Renk", required: true, allowCustom: false),
            Attr(Beden, "Beden", required: true, allowCustom: false),
            Attr(Not, "Not", required: false, allowCustom: true));
        db.ChannelAttributeValues.AddRange(
            Val(Renk, 1, "Kırmızı"), Val(Renk, 2, "Mavi"), Val(Renk, 3, "Pasif Renk", active: false),
            Val(Beden, 70, "M"), Val(Beden, 71, "L"));
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static ChannelAttribute Attr(long id, string name, bool required, bool allowCustom) =>
        new() { Channel = "Trendyol", CategoryExternalId = ExtCategory, ExternalId = id, Name = name, Required = required, AllowCustom = allowCustom };

    private static ChannelAttributeValue Val(long attr, long id, string name, bool active = true) =>
        new() { Channel = "Trendyol", CategoryExternalId = ExtCategory, AttributeExternalId = attr, ExternalId = id, Name = name, IsActive = active };

    private static async Task<(Guid Tenant, Guid Category)> SetupAsync(string name, params string?[] variants)
    {
        await using var db = NewDb();
        var tenant = new Tenant { Name = name };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        await using var seed = NewDb(tenant.Id);
        var category = new Category { Name = "Tişörtler" };
        seed.Categories.Add(category);
        await seed.SaveChangesAsync();
        seed.CategoryMappings.Add(new CategoryMapping { Channel = "Trendyol", CategoryId = category.Id, ExternalCategoryId = ExtCategory });
        for (var i = 0; i < variants.Length; i++)
            seed.Products.Add(new Product { Name = "P" + i, Barcode = $"{name}-{i}", Price = 1, CategoryId = category.Id, Variant = variants[i] });
        await seed.SaveChangesAsync();
        return (tenant.Id, category.Id);
    }

    private static string Body(object? value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    // --- varyant ayrıştırma ve özellik üretimi ---

    [Fact]
    public void Varyant_metni_anahtar_deger_ciftlerine_ayrilir()
    {
        var parsed = ProductAttributeBuilder.ParseVariant("Beden: M, renk : Mavi ; Not: a:b, bozuk, :bos, Ad:");

        Assert.Equal("M", parsed["BEDEN"]); // anahtar büyük/küçük harf duyarsız
        Assert.Equal("Mavi", parsed["Renk"]);
        Assert.Equal("a:b", parsed["Not"]); // ilk ':' ayırıcı
        Assert.Equal(3, parsed.Count);
        Assert.Empty(ProductAttributeBuilder.ParseVariant(null));
    }

    [Fact]
    public void Sabit_deger_ve_urun_ozelliginden_eslemeyle_ozellikler_uretilir_eksik_zorunlular_bildirilir()
    {
        var attributes = new[] { Attr(Renk, "Renk", true, false), Attr(Beden, "Beden", true, false), Attr(Not, "Not", false, true) };
        var mappings = new[]
        {
            new AttributeMapping { ExternalAttributeId = Renk, SourceAttribute = "Renk", Values = [new() { SourceValue = "mavi", ExternalValueId = 2 }] },
            new AttributeMapping { ExternalAttributeId = Not, SourceAttribute = "Not" }, // serbest metin: eşlenmemiş değer olduğu gibi gider
        };
        var product = new Product { Variant = "Renk: Mavi, Not: Yıkanabilir" };

        var (built, missing) = ProductAttributeBuilder.Build(product, attributes, mappings);

        Assert.Equal([(Renk, 2L, (string?)null), (Not, null, "Yıkanabilir")], built.Select(a => (a.AttributeId, a.ValueId, a.CustomValue)));
        Assert.Equal(["Beden"], missing); // eşlemesi yok

        // Listeli özellikte eşlenmemiş değer eksik sayılır; sabit değer her ürüne uygulanır.
        var product2 = new Product { Variant = "Renk: Yeşil" };
        mappings[0].Values.Clear();
        var withFixed = mappings.Append(new AttributeMapping { ExternalAttributeId = Beden, FixedValueId = 70 }).ToArray();
        var (built2, missing2) = ProductAttributeBuilder.Build(product2, attributes, withFixed);
        Assert.Equal(["Renk"], missing2);
        Assert.Contains(built2, a => a.AttributeId == Beden && a.ValueId == 70);
    }

    // --- durum ---

    [Fact]
    public void Satir_durumu_zorunlulugu_ve_eksik_degerleri_dikkate_alir()
    {
        var required = Attr(Renk, "Renk", true, false);
        var optional = Attr(Not, "Not", false, false);
        var mapped = new AttributeMapping { SourceAttribute = "Renk", Values = [new() { SourceValue = "Mavi", ExternalValueId = 2 }] };

        Assert.Equal("Missing", AttributeMappingStatus.Of(required, null, null));
        Assert.Equal("Optional", AttributeMappingStatus.Of(optional, null, null));
        Assert.Equal("Mapped", AttributeMappingStatus.Of(required, new AttributeMapping { FixedValueId = 1 }, null));
        Assert.Equal("Mapped", AttributeMappingStatus.Of(required, mapped, ["Mavi"]));
        Assert.Equal("Missing", AttributeMappingStatus.Of(required, mapped, ["Mavi", "Yeşil"])); // Yeşil eşlenmemiş
        Assert.Equal("Mapped", AttributeMappingStatus.Of(Attr(Not, "Not", true, true), new AttributeMapping { SourceAttribute = "Not" }, ["x"]));
    }

    // --- API ---

    [Fact]
    public async Task Liste_kanal_ozelliklerini_zorunluları_once_verir_ve_urunlerdeki_kendi_ozellikleri_gosterir()
    {
        var (tenant, category) = await SetupAsync("Liste", "Renk: Mavi, Beden: M", "Renk: Kırmızı", null);
        await using var db = NewDb(tenant);

        var list = (await new AttributeMappingController(db).List("Trendyol", category, default)).Value!;

        Assert.Equal(["Beden", "Renk", "Not"], list.Rows.Select(r => r.Name)); // zorunlular önce, kendi içinde ada göre
        Assert.Equal((2, 0), (list.RequiredTotal, list.RequiredMapped));
        Assert.Equal(["Beden", "Renk"], list.Sources.Select(s => s.Name));
        Assert.Equal(["Kırmızı", "Mavi"], list.Sources.Single(s => s.Name == "Renk").Values);
        Assert.Equal(2, list.Rows.Single(r => r.Name == "Renk").ValueCount); // pasif değer sayılmaz
        Assert.False(list.NeedsSync);
    }

    [Fact]
    public async Task Kategori_eslenmemisse_ya_da_ozellik_cekilmemisse_uygun_durum_doner()
    {
        var (tenant, category) = await SetupAsync("Durum");
        await using (var db0 = NewDb(tenant)) { db0.CategoryMappings.RemoveRange(db0.CategoryMappings); await db0.SaveChangesAsync(); }
        await using var db = NewDb(tenant);
        var c = new AttributeMappingController(db);

        var unmapped = (await c.List("Trendyol", category, default)).Value!;
        Assert.Null(unmapped.ChannelCategory);

        await using (var seed = NewDb(tenant)) { seed.CategoryMappings.Add(new CategoryMapping { Channel = "Trendyol", CategoryId = category, ExternalCategoryId = 999 }); await seed.SaveChangesAsync(); }
        await using (var seed = NewDb()) { seed.ChannelCategories.Add(new ChannelCategory { Channel = "Trendyol", ExternalId = 999, Name = "Özelliksiz", IsLeaf = true }); await seed.SaveChangesAsync(); }
        await using var db2 = NewDb(tenant);
        var noAttrs = (await new AttributeMappingController(db2).List("Trendyol", category, default)).Value!;
        Assert.True(noAttrs.NeedsSync);
        Assert.IsType<NotFoundObjectResult>((await c.List("Trendyol", Guid.NewGuid(), default)).Result);
    }

    [Fact]
    public async Task Sabit_deger_kaydedilir_ve_satir_eslendi_olur()
    {
        var (tenant, category) = await SetupAsync("Sabit", "Renk: Mavi");
        await using var db = NewDb(tenant);
        var c = new AttributeMappingController(db);

        Assert.IsType<NoContentResult>(await c.Save(new("Trendyol", category, Beden, null, 70, null, null), default));

        var list = (await c.List("Trendyol", category, default)).Value!;
        var row = list.Rows.Single(r => r.Name == "Beden");
        Assert.Equal(("Mapped", "M"), (row.Status, row.Mapping!.FixedValueName));
        Assert.Equal((2, 1), (list.RequiredTotal, list.RequiredMapped));
    }

    [Fact]
    public async Task Urun_ozelliginden_deger_eslemesi_kaydedilir_eslenmemis_satirlar_atlanir_ve_yeniden_kayit_degistirir()
    {
        var (tenant, category) = await SetupAsync("Kaynak", "Renk: Mavi", "Renk: Kırmızı", "Renk: Yeşil");
        await using var db = NewDb(tenant);
        var c = new AttributeMappingController(db);

        await c.Save(new("Trendyol", category, Renk, "Renk", null, null,
            [new("Mavi", 2, null), new("Kırmızı", 1, null), new("Yeşil", null, null)]), default);
        var first = (await c.List("Trendyol", category, default)).Value!.Rows.Single(r => r.Name == "Renk");
        Assert.Equal("Missing", first.Status); // Yeşil henüz eşlenmedi
        Assert.Equal(["Kırmızı", "Mavi"], first.Mapping!.Values.Select(v => v.SourceValue));
        Assert.Equal("Mavi", first.Mapping.Values.Single(v => v.SourceValue == "Mavi").ExternalValueName);

        await using var db2 = NewDb(tenant);
        var c2 = new AttributeMappingController(db2);
        await c2.Save(new("Trendyol", category, Renk, "Renk", null, null,
            [new("Mavi", 2, null), new("Kırmızı", 1, null), new("Yeşil", 2, null)]), default);
        var second = (await c2.List("Trendyol", category, default)).Value!.Rows.Single(r => r.Name == "Renk");
        Assert.Equal("Mapped", second.Status);
        Assert.Equal(3, await NewDb(tenant).AttributeValueMappings.CountAsync()); // eski satırlar birikmedi
        Assert.Equal(1, await NewDb(tenant).AttributeMappings.CountAsync());
    }

    [Fact]
    public async Task Gecersiz_kayitlar_reddedilir()
    {
        var (tenant, category) = await SetupAsync("Gecersiz");
        await using var db = NewDb(tenant);
        var c = new AttributeMappingController(db);
        async Task<string> Err(SaveAttributeMappingInput i) =>
            Body(((BadRequestObjectResult)await c.Save(i, default)).Value);

        Assert.Contains("birlikte", await Err(new("Trendyol", category, Renk, "Renk", 1, null, null)));
        Assert.Contains("seçin", await Err(new("Trendyol", category, Renk, null, null, null, null)));
        Assert.Contains("değer listesinde yok", await Err(new("Trendyol", category, Renk, null, 999, null, null)));
        Assert.Contains("değer listesinde yok", await Err(new("Trendyol", category, Renk, null, 3, null, null))); // pasif değer
        Assert.Contains("serbest metin kabul etmez", await Err(new("Trendyol", category, Renk, null, null, "Turkuaz", null)));
        Assert.Contains("bulunamadı", await Err(new("Trendyol", category, 12345, null, 1, null, null))); // kategoride olmayan özellik
        Assert.Contains("birden fazla", await Err(new("Trendyol", category, Renk, "Renk", null, null, [new("Mavi", 2, null), new("mavi", 1, null)])));
        Assert.Contains("yalnızca biri", await Err(new("Trendyol", category, Renk, "Renk", null, null, [new("Mavi", 2, "Mavi")])));
        Assert.Contains("kabul etmez", await Err(new("Trendyol", category, Renk, "Renk", null, null, [new("Mavi", null, "Turkuaz")])));
        Assert.Empty(await NewDb(tenant).AttributeMappings.ToListAsync());

        // Serbest metin yalnızca allowCustom özellikte geçerli.
        Assert.IsType<NoContentResult>(await c.Save(new("Trendyol", category, Not, null, null, "Pamuklu", null), default));
    }

    [Fact]
    public async Task Kategori_eslemesi_yoksa_ozellik_eslenemez_ve_silme_calisir_ve_kiracilar_ayridir()
    {
        var (a, catA) = await SetupAsync("Kiraci A");
        var (b, catB) = await SetupAsync("Kiraci B");
        await using var dbA = NewDb(a);
        var cA = new AttributeMappingController(dbA);
        await cA.Save(new("Trendyol", catA, Beden, null, 70, null, null), default);

        await using var dbB = NewDb(b);
        var cB = new AttributeMappingController(dbB);
        Assert.Null((await cB.List("Trendyol", catB, default)).Value!.Rows.Single(r => r.Name == "Beden").Mapping); // A'nın eşlemesi B'de görünmez
        Assert.IsType<NotFoundObjectResult>(await cB.Remove("Trendyol", catA, Beden, default));

        Assert.IsType<NoContentResult>(await cA.Remove("Trendyol", catA, Beden, default));
        Assert.Empty(await NewDb(a).AttributeMappings.ToListAsync());

        await using (var seed = NewDb(a)) { seed.CategoryMappings.RemoveRange(seed.CategoryMappings); await seed.SaveChangesAsync(); }
        await using var dbA2 = NewDb(a);
        var res = await new AttributeMappingController(dbA2).Save(new("Trendyol", catA, Beden, null, 70, null, null), default);
        Assert.Contains("Önce kategoriyi", Body(((BadRequestObjectResult)res).Value));
    }

    [Fact]
    public async Task Deger_aramasi_adla_filtreler_pasifleri_gizler()
    {
        await using var db = NewDb();
        var c = new AttributeMappingController(db);

        var all = (await c.Values("Trendyol", ExtCategory, Renk, null, default)).Value!;
        var found = (await c.Values("Trendyol", ExtCategory, Renk, "mav", default)).Value!;

        Assert.Equal(["Kırmızı", "Mavi"], all.Select(v => v.Name)); // "Pasif Renk" yok
        Assert.Equal(["Mavi"], found.Select(v => v.Name));
        Assert.IsType<BadRequestObjectResult>((await c.Values("N11", ExtCategory, Renk, null, default)).Result);
    }
}
