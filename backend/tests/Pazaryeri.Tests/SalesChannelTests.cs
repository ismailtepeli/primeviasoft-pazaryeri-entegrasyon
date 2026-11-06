using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Modules;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Modules;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Tests;

public class SalesChannelTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_SalesChannels;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private static AppDbContext NewDb(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    private static SetupService Setup(AppDbContext db, Guid tenant)
    {
        var provider = new FixedTenant(tenant);
        var modules = new ModuleService(db, provider, new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);
        return new SetupService(db, modules, provider, TimeProvider.System);
    }

    private static ModuleService Modules(AppDbContext db, Guid tenant) =>
        new(db, new FixedTenant(tenant), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);

    public async Task InitializeAsync()
    {
        await using var db = NewDb(null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> TenantAsync(string name, bool completed = false)
    {
        await using var db = NewDb(null);
        var t = new Tenant { Name = name, SetupCompletedAt = completed ? DateTime.UtcNow : null };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private static HashSet<string> Defaults() => ModuleCatalog.Resolve(new Dictionary<string, bool>());

    // --- saf plan ---

    [Fact]
    public void Hicbir_kanal_secilmezse_kanal_modulleri_ve_bagli_faturalar_kapanir_bagimlilar_once()
    {
        var plan = SalesChannelCatalog.Plan([], Defaults());
        Assert.Empty(plan.Enable);
        Assert.Equal(new[] { "invoices", "stock-price", "orders", "marketplace" }.OrderBy(x => x), plan.Disable.OrderBy(x => x));
        Assert.True(plan.Disable.ToList().IndexOf(ModuleIds.Invoices) < plan.Disable.ToList().IndexOf(ModuleIds.Orders));
        Assert.True(plan.Disable.ToList().IndexOf(ModuleIds.StockPrice) < plan.Disable.ToList().IndexOf(ModuleIds.Marketplace));
    }

    [Fact]
    public void Yalnizca_B2B_secilirse_bayi_modulleri_acilir_pazaryeri_kapanir_siparisler_ve_faturalar_kalir()
    {
        var plan = SalesChannelCatalog.Plan([SalesChannelIds.B2B], Defaults());
        Assert.Equal(new[] { ModuleIds.Dealers, ModuleIds.PriceLists, ModuleIds.B2BStore }.OrderBy(x => x), plan.Enable.OrderBy(x => x));
        Assert.True(plan.Enable.ToList().IndexOf(ModuleIds.Dealers) < plan.Enable.ToList().IndexOf(ModuleIds.B2BStore)); // bağımlılık önce
        Assert.Equal(new[] { ModuleIds.Marketplace, ModuleIds.StockPrice }.OrderBy(x => x), plan.Disable.OrderBy(x => x));
    }

    [Fact]
    public void Iki_kanal_birlikte_secilince_ortak_siparis_modulu_korunur()
    {
        var plan = SalesChannelCatalog.Plan([SalesChannelIds.Marketplace, SalesChannelIds.B2B], Defaults());
        Assert.DoesNotContain(ModuleIds.Orders, plan.Disable);
        Assert.Empty(plan.Disable);
        Assert.Equal(3, plan.Enable.Count);
    }

    [Fact]
    public void Plan_cekirdege_ve_kanal_disi_modullere_dokunmaz_ve_B2C_secilebilir()
    {
        var plan = SalesChannelCatalog.Plan([], Defaults());
        Assert.DoesNotContain(ModuleIds.Core, plan.Disable);
        Assert.True(SalesChannelCatalog.Find(SalesChannelIds.B2C)!.Available);
        Assert.True(ModuleCatalog.Find(ModuleIds.B2CStore)!.Available);
    }

    // --- servis ---

    [Fact]
    public async Task Kurulum_kanallari_uygular_ve_tamamlandi_isaretler_yeni_kiraci_once_tamamlanmamistir()
    {
        var tenant = await TenantAsync("Yeni");
        await using var db = NewDb(tenant);
        var setup = Setup(db, tenant);
        Assert.False((await setup.GetAsync()).Completed);

        var outcome = await setup.ApplyAsync([SalesChannelIds.B2B]);
        Assert.Equal(SetupResult.Applied, outcome.Result);

        var state = await setup.GetAsync();
        Assert.True(state.Completed);
        Assert.Equal((false, true, false), (state.Channels.Single(c => c.Id == "marketplace").Enabled, state.Channels.Single(c => c.Id == "b2b").Enabled, state.Channels.Single(c => c.Id == "b2c").Enabled));
        var enabled = await Modules(db, tenant).EnabledAsync();
        Assert.Contains(ModuleIds.B2BStore, enabled);
        Assert.DoesNotContain(ModuleIds.Marketplace, enabled);
        Assert.Contains(ModuleIds.Orders, enabled);
    }

    [Fact]
    public async Task Kanal_sonradan_degistirilir_tekrar_acilir_ve_hepsi_kapaninca_yalnizca_cekirdek_kalir()
    {
        var tenant = await TenantAsync("Degisim", completed: true);
        await using var db = NewDb(tenant);
        var setup = Setup(db, tenant);
        await setup.ApplyAsync([SalesChannelIds.Marketplace, SalesChannelIds.B2B]);
        Assert.Contains(ModuleIds.B2BStore, await Modules(db, tenant).EnabledAsync());

        await setup.ApplyAsync([SalesChannelIds.Marketplace]);
        var enabled = await Modules(db, tenant).EnabledAsync();
        Assert.DoesNotContain(ModuleIds.B2BStore, enabled);
        Assert.Contains(ModuleIds.Marketplace, enabled);

        await setup.ApplyAsync([]);
        Assert.Equal([ModuleIds.Core], (await Modules(db, tenant).EnabledAsync()).OrderBy(x => x).ToArray());

        await setup.ApplyAsync([SalesChannelIds.B2B]); // geri açılır
        Assert.Contains(ModuleIds.B2BStore, await Modules(db, tenant).EnabledAsync());
    }

    [Fact]
    public async Task Bilinmeyen_kanal_reddedilir_hicbir_modul_degismez()
    {
        var tenant = await TenantAsync("Red");
        await using var db = NewDb(tenant);
        var setup = Setup(db, tenant);
        var before = (await Modules(db, tenant).EnabledAsync()).OrderBy(x => x).ToList();

        Assert.Equal(SetupResult.UnknownChannel, (await setup.ApplyAsync(["marketplace", "yok"])).Result);

        Assert.Equal(before, (await Modules(db, tenant).EnabledAsync()).OrderBy(x => x).ToList());
        Assert.False((await setup.GetAsync()).Completed); // reddedilen istek kurulumu tamamlamaz
    }

    [Fact]
    public async Task B2C_kanali_sihirbazdan_acilir_kapanir_ve_kiracilar_birbirini_etkilemez()
    {
        var a = await TenantAsync("A");
        var b = await TenantAsync("B");
        await using var db = NewDb(a);
        Assert.Equal(SetupResult.Applied, (await Setup(db, a).ApplyAsync([SalesChannelIds.B2B, SalesChannelIds.B2C])).Result);
        Assert.Contains(ModuleIds.B2CStore, await Modules(db, a).EnabledAsync());
        await Setup(db, a).ApplyAsync([SalesChannelIds.B2B]);
        Assert.DoesNotContain(ModuleIds.B2CStore, await Modules(db, a).EnabledAsync());
        await using var dbB = NewDb(b);
        Assert.DoesNotContain(ModuleIds.B2BStore, await Modules(dbB, b).EnabledAsync());
        Assert.False((await Setup(dbB, b).GetAsync()).Completed);
    }

    [Fact]
    public async Task Sihirbazdan_once_var_olan_kiraci_migration_ile_tamamlanmis_sayilir_katalog_durumu_dogru()
    {
        var tenant = await TenantAsync("Mevcut", completed: true);
        await using var db = NewDb(tenant);
        var state = await Setup(db, tenant).GetAsync();
        Assert.True(state.Completed);
        Assert.Equal(3, state.Channels.Count);
        Assert.True(state.Channels.Single(c => c.Id == "marketplace").Enabled); // varsayılan: pazaryeri açık
    }
}
