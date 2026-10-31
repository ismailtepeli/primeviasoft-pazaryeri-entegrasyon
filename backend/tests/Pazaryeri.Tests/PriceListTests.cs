using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Pricing;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Pricing;

namespace Pazaryeri.Tests;

public class PriceListTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_PriceLists;Trusted_Connection=True;TrustServerCertificate=True";

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

    private static async Task<Guid> TenantAsync(string name)
    {
        await using var db = NewDb(null);
        var t = new Tenant { Name = name };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private static PriceListsController Controller(AppDbContext db) =>
        new(db, new DealerPriceService(db), new PriceListImportService(db));

    private static PriceListInput ListInput(string name = "Altın", Guid? group = null, Guid? dealer = null, decimal? discount = null, string currency = "TRY",
        bool includesVat = false, bool showVat = false, bool active = true) =>
        new(name, currency, group, dealer, discount, includesVat, showVat, active);

    // --- çözümleme kuralı (saf) ---

    private static readonly PriceListRule Plain = new(null, false, false, "TRY");

    [Fact]
    public void Liste_yoksa_genel_fiyat_KDV_dahil_kabul_edilir()
    {
        var p = DealerPricing.Resolve(120m, 20, null, null, null, null);
        Assert.Equal((PriceSource.General, 120m, 100m, 120m), (p.Source, p.Gross, p.Net, p.Display));
        Assert.True(p.DisplayIncludesVat);
    }

    [Fact]
    public void Genel_fiyat_KDV_hariç_gösterim_ayarına_uyar_net_brut_degismez()
    {
        var shown = DealerPricing.Resolve(120m, 20, null, null, null, null, generalShowVatIncluded: true);
        var hidden = DealerPricing.Resolve(120m, 20, null, null, null, null, generalShowVatIncluded: false);
        Assert.Equal((120m, true), (shown.Display, shown.DisplayIncludesVat));
        Assert.Equal((100m, false), (hidden.Display, hidden.DisplayIncludesVat));
        Assert.Equal((hidden.Net, hidden.Gross), (shown.Net, shown.Gross)); // sipariş toplamları etkilenmez

        // liste fiyatı kendi gösterim ayarını korur; genel ayar yalnızca liste dışı fiyatı etkiler
        var list = new PriceListRule(null, false, true, "TRY");
        var fromList = DealerPricing.Resolve(120m, 20, list, 100m, null, null, generalShowVatIncluded: false);
        Assert.Equal((PriceSource.DealerList, 120m, true), (fromList.Source, fromList.Display, fromList.DisplayIncludesVat));
        // listede kalemi olmayan ürün genel fiyata düşer ve genel ayara uyar
        var fallback = DealerPricing.Resolve(120m, 20, list, null, null, null, generalShowVatIncluded: false);
        Assert.Equal((PriceSource.General, 100m, false), (fallback.Source, fallback.Display, fallback.DisplayIncludesVat));
    }

    [Fact]
    public void Oncelik_bayiye_ozel_sonra_grup_sonra_genel()
    {
        var dealer = Plain; var group = Plain;
        var all = DealerPricing.Resolve(200m, 20, dealer, 90m, group, 95m);
        Assert.Equal((PriceSource.DealerList, 90m), (all.Source, all.Net));

        var groupOnly = DealerPricing.Resolve(200m, 20, dealer, null, group, 95m); // bayi listesinde ürün yok
        Assert.Equal((PriceSource.GroupList, 95m), (groupOnly.Source, groupOnly.Net));

        var general = DealerPricing.Resolve(200m, 20, dealer, null, group, null);
        Assert.Equal(PriceSource.General, general.Source);
    }

    [Fact]
    public void Kalem_fiyati_yoksa_liste_iskontosu_genel_fiyattan_uygulanir()
    {
        var group = new PriceListRule(10m, false, true, "TRY");
        var p = DealerPricing.Resolve(200m, 20, null, null, group, null);
        Assert.Equal((PriceSource.GroupList, 180m, 150m, 180m), (p.Source, p.Gross, p.Net, p.Display));

        // Kalem fiyatı iskontodan önceliklidir.
        var item = DealerPricing.Resolve(200m, 20, null, null, group, 100m);
        Assert.Equal(100m, item.Net);

        // Bayi listesinde iskonto da kalem de yoksa gruba inilir.
        var fall = DealerPricing.Resolve(200m, 20, Plain, null, group, null);
        Assert.Equal((PriceSource.GroupList, 180m), (fall.Source, fall.Gross));
    }

    [Fact]
    public void KDV_dahil_hariç_donusumu_ve_yuvarlama_iki_basamak_sifirdan_uzaga()
    {
        // Girilen fiyat KDV hariç: 100 → brüt 120; gösterim dahil seçili.
        var excl = DealerPricing.Resolve(0m, 20, null, null, new PriceListRule(null, false, true, "TRY"), 100m);
        Assert.Equal((100m, 120m, 120m), (excl.Net, excl.Gross, excl.Display));

        // Girilen fiyat KDV dahil: 100 → net 83,33; gösterim hariç.
        var incl = DealerPricing.Resolve(0m, 20, null, null, new PriceListRule(null, true, false, "TRY"), 100m);
        Assert.Equal((83.33m, 100m, 83.33m, false), (incl.Net, incl.Gross, incl.Display, incl.DisplayIncludesVat));

        Assert.Equal(1.01m, DealerPricing.Round(1.005m)); // yarım yukarı
        Assert.Equal(1.00m, DealerPricing.Round(1.004m));
        var disc = DealerPricing.Resolve(99.99m, 18, null, null, new PriceListRule(33.33m, false, true, "TRY"), null);
        Assert.Equal(66.66m, disc.Gross);
    }

    [Fact]
    public void Para_birimi_kullanilan_listeden_gelir()
    {
        var p = DealerPricing.Resolve(100m, 20, null, null, new PriceListRule(null, false, false, "USD"), 5m);
        Assert.Equal("USD", p.Currency);
        Assert.Equal("TRY", DealerPricing.Resolve(100m, 20, null, null, null, null).Currency);
    }

    // --- minimum ve koli adedi ---

    [Theory]
    [InlineData(1, 1, 1, true, 1)]
    [InlineData(5, 10, 5, false, 10)]   // minimumun altı
    [InlineData(12, 10, 5, false, 15)]  // koli katı değil → yukarı yuvarla
    [InlineData(15, 10, 5, true, 15)]
    [InlineData(0, 1, 1, false, 1)]
    [InlineData(8, 6, 4, true, 8)]      // minimum 6, koli 4 → etkin minimum 8; 8 uygun
    [InlineData(4, 6, 4, false, 8)]
    public void Siparis_adedi_minimum_ve_koli_katina_gore_dogrulanir(int qty, int min, int pack, bool valid, int suggested)
    {
        var r = OrderQuantityRules.Check(qty, min, pack);
        Assert.Equal((valid, suggested), (r.Valid, r.Suggested));
        Assert.Equal(valid, r.Error is null);
    }

    [Fact]
    public void Urun_adet_ayarlari_dogrulanir()
    {
        Assert.Null(OrderQuantityRules.ValidateSettings(1, 1));
        Assert.NotNull(OrderQuantityRules.ValidateSettings(0, 1));
        Assert.NotNull(OrderQuantityRules.ValidateSettings(1, 0));
        Assert.NotNull(OrderQuantityRules.ValidateSettings(100001, 1));
        Assert.Equal((8, 4), OrderQuantityRules.Normalize(6, 4));
    }

    // --- yönetici API ---

    [Fact]
    public async Task Liste_olusur_dogrulamalar_calisir_ve_ad_kiracida_tekildir()
    {
        var tenant = await TenantAsync("Liste");
        await using var db = NewDb(tenant);
        var group = new DealerGroup { Name = "Altın" };
        db.DealerGroups.Add(group);
        await db.SaveChangesAsync();
        var c = Controller(db);

        var created = Assert.IsType<PriceListDto>(Assert.IsType<CreatedResult>((await c.Create(ListInput(group: group.Id, discount: 5m), default)).Result).Value);
        Assert.Equal(("Altın", 5m, 0), (created.GroupName, created.DiscountPercent, created.ItemCount));

        Assert.IsType<ConflictObjectResult>((await c.Create(ListInput(), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Create(ListInput("x", group: group.Id, dealer: Guid.NewGuid()), default)).Result); // ikisine birden
        Assert.IsType<BadRequestObjectResult>((await c.Create(ListInput("y", discount: 101m), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Create(ListInput("z", currency: "GBP"), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Create(ListInput(" "), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await c.Create(ListInput("w", group: Guid.NewGuid()), default)).Result);

        var updated = (await c.Update(created.Id, ListInput("Altın Plus", group: group.Id, discount: 0m, includesVat: true), default)).Value!;
        Assert.Equal(("Altın Plus", null, true), (updated.Name, updated.DiscountPercent, updated.PricesIncludeVat)); // 0 iskonto = yok
        Assert.IsType<NotFoundObjectResult>((await c.Update(Guid.NewGuid(), ListInput("q"), default)).Result);
        Assert.IsType<NoContentResult>(await c.Delete(created.Id, default));
        Assert.IsType<NotFoundObjectResult>(await c.Delete(created.Id, default));
    }

    [Fact]
    public async Task Liste_fiyati_yazilir_bos_fiyat_kalemi_siler_adet_ayari_kaydedilir()
    {
        var tenant = await TenantAsync("Kalem");
        await using var db = NewDb(tenant);
        var product = new Product { Name = "Kalem", Barcode = "B1", Price = 100m };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var c = Controller(db);
        var list = (await c.Create(ListInput(), default)).Result as CreatedResult;
        var id = ((PriceListDto)list!.Value!).Id;

        Assert.IsType<NoContentResult>(await c.SetItem(id, product.Id, new PriceListItemInput(79.999m), default));
        var page = (await c.Items(id, null, false, 1, 20, default)).Value!;
        Assert.Equal((1, 80.00m, 100m), (page.Total, page.Items[0].ListPrice, page.Items[0].GeneralPrice)); // 2 basamağa yuvarlanır

        Assert.Single((await c.Items(id, null, true, 1, 20, default)).Value!.Items);
        Assert.IsType<BadRequestObjectResult>(await c.SetItem(id, product.Id, new PriceListItemInput(-1m), default));
        Assert.IsType<NotFoundObjectResult>(await c.SetItem(id, Guid.NewGuid(), new PriceListItemInput(1m), default));

        Assert.IsType<NoContentResult>(await c.SetItem(id, product.Id, new PriceListItemInput(null), default));
        Assert.Empty((await c.Items(id, null, true, 1, 20, default)).Value!.Items);
        Assert.Single((await c.Items(id, "Kal", false, 1, 20, default)).Value!.Items); // fiyatsız ürünler de listelenir
        Assert.Empty((await c.Items(id, "yok", false, 1, 20, default)).Value!.Items);

        Assert.IsType<NoContentResult>(await c.SetQuantity(product.Id, new QuantityRuleInput(10, 5), default));
        Assert.IsType<BadRequestObjectResult>(await c.SetQuantity(product.Id, new QuantityRuleInput(0, 5), default));
        Assert.IsType<NotFoundObjectResult>(await c.SetQuantity(Guid.NewGuid(), new QuantityRuleInput(1, 1), default));
        await using var check = NewDb(tenant);
        var saved = await check.Products.SingleAsync();
        Assert.Equal((10, 5), (saved.MinOrderQuantity, saved.PackSize));
    }

    // --- Excel şablonu ve dışa aktarma ---

    [Fact]
    public void Sablon_icerige_aktarmayla_ayni_basliklari_tasir_ve_bos_gelir()
    {
        using var ms = new MemoryStream(PriceListImportService.Template());
        using var book = new ClosedXML.Excel.XLWorkbook(ms);
        var sheet = book.Worksheets.Single();
        Assert.Equal(new[] { "Barkod", "Fiyat", "Minimum", "Koli" }, sheet.Row(1).Cells(1, 4).Select(c => c.GetString()));
        Assert.Equal(1, sheet.RowsUsed().Count());   // yalnızca başlık: örnek satır yok, yanlışlıkla içe aktarılmaz
        Assert.True(sheet.Cell(1, 1).Style.Font.Bold);
    }

    [Fact]
    public async Task Disa_aktarilan_dosya_ayni_listeye_ve_baska_listeye_hatasiz_geri_yuklenir()
    {
        var tenant = await TenantAsync("Aktar");
        await using var db = NewDb(tenant);
        db.Products.AddRange(
            new Product { Name = "Kalem", Barcode = "0012345678905", Price = 100m, MinOrderQuantity = 20, PackSize = 10 },  // baştaki sıfır korunmalı; ürün değeri 20/10
            new Product { Name = "Defter", Barcode = "D-1", Price = 60m },
            new Product { Name = "Silgi", Barcode = "S1", Price = 5m });                                                      // listede fiyatı yok
        await db.SaveChangesAsync();
        var c = Controller(db);
        var a = ((PriceListDto)((CreatedResult)(await c.Create(ListInput("A"), default)).Result!).Value!).Id;
        var b = ((PriceListDto)((CreatedResult)(await c.Create(ListInput("B listesi / özel"), default)).Result!).Value!).Id;
        var kalem = await db.Products.SingleAsync(p => p.Name == "Kalem");
        var defter = await db.Products.SingleAsync(p => p.Name == "Defter");
        await c.SetItem(a, kalem.Id, new PriceListItemInput(79.5m, 10, 5), default);   // bu listede 10/5 geçersiz kılar
        await c.SetItem(a, defter.Id, new PriceListItemInput(45m), default);           // geçersiz kılma yok: ürünün değeri

        var import = new PriceListImportService(db);
        var exported = await import.ExportAsync(a, default);
        Assert.NotNull(exported);
        Assert.Equal("A", exported!.Value.Name);

        using (var ms = new MemoryStream(exported.Value.File))
        using (var book = new ClosedXML.Excel.XLWorkbook(ms))
        {
            var sheet = book.Worksheets.Single();
            Assert.Equal(3, sheet.RowsUsed().Count());                                   // başlık + 2 fiyatlı ürün
            var rows = sheet.RowsUsed().Skip(1).Select(r => (r.Cell(1).GetString(), r.Cell(2).GetValue<decimal>(), r.Cell(3).IsEmpty() ? (int?)null : r.Cell(3).GetValue<int>(), r.Cell(4).IsEmpty() ? (int?)null : r.Cell(4).GetValue<int>())).ToList();
            Assert.Equal(new (string, decimal, int?, int?)[] { ("D-1", 45m, null, null), ("0012345678905", 79.5m, 10, 5) }, rows);  // ada göre sıralı; boş hücre = ürünün değeri
        }

        // aynı listeye geri yükle: hepsi güncellenir, hata yok
        var again = await import.ImportAsync(a, new MemoryStream(exported.Value.File), default);
        Assert.Equal((0, 2, 0), (again.Created, again.Updated, again.Errors.Count));
        // başka listeye yükle: hepsi yeni kalem
        var toB = await import.ImportAsync(b, new MemoryStream(exported.Value.File), default);
        Assert.Equal((2, 0, 0), (toB.Created, toB.Updated, toB.Errors.Count));
        var page = (await c.Items(b, null, true, 1, 20, default)).Value!;
        Assert.Equal(new[] { 45m, 79.5m }, page.Items.Select(i => i.ListPrice!.Value).OrderBy(x => x));
        // geçersiz kılmalar B listesine de taşındı; ürün kaydı (20/10) değişmedi
        var kalemB = page.Items.Single(i => i.Barcode == "0012345678905");
        Assert.Equal((10, 5, 20, 10), (kalemB.ListMinOrderQuantity, kalemB.ListPackSize, kalemB.MinOrderQuantity, kalemB.PackSize));
        Assert.Null(page.Items.Single(i => i.Barcode == "D-1").ListMinOrderQuantity);
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == kalem.Id);
        Assert.Equal((20, 10), (product.MinOrderQuantity, product.PackSize));
    }

    // --- kalem düzeyinde minimum/koli ---

    [Fact]
    public async Task Adet_kurali_bayi_listesi_kalemi_grup_listesi_kalemi_ve_urun_sirasiyla_alan_alan_cozulur()
    {
        var tenant = await TenantAsync("AdetKural");
        await using var db = NewDb(tenant);
        var group = new DealerGroup { Name = "Gümüş" };
        db.DealerGroups.Add(group);
        await db.SaveChangesAsync();
        var both = new Dealer { Name = "İkisi", TaxNumber = "1000000001", Email = "a@a.com", Status = DealerStatus.Active, GroupId = group.Id };
        var onlyGroup = new Dealer { Name = "Grup", TaxNumber = "1000000002", Email = "b@b.com", Status = DealerStatus.Active, GroupId = group.Id };
        var bare = new Dealer { Name = "Yalın", TaxNumber = "1000000003", Email = "c@c.com", Status = DealerStatus.Active };
        var p = new Product { Name = "P", Barcode = "P", Price = 100m, VatRate = 20, MinOrderQuantity = 6, PackSize = 4 };   // ürün: 6/4 -> 8/4
        db.AddRange(both, onlyGroup, bare, p);
        await db.SaveChangesAsync();
        var groupList = new PriceList { Name = "Grup", GroupId = group.Id };
        var dealerList = new PriceList { Name = "Özel", DealerId = both.Id };
        db.PriceLists.AddRange(groupList, dealerList);
        await db.SaveChangesAsync();
        db.PriceListItems.AddRange(
            new PriceListItem { PriceListId = groupList.Id, ProductId = p.Id, Price = 90m, MinOrderQuantity = 20, PackSize = 10 },
            new PriceListItem { PriceListId = dealerList.Id, ProductId = p.Id, Price = 80m, MinOrderQuantity = null, PackSize = 5 });   // yalnızca koli
        await db.SaveChangesAsync();

        var svc = new DealerPriceService(db);
        async Task<(int, int)> Q(Guid dealer) { var r = (await svc.ResolveAsync(dealer, [p.Id], default))!.Single(); return (r.MinOrderQuantity, r.PackSize); }
        Assert.Equal((20, 5), await Q(both.Id));        // koli bayi listesinden, minimum boş: gruptan (20, 5 in katı)
        Assert.Equal((20, 10), await Q(onlyGroup.Id));  // grup listesi
        Assert.Equal((8, 4), await Q(bare.Id));         // listesiz: ürün, koli katına yuvarlanmış

        // bayi listesi pasifse grup listesine düşer
        (await db.PriceLists.SingleAsync(l => l.Id == dealerList.Id)).IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal((20, 10), await Q(both.Id));

        // kalemde yalnızca minimum geçersiz kılınırsa koli ürününkinden gelir ve minimum koli katına yuvarlanır
        var item = await db.PriceListItems.SingleAsync(i => i.PriceListId == groupList.Id);
        item.MinOrderQuantity = 10; item.PackSize = null;
        await db.SaveChangesAsync();
        Assert.Equal((12, 4), await Q(onlyGroup.Id));   // 10 -> 4 ün katı 12, koli ürünün 4 ü
    }

    [Fact]
    public async Task Kalem_adet_ayari_dogrulanir_fiyatsiz_kaydedilmez_ve_fiyat_silinince_gider()
    {
        var tenant = await TenantAsync("KalemAdet");
        await using var db = NewDb(tenant);
        var product = new Product { Name = "K", Barcode = "K1", Price = 100m, MinOrderQuantity = 3, PackSize = 3 };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var c = Controller(db);
        var id = ((PriceListDto)((CreatedResult)(await c.Create(ListInput(), default)).Result!).Value!).Id;

        Assert.IsType<BadRequestObjectResult>(await c.SetItem(id, product.Id, new PriceListItemInput(10m, 0, null), default));
        Assert.IsType<BadRequestObjectResult>(await c.SetItem(id, product.Id, new PriceListItemInput(10m, null, 100001), default));
        Assert.IsType<BadRequestObjectResult>(await c.SetItem(id, product.Id, new PriceListItemInput(null, 5, 5), default));       // fiyatsız adet ayarı
        Assert.Empty((await c.Items(id, null, true, 1, 20, default)).Value!.Items);

        Assert.IsType<NoContentResult>(await c.SetItem(id, product.Id, new PriceListItemInput(10m, 12, 6), default));
        var row = (await c.Items(id, null, true, 1, 20, default)).Value!.Items.Single();
        Assert.Equal((12, 6, 3, 3), (row.ListMinOrderQuantity, row.ListPackSize, row.MinOrderQuantity, row.PackSize)); // ürün değeri yanında geçersiz kılma

        // fiyat yazılırken adet verilmezse geçersiz kılma temizlenir (PUT, kalemi bütünüyle yazar)
        Assert.IsType<NoContentResult>(await c.SetItem(id, product.Id, new PriceListItemInput(11m), default));
        var cleared = (await c.Items(id, null, true, 1, 20, default)).Value!.Items.Single();
        Assert.Equal((null, null, 11m), (cleared.ListMinOrderQuantity, cleared.ListPackSize, cleared.ListPrice));

        await c.SetItem(id, product.Id, new PriceListItemInput(11m, 6, null), default);
        Assert.IsType<NoContentResult>(await c.SetItem(id, product.Id, new PriceListItemInput(null), default));                      // fiyat silinir: kalem ve ayarı gider
        Assert.Empty(await db.PriceListItems.ToListAsync());
    }

    [Fact]
    public async Task Bos_ya_da_olmayan_liste_disa_aktarimi_ve_diger_kiracinin_listesi()
    {
        var t1 = await TenantAsync("Aktar1");
        var t2 = await TenantAsync("Aktar2");
        Guid listId;
        await using (var db = NewDb(t1))
        {
            var c = Controller(db);
            listId = ((PriceListDto)((CreatedResult)(await c.Create(ListInput("Boş"), default)).Result!).Value!).Id;
            var empty = await new PriceListImportService(db).ExportAsync(listId, default);
            using var ms = new MemoryStream(empty!.Value.File);
            using var book = new ClosedXML.Excel.XLWorkbook(ms);
            Assert.Equal(1, book.Worksheets.Single().RowsUsed().Count());              // yalnızca başlık
            Assert.Null(await new PriceListImportService(db).ExportAsync(Guid.NewGuid(), default));
            var notFound = await c.Export(Guid.NewGuid(), default);
            Assert.IsType<NotFoundObjectResult>(notFound);
        }
        await using var other = NewDb(t2);
        Assert.Null(await new PriceListImportService(other).ExportAsync(listId, default)); // başka kiracının listesi görünmez
    }

    [Fact]
    public async Task Disa_aktarma_ucu_xlsx_dosyasi_ve_guvenli_ad_doner()
    {
        var tenant = await TenantAsync("Uc");
        await using var db = NewDb(tenant);
        var c = Controller(db);
        var id = ((PriceListDto)((CreatedResult)(await c.Create(ListInput("Altın / Özel 2027!"), default)).Result!).Value!).Id;
        var file = Assert.IsType<FileContentResult>(await c.Export(id, default));
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.Matches(@"^fiyat-listesi-[a-z0-9ığüşöç-]+\.xlsx$", file.FileDownloadName.ToLowerInvariant());
        Assert.DoesNotContain("/", file.FileDownloadName);
        Assert.Equal("fiyat-listesi-sablon.xlsx", c.Template().FileDownloadName);
    }

    [Fact]
    public async Task Bayi_icin_fiyat_cozumlenir_pasif_liste_yok_sayilir_ve_onizleme_ayni_kurali_kullanir()
    {
        var tenant = await TenantAsync("Cozum");
        await using var db = NewDb(tenant);
        var group = new DealerGroup { Name = "Gümüş" };
        db.DealerGroups.Add(group);
        await db.SaveChangesAsync();
        var dealer = new Dealer { Name = "Mavi", TaxNumber = "1234567890", Email = "m@m.com", Status = DealerStatus.Active, GroupId = group.Id };
        var other = new Dealer { Name = "Diğer", TaxNumber = "1234567891", Email = "d@d.com", Status = DealerStatus.Active };
        var a = new Product { Name = "A", Barcode = "A", Price = 200m, VatRate = 20, MinOrderQuantity = 6, PackSize = 4 };
        var b = new Product { Name = "B", Barcode = "B", Price = 50m, VatRate = 20 };
        db.AddRange(dealer, other, a, b);
        await db.SaveChangesAsync();

        var groupList = new PriceList { Name = "Grup", GroupId = group.Id, DiscountPercent = 10m, ShowVatIncluded = true };
        var dealerList = new PriceList { Name = "Mavi özel", DealerId = dealer.Id, ShowVatIncluded = true };
        db.PriceLists.AddRange(groupList, dealerList);
        await db.SaveChangesAsync();
        db.PriceListItems.Add(new PriceListItem { PriceListId = dealerList.Id, ProductId = a.Id, Price = 150m }); // bayiye özel: KDV hariç 150
        await db.SaveChangesAsync();

        var svc = new DealerPriceService(db);
        var r = (await svc.ResolveAsync(dealer.Id, [a.Id, b.Id], default))!.ToDictionary(x => x.ProductId);
        Assert.Equal((PriceSource.DealerList, 180m), (r[a.Id].Price.Source, r[a.Id].Price.Gross));
        Assert.Equal((8, 4), (r[a.Id].MinOrderQuantity, r[a.Id].PackSize)); // 6 → koli katına 8
        Assert.Equal((PriceSource.GroupList, 45m), (r[b.Id].Price.Source, r[b.Id].Price.Gross)); // grup iskontosu 10%

        // Grupsuz diğer bayi genel fiyatı görür.
        var o = (await svc.ResolveAsync(other.Id, [a.Id], default))!.Single();
        Assert.Equal((PriceSource.General, 200m), (o.Price.Source, o.Price.Gross));

        // Pasif liste yok sayılır.
        dealerList.IsActive = false;
        await db.SaveChangesAsync();
        var afterOff = (await svc.ResolveAsync(dealer.Id, [a.Id], default))!.Single();
        Assert.Equal((PriceSource.GroupList, 180m), (afterOff.Price.Source, afterOff.Price.Gross));

        Assert.Null(await svc.ResolveAsync(Guid.NewGuid(), [a.Id], default));

        var c = Controller(db);
        var preview = (await c.Preview(dealer.Id, "B", default)).Value!;
        Assert.Equal(("GroupList", 45m), (preview.Single().Source, preview.Single().Display));
        Assert.IsType<NotFoundObjectResult>((await c.Preview(Guid.NewGuid(), null, default)).Result);
    }

    [Fact]
    public async Task Baska_kiracinin_listesi_ve_fiyati_gorunmez()
    {
        var t1 = await TenantAsync("Bir");
        var t2 = await TenantAsync("Iki");
        await using (var seed = NewDb(t1))
        {
            seed.PriceLists.Add(new PriceList { Name = "Gizli" });
            await seed.SaveChangesAsync();
        }
        await using var db2 = NewDb(t2);
        Assert.Empty(await Controller(db2).List(default));
    }

    [Fact]
    public async Task Gruba_bagli_listesi_olan_grup_silinemez()
    {
        var tenant = await TenantAsync("Grup");
        await using var db = NewDb(tenant);
        var group = new DealerGroup { Name = "G" };
        db.DealerGroups.Add(group);
        await db.SaveChangesAsync();
        db.PriceLists.Add(new PriceList { Name = "L", GroupId = group.Id });
        await db.SaveChangesAsync();
        Assert.IsType<ConflictObjectResult>(await new DealerGroupsController(db).Delete(group.Id, default));
    }

    // --- Excel ile toplu yükleme ---

    private static MemoryStream Workbook(Action<IXLWorksheet> fill)
    {
        using var book = new XLWorkbook();
        fill(book.AddWorksheet("Fiyatlar"));
        var ms = new MemoryStream();
        book.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Excel_yukleme_fiyat_ve_adet_yazar_hatali_satirlari_raporlar_gecerlileri_kaydeder()
    {
        var tenant = await TenantAsync("Excel");
        await using var db = NewDb(tenant);
        db.Products.AddRange(new Product { Name = "A", Barcode = "A1", Price = 10m }, new Product { Name = "B", Barcode = "B1", Price = 20m });
        await db.SaveChangesAsync();
        var c = Controller(db);
        var id = ((PriceListDto)((CreatedResult)(await c.Create(ListInput(), default)).Result!).Value!).Id;

        using var file = Workbook(ws =>
        {
            ws.Cell(1, 1).Value = "Barkod"; ws.Cell(1, 2).Value = "Fiyat"; ws.Cell(1, 3).Value = "Minimum"; ws.Cell(1, 4).Value = "Koli";
            ws.Cell(2, 1).Value = "A1"; ws.Cell(2, 2).Value = 7.555m; ws.Cell(2, 3).Value = 10; ws.Cell(2, 4).Value = 5;
            ws.Cell(3, 1).Value = "YOK"; ws.Cell(3, 2).Value = 1;
            ws.Cell(4, 1).Value = "B1"; ws.Cell(4, 2).Value = "abc";
            ws.Cell(5, 1).Value = "B1"; ws.Cell(5, 2).Value = 15; ws.Cell(5, 3).Value = 0;   // minimum geçersiz
            ws.Cell(6, 1).Value = "A1"; ws.Cell(6, 2).Value = 99;                            // dosyada tekrar
            ws.Cell(7, 1).Value = ""; ws.Cell(7, 2).Value = 3;
        });
        var form = new FormFile(file, 0, file.Length, "file", "fiyat.xlsx");
        var result = (await c.Import(id, form, default)).Value!;

        Assert.Equal((1, 0, 5), (result.Created, result.Updated, result.Skipped));
        Assert.Equal(new[] { 3, 4, 5, 6, 7 }, result.Errors.Select(e => e.Row).OrderBy(r => r));

        await using var check = NewDb(tenant);
        var item = await check.PriceListItems.SingleAsync();
        Assert.Equal(7.56m, item.Price);
        Assert.Equal((10, 5), (item.MinOrderQuantity, item.PackSize));             // listede geçersiz kılma olarak saklanır
        var a = await check.Products.SingleAsync(p => p.Barcode == "A1");
        Assert.Equal((1, 1), (a.MinOrderQuantity, a.PackSize));                    // ürünün varsayılanı değişmez

        // İkinci yüklemede mevcut kalem güncellenir.
        using var again = Workbook(ws => { ws.Cell(1, 1).Value = "Barkod"; ws.Cell(1, 2).Value = "Fiyat"; ws.Cell(2, 1).Value = "A1"; ws.Cell(2, 2).Value = 9; });
        var second = (await c.Import(id, new FormFile(again, 0, again.Length, "file", "f.xlsx"), default)).Value!;
        Assert.Equal((0, 1), (second.Created, second.Updated));
        var cleared = await check.PriceListItems.AsNoTracking().SingleAsync();
        Assert.Equal((9m, null, null), (cleared.Price, cleared.MinOrderQuantity, cleared.PackSize));   // Minimum/Koli sütunu yoksa geçersiz kılma temizlenir
    }

    [Fact]
    public async Task Excel_yukleme_baslik_dosya_ve_liste_hatalarini_reddeder()
    {
        var tenant = await TenantAsync("ExcelHata");
        await using var db = NewDb(tenant);
        var c = Controller(db);
        var id = ((PriceListDto)((CreatedResult)(await c.Create(ListInput(), default)).Result!).Value!).Id;

        using var noHeader = Workbook(ws => ws.Cell(1, 1).Value = "Kod");
        Assert.IsType<BadRequestObjectResult>((await c.Import(id, new FormFile(noHeader, 0, noHeader.Length, "file", "f.xlsx"), default)).Result);

        using var junk = new MemoryStream("bu excel degil"u8.ToArray());
        Assert.IsType<BadRequestObjectResult>((await c.Import(id, new FormFile(junk, 0, junk.Length, "file", "f.xlsx"), default)).Result);

        Assert.IsType<BadRequestObjectResult>((await c.Import(id, null, default)).Result);

        using var ok = Workbook(ws => { ws.Cell(1, 1).Value = "Barkod"; ws.Cell(1, 2).Value = "Fiyat"; });
        Assert.IsType<NotFoundObjectResult>((await c.Import(Guid.NewGuid(), new FormFile(ok, 0, ok.Length, "file", "f.xlsx"), default)).Result);
    }
}
