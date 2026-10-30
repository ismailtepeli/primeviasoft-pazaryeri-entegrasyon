using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Modules;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Modules;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Tests;

public class ModuleCatalogTests
{
    [Fact]
    public void Kimlikler_tekil_bagimliliklar_tanimli_ve_dongu_yok()
    {
        var ids = ModuleCatalog.All.Select(m => m.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ModuleCatalog.All, m => Assert.All(m.DependsOn, d => Assert.Contains(d, ids)));

        // Her modülün bağımlılık zinciri kendine dönmez.
        foreach (var m in ModuleCatalog.All)
        {
            var seen = new HashSet<string>();
            var stack = new Stack<string>(m.DependsOn);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                Assert.NotEqual(m.Id, d);
                if (seen.Add(d)) foreach (var next in ModuleCatalog.Find(d)!.DependsOn) stack.Push(next);
            }
        }
    }

    [Fact]
    public void Cekirdek_tektir_bagimliligi_yoktur_ve_her_is_turu_bir_module_baglidir()
    {
        var core = Assert.Single(ModuleCatalog.All, m => m.Core);
        Assert.Equal((ModuleIds.Core, 0), (core.Id, core.DependsOn.Count));
        foreach (var type in Enum.GetValues<SyncJobType>()) Assert.NotNull(ModuleCatalog.Find(ModuleCatalog.ModuleFor(type)));
    }

    [Fact]
    public void Kayit_yoksa_bugunku_moduller_acik_B2B_modulleri_kapalidir_cekirdek_hep_acik()
    {
        var enabled = ModuleCatalog.Resolve(new Dictionary<string, bool>());
        Assert.Equal(["core", "invoices", "marketplace", "orders", "stock-price"], enabled.Order());

        var forced = ModuleCatalog.Resolve(new Dictionary<string, bool> { [ModuleIds.Core] = false, [ModuleIds.Dealers] = true, [ModuleIds.Orders] = false });
        Assert.Contains(ModuleIds.Core, forced);
        Assert.Contains(ModuleIds.Dealers, forced);
        Assert.DoesNotContain(ModuleIds.Orders, forced);
    }
}

public class ModuleServiceTests : IAsyncLifetime
{
    private const string Conn = "Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Tests_Modules;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class FixedTenant(Guid? id) : ITenantProvider
    {
        public Guid? TenantId => id;
    }

    private static AppDbContext NewDb(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn).Options, new FixedTenant(tenantId));

    private static ModuleService Service(AppDbContext db, Guid? tenant, IMemoryCache? cache = null) =>
        new(db, new FixedTenant(tenant), cache ?? new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);

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

    [Fact]
    public async Task Liste_tum_modulleri_varsayilan_durumlariyla_verir()
    {
        var tenant = await TenantAsync("Liste");
        await using var db = NewDb(tenant);

        var list = await Service(db, tenant).ListAsync();

        Assert.Equal(ModuleCatalog.All.Count, list.Count);
        Assert.Equal(["core", "marketplace", "stock-price", "orders", "invoices"], list.Where(m => m.Enabled).Select(m => m.Id));
    }

    [Fact]
    public async Task Bagimliligi_acik_olmayan_modul_acilamaz_acilinca_kaydedilir()
    {
        var tenant = await TenantAsync("Ac");
        await using var db = NewDb(tenant);
        var service = Service(db, tenant);

        var denied = await service.SetAsync(ModuleIds.B2BStore, true);
        Assert.Equal(ModuleChangeResult.MissingDependencies, denied.Result);
        Assert.Equal(["dealers"], denied.Related);

        Assert.Equal(ModuleChangeResult.Changed, (await service.SetAsync(ModuleIds.Dealers, true)).Result);
        Assert.Equal(ModuleChangeResult.Changed, (await service.SetAsync(ModuleIds.B2BStore, true)).Result);
        Assert.Equal(ModuleChangeResult.Unchanged, (await service.SetAsync(ModuleIds.B2BStore, true)).Result);

        await using var fresh = NewDb(tenant); // ayrı bağlam + ayrı önbellek: kalıcı olmalı
        Assert.True(await Service(fresh, tenant).IsEnabledAsync(ModuleIds.B2BStore));
    }

    [Fact]
    public async Task Bagimlisi_acik_modul_kapatilamaz_cekirdek_kapatilamaz_bilinmeyen_reddedilir()
    {
        var tenant = await TenantAsync("Kapat");
        await using var db = NewDb(tenant);
        var service = Service(db, tenant);

        var blocked = await service.SetAsync(ModuleIds.Orders, false); // faturalar buna bağlı
        Assert.Equal(ModuleChangeResult.HasDependents, blocked.Result);
        Assert.Equal(["invoices"], blocked.Related);
        Assert.Equal(ModuleChangeResult.HasDependents, (await service.SetAsync(ModuleIds.Marketplace, false)).Result); // stok-fiyat buna bağlı
        Assert.Equal(ModuleChangeResult.CoreCannotDisable, (await service.SetAsync(ModuleIds.Core, false)).Result);
        Assert.Equal(ModuleChangeResult.Unknown, (await service.SetAsync("yok", true)).Result);

        Assert.Equal(ModuleChangeResult.Changed, (await service.SetAsync(ModuleIds.Invoices, false)).Result);
        Assert.Equal(ModuleChangeResult.Changed, (await service.SetAsync(ModuleIds.Orders, false)).Result); // artık bağımlısı yok
        Assert.False(await service.IsEnabledAsync(ModuleIds.Orders));
        Assert.Equal(ModuleChangeResult.MissingDependencies, (await service.SetAsync(ModuleIds.Invoices, true)).Result); // sipariş kapalıyken açılamaz
    }

    [Fact]
    public async Task Kiracilar_birbirinin_modul_durumunu_gormez_ve_onbellek_degisiklikte_yenilenir()
    {
        var a = await TenantAsync("A");
        var b = await TenantAsync("B");
        var cache = new MemoryCache(new MemoryCacheOptions());
        await using var dbA = NewDb(a);
        await using var dbB = NewDb(b);
        var serviceA = Service(dbA, a, cache);
        var serviceB = Service(dbB, b, cache);

        Assert.False(await serviceA.IsEnabledAsync(ModuleIds.Dealers)); // önbelleğe girer
        await serviceA.SetAsync(ModuleIds.Dealers, true);

        Assert.True(await serviceA.IsEnabledAsync(ModuleIds.Dealers)); // önbellek geçersiz kılındı
        Assert.False(await serviceB.IsEnabledAsync(ModuleIds.Dealers));
        Assert.True(await serviceB.IsEnabledAsync(ModuleIds.Dealers, a)); // iş işçisi gibi kiracı verilerek okuma
    }

    [Fact]
    public async Task Moduller_uc_noktasi_listeler_ve_kural_ihlallerini_409_ile_bildirir()
    {
        var tenant = await TenantAsync("Api");
        await using var db = NewDb(tenant);
        var controller = new ModulesController(Service(db, tenant));

        Assert.Equal(ModuleCatalog.All.Count, (await controller.List(default)).Count);
        Assert.IsType<BadRequestObjectResult>(await controller.Set("dealers", new ModuleToggleInput(null), default));
        Assert.IsType<NotFoundObjectResult>(await controller.Set("yok", new ModuleToggleInput(true), default));
        var core = Assert.IsType<ConflictObjectResult>(await controller.Set("core", new ModuleToggleInput(false), default));
        Assert.Contains("core_cannot_disable", core.Value!.ToString());
        var missing = Assert.IsType<ConflictObjectResult>(await controller.Set("b2b-store", new ModuleToggleInput(true), default));
        Assert.Contains("missing_dependencies", missing.Value!.ToString());
        var ok = Assert.IsType<OkObjectResult>(await controller.Set("dealers", new ModuleToggleInput(true), default));
        Assert.Contains(((IReadOnlyList<ModuleStatus>)ok.Value!), m => m is { Id: "dealers", Enabled: true });
    }

    private static async Task<(ActionExecutingContext Context, bool Called)> RunFilterAsync(RequiresModuleAttribute filter, Guid? tenant, ModuleService modules)
    {
        var services = new ServiceCollection()
            .AddSingleton<ITenantProvider>(new FixedTenant(tenant))
            .AddSingleton<IModuleService>(modules)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var actionContext = new ActionContext(http, new RouteData(), new ControllerActionDescriptor());
        var context = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), new object());
        var called = false;
        await filter.OnActionExecutionAsync(context, () => { called = true; return Task.FromResult(new ActionExecutedContext(actionContext, [], new object())); });
        return (context, called);
    }

    [Fact]
    public async Task Koruma_filtresi_kapali_modulde_403_module_disabled_doner_acikta_ve_oturumsuzda_gecirir()
    {
        var tenant = await TenantAsync("Filtre");
        await using var db = NewDb(tenant);
        var modules = Service(db, tenant);

        var closed = await RunFilterAsync(new RequiresModuleAttribute(ModuleIds.Dealers), tenant, modules);
        Assert.False(closed.Called);
        var result = Assert.IsType<ObjectResult>(closed.Context.Result);
        Assert.Equal(403, result.StatusCode);
        Assert.Contains("module_disabled", result.Value!.ToString());

        await modules.SetAsync(ModuleIds.Dealers, true);
        Assert.True((await RunFilterAsync(new RequiresModuleAttribute(ModuleIds.Dealers), tenant, modules)).Called);
        Assert.True((await RunFilterAsync(new RequiresModuleAttribute(ModuleIds.Dealers), null, modules)).Called); // oturum yoksa yetkilendirme karar verir
    }
}
