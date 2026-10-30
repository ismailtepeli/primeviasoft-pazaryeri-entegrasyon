using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Stock;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;
using Pazaryeri.Infrastructure.Stock;

namespace Pazaryeri.Tests;

public class StockPoolTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_StockPool;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private sealed class RecordingQueue(AppDbContext db) : IJobQueue
    {
        public async Task<SyncJob> EnqueueAsync(SyncJobType type, string channel, string? batchId = null, CancellationToken ct = default, string? payload = null)
        {
            var job = new SyncJob { Type = type, Channel = channel };
            db.SyncJobs.Add(job);
            await db.SaveChangesAsync(ct);
            return job;
        }
    }

    private static AppDbContext NewDb(Guid? tenantId = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    private static StockPoolService Pool(AppDbContext db) => new(db, new RecordingQueue(db));

    public async Task InitializeAsync()
    {
        await using var db = NewDb();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<(Guid Tenant, Guid Product)> SetupAsync(string name, int stock = 10, bool trendyolConnected = false, bool poolEnabled = true)
    {
        await using var global = NewDb();
        var tenant = new Tenant { Name = name };
        global.Tenants.Add(tenant);
        await global.SaveChangesAsync();
        await using var seed = NewDb(tenant.Id);
        var product = new Product { Name = "P", Barcode = "BC-" + name, Price = 1, Stock = stock };
        seed.Products.Add(product);
        if (trendyolConnected) seed.ChannelConnections.Add(new ChannelConnection { Channel = "Trendyol", Status = ConnectionStatus.Connected });
        if (!poolEnabled) seed.StockSettings.Add(new StockSettings { DeductAcrossChannels = false });
        await seed.SaveChangesAsync();
        return (tenant.Id, product.Id);
    }

    private static string Barcode(string name) => "BC-" + name;

    // --- satış ---

    [Fact]
    public async Task Satis_stogu_dusurur_deftere_yazar_ve_ayni_siparis_tekrar_gelirse_stok_bir_kez_duser()
    {
        var (tenant, product) = await SetupAsync("Satis");
        await using var db = NewDb(tenant);

        var first = Assert.Single(await Pool(db).DeductForSaleAsync("Hepsiburada", "ORD-1:L1", [new(Barcode("Satis"), 3)]));
        Assert.Equal((StockOutcome.Applied, 3, 7, 0), (first.Outcome, first.Applied, first.StockAfter, first.Oversold));

        await using var db2 = NewDb(tenant);
        var again = Assert.Single(await Pool(db2).DeductForSaleAsync("Hepsiburada", "ORD-1:L1", [new(Barcode("Satis"), 3)]));
        Assert.Equal(StockOutcome.Duplicate, again.Outcome);

        await using var check = NewDb(tenant);
        Assert.Equal(7, (await check.Products.SingleAsync()).Stock);
        var movement = await check.StockMovements.SingleAsync();
        Assert.Equal((StockMovementType.Sale, -3, 3, 7, "ORD-1:L1", "Hepsiburada"), (movement.Type, movement.Quantity, movement.Requested, movement.StockAfter, movement.Reference, movement.Channel));
    }

    [Fact]
    public async Task Ayni_referans_baska_kanalda_ayri_olaydir()
    {
        var (tenant, _) = await SetupAsync("Kanal", 10);
        await using var db = NewDb(tenant);
        await Pool(db).DeductForSaleAsync("Hepsiburada", "ORD-9", [new(Barcode("Kanal"), 2)]);
        await using var db2 = NewDb(tenant);
        await Pool(db2).DeductForSaleAsync("N11", "ORD-9", [new(Barcode("Kanal"), 2)]);

        Assert.Equal(6, (await NewDb(tenant).Products.SingleAsync()).Stock);
    }

    [Fact]
    public async Task Stok_yetmezse_negatife_dusmez_fazla_satis_kaydedilir()
    {
        var (tenant, _) = await SetupAsync("Fazla", 2);
        await using var db = NewDb(tenant);

        var result = Assert.Single(await Pool(db).DeductForSaleAsync("N11", "ORD-2", [new(Barcode("Fazla"), 5)]));

        Assert.Equal((StockOutcome.Applied, 2, 0, 3), (result.Outcome, result.Applied, result.StockAfter, result.Oversold));
        var movement = await NewDb(tenant).StockMovements.SingleAsync();
        Assert.Equal((-2, 5), (movement.Quantity, movement.Requested));
    }

    [Fact]
    public async Task Bilinmeyen_barkod_gecersiz_adet_ve_ayni_barkodlu_satirlar_dogru_islenir()
    {
        var (tenant, _) = await SetupAsync("Satirlar", 10);
        await using var db = NewDb(tenant);

        var results = await Pool(db).DeductForSaleAsync("N11", "ORD-3", [new("YOK", 1), new(Barcode("Satirlar"), 0), new(Barcode("Satirlar"), 2), new(Barcode("Satirlar").ToLowerInvariant(), 1)]);

        Assert.Equal(StockOutcome.UnknownBarcode, results.Single(r => r.Barcode == "YOK").Outcome);
        Assert.Contains(results, r => r.Outcome == StockOutcome.Applied && r.Applied == 3); // 2 + 1 tek satıra birleşti (barkod büyük/küçük harf duyarsız)
        Assert.Equal(7, (await NewDb(tenant).Products.SingleAsync()).Stock);
        Assert.Single(await NewDb(tenant).StockMovements.ToListAsync());

        var zero = Assert.Single(await Pool(NewDb(tenant)).DeductForSaleAsync("N11", "ORD-4", [new(Barcode("Satirlar"), 0)]));
        Assert.Equal(StockOutcome.InvalidQuantity, zero.Outcome);
        await Assert.ThrowsAsync<ArgumentException>(() => Pool(NewDb(tenant)).DeductForSaleAsync("", "ORD", [new("x", 1)]));
        await Assert.ThrowsAsync<ArgumentException>(() => Pool(NewDb(tenant)).DeductForSaleAsync("N11", " ", [new("x", 1)]));
    }

    [Fact]
    public async Task Ortak_havuz_kapaliysa_satis_stogu_etkilemez()
    {
        var (tenant, _) = await SetupAsync("Kapali", 10, trendyolConnected: true, poolEnabled: false);
        await using var db = NewDb(tenant);

        var result = Assert.Single(await Pool(db).DeductForSaleAsync("Hepsiburada", "ORD-5", [new(Barcode("Kapali"), 4)]));

        Assert.Equal(StockOutcome.PoolDisabled, result.Outcome);
        Assert.Equal(10, (await NewDb(tenant).Products.SingleAsync()).Stock);
        Assert.Empty(await NewDb(tenant).StockMovements.ToListAsync());
        Assert.Empty(await NewDb(tenant).SyncJobs.ToListAsync()); // senkron da tetiklenmez
    }

    // --- iptal / iade ---

    [Fact]
    public async Task Iptal_dusulen_stogu_geri_ekler_tekrar_gelirse_etkisizdir_ve_satistan_fazlasi_eklenmez()
    {
        var (tenant, _) = await SetupAsync("Iptal", 10);
        var bc = Barcode("Iptal");
        await Pool(NewDb(tenant)).DeductForSaleAsync("Hepsiburada", "ORD-6", [new(bc, 4)]);

        var cancel = Assert.Single(await Pool(NewDb(tenant)).RestoreAsync("Hepsiburada", "ORD-6", "CANCEL-6", [new(bc, 10)])); // 10 istendi, yalnızca 4 satılmıştı
        Assert.Equal((StockOutcome.Applied, 4, 10), (cancel.Outcome, cancel.Applied, cancel.StockAfter));

        var repeat = Assert.Single(await Pool(NewDb(tenant)).RestoreAsync("Hepsiburada", "ORD-6", "CANCEL-6", [new(bc, 4)]));
        Assert.Equal(StockOutcome.Duplicate, repeat.Outcome);

        var another = Assert.Single(await Pool(NewDb(tenant)).RestoreAsync("Hepsiburada", "ORD-6", "RETURN-6", [new(bc, 1)])); // hepsi zaten geri eklendi
        Assert.Equal(StockOutcome.NothingToRestore, another.Outcome);
        Assert.Equal(10, (await NewDb(tenant).Products.SingleAsync()).Stock);
    }

    [Fact]
    public async Task Kismi_iadeler_ayri_olaylar_olarak_toplanir_ve_satilani_asmaz()
    {
        var (tenant, _) = await SetupAsync("Kismi", 10);
        var bc = Barcode("Kismi");
        await Pool(NewDb(tenant)).DeductForSaleAsync("N11", "ORD-7", [new(bc, 5)]);

        await Pool(NewDb(tenant)).RestoreAsync("N11", "ORD-7", "RET-A", [new(bc, 2)]);
        await Pool(NewDb(tenant)).RestoreAsync("N11", "ORD-7", "RET-B", [new(bc, 2)]);
        var last = Assert.Single(await Pool(NewDb(tenant)).RestoreAsync("N11", "ORD-7", "RET-C", [new(bc, 2)])); // yalnızca 1 kaldı

        Assert.Equal((StockOutcome.Applied, 1, 10), (last.Outcome, last.Applied, last.StockAfter));
        Assert.Equal(4, await NewDb(tenant).StockMovements.CountAsync()); // 1 satış + 3 geri ekleme
    }

    [Fact]
    public async Task Satisi_olmayan_siparis_icin_iptal_stok_eklemez_ve_baska_kanalin_satisi_sayilmaz()
    {
        var (tenant, _) = await SetupAsync("Satissiz", 10);
        var bc = Barcode("Satissiz");
        await Pool(NewDb(tenant)).DeductForSaleAsync("N11", "ORD-8", [new(bc, 3)]);

        var none = Assert.Single(await Pool(NewDb(tenant)).RestoreAsync("N11", "OLMAYAN", "C-1", [new(bc, 3)]));
        var wrongChannel = Assert.Single(await Pool(NewDb(tenant)).RestoreAsync("Hepsiburada", "ORD-8", "C-2", [new(bc, 3)]));

        Assert.Equal([StockOutcome.NothingToRestore, StockOutcome.NothingToRestore], [none.Outcome, wrongChannel.Outcome]);
        Assert.Equal(7, (await NewDb(tenant).Products.SingleAsync()).Stock);
    }

    // --- diğer kanalların güncellenmesi ---

    [Fact]
    public async Task Satis_baska_kanaldan_geldiyse_bagli_Trendyol_icin_stok_senkronu_kuyruga_alinir_tekrar_eklenmez()
    {
        var (tenant, _) = await SetupAsync("Tetik", 10, trendyolConnected: true);
        await Pool(NewDb(tenant)).DeductForSaleAsync("Hepsiburada", "ORD-10", [new(Barcode("Tetik"), 1)]);
        await Pool(NewDb(tenant)).DeductForSaleAsync("Hepsiburada", "ORD-11", [new(Barcode("Tetik"), 1)]); // ilk iş sırada

        var job = Assert.Single(await NewDb(tenant).SyncJobs.ToListAsync());
        Assert.Equal((SyncJobType.StockPriceSync, "Trendyol"), (job.Type, job.Channel));
    }

    [Fact]
    public async Task Satis_Trendyoldan_geldiyse_kendisi_icin_kuyruga_alinmaz_degisiklik_olmadiysa_da_alinmaz()
    {
        var (tenant, _) = await SetupAsync("Kaynak", 10, trendyolConnected: true);
        await Pool(NewDb(tenant)).DeductForSaleAsync("Trendyol", "ORD-12", [new(Barcode("Kaynak"), 1)]);
        await Pool(NewDb(tenant)).DeductForSaleAsync("Hepsiburada", "ORD-13", [new("YOK", 1)]);   // bilinmeyen barkod: değişiklik yok

        Assert.Empty(await NewDb(tenant).SyncJobs.ToListAsync());
    }

    // --- eşzamanlılık ---

    [Fact]
    public async Task Ayni_anda_gelen_siparisler_kayip_guncelleme_olmadan_dusulur()
    {
        var (tenant, _) = await SetupAsync("Esz", 100);
        var bc = Barcode("Esz");

        await Task.WhenAll(Enumerable.Range(1, 8).Select(async i =>
        {
            await using var db = NewDb(tenant);
            await Pool(db).DeductForSaleAsync("N11", $"ORD-C{i}", [new(bc, 5)]);
        }));

        await using var check = NewDb(tenant);
        Assert.Equal(60, (await check.Products.SingleAsync()).Stock);
        Assert.Equal(8, await check.StockMovements.CountAsync());
    }

    [Fact]
    public async Task Ayni_olay_ayni_anda_iki_kez_gelirse_stok_bir_kez_duser()
    {
        var (tenant, _) = await SetupAsync("Cift", 100);
        var bc = Barcode("Cift");

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = NewDb(tenant);
            return (await Pool(db).DeductForSaleAsync("N11", "ORD-SAME", [new(bc, 5)])).Single().Outcome;
        }));

        Assert.Equal(1, results.Count(r => r == StockOutcome.Applied));
        Assert.Equal(3, results.Count(r => r == StockOutcome.Duplicate));
        Assert.Equal(95, (await NewDb(tenant).Products.SingleAsync()).Stock);
    }

    // --- elle düzeltme ve defter görünümü ---

    [Fact]
    public async Task Urun_sayfasindan_stok_degisince_duzeltme_hareketi_yazilir()
    {
        var (tenant, product) = await SetupAsync("Elle", 10);
        await using var db = NewDb(tenant);
        var controller = new ProductsController(db);
        var current = (await controller.Get(product, default)).Value!;

        await controller.Update(product, new ProductInput(current.Name, current.Barcode, null, null, null, "d", null, 25, current.Price), default);
        await using var db2 = NewDb(tenant);
        await new ProductsController(db2).Update(product, new ProductInput(current.Name, current.Barcode, null, null, null, "d", null, 25, current.Price), default); // stok değişmedi

        var movement = await NewDb(tenant).StockMovements.SingleAsync();
        Assert.Equal((StockMovementType.Adjustment, "Manuel", 15, 25), (movement.Type, movement.Channel, movement.Quantity, movement.StockAfter));
    }

    [Fact]
    public async Task Defter_listesi_en_yeniyi_basa_alir_arar_sayfalar_ve_kiracilar_arasi_sizmaz()
    {
        var (a, _) = await SetupAsync("Defter A", 100);
        var (b, _) = await SetupAsync("Defter B", 100);
        for (var i = 1; i <= 25; i++) await Pool(NewDb(a)).DeductForSaleAsync("N11", $"ORD-{i:00}", [new(Barcode("Defter A"), 1)]);
        await Pool(NewDb(b)).DeductForSaleAsync("N11", "ORD-B", [new(Barcode("Defter B"), 1)]);

        await using var db = NewDb(a);
        var controller = new StockController(db, TimeProvider.System);
        var first = await controller.Movements(null, 1, 10, default);
        var found = await controller.Movements("ORD-07", 1, 10, default);

        Assert.Equal((25, 10), (first.Total, first.Items.Count));
        Assert.Equal("ORD-25", first.Items[0].Reference); // en yeni önce
        Assert.Equal(["ORD-07"], found.Items.Select(x => x.Reference));
        Assert.All(first.Items, x => Assert.Equal(("Sale", -1), (x.Type, x.Quantity)));
        Assert.Equal(0, (await new StockController(NewDb(b), TimeProvider.System).Movements("ORD-07", 1, 10, default)).Total);
    }
}
